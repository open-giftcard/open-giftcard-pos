using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GiftCardPos.Tests;

/// <summary>
/// The response headers and the two probes, asserted by making real requests
/// rather than by reading the middleware. A header that is configured but never
/// reaches a response protects nobody.
/// </summary>
public sealed class SecurityAndHealthTests : IClassFixture<PosAppFactory>
{
    private readonly PosAppFactory factory;

    public SecurityAndHealthTests(PosAppFactory factory) => this.factory = factory;

    [Theory]
    [InlineData("Content-Security-Policy")]
    [InlineData("X-Content-Type-Options")]
    [InlineData("Referrer-Policy")]
    [InlineData("X-Frame-Options")]
    [InlineData("Cross-Origin-Opener-Policy")]
    [InlineData("Permissions-Policy")]
    public async Task Every_response_carries_the_security_headers(string header)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.True(
            response.Headers.Contains(header) ||
            response.Content.Headers.Contains(header),
            $"No {header} on the response.");
    }

    [Fact]
    public async Task Script_is_forbidden_outright_because_this_till_ships_none()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative));
        var policy = string.Join(
            ' ',
            response.Headers.GetValues("Content-Security-Policy"));

        Assert.Contains("script-src 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", policy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Liveness_does_not_depend_on_the_backend()
    {
        // The backend address points nowhere in this fixture. Liveness must
        // still answer, or an orchestrator would kill a till that is merely
        // waiting for the platform to come back.
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_refuses_when_the_backend_cannot_be_reached()
    {
        // A till that cannot reach the platform cannot take a payment, and
        // saying otherwise lets a cashier scan a card and fail mid-sale.
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("backend-unavailable", body, StringComparison.Ordinal);

        // The backend's host must not leak through an unauthenticated endpoint.
        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", body, StringComparison.Ordinal);
    }
}

/// <summary>
/// Hosts the till with a backend address that resolves to nothing, which is the
/// state these tests are about.
/// </summary>
public sealed class PosAppFactory : WebApplicationFactory<Program>
{
    public const string LocalKey = "local-integration-test-key";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");
        builder.ConfigureHostConfiguration(configuration =>
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    // Reserved by RFC 5737 for documentation, so it cannot
                    // accidentally reach a real host.
                    ["Pos:BackendBaseUrl"] = "http://192.0.2.1:5143",

                    // Startup validation requires these. They are deliberately
                    // obvious fakes: nothing here authenticates anywhere.
                    ["Pos:ClientCode"] = "TILL-TEST",
                    ["Pos:ClientSecret"] = "not-a-real-secret",
                    ["Pos:TerminalCode"] = "T-TEST",
                    ["Pos:LocalApiKey"] = LocalKey,
                }));

        return base.CreateHost(builder);
    }
}
