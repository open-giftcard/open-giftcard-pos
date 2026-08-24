using Microsoft.AspNetCore.DataProtection;

namespace GiftCardPos.Web.Configuration;

/// <summary>
/// Configures the till's antiforgery key ring.
///
/// A restarted till or another instance serving the same lane must be able to
/// validate a form issued before the handover. Production therefore names a
/// durable shared directory instead of accepting process-local keys.
/// </summary>
internal static class DataProtectionConfiguration
{
    internal const string ApplicationName = "GiftCardPos";

    internal static string Configure(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var keysPath = ResolveKeysPath(configuration, environment);
        Directory.CreateDirectory(keysPath);

        services
            .AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        return keysPath;
    }

    internal static string ResolveKeysPath(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var configured = configuration["DataProtection:KeysPath"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "DataProtection:KeysPath is required outside Development so " +
                    "antiforgery keys survive restarts and are shared across instances.");
            }

            configured = Path.Combine(
                environment.ContentRootPath,
                ".local",
                "dataprotection-keys");
        }

        return Path.GetFullPath(configured, environment.ContentRootPath);
    }
}
