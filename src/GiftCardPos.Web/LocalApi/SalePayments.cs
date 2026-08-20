using GiftCardPos.Web.Backend;

namespace GiftCardPos.Web.LocalApi;

/// <param name="Outcome">
/// Approved, Declined or Indeterminate. Indeterminate means the platform may
/// have taken the payment and did not say so, and collapsing it into Declined
/// double-charges customers.
/// </param>
public sealed record SalePaymentOutcome(
    PendingSaleState Outcome,
    string? Reason,
    decimal ApprovedAmount,
    decimal OutstandingAmount,
    string? Currency,
    Guid? PaymentReference);

/// <summary>
/// Taking one gift card payment for a sale, whether the sale arrived from a
/// till over the local API or from a cashier typing it on this screen.
///
/// Both routes do exactly the same thing to the customer's money, so they run
/// the same code. When those two drifted apart is when one of them would start
/// charging differently from the other.
/// </summary>
public sealed class SalePayments(PosApiClient api)
{
    public async Task<SalePaymentOutcome> TakeAsync(
        string credential,
        decimal amount,
        string saleReference,
        CancellationToken cancellationToken)
    {
        var held = await api.CreateProvisionAsync(
            credential,
            amount,
            saleReference,
            cancellationToken).ConfigureAwait(false);

        if (!held.Ok)
        {
            return new SalePaymentOutcome(
                PendingSaleState.Declined, held.Error, 0m, amount, null, null);
        }

        var provision = held.Value!;

        // Confirm exactly what was approved. The held amount is the ceiling, so
        // this can never charge more than the card agreed to, and on a partial
        // approval it is already less than the sale total.
        var confirmed = await api
            .ConfirmAsync(provision.Id, provision.Amount, cancellationToken)
            .ConfigureAwait(false);

        if (!confirmed.Ok)
        {
            // The hold exists and the charge did not complete. Reporting this as
            // declined would have the till collect the whole amount again while
            // the customer's value is still held.
            return new SalePaymentOutcome(
                PendingSaleState.Indeterminate,
                confirmed.Error,
                0m,
                amount,
                provision.Currency,
                provision.Id);
        }

        var settled = confirmed.Value!;
        var approved = settled.ConfirmedAmount ?? settled.Amount;
        return new SalePaymentOutcome(
            PendingSaleState.Approved,
            null,
            approved,
            amount - approved,
            settled.Currency,
            settled.Id);
    }
}
