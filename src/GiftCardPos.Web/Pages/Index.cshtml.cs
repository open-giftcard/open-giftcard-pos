using GiftCardPos.Web.Backend;
using GiftCardPos.Web.Cart;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace GiftCardPos.Web.Pages;

public sealed class IndexModel(
    PosApiClient api,
    IOptions<PosOptions> options) : PageModel
{
    private readonly PosOptions settings = options.Value;

    public IReadOnlyList<CartLine> Lines => MockCart.Lines;

    public string Currency => settings.Currency;

    public string FormattedTotal => MockCart.FormatTotal(settings.Currency);

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
        if (string.IsNullOrWhiteSpace(Credential))
        {
            Error = "Scan the customer's QR code, or type their 12-digit code.";
            return Page();
        }

        // Hold the exact basket total. Confirmation may still charge less, but
        // never more, so the hold is the ceiling the customer is protected by.
        var reference = MockCart.NewTransactionReference();
        var result = await api.CreateProvisionAsync(
            Credential,
            MockCart.Total,
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
}
