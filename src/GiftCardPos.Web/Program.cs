using GiftCardPos.Web.LocalApi;
using GiftCardPos.Web.Security;
using GiftCardPos.Web.Backend;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<GiftCardPos.Web.LocalApi.PendingSaleStore>();
builder.Services.AddSingleton<GiftCardPos.Web.LocalApi.SalePayments>();

if (builder.Environment.IsDevelopment())
{
    // Windows Event Log may be unavailable to an unprivileged local process.
    // A logger failure must never prevent antiforgery key creation or abort a sale.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();

    var keyDirectory = Path.Combine(
        Path.GetTempPath(),
        "giftcard-pos",
        "dataprotection-keys");
    Directory.CreateDirectory(keyDirectory);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
        .SetApplicationName("GiftCardPos");
}

builder.Services.AddOptions<PosOptions>()
    .BindConfiguration(PosOptions.SectionName)
    .Validate(
        options => Uri.TryCreate(options.BackendBaseUrl, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https",
        "Pos:BackendBaseUrl must be an absolute HTTP or HTTPS URL.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.ClientCode)
            && !string.IsNullOrWhiteSpace(options.TerminalCode),
        "Pos:ClientCode and Pos:TerminalCode are required.")
    .Validate(
        // Fail at startup rather than at the first customer. A till that cannot
        // authenticate is broken, and finding out mid-sale is the worst time.
        options => !string.IsNullOrWhiteSpace(options.ClientSecret),
        "Pos:ClientSecret is required. Supply it through user secrets or the environment; never commit it.")
    .ValidateOnStart();

// A named client plus a singleton, deliberately not AddHttpClient<PosApiClient>:
// a typed client is transient, so the device-token cache would be discarded
// after every call and the till would re-authenticate on each request.
builder.Services.AddSingleton<PosApiClient>();
builder.Services.AddHttpClient(PosApiClient.HttpClientName, (serviceProvider, client) =>
{
    var options = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<PosOptions>>().Value;
    client.BaseAddress = new Uri(options.BackendBaseUrl);
    // A counter cannot wait. Failing fast keeps the queue moving, and the hold
    // releases itself if the platform did in fact take the request.
    client.Timeout = TimeSpan.FromSeconds(15);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.MapLocalSaleApi();

// Liveness: this process is up. Deliberately touches nothing external, so a
// backend outage does not cause an orchestrator to kill a healthy till.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Readiness: this till can actually take a payment, which it cannot do without
// the backend. Reporting ready while the platform is unreachable would let a
// cashier scan a customer's card and fail halfway through the sale.
//
// This deliberately cascades. A till has exactly one dependency and no local
// authority of its own: there is no useful sense in which it is ready while the
// platform is not. The probe is cheap, short, and asks the backend the same
// question, so a till never claims more confidence than the platform behind it.
app.MapGet("/health/ready", async (
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    using var probe = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken,
        probe.Token);
    try
    {
        var client = httpClientFactory.CreateClient(PosApiClient.HttpClientName);
        using var response = await client
            .GetAsync(new Uri("/health/ready", UriKind.Relative), linked.Token)
            .ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? Results.Ok(new { status = "ready" })
            : Results.Json(
                new { status = "backend-unavailable" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception exception) when (
        exception is HttpRequestException or TaskCanceledException or OperationCanceledException)
    {
        // The reason is not echoed: this endpoint is unauthenticated and the
        // message can carry the backend's host.
        return Results.Json(
            new { status = "backend-unavailable" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

await app.RunAsync();

/// <summary>Exposed so the test host can boot the application.</summary>
public partial class Program;
