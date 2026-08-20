using System.Text.Json;

namespace GiftCardPos.Tests;

/// <summary>
/// Holds the hand-transcribed backend calls against the pinned contract.
///
/// This till has no generated client: it writes its routes and request bodies by
/// hand. Without this, a backend rename would surface at a counter as a failed
/// sale rather than here as a failed build.
///
/// **What this does not catch, stated plainly.** The lists below are themselves
/// hand-written, so they detect the backend moving away from this client, not
/// this client falling behind the backend. The backend made an idempotency key
/// required on 2026-08-20 while this client sent none, and assertions of this
/// shape would have passed throughout: the field is present in the contract,
/// which is all they check. That gap closes only with a generated client or a
/// test that serialises the real request and compares it to the schema, and it
/// is recorded as open work rather than papered over here.
///
/// `scripts/verify-contract-pin.sh` separately asserts the document is the one
/// `contracts/README.md` claims, because a recaptured file with a stale hash
/// passes everything here happily.
/// </summary>
public sealed class BackendContractTests
{
    private static readonly JsonDocument Contract = LoadContract();

    /// <summary>Every route this application sends a request to.</summary>
    public static TheoryData<string, string> CalledRoutes() => new()
    {
        { "/api/v1/pos/auth/token", "post" },
        { "/api/v1/pos/payment-provisions", "post" },
        { "/api/v1/pos/payment-provisions/{provisionId}", "get" },
        { "/api/v1/pos/payment-provisions/{provisionId}/cancel", "post" },
        { "/api/v1/pos/payment-provisions/{provisionId}/confirm", "post" },
    };

    [Theory]
    [MemberData(nameof(CalledRoutes))]
    public void Every_route_this_till_calls_exists_in_the_pinned_contract(
        string path,
        string method)
    {
        var paths = Contract.RootElement.GetProperty("paths");

        Assert.True(
            paths.TryGetProperty(path, out var operations),
            $"The pinned contract has no path '{path}'. Either the backend renamed " +
            "it or this till is calling something that no longer exists.");
        Assert.True(
            operations.TryGetProperty(method, out _),
            $"The pinned contract has no {method.ToUpperInvariant()} on '{path}'.");
    }

    /// <summary>
    /// Fields this till puts in a provision request. A field the backend dropped
    /// or renamed is a silent failure at the till, so it is asserted here.
    /// </summary>
    [Theory]
    [InlineData("paymentToken")]
    [InlineData("paymentCode")]
    [InlineData("amount")]
    [InlineData("posTransactionReference")]
    [InlineData("idempotencyKey")]
    public void Every_field_this_till_sends_when_taking_a_hold_exists_in_the_contract(
        string field)
    {
        var properties = RequestProperties("/api/v1/pos/payment-provisions", "post");

        Assert.True(
            properties.TryGetProperty(field, out _),
            $"The provision request schema has no '{field}'. This till sends it, so " +
            "either the backend renamed it or this client is sending something the " +
            "backend will ignore.");
    }

    [Fact]
    public void The_confirmation_request_still_carries_an_amount()
    {
        // Confirming for less than was held is how a partial capture works, so
        // the amount is not optional detail.
        var properties = RequestProperties(
            "/api/v1/pos/payment-provisions/{provisionId}/confirm",
            "post");

        Assert.True(properties.TryGetProperty("amount", out _));
    }

    private static JsonElement RequestProperties(string path, string method)
    {
        var schema = Contract.RootElement
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty(method)
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");

        // The generator emits a $ref for named request records.
        if (schema.TryGetProperty("$ref", out var reference))
        {
            var name = reference.GetString()!.Split('/')[^1];
            schema = Contract.RootElement
                .GetProperty("components")
                .GetProperty("schemas")
                .GetProperty(name);
        }

        return schema.GetProperty("properties");
    }

    private static JsonDocument LoadContract()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null &&
            !File.Exists(Path.Combine(directory, "contracts", "backend.openapi.json")))
        {
            directory = Directory.GetParent(directory)?.FullName;
        }

        Assert.NotNull(directory);
        return JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(directory!, "contracts", "backend.openapi.json")));
    }
}
