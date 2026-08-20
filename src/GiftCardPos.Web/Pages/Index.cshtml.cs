using System.Globalization;
using GiftCardPos.Web.Backend;
using GiftCardPos.Web.Display;
using GiftCardPos.Web.LocalApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace GiftCardPos.Web.Pages;

/// <summary>
/// The cashier's screen, serving both tiers.
///
/// When the shop's till has handed a sale over the local API, the amount and the
/// reference come from it and the cashier only presents the card. When it has
/// not, the cashier types the amount themselves, which is what a shop does when
/// its till cannot be integrated at all.
///
/// The fixed demonstration basket this replaced made the application look like a
/// small supermarket, which is the one thing it must not become: products,
/// quantities and tax belong to whatever till the shop already runs.
/// </summary>
public sealed class IndexModel(
    SalePayments payments,
    PendingSaleStore sales,
    IOptions<PosOptions> options) : PageModel
{
    private readonly PosOptions settings = options.Value;

    public string Currency => settings.Currency;

    /// <summary>Set when a till is waiting on this lane.</summary>
    public PendingSale? HandedOver { get; private set; }

    public string? HandedOverAmount => HandedOver is null
        ? null
        : Money.Format(HandedOver.Amount, HandedOver.Currency);

    /// <summary>The amount the shop's own till says is still owed.</summary>
    [BindProperty]
    public string? Amount { get; set; }

    /// <summary>
    /// The sale's identity in the shop's own till, so a receipt and a refund can
    /// be tied back to it. It is also the idempotency key, which is why a retry
    /// of the same sale is answered with the payment it already took.
    /// </summary>
    [BindProperty]
    public string? SaleReference { get; set; }

    /// <summary>
    /// What the cashier scanned or typed. Never rendered back to the page and
    /// never logged: it is a live payment credential until the platform consumes
    /// it.
    /// </summary>
    [BindProperty]
    public string? Credential { get; set; }

    public string? Error { get; private set; }

    public string? Result { get; private set; }

    public void OnGet() => HandedOver = sales.AwaitingCard();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        HandedOver = sales.AwaitingCard();

        decimal amount;
        string reference;
        if (HandedOver is not null)
        {
            // The till owns these. A cashier must not be able to change the
            // amount a sale was handed over for.
            amount = HandedOver.Amount;
            reference = HandedOver.SaleReference;
        }
        else if (TryParseAmount(Amount, out var typed))
        {
            amount = typed;
            reference = string.IsNullOrWhiteSpace(SaleReference)
                ? "SALE-" + DateTime.UtcNow.ToString(
                    "yyyyMMddHHmmss",
                    CultureInfo.InvariantCulture)
                : SaleReference.Trim();
        }
        else
        {
            Error = "Enter the amount to take from the card, for example 12.50.";
            Credential = null;
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Credential))
        {
            Error = "Scan the customer's QR code, or type their 12-digit code.";
            return Page();
        }

        var outcome = await payments
            .TakeAsync(Credential, amount, reference, cancellationToken)
            .ConfigureAwait(false);

        // Drop the credential the moment the platform has resolved it, so a
        // re-rendered page cannot carry it.
        Credential = null;

        if (HandedOver is not null)
        {
            // Tell the till before telling the cashier. The till is the system of
            // record for the sale, and it is the one that cannot ask again.
            sales.Settle(
                HandedOver.Id,
                outcome.Outcome,
                outcome.Reason,
                outcome.ApprovedAmount,
                outcome.OutstandingAmount,
                outcome.PaymentReference);
            HandedOver = null;
        }

        switch (outcome.Outcome)
        {
            case PendingSaleState.Approved:
                Result = outcome.OutstandingAmount > 0m
                    ? $"Took {Money.Format(outcome.ApprovedAmount, outcome.Currency ?? Currency)}. " +
                      $"{Money.Format(outcome.OutstandingAmount, outcome.Currency ?? Currency)} still to pay."
                    : $"Took {Money.Format(outcome.ApprovedAmount, outcome.Currency ?? Currency)}. Paid in full.";
                Amount = null;
                SaleReference = null;
                return Page();

            case PendingSaleState.Indeterminate:
                Error =
                    "The platform did not confirm before the connection ended. " +
                    "The card may have been charged. Check before taking payment again.";
                return Page();

            default:
                Error = outcome.Reason;
                return Page();
        }
    }

    /// <summary>
    /// Invariant parsing on purpose. A till in a comma-decimal locale must not
    /// read "12.50" as one thousand two hundred and fifty, and the operator
    /// entering the figure is copying digits from another screen.
    /// </summary>
    internal static bool TryParseAmount(string? value, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!decimal.TryParse(
                value.Trim(),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            return false;
        }

        if (parsed <= 0m || parsed > 1_000_000_000m ||
            decimal.Round(parsed, 4) != parsed)
        {
            return false;
        }

        amount = parsed;
        return true;
    }
}
