using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GiftCardPos.Tests;

/// <summary>
/// The handover endpoints, over HTTP.
///
/// Each test builds its own application rather than sharing one. The lane holds
/// state deliberately, so a shared host would make these tests depend on the
/// order they happen to run in, which is exactly the kind of green-for-the-wrong-
/// reason result this project keeps finding.
/// </summary>
public sealed class LocalHandoverApiTests : IDisposable
{
    private const string Base = "/local/v1/sale";
    private readonly PosAppFactory factory = new();

    public void Dispose() => factory.Dispose();

    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Pos-Local-Key", PosAppFactory.LocalKey);
        return client;
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Starting_a_sale_hands_it_to_the_cashier()
    {
        using var client = Client();

        using var response = await client.PostAsJsonAsync(
            $"{Base}/start",
            new { amount = 12.50m, saleReference = "SALE-1" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal("awaiting-card", body.GetProperty("outcome").GetString());
        Assert.Equal(12.50m, body.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Starting_without_the_shared_key_is_refused()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"{Base}/start",
            new { amount = 12.50m, saleReference = "SALE-1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Repeating_a_start_returns_the_same_sale()
    {
        using var client = Client();

        using var first = await client.PostAsJsonAsync(
            $"{Base}/start", new { amount = 12.50m, saleReference = "SALE-1" });
        using var again = await client.PostAsJsonAsync(
            $"{Base}/start", new { amount = 12.50m, saleReference = "SALE-1" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(
            (await BodyAsync(first)).GetProperty("saleId").GetGuid(),
            (await BodyAsync(again)).GetProperty("saleId").GetGuid());
    }

    [Fact]
    public async Task A_second_sale_is_refused_while_the_lane_is_busy()
    {
        using var client = Client();
        using var first = await client.PostAsJsonAsync(
            $"{Base}/start", new { amount = 12.50m, saleReference = "SALE-1" });
        first.EnsureSuccessStatusCode();

        using var second = await client.PostAsJsonAsync(
            $"{Base}/start", new { amount = 5m, saleReference = "SALE-2" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("lane_busy", (await BodyAsync(second)).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task A_till_can_ask_what_happened_to_its_sale()
    {
        using var client = Client();
        using var started = await client.PostAsJsonAsync(
            $"{Base}/start", new { amount = 12.50m, saleReference = "SALE-1" });
        var saleId = (await BodyAsync(started)).GetProperty("saleId").GetGuid();

        using var polled = await client.GetAsync(new Uri($"{Base}/{saleId}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, polled.StatusCode);
        Assert.Equal("awaiting-card", (await BodyAsync(polled)).GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task An_unknown_sale_is_not_found()
    {
        using var client = Client();

        using var response = await client.GetAsync(
            new Uri($"{Base}/{Guid.NewGuid()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_till_can_take_its_sale_back()
    {
        using var client = Client();
        using var started = await client.PostAsJsonAsync(
            $"{Base}/start", new { amount = 12.50m, saleReference = "SALE-1" });
        var saleId = (await BodyAsync(started)).GetProperty("saleId").GetGuid();

        using var cancelled = await client.PostAsync(
            new Uri($"{Base}/{saleId}/cancel", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("cancelled", (await BodyAsync(cancelled)).GetProperty("outcome").GetString());

        // And the lane is free again.
        using var next = await client.PostAsJsonAsync(
            $"{Base}/start", new { amount = 5m, saleReference = "SALE-2" });
        Assert.Equal(HttpStatusCode.Created, next.StatusCode);
    }

    [Fact]
    public async Task Cancelling_a_finished_sale_does_not_report_success()
    {
        // A cancel racing a cashier who has already taken the payment must not
        // tell the till the sale was called off.
        using var client = Client();
        using var started = await client.PostAsJsonAsync(
            $"{Base}/start", new { amount = 12.50m, saleReference = "SALE-1" });
        var saleId = (await BodyAsync(started)).GetProperty("saleId").GetGuid();
        using var first = await client.PostAsync(
            new Uri($"{Base}/{saleId}/cancel", UriKind.Relative), null);
        first.EnsureSuccessStatusCode();

        using var again = await client.PostAsync(
            new Uri($"{Base}/{saleId}/cancel", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task An_amount_that_is_not_payable_is_refused(double amount)
    {
        using var client = Client();

        using var response = await client.PostAsJsonAsync(
            $"{Base}/start", new { amount, saleReference = "SALE-1" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
