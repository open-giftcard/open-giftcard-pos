using System.Net;
using System.Security.Cryptography;
using System.Text;
using GiftCardPos.Web.Backend;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GiftCardPos.Web.LocalApi;

/// <summary>
/// The integration surface an existing till calls.
///
/// This is the shape retail already understands: the till owns the sale and
/// sends an amount, a separate component owns the payment and answers with a
/// result. It is the same separation the nexo Retailer protocol describes and
/// the same request/response shape a local Terminal API integration uses, which
/// is why it needs no library on the till's side. Any language with an HTTP
/// client can integrate.
///
/// This variant expects the till to supply the credential, because a till with
/// its own scanner already has it. The variant where the cashier scans on this
/// application's screen while the till waits needs a pending-sale state machine
/// and is deliberately not built yet.
///
/// One call takes a payment: it holds the amount, then confirms what was
/// approved. Partial approval is requested on the till's behalf, so a card that
/// cannot cover the sale answers with what it did cover and what is still owed,
/// rather than refusing.
/// </summary>
internal static class LocalSaleApi
{
    public const string RouteBase = "/local/v1";

    public static IEndpointRouteBuilder MapLocalSaleApi(this IEndpointRouteBuilder app)
    {
        app.MapPost($"{RouteBase}/sale/payment", TakePaymentAsync)
            .WithName("LocalTakePayment")
            .ExcludeFromDescription();

        return app;
    }

    private static async Task<IResult> TakePaymentAsync(
        [FromBody] LocalPaymentRequest request,
        HttpContext context,
        PosApiClient api,
        IOptions<PosOptions> options,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";

        var settings = options.Value;
        if (!IsLoopback(context))
        {
            // A lane on the network is not this lane. Anything reaching here
            // from elsewhere is another machine claiming to be the till.
            return Results.Json(
                new LocalPaymentResult("declined", "not_local", null, null, null, null),
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (!IsAuthorised(context, settings))
        {
            // Loopback is not authentication: every process on this machine can
            // reach it, so malware on the till could otherwise drain presented
            // cards.
            return Results.Json(
                new LocalPaymentResult("declined", "unauthorised", null, null, null, null),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (request is null ||
            string.IsNullOrWhiteSpace(request.Credential) ||
            string.IsNullOrWhiteSpace(request.SaleReference) ||
            request.Amount is not > 0m ||
            decimal.Round(request.Amount, 4) != request.Amount)
        {
            return Results.Json(
                new LocalPaymentResult("declined", "invalid_request", null, null, null, null),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var held = await api.CreateProvisionAsync(
            request.Credential,
            request.Amount,
            request.SaleReference.Trim(),
            cancellationToken).ConfigureAwait(false);

        if (!held.Ok)
        {
            return Results.Json(
                new LocalPaymentResult("declined", held.Error, null, null, null, null));
        }

        var provision = held.Value!;

        // Confirm exactly what was approved. The held amount is the ceiling, so
        // this can never charge more than the card agreed to.
        var confirmed = await api
            .ConfirmAsync(provision.Id, provision.Amount, cancellationToken)
            .ConfigureAwait(false);

        if (!confirmed.Ok)
        {
            // The hold exists and the charge did not complete. Indeterminate is
            // not declined: a till told "declined" here would take the whole
            // amount by another tender while the customer's value is still held.
            return Results.Json(
                new LocalPaymentResult(
                    "indeterminate",
                    confirmed.Error,
                    0m,
                    request.Amount,
                    provision.Currency,
                    provision.Id));
        }

        var settled = confirmed.Value!;
        var approved = settled.ConfirmedAmount ?? settled.Amount;
        return Results.Ok(
            new LocalPaymentResult(
                "approved",
                null,
                approved,
                request.Amount - approved,
                settled.Currency,
                settled.Id));
    }

    private static bool IsLoopback(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        return address is null || IPAddress.IsLoopback(address);
    }

    private static bool IsAuthorised(HttpContext context, PosOptions settings)
    {
        var expected = settings.LocalApiKey;
        if (string.IsNullOrWhiteSpace(expected))
        {
            // Fail closed. An unset key disables the surface rather than opening
            // it, so a shop that never configured one cannot be integrated
            // against by anything else running on the machine.
            return false;
        }

        if (!context.Request.Headers.TryGetValue("X-Pos-Local-Key", out var presented))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented.ToString()),
            Encoding.UTF8.GetBytes(expected));
    }
}

/// <param name="Amount">What the till still needs to collect for this sale.</param>
/// <param name="SaleReference">
/// The sale's identity in the till. Doubles as the idempotency key, so repeating
/// a request whose response was lost returns the payment already taken instead of
/// taking a second one.
/// </param>
/// <param name="Credential">The scanned QR value or the 12-digit code.</param>
public sealed record LocalPaymentRequest(
    decimal Amount,
    string? SaleReference,
    string? Credential);

/// <param name="Outcome">
/// <c>approved</c>, <c>declined</c>, or <c>indeterminate</c>. A till that treats
/// indeterminate as declined will double-charge customers: it means the platform
/// may have taken the payment and did not say so before the call ended.
/// </param>
/// <param name="OutstandingAmount">
/// Still owed on this sale after the card paid. Above zero on a partial
/// approval, which is the ordinary case for a gift card.
/// </param>
public sealed record LocalPaymentResult(
    string Outcome,
    string? Reason,
    decimal? ApprovedAmount,
    decimal? OutstandingAmount,
    string? Currency,
    Guid? PaymentReference);
