using System.Net;
using GiftCardPos.Web.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GiftCardPos.Tests;

public sealed class DataProtectionConfigurationTests
{
    private const string AntiforgeryField = "__RequestVerificationToken";

    [Fact]
    public void Non_development_host_refuses_to_start_without_a_durable_key_path()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
        {
            webHost.UseEnvironment("Production");
            webHost.ConfigureLogging(logging => logging.ClearProviders());
            webHost.UseSetting("Pos:BackendBaseUrl", "http://192.0.2.1:5143");
            webHost.UseSetting("Pos:ClientCode", "TILL-STARTUP-TEST");
            webHost.UseSetting("Pos:ClientSecret", "not-a-real-secret");
            webHost.UseSetting("Pos:TerminalCode", "T-STARTUP-TEST");
        });

        var exception = Assert.Throws<InvalidOperationException>(() => _ = factory.Services);

        Assert.Contains("DataProtection:KeysPath is required", exception.Message);
    }

    [Fact]
    public void Non_development_requires_a_durable_key_path()
    {
        using var directory = new TemporaryDirectory();
        var configuration = new ConfigurationBuilder().Build();
        var environment = new TestHostEnvironment("Production", directory.Path);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DataProtectionConfiguration.ResolveKeysPath(configuration, environment));

        Assert.Contains("DataProtection:KeysPath is required", exception.Message);
    }

    [Fact]
    public void Development_uses_a_repository_local_key_ring_by_default()
    {
        using var directory = new TemporaryDirectory();
        var configuration = new ConfigurationBuilder().Build();
        var environment = new TestHostEnvironment("Development", directory.Path);

        var resolved = DataProtectionConfiguration.ResolveKeysPath(
            configuration,
            environment);

        Assert.Equal(
            System.IO.Path.Combine(directory.Path, ".local", "dataprotection-keys"),
            resolved);
    }

    [Fact]
    public async Task Antiforgery_token_survives_a_till_restart()
    {
        using var directory = new TemporaryDirectory();
        var keysPath = System.IO.Path.Combine(directory.Path, "shared-keys");

        string cookie;
        string token;
        using (var firstHost = new DurablePosAppFactory(keysPath))
        using (var firstClient = firstHost.CreateClient(ClientOptions()))
        using (var page = await firstClient.GetAsync(new Uri("/", UriKind.Relative)))
        {
            page.EnsureSuccessStatusCode();
            cookie = AntiforgeryCookie(page);
            token = AntiforgeryToken(await page.Content.ReadAsStringAsync());
        }

        using var restartedHost = new DurablePosAppFactory(keysPath);
        using var restartedClient = restartedHost.CreateClient(ClientOptions());
        restartedClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookie);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [AntiforgeryField] = token,
            ["Amount"] = string.Empty,
            ["SaleReference"] = "SALE-RESTART",
            ["Credential"] = string.Empty,
        });

        using var response = await restartedClient.PostAsync(
            new Uri("/", UriKind.Relative),
            form);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Enter the amount to take", body, StringComparison.Ordinal);
        Assert.NotEmpty(Directory.GetFiles(keysPath, "*.xml"));
    }

    private static WebApplicationFactoryClientOptions ClientOptions() => new()
    {
        AllowAutoRedirect = false,
        HandleCookies = false,
    };

    private static string AntiforgeryCookie(HttpResponseMessage response)
    {
        var setCookie = response.Headers
            .GetValues("Set-Cookie")
            .Single(value => value.StartsWith(
                ".AspNetCore.Antiforgery.",
                StringComparison.Ordinal));
        return setCookie.Split(';', 2)[0];
    }

    private static string AntiforgeryToken(string html)
    {
        const string marker = "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "The till form did not render an antiforgery token.");
        start += marker.Length;
        var end = html.IndexOf('"', start);
        Assert.True(end > start, "The till form rendered an empty antiforgery token.");
        return WebUtility.HtmlDecode(html[start..end]);
    }

    private sealed class DurablePosAppFactory(string keysPath) : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.UseEnvironment("Production");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureHostConfiguration(configuration =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DataProtection:KeysPath"] = keysPath,
                    ["AllowedHosts"] = "localhost",
                    ["Pos:BackendBaseUrl"] = "https://api.example",
                    ["Pos:ClientCode"] = "TILL-RESTART-TEST",
                    ["Pos:ClientSecret"] = "not-a-real-secret",
                    ["Pos:TerminalCode"] = "T-RESTART-TEST",
                }));

            return base.CreateHost(builder);
        }
    }

    private sealed class TestHostEnvironment(
        string environmentName,
        string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "GiftCardPos.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "giftcard-pos-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
