using GiftCardPos.Web.Backend;
using GiftCardPos.Web.Display;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GiftCardPos.Web.Pages;

public sealed class ReceiptModel(PosApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid ProvisionId { get; set; }

    public PaymentProvision? Provision { get; private set; }

    public string? Error { get; private set; }

    public string FormattedCharged =>
        Provision?.ConfirmedAmount is null
            ? string.Empty
            : Money.Format(Provision.ConfirmedAmount.Value, Provision.Currency);

    /// <summary>
    /// What went back to the card because the sale confirmed for less than the
    /// hold. Zero in the ordinary case, and worth showing when it is not.
    /// </summary>
    public string FormattedReleased =>
        Provision?.ConfirmedAmount is null
            ? string.Empty
            : Money.Format(Provision.Amount - Provision.ConfirmedAmount.Value, Provision.Currency);

    public bool HasRelease =>
        Provision?.ConfirmedAmount is not null && Provision.Amount > Provision.ConfirmedAmount.Value;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await api.GetProvisionAsync(ProvisionId, cancellationToken)
            .ConfigureAwait(false);
        if (result.Ok)
        {
            Provision = result.Value;
        }
        else
        {
            Error = result.Error;
        }
    }
}
