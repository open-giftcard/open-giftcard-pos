using System.Security.Cryptography;
using GiftCardPos.Web.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GiftCardPos.Tests;

public sealed class DeploymentSafetyTests
{
    [Theory]
    [InlineData("https://api.example", false, true)]
    [InlineData("https://api.example", true, true)]
    [InlineData("http://127.0.0.1:5143", true, true)]
    [InlineData("http://api.example", false, false)]
    [InlineData("ftp://api.example", true, false)]
    [InlineData("not-a-url", true, false)]
    public void Backend_transport_matches_the_environment_boundary(
        string value,
        bool isDevelopment,
        bool expected)
    {
        Assert.Equal(
            expected,
            DeploymentSafety.IsBackendTransportAllowed(value, isDevelopment));
    }

    [Theory]
    [InlineData("pos.example", false, true)]
    [InlineData("localhost;127.0.0.1", false, true)]
    [InlineData("*", false, false)]
    [InlineData("*.example", false, false)]
    [InlineData("+", false, false)]
    [InlineData("", false, false)]
    [InlineData("*", true, true)]
    public void Allowed_hosts_matches_the_environment_boundary(
        string value,
        bool isDevelopment,
        bool expected)
    {
        Assert.Equal(
            expected,
            DeploymentSafety.IsAllowedHostsPolicySafe(value, isDevelopment));
    }

    [Fact]
    public void Production_host_refuses_a_plain_http_backend()
    {
        var keysPath = Path.Combine(
            Path.GetTempPath(),
            "giftcard-pos-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
            {
                webHost.UseEnvironment("Production");
                webHost.ConfigureLogging(logging => logging.ClearProviders());
                webHost.UseSetting("DataProtection:KeysPath", keysPath);
                webHost.UseSetting("AllowedHosts", "pos.example");
                webHost.UseSetting("Pos:BackendBaseUrl", "http://api.example");
                webHost.UseSetting("Pos:ClientCode", "TEST-POS");
                webHost.UseSetting("Pos:ClientSecret", Convert.ToHexString(
                    RandomNumberGenerator.GetBytes(32)));
                webHost.UseSetting("Pos:TerminalCode", "T-01");
            });

            var exception = Assert.Throws<OptionsValidationException>(
                () => _ = factory.CreateClient());

            Assert.Contains("must use HTTPS outside Development", exception.Message);
        }
        finally
        {
            if (Directory.Exists(keysPath))
            {
                Directory.Delete(keysPath, recursive: true);
            }
        }
    }

    [Fact]
    public void Production_host_refuses_wildcard_allowed_hosts()
    {
        var keysPath = Path.Combine(
            Path.GetTempPath(),
            "giftcard-pos-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
            {
                webHost.UseEnvironment("Production");
                webHost.ConfigureLogging(logging => logging.ClearProviders());
                webHost.UseSetting("DataProtection:KeysPath", keysPath);
                webHost.UseSetting("AllowedHosts", "*");
                webHost.UseSetting("Pos:BackendBaseUrl", "https://api.example");
                webHost.UseSetting("Pos:ClientCode", "TEST-POS");
                webHost.UseSetting("Pos:ClientSecret", Convert.ToHexString(
                    RandomNumberGenerator.GetBytes(32)));
                webHost.UseSetting("Pos:TerminalCode", "T-01");
            });

            var exception = Assert.Throws<InvalidOperationException>(
                () => _ = factory.Services);

            Assert.Contains("AllowedHosts must name the exact POS hosts", exception.Message);
        }
        finally
        {
            if (Directory.Exists(keysPath))
            {
                Directory.Delete(keysPath, recursive: true);
            }
        }
    }
}
