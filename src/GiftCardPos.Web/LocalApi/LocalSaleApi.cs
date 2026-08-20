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

        // The cashier-in-the-loop shape: the till hands the sale over and asks
        // about it, rather than holding a connection open while a person acts.
        app.MapPost($"{RouteBase}/sale/start", StartSaleAsync)
            .WithName("LocalStartSale")
            .ExcludeFromDescription();

        app.MapGet($"{RouteBase}/sale/{{saleId:guid}}", GetSaleAsync)
            .WithName("LocalGetSale")
            .ExcludeFromDescription();

        app.MapPost($"{RouteBase}/sale/{{saleId:guid}}/cancel", CancelSaleAsync)
            .WithName("LocalCancelSale")
            .ExcludeFromDescription();

        return app;
    }

    private static async Task<IResult> TakePaymentAsync(
        [FromBody] LocalPaymentRequest request,
        HttpContext context,
        SalePayments payments,
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

        var outcome = await payments.TakeAsync(
            request.Credential,
            request.Amount,
            request.SaleReference.Trim(),
            cancellationToken).ConfigureAwait(false);

        var result = new LocalPaymentResult(
            outcome.Outcome switch
            {
                PendingSaleState.Approved => "approved",
                PendingSaleState.Indeterminate => "indeterminate",
                _ => "declined",
            },
            outcome.Reason,
            outcome.ApprovedAmount,
            outcome.OutstandingAmount,
            outcome.Currency,
            outcome.PaymentReference);

        return Results.Json(result);
    }

    private static async Task<IResult> StartSaleAsync(
        [FromBody] LocalStartSaleRequest request,
        HttpContext context,
        PendingSaleStore sales,
        IOptions<PosOptions> options,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        context.Response.Headers.CacheControl = "no-store";
        var settings = options.Value;

        if (Refuse(context, settings) is { } refusal)
        {
            return refusal;
        }

        if (request is null ||
            string.IsNullOrWhiteSpace(request.SaleReference) ||
            request.Amount is not > 0m ||
            decimal.Round(request.Amount, 4) != request.Amount)
        {
            return Results.Json(
                new { outcome = "declined", reason = "invalid_request" },
                statusCode: StatusCodes.Status400BadRequest);
        }

        var started = sales.Start(
            request.SaleReference.Trim(),
            request.Amount,
            settings.Currency);

        if (started.Sale is null)
        {
            // One reader, one person standing at it. A second concurrent sale
            // would only make it ambiguous which one a scan belongs to.
            return Results.Json(
                new { outcome = "declined", reason = "lane_busy" },
                statusCode: StatusCodes.Status409Conflict);
        }

        return Results.Json(
            ToStatus(started.Sale),
            statusCode: started.IsRepeat
                ? StatusCodes.Status200OK
                : StatusCodes.Status201Created);
    }

    private static async Task<IResult> GetSaleAsync(
        Guid saleId,
        HttpContext context,
        PendingSaleStore sales,
        IOptions<PosOptions> options,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        context.Response.Headers.CacheControl = "no-store";

        if (Refuse(context, options.Value) is { } refusal)
        {
            return refusal;
        }

        var sale = sales.Find(saleId);
        return sale is null ? Results.NotFound() : Results.Ok(ToStatus(sale));
    }

    private static async Task<IResult> CancelSaleAsync(
        Guid saleId,
        HttpContext context,
        PendingSaleStore sales,
        IOptions<PosOptions> options,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        context.Response.Headers.CacheControl = "no-store";

        if (Refuse(context, options.Value) is { } refusal)
        {
            return refusal;
        }

        var cancelled = sales.Settle(
            saleId,
            PendingSaleState.Cancelled,
            "cancelled_by_till",
            approvedAmount: null,
            outstandingAmount: null,
            paymentReference: null);

        if (cancelled is not null)
        {
            return Results.Ok(ToStatus(cancelled));
        }

        // Either unknown, or already finished. A cancel that arrives after the
        // cashier has taken the payment must not report success.
        var existing = sales.Find(saleId);
        return existing is null
            ? Results.NotFound()
            : Results.Json(ToStatus(existing), statusCode: StatusCodes.Status409Conflict);
    }

    /// <summary>The two guards every local endpoint shares.</summary>
    private static IResult? Refuse(HttpContext context, PosOptions settings)
    {
        if (!IsLoopback(context))
        {
            return Results.Json(
                new { outcome = "declined", reason = "not_local" },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return IsAuthorised(context, settings)
            ? null
            : Results.Json(
                new { outcome = "declined", reason = "unauthorised" },
                statusCode: StatusCodes.Status401Unauthorized);
    }

    internal static LocalSaleStatus ToStatus(PendingSale sale) =>
        new(
            sale.Id,
            sale.State switch
            {
                PendingSaleState.AwaitingCard => "awaiting-card",
                PendingSaleState.Approved => "approved",
                PendingSaleState.Declined => "declined",
                PendingSaleState.Indeterminate => "indeterminate",
                PendingSaleState.Cancelled => "cancelled",
                PendingSaleState.Expired => "expired",
                _ => "declined",
            },
            sale.Reason,
            sale.Amount,
            sale.ApprovedAmount,
            sale.OutstandingAmount,
            sale.Currency,
            sale.PaymentReference,
            sale.ExpiresAtUtc);

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

/// <param name="SaleReference">
/// The sale's identity in the till, and the idempotency key. Starting twice with
/// the same reference returns the sale already waiting rather than putting a
/// second one in front of the cashier.
/// </param>
public sealed record LocalStartSaleRequest(decimal Amount, string? SaleReference);

/// <param name="Outcome">
/// <c>awaiting-card</c> while the cashier has not presented one yet, then
/// <c>approved</c>, <c>declined</c>, <c>indeterminate</c>, <c>cancelled</c> or
/// <c>expired</c>. Only <c>awaiting-card</c> is worth asking about again.
/// </param>
public sealed record LocalSaleStatus(
    Guid SaleId,
    string Outcome,
    string? Reason,
    decimal Amount,
    decimal? ApprovedAmount,
    decimal? OutstandingAmount,
    string Currency,
    Guid? PaymentReference,
    DateTimeOffset ExpiresAtUtc);
