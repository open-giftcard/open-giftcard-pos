namespace GiftCardPos.Web.Security;

/// <summary>
/// Applies conservative response security headers.
///
/// This till renders server-side HTML and ships no JavaScript, so
/// <c>script-src 'none'</c> is a statement of fact rather than an aspiration:
/// there is nothing to allow, and anything that appears claiming to be script
/// did not come from here.
///
/// <c>Referrer-Policy: no-referrer</c> matters at a counter. A payment credential
/// can be typed into this application, and a referrer header is the easiest way
/// for a value that was only ever meant for one request to leave on another.
///
/// Framing is denied outright. A till screen embedded in another page is either
/// a mistake or an attempt to put a fake total in front of a cashier.
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'none'; " +
        "style-src 'self'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "form-action 'self'; " +
        "frame-src 'none'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'none'; " +
        "object-src 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var headers = context.Response.Headers;
        headers["Content-Security-Policy"] = ContentSecurityPolicy;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Frame-Options"] = "DENY";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Permissions-Policy"] =
            "geolocation=(), camera=(), microphone=(), payment=()";

        // A till screen shows one sale in progress. Restoring it from a shared
        // proxy or the back-forward cache would put a previous customer's total,
        // and the state of their payment, in front of the next one.
        headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        headers["Pragma"] = "no-cache";

        return next(context);
    }
}
