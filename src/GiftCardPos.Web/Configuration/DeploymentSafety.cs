namespace GiftCardPos.Web.Configuration;

internal static class DeploymentSafety
{
    internal static bool IsBackendTransportAllowed(
        string? backendBaseUrl,
        bool isDevelopment)
    {
        if (!Uri.TryCreate(backendBaseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps ||
            (isDevelopment && uri.Scheme == Uri.UriSchemeHttp);
    }

    internal static bool IsAllowedHostsPolicySafe(
        string? allowedHosts,
        bool isDevelopment)
    {
        if (isDevelopment)
        {
            return true;
        }

        var hosts = (allowedHosts ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return hosts.Length > 0 && hosts.All(host =>
            !host.Contains('*', StringComparison.Ordinal) &&
            !host.Equals("+", StringComparison.Ordinal));
    }
}
