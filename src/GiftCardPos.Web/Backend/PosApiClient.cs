using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace GiftCardPos.Web.Backend;

/// <summary>
/// The till's only route to the platform.
///
/// The POS client secret is held here, server-side, and never reaches the
/// browser. That is the whole reason this is a server-rendered application
/// rather than a page that calls the API directly: a till authenticates as a
/// device, and its credential is as sensitive as any other (ADR-043).
/// </summary>
public sealed class PosApiClient(
    IHttpClientFactory httpClientFactory,
    IOptions<PosOptions> options,
    TimeProvider timeProvider,
    ILogger<PosApiClient> logger) : IDisposable
{
    public const string HttpClientName = "platform";

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly Action<ILogger, Exception?> PlatformUnreachable =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2001, nameof(PlatformUnreachable)),
            "The platform could not be reached.");

    private static readonly Action<ILogger, Exception?> PlatformRefusedTill =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2002, nameof(PlatformRefusedTill)),
            "The platform refused this till's credentials.");

    private readonly PosOptions settings = options.Value;
    private readonly SemaphoreSlim tokenGate = new(1, 1);

    private string? accessToken;
    private DateTimeOffset accessTokenExpiresAtUtc = DateTimeOffset.MinValue;

    /// <summary>
    /// Presents a credential and holds value for the sale. Returns the failure
    /// as data rather than throwing, because a refused credential is an ordinary
    /// thing to happen at a counter and the cashier needs to be told, not shown
    /// an error page.
    /// </summary>
    public async Task<PosResult<PaymentProvision>> CreateProvisionAsync(
        string credential,
        decimal amount,
        string posTransactionReference,
        CancellationToken cancellationToken)
    {
        var numeric = LooksNumeric(credential);
        var body = new
        {
            paymentToken = numeric ? null : (string?)credential.Trim(),
            paymentCode = numeric ? (string?)Normalize(credential) : null,
            amount,
            posTransactionReference,

            // The sale reference is the key. One sale takes one hold, so
            // retrying the same sale after a lost response must be answered with
            // the hold it already has rather than refused as a replay of a spent
            // credential. A value generated per call would defeat that.
            idempotencyKey = posTransactionReference,
        };

        return await SendAsync<PaymentProvision>(
            HttpMethod.Post,
            "/api/v1/pos/payment-provisions",
            body,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads back a hold this till created. Another till's is not found.</summary>
    public Task<PosResult<PaymentProvision>> GetProvisionAsync(
        Guid provisionId,
        CancellationToken cancellationToken) =>
        SendAsync<PaymentProvision>(
            HttpMethod.Get,
            $"/api/v1/pos/payment-provisions/{provisionId}",
            content: null,
            cancellationToken);

    public Task<PosResult<PaymentProvision>> ConfirmAsync(
        Guid provisionId,
        decimal amount,
        CancellationToken cancellationToken) =>
        SendAsync<PaymentProvision>(
            HttpMethod.Post,
            $"/api/v1/pos/payment-provisions/{provisionId}/confirm",
            new { amount },
            cancellationToken);

    public Task<PosResult<PaymentProvision>> CancelAsync(
        Guid provisionId,
        CancellationToken cancellationToken) =>
        SendAsync<PaymentProvision>(
            HttpMethod.Post,
            $"/api/v1/pos/payment-provisions/{provisionId}/cancel",
            content: null,
            cancellationToken);

    /// <summary>
    /// A 12-digit numeric code, allowing the spaces or hyphens a cashier would
    /// naturally type and a scanner might emit. Anything else is treated as the
    /// opaque QR credential, which is what a barcode scanner types verbatim.
    /// </summary>
    internal static bool LooksNumeric(string credential)
    {
        var digits = Normalize(credential);
        return digits.Length == 12 && digits.All(char.IsAsciiDigit);
    }

    internal static string Normalize(string credential) =>
        new(credential.Where(character => !char.IsWhiteSpace(character) && character != '-').ToArray());

    private async Task<PosResult<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        object? content,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var response = await SendOnceAsync(method, path, content, cancellationToken)
                .ConfigureAwait(false);

            // One retry on 401: the 15-minute device token may have lapsed
            // mid-sale, and re-authenticating is cheaper than failing the sale.
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                InvalidateToken();
                response = await SendOnceAsync(method, path, content, cancellationToken)
                    .ConfigureAwait(false);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    var value = await response.Content
                        .ReadFromJsonAsync<T>(Json, cancellationToken)
                        .ConfigureAwait(false);
                    return value is null
                        ? PosResult.Failure<T>("The platform returned an empty response.")
                        : PosResult.Success(value);
                }

                return PosResult.Failure<T>(
                    await DescribeAsync(response, cancellationToken).ConfigureAwait(false));
            }
        }
        catch (PosAuthenticationException exception)
        {
            // The platform answered, and said no. Reporting that as a
            // connection problem sends the cashier to check a network that is
            // working, when the actual answer is that this till is not
            // registered or its secret is wrong.
            PlatformRefusedTill(logger, exception);
            return PosResult.Failure<T>(exception.Message);
        }
        catch (HttpRequestException exception)
        {
            PlatformUnreachable(logger, exception);
            return PosResult.Failure<T>("The platform could not be reached. Check the connection and try again.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PosResult.Failure<T>("The platform did not respond in time. The sale was not charged.");
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpMethod method,
        string path,
        object? content,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (content is not null)
        {
            request.Content = JsonContent.Create(content, options: Json);
        }

        using var client = httpClientFactory.CreateClient(HttpClientName);
        return await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Turns a platform refusal into something a cashier can act on. Deliberately
    /// plain: the platform refuses unknown, replayed, and expired credentials
    /// identically, and repeating its exact wording to a customer-facing screen
    /// would leak nothing useful but confuse the person reading it.
    /// </summary>
    private static async Task<string> DescribeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var code = await ReadCodeAsync(response, cancellationToken).ConfigureAwait(false);
        return code switch
        {
            "payment.credential.invalid" or "payment.credential.refused" =>
                "That code was not accepted. Ask the customer for a fresh one.",
            "payment.provision.insufficient_value" =>
                "The card does not have enough available value for this sale.",
            "payment.provision.not_confirmable" or "payment.provision.not_cancellable" =>
                "This sale was already completed or released.",
            "payment.confirmation.exceeds_hold" =>
                "The amount is higher than the hold. Release it and start again.",
            "gift_card.not_found" or "gift_card.payment.ineligible" =>
                "That card cannot be used for payment.",
            _ when response.StatusCode == HttpStatusCode.Unauthorized =>
                "This till is not authorised. Check its POS credentials.",
            _ => "The sale could not be completed. Try again, or ask for another payment method.",
        };
    }

    private static async Task<string?> ReadCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return document.RootElement.TryGetProperty("code", out var code)
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Dispose() => tokenGate.Dispose();

    private void InvalidateToken()
    {
        accessToken = null;
        accessTokenExpiresAtUtc = DateTimeOffset.MinValue;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        // Renew a minute early so a sale never starts on a token about to lapse.
        if (accessToken is not null && accessTokenExpiresAtUtc > now.AddMinutes(1))
        {
            return accessToken;
        }

        await tokenGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (accessToken is not null && accessTokenExpiresAtUtc > now.AddMinutes(1))
            {
                return accessToken;
            }

            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(
                "/api/v1/pos/auth/token",
                new
                {
                    clientCode = settings.ClientCode,
                    clientSecret = settings.ClientSecret,
                    terminalCode = settings.TerminalCode,
                },
                Json,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new PosAuthenticationException(
                    "The till could not authenticate. Check that this POS client and "
                    + "terminal are registered on the platform and that the client "
                    + "secret matches.");
            }

            var issued = await response.Content
                .ReadFromJsonAsync<PosAccessToken>(Json, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new HttpRequestException("The platform issued no device token.");

            accessToken = issued.AccessToken;
            accessTokenExpiresAtUtc = issued.ExpiresAtUtc;
            return accessToken;
        }
        finally
        {
            tokenGate.Release();
        }
    }
}

/// <summary>
/// The platform answered and refused this till, as distinct from not answering
/// at all. Kept separate so the cashier is not sent to check a working network.
/// </summary>
internal sealed class PosAuthenticationException(string message)
    : HttpRequestException(message);
