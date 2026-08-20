// Calling the till's local API from C#.
//
// Copy this into your own codebase and change it. It is an example, not a
// library, and this repository neither compiles nor ships it.
//
// Nothing here is specific to this platform's stack: it is HttpClient and
// System.Text.Json, so it drops into a .NET till as it stands.

using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Example;

/// <summary>
/// The platform may have taken the payment and did not say so.
///
/// Its own exception type on purpose. The most damaging mistake a till can make
/// is treating this as a decline and collecting the whole amount again, so it
/// must not be caught by accident alongside an ordinary refusal.
/// </summary>
public sealed class PaymentIndeterminateException(Guid? paymentReference)
    : Exception($"The payment may have been taken. Check {paymentReference} before charging again.")
{
    public Guid? PaymentReference { get; } = paymentReference;
}

public sealed record Payment(
    decimal Approved,
    decimal Outstanding,
    string Currency,
    Guid? Reference)
{
    public bool FullyPaid => Outstanding <= 0m;
}

public sealed class Till(string localApiKey, Uri? baseAddress = null) : IDisposable
{
    private const string Payment = "/local/v1/sale/payment";
    private const string Start = "/local/v1/sale/start";
    private const string Sale = "/local/v1/sale/";

    private readonly HttpClient http = new()
    {
        BaseAddress = baseAddress ?? new Uri("http://127.0.0.1:5190"),
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "X-Pos-Local-Key", localApiKey } },
    };

    public void Dispose() => http.Dispose();

    /// <summary>
    /// Your till has the scanner.
    ///
    /// Retry a failed call with the <em>same</em> sale reference. It is the
    /// idempotency key: the same one returns the payment already taken, a new
    /// one takes a second payment.
    /// </summary>
    public async Task<Payment> TakePaymentAsync(
        decimal amount,
        string saleReference,
        string credential,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(
            Payment,
            new { amount, saleReference, credential },
            cancellationToken);

        // A refusal is data, not a crash: declined is an ordinary outcome at a
        // counter, so the body is read either way.
        var result = await response.Content.ReadFromJsonAsync<SaleResult>(cancellationToken);
        return Interpret(result);
    }

    /// <summary>
    /// This machine has the scanner. The cashier presents the card on the till's
    /// own screen, so this asks rather than holding a request open across
    /// however long a person takes.
    /// </summary>
    public async Task<Payment> HandOverAsync(
        decimal amount,
        string saleReference,
        TimeSpan? pollEvery = null,
        TimeSpan? giveUpAfter = null,
        CancellationToken cancellationToken = default)
    {
        var interval = pollEvery ?? TimeSpan.FromSeconds(2);
        var deadline = DateTimeOffset.UtcNow.Add(giveUpAfter ?? TimeSpan.FromMinutes(5));

        using var startResponse = await http.PostAsJsonAsync(
            Start,
            new { amount, saleReference },
            cancellationToken);
        var status = await startResponse.Content.ReadFromJsonAsync<SaleResult>(cancellationToken);
        if (status?.SaleId is not { } saleId)
        {
            throw new InvalidOperationException($"Sale was not accepted: {status?.Reason}");
        }

        while (status.Outcome == "awaiting-card")
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                // Take the sale back rather than leaving it on the screen.
                using var _ = await http.PostAsync(
                    $"{Sale}{saleId}/cancel", null, cancellationToken);
                throw new TimeoutException("No card was presented.");
            }

            await Task.Delay(interval, cancellationToken);
            status = await http.GetFromJsonAsync<SaleResult>(
                $"{Sale}{saleId}", cancellationToken);
        }

        return Interpret(status);
    }

    private static Payment Interpret(SaleResult? result)
    {
        if (result is null)
        {
            throw new InvalidOperationException("The till returned nothing.");
        }

        if (result.Outcome == "indeterminate")
        {
            throw new PaymentIndeterminateException(result.PaymentReference);
        }

        if (result.Outcome != "approved")
        {
            throw new InvalidOperationException(result.Reason ?? result.Outcome);
        }

        return new Payment(
            result.ApprovedAmount ?? 0m,
            result.OutstandingAmount ?? 0m,
            result.Currency ?? string.Empty,
            result.PaymentReference);
    }

    private sealed record SaleResult(
        [property: JsonPropertyName("saleId")] Guid? SaleId,
        [property: JsonPropertyName("outcome")] string Outcome,
        [property: JsonPropertyName("reason")] string? Reason,
        [property: JsonPropertyName("approvedAmount")] decimal? ApprovedAmount,
        [property: JsonPropertyName("outstandingAmount")] decimal? OutstandingAmount,
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("paymentReference")] Guid? PaymentReference);
}

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var till = new Till(
            Environment.GetEnvironmentVariable("POS_LOCAL_KEY")
            ?? throw new InvalidOperationException("Set POS_LOCAL_KEY."));

        try
        {
            var payment = args.Length > 0
                ? await till.TakePaymentAsync(12.50m, "SALE-1234", args[0])
                : await till.HandOverAsync(12.50m, "SALE-1234");

            Console.WriteLine($"Took {payment.Approved:0.00} {payment.Currency}");
            if (!payment.FullyPaid)
            {
                // Normal for a gift card: collect the rest by another tender.
                Console.WriteLine(
                    $"Still to pay: {payment.Outstanding:0.00} {payment.Currency}");
            }

            return 0;
        }
        catch (PaymentIndeterminateException uncertain)
        {
            // Do not charge again here. Somebody has to look.
            Console.Error.WriteLine($"UNCERTAIN: {uncertain.Message}");
            return 2;
        }
        catch (InvalidOperationException refused)
        {
            Console.Error.WriteLine($"Declined: {refused.Message}");
            return 1;
        }
    }
}
