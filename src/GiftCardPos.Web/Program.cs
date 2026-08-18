using GiftCardPos.Web.Backend;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddSingleton(TimeProvider.System);

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

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

await app.RunAsync();

/// <summary>Exposed so the test host can boot the application.</summary>
public partial class Program;
