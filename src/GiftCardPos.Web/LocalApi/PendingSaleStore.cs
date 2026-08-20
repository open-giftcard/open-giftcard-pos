namespace GiftCardPos.Web.LocalApi;

/// <summary>Where a sale handed over by a till has got to.</summary>
public enum PendingSaleState
{
    /// <summary>Waiting for the cashier to present the customer's card.</summary>
    AwaitingCard = 1,
    Approved = 2,
    Declined = 3,

    /// <summary>
    /// The platform may have taken the payment and did not say so. Never
    /// collapsed into <see cref="Declined"/>: a till told declined here would
    /// collect the whole amount again.
    /// </summary>
    Indeterminate = 4,
    Cancelled = 5,

    /// <summary>Nobody presented a card in time.</summary>
    Expired = 6,
}

/// <summary>
/// One sale a till has handed to this lane and is waiting on.
/// </summary>
public sealed record PendingSale(
    Guid Id,
    string SaleReference,
    decimal Amount,
    string Currency,
    PendingSaleState State,
    string? Reason,
    decimal? ApprovedAmount,
    decimal? OutstandingAmount,
    Guid? PaymentReference,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset ExpiresAtUtc)
{
    public bool IsTerminal => State is not PendingSaleState.AwaitingCard;
}

/// <summary>
/// The handover point between the till and the cashier.
///
/// A till starts a sale here and then asks about it; the cashier sees it on this
/// screen and presents the card. In memory on purpose: this is one physical
/// lane, the state is meaningless on another machine, and a sale that did not
/// survive a restart of this process is a sale the cashier must start again
/// anyway. Nothing financial lives here. The platform holds the money, and the
/// worst this can lose is the knowledge that a cashier was mid-scan.
///
/// At most one sale awaits a card at a time. A lane has one card reader and one
/// person standing at it, so a second concurrent sale would only make it
/// ambiguous which one a scan belongs to.
/// </summary>
public sealed class PendingSaleStore(TimeProvider clock)
{
    /// <summary>
    /// How long a handed-over sale waits for a card before it lapses. Generous
    /// compared with the platform's own hold window, because no value is
    /// reserved until the card is actually presented; this only bounds how long
    /// an abandoned sale clutters the screen.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>How long a finished sale stays readable so the till can collect it.</summary>
    public static readonly TimeSpan ResultRetention = TimeSpan.FromMinutes(10);

    private readonly Lock gate = new();
    private readonly Dictionary<Guid, PendingSale> sales = [];

    /// <summary>
    /// Starts a sale, or returns the one this reference already named. Repeating
    /// a start whose response was lost must not leave the cashier facing two
    /// sales for one basket.
    /// </summary>
    public PendingSaleStart Start(string saleReference, decimal amount, string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saleReference);

        var now = clock.GetUtcNow();
        lock (gate)
        {
            Sweep(now);

            var existing = sales.Values.FirstOrDefault(sale =>
                string.Equals(sale.SaleReference, saleReference, StringComparison.Ordinal));
            if (existing is not null)
            {
                return new PendingSaleStart(existing, IsRepeat: true);
            }

            if (sales.Values.Any(sale => sale.State == PendingSaleState.AwaitingCard))
            {
                return new PendingSaleStart(null, IsRepeat: false);
            }

            var started = new PendingSale(
                Guid.NewGuid(),
                saleReference,
                amount,
                currency,
                PendingSaleState.AwaitingCard,
                Reason: null,
                ApprovedAmount: null,
                OutstandingAmount: null,
                PaymentReference: null,
                now,
                now.Add(Lifetime));
            sales[started.Id] = started;
            return new PendingSaleStart(started, IsRepeat: false);
        }
    }

    /// <summary>The sale the cashier should be presenting a card for, if any.</summary>
    public PendingSale? AwaitingCard()
    {
        var now = clock.GetUtcNow();
        lock (gate)
        {
            Sweep(now);
            return sales.Values.FirstOrDefault(
                sale => sale.State == PendingSaleState.AwaitingCard);
        }
    }

    public PendingSale? Find(Guid saleId)
    {
        var now = clock.GetUtcNow();
        lock (gate)
        {
            Sweep(now);
            return sales.GetValueOrDefault(saleId);
        }
    }

    /// <summary>
    /// Records an outcome. Only a sale still awaiting a card can be settled, so
    /// a late second scan cannot overwrite a result the till has already read.
    /// </summary>
    public PendingSale? Settle(
        Guid saleId,
        PendingSaleState state,
        string? reason,
        decimal? approvedAmount,
        decimal? outstandingAmount,
        Guid? paymentReference)
    {
        var now = clock.GetUtcNow();
        lock (gate)
        {
            Sweep(now);
            if (!sales.TryGetValue(saleId, out var sale) ||
                sale.State != PendingSaleState.AwaitingCard)
            {
                return null;
            }

            var settled = sale with
            {
                State = state,
                Reason = reason,
                ApprovedAmount = approvedAmount,
                OutstandingAmount = outstandingAmount,
                PaymentReference = paymentReference,
                ExpiresAtUtc = now.Add(ResultRetention),
            };
            sales[saleId] = settled;
            return settled;
        }
    }

    /// <summary>
    /// Drops sales nobody is waiting on any more. Lazy rather than a background
    /// worker: a lane that is not being used does not need sweeping.
    /// </summary>
    private void Sweep(DateTimeOffset now)
    {
        var lapsed = sales
            .Where(entry => entry.Value.ExpiresAtUtc <= now)
            .ToArray();

        foreach (var entry in lapsed)
        {
            // An unscanned sale becomes readable as expired for the retention
            // window, so a till polling it learns why rather than being told the
            // sale never existed.
            sales[entry.Key] = entry.Value.State == PendingSaleState.AwaitingCard
                ? entry.Value with
                {
                    State = PendingSaleState.Expired,
                    Reason = "no_card_presented",
                    ExpiresAtUtc = now.Add(ResultRetention),
                }
                : entry.Value;

            if (entry.Value.State != PendingSaleState.AwaitingCard)
            {
                sales.Remove(entry.Key);
            }
        }
    }
}

/// <param name="Sale">Null when another sale is already awaiting a card.</param>
/// <param name="IsRepeat">True when this reference had already started a sale.</param>
public sealed record PendingSaleStart(PendingSale? Sale, bool IsRepeat);
