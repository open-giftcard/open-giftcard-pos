using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GiftCardPos.Web.Backend;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GiftCardPos.Tests;

/// <summary>
/// Checks the bytes this till actually puts on the wire against the pinned
/// contract, rather than a hand-written list of what it is believed to send.
///
/// <para>
/// <see cref="BackendContractTests"/> asserts that every route and field named
/// in its own <c>InlineData</c> exists in the contract. Those lists are written
/// by hand, so they detect the backend moving away from this till but not this
/// till falling behind the backend. The backend made an idempotency key
/// required on 2026-08-20 while this client sent none, and assertions of that
/// shape passed throughout.
/// </para>
///
/// <para>
/// These tests drive <see cref="PosApiClient"/> through a capturing handler and
/// read the request body it produced. Nothing is transcribed, so a field this
/// till stops sending, or starts sending under a new name, changes the captured
/// body and fails here.
/// </para>
///
/// <para>
/// The required-field check is deliberately written to be driven by the
/// contract rather than by a list kept here. It was vacuous when written,
/// because the served document declared no <c>required</c> on any object
/// schema. That is no longer true: the pinned contract now declares required
/// fields on 15 schemas, and <c>CreatePaymentProvisionRequest</c> requires
/// <c>idempotencyKey</c>, which is the exact field whose absence caused the
/// live defect described above. The check enforces rather than decorates, and
/// it did so without any change here, which was the point of driving it from
/// the document.
/// </para>
/// </summary>
public sealed class SerialisedRequestContractTests
{
    private static readonly JsonDocument Contract = LoadContract();

    [Fact]
    public async Task Taking_a_hold_sends_only_fields_the_contract_declares()
    {
        var body = await CaptureAsync(client => client.CreateProvisionAsync(
            "123456789012",
            12.34m,
            "SALE-1",
            CancellationToken.None));

        AssertBodyMatchesSchema(body, "/api/v1/pos/payment-provisions", "post");
    }

    [Fact]
    public async Task Confirming_a_hold_sends_only_fields_the_contract_declares()
    {
        var body = await CaptureAsync(client => client.ConfirmAsync(
            Guid.NewGuid(),
            12.34m,
            CancellationToken.None));

        AssertBodyMatchesSchema(
            body,
            "/api/v1/pos/payment-provisions/{provisionId}/confirm",
            "post");
    }

    /// <summary>
    /// The idempotency key is the field whose absence caused a live defect, so
    /// it is asserted on the captured body directly. A hold retried for the same
    /// sale must reuse the sale reference; a value generated per call would be
    /// treated as a replay of a spent credential.
    /// </summary>
    [Fact]
    public async Task Taking_a_hold_sends_the_sale_reference_as_the_idempotency_key()
    {
        var body = await CaptureAsync(client => client.CreateProvisionAsync(
            "123456789012",
            12.34m,
            "SALE-42",
            CancellationToken.None));

        Assert.True(
            body.TryGetProperty("idempotencyKey", out var key),
            "This till sent no idempotency key. A retried sale will be refused as a replay.");
        Assert.Equal("SALE-42", key.GetString());
    }

    private static void AssertBodyMatchesSchema(JsonElement body, string path, string method)
    {
        var schema = RequestSchema(path, method);
        var declared = schema.TryGetProperty("properties", out var properties)
            ? properties.EnumerateObject().Select(item => item.Name).ToHashSet(StringComparer.Ordinal)
            : [];

        foreach (var sent in body.EnumerateObject())
        {
            Assert.True(
                declared.Contains(sent.Name),
                $"This till sends '{sent.Name}' to {method.ToUpperInvariant()} {path}, " +
                "which the pinned contract does not declare.");
        }

        if (!schema.TryGetProperty("required", out var required))
        {
            // Nothing to enforce until the backend declares required fields.
            return;
        }

        foreach (var name in required.EnumerateArray().Select(item => item.GetString()))
        {
            Assert.True(
                body.TryGetProperty(name!, out _),
                $"The contract requires '{name}' on {method.ToUpperInvariant()} {path}, " +
                "and this till does not send it.");
        }
    }

    private static JsonElement RequestSchema(string path, string method)
    {
        var schema = Contract.RootElement
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty(method)
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");

        if (!schema.TryGetProperty("$ref", out var reference))
        {
            return schema;
        }

        var name = reference.GetString()!.Split('/')[^1];
        return Contract.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(name);
    }

    private static async Task<JsonElement> CaptureAsync(Func<PosApiClient, Task> call)
    {
        using var handler = new CapturingHandler();
        using var client = new PosApiClient(
            new SingleClientFactory(handler),
            Options.Create(new PosOptions
            {
                BackendBaseUrl = "https://platform.example",
                ClientCode = "TILL-1",
                ClientSecret = "secret",
                TerminalCode = "LANE-1",
            }),
            TimeProvider.System,
            NullLogger<PosApiClient>.Instance);

        await call(client);

        Assert.NotNull(handler.LastBody);
        return JsonDocument.Parse(handler.LastBody!).RootElement.Clone();
    }

    private static JsonDocument LoadContract()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "contracts", "backend.openapi.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(directory!.FullName, "contracts", "backend.openapi.json")));
    }

    /// <summary>
    /// The client disposes what the factory hands it after every call, which is
    /// correct against a real <see cref="IHttpClientFactory"/> because handlers
    /// are pooled behind it. So each call gets a fresh client over one shared
    /// handler, and the handler outlives them.
    /// </summary>
    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://platform.example"),
            };
    }

    /// <summary>
    /// Answers the token request so the client reaches the call under test, and
    /// keeps the body of every other request it makes.
    /// </summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/pos/auth/token", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        accessToken = "token",
                        expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15),
                        posClientId = Guid.NewGuid(),
                        posTerminalId = Guid.NewGuid(),
                        storeReference = "STORE-1",
                    }),
                };
            }

            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    id = Guid.NewGuid(),
                    state = "Pending",
                    amount = 12.34m,
                    requestedAmount = 12.34m,
                    outstandingAmount = 0m,
                }),
            };
        }
    }
}
