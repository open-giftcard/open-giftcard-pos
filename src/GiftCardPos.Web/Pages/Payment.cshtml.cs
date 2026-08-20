using GiftCardPos.Web.Backend;
using GiftCardPos.Web.Display;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace GiftCardPos.Web.Pages;

/// <summary>
/// The held sale, between reserving value and charging it.
///
/// This screen exists because a provision is not a payment. Value is reserved
/// and unspendable, but nothing has been posted, and the hold lapses on its own
/// after two minutes (ADR-044). Showing that gap is the clearest way to explain
/// why the platform separates the two.
/// </summary>
public sealed class PaymentModel(
    PosApiClient api,
    IOptions<PosOptions> options,
    TimeProvider timeProvider) : PageModel
{
    private readonly PosOptions settings = options.Value;

    [BindProperty(SupportsGet = true)]
    public Guid ProvisionId { get; set; }

    /// <summary>
    /// What the till will actually charge. Defaults to the basket total and may
    /// be reduced, which is what a voided line item looks like at a counter. It
    /// can never exceed the hold; the platform refuses that outright.
    /// </summary>
    [BindProperty]
    public string Amount { get; set; } = string.Empty;

    public PaymentProvision? Provision { get; private set; }

    public string Currency => settings.Currency;

    public string FormattedHold =>
        Provision is null ? string.Empty : Money.Format(Provision.Amount, Provision.Currency);

    public int SecondsRemaining =>
        Provision is null
            ? 0
            : Math.Max(0, (int)(Provision.ExpiresAtUtc - timeProvider.GetUtcNow()).TotalSeconds);

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (loaded is not null)
        {
            return loaded;
        }

        Amount = FormatAmount(Provision!.Amount);
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync(CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (loaded is not null)
        {
            return loaded;
        }

        if (!TryParseAmount(Amount, out var amount)
            || amount <= 0
            || amount > Provision!.Amount)
        {
            Error = $"Enter an amount between 0 and {FormattedHold}.";
            return Page();
        }

        Amount = FormatAmount(amount);
        var confirmed = await api.ConfirmAsync(ProvisionId, amount, cancellationToken)
            .ConfigureAwait(false);
        if (!confirmed.Ok)
        {
            Error = confirmed.Error;
            return Page();
        }

        return RedirectToPage("Receipt", new { provisionId = ProvisionId });
    }

    internal static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    internal static bool TryParseAmount(string? value, out decimal amount)
    {
        amount = 0;
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        // The browser may submit either separator depending on the till's
        // locale. Money fields do not use thousands separators, so a lone
        // comma is unambiguously the decimal separator.
        if (normalized.Contains(',') && !normalized.Contains('.'))
        {
            normalized = normalized.Replace(',', '.');
        }

        return decimal.TryParse(
            normalized,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out amount);
    }

    public async Task<IActionResult> OnPostCancelAsync(CancellationToken cancellationToken)
    {
        var cancelled = await api.CancelAsync(ProvisionId, cancellationToken).ConfigureAwait(false);
        if (!cancelled.Ok)
        {
            var loaded = await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (loaded is not null)
            {
                return loaded;
            }

            Error = cancelled.Error;
            return Page();
        }

        return RedirectToPage("Index");
    }

    private async Task<IActionResult?> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await api.GetProvisionAsync(ProvisionId, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Ok)
        {
            Error = result.Error;
            return Page();
        }

        Provision = result.Value;

        // A settled hold has nothing left to decide. Send the cashier to the
        // receipt rather than offering buttons that will be refused.
        return string.Equals(Provision!.State, "Confirmed", StringComparison.Ordinal)
            ? RedirectToPage("Receipt", new { provisionId = ProvisionId })
            : null;
    }
}
