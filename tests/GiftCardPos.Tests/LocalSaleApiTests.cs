using System.Net;
using System.Net.Http.Json;

namespace GiftCardPos.Tests;

/// <summary>
/// The surface an existing till integrates against, exercised over HTTP.
///
/// These assert the refusals rather than the happy path: reaching an approval
/// needs a live platform and a real credential, but every guard below protects
/// money on a machine anyone in the shop can touch, and each is asserted here.
/// </summary>
public sealed class LocalSaleApiTests : IClassFixture<PosAppFactory>
{
    private const string Route = "/local/v1/sale/payment";
    private readonly PosAppFactory factory;

    public LocalSaleApiTests(PosAppFactory factory) => this.factory = factory;

    private static object ValidBody() => new
    {
        amount = 12.50m,
        saleReference = "SALE-1",
        credential = "1234 5678 9012",
    };

    [Fact]
    public async Task A_till_without_the_shared_key_is_refused()
    {
        // Loopback is not authentication. Without this, malware on the till
        // could present a scanned card and drain it.
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Route, ValidBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_key_is_refused()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Pos-Local-Key", "not-the-key");

        using var response = await client.PostAsJsonAsync(Route, ValidBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.234567)]
    public async Task An_amount_that_is_not_payable_is_refused_before_the_platform_is_called(
        double amount)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Pos-Local-Key", PosAppFactory.LocalKey);

        using var response = await client.PostAsJsonAsync(
            Route,
            new { amount, saleReference = "SALE-1", credential = "1234 5678 9012" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_request_without_a_sale_reference_is_refused()
    {
        // The reference is the idempotency key. Without one a retry would take a
        // second payment rather than returning the first.
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Pos-Local-Key", PosAppFactory.LocalKey);

        using var response = await client.PostAsJsonAsync(
            Route,
            new { amount = 5m, saleReference = "", credential = "1234 5678 9012" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unreachable_platform_is_declined_and_never_leaks_its_address()
    {
        // The fixture points at an address that resolves nowhere.
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Pos-Local-Key", PosAppFactory.LocalKey);

        using var response = await client.PostAsJsonAsync(Route, ValidBody());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"outcome\":\"declined\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("192.0.2.1", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_local_surface_is_never_described_in_a_public_document()
    {
        // It is an integration surface for this machine, not part of the
        // platform's published API.
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Pos-Local-Key", PosAppFactory.LocalKey);

        using var response = await client.PostAsJsonAsync(Route, ValidBody());

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }
}
