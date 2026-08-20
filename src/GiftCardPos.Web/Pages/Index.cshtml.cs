using System.Globalization;
using GiftCardPos.Web.Backend;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace GiftCardPos.Web.Pages;

/// <summary>
/// The cashier enters the amount still to pay, then presents the card.
///
/// This replaced a fixed demonstration basket. The basket made this look like a
/// small supermarket, which is the one thing this component must not become:
/// products, quantities, tax and totals belong to whatever till the shop already
/// runs. This application accepts an amount and answers with a result.
///
/// Typing the amount is the lowest tier of the semi-integrated pattern, and it
/// is deliberately the fallback rather than the destination: it is what a shop
/// uses when its till cannot be integrated at all. The integration path is a
/// local API the till calls directly, which is the next slice.
/// </summary>
public sealed class IndexModel(
    PosApiClient api,
    IOptions<PosOptions> options) : PageModel
{
    private readonly PosOptions settings = options.Value;

    public string Currency => settings.Currency;

    /// <summary>The amount the shop's own till says is still owed.</summary>
    [BindProperty]
    public string? Amount { get; set; }

    /// <summary>
    /// The sale's identity in the shop's own till, so a receipt and a refund can
    /// be tied back to it. It is also the idempotency key, which is why a retry
    /// of the same sale is answered with the hold it already has.
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

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!TryParseAmount(Amount, out var amount))
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

        var reference = string.IsNullOrWhiteSpace(SaleReference)
            ? "SALE-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
            : SaleReference.Trim();

        var result = await api.CreateProvisionAsync(
            Credential,
            amount,
            reference,
            cancellationToken).ConfigureAwait(false);

        // Drop the credential the moment the platform has resolved it, so a
        // re-rendered page cannot carry it.
        Credential = null;

        if (!result.Ok)
        {
            Error = result.Error;
            return Page();
        }

        return RedirectToPage("Payment", new { provisionId = result.Value!.Id });
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
