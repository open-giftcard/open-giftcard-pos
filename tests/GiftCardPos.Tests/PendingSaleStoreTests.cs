using System.Globalization;
using GiftCardPos.Web.LocalApi;

namespace GiftCardPos.Tests;

/// <summary>
/// The handover point between a till and a cashier. Its job is to make sure one
/// basket produces one payment, and that a till always learns what happened.
/// </summary>
public sealed class PendingSaleStoreTests
{
    private static PendingSaleStore Store(out TestClock clock)
    {
        clock = new TestClock(
            DateTimeOffset.Parse("2026-08-20T10:00:00Z", CultureInfo.InvariantCulture));
        return new PendingSaleStore(clock);
    }

    [Fact]
    public void A_handed_over_sale_waits_for_a_card()
    {
        var store = Store(out _);

        var started = store.Start("SALE-1", 12.50m, "USD");

        Assert.NotNull(started.Sale);
        Assert.False(started.IsRepeat);
        Assert.Equal(PendingSaleState.AwaitingCard, started.Sale!.State);
        Assert.Equal(started.Sale.Id, store.AwaitingCard()?.Id);
    }

    [Fact]
    public void Starting_the_same_reference_twice_does_not_queue_a_second_sale()
    {
        // A till whose response was lost retries. The cashier must not end up
        // facing two sales for one basket.
        var store = Store(out _);

        var first = store.Start("SALE-1", 12.50m, "USD");
        var again = store.Start("SALE-1", 12.50m, "USD");

        Assert.True(again.IsRepeat);
        Assert.Equal(first.Sale!.Id, again.Sale!.Id);
    }

    [Fact]
    public void A_second_different_sale_is_refused_while_one_awaits_a_card()
    {
        // One reader, one person standing at it. Two live sales would make it
        // ambiguous which one a scan belongs to.
        var store = Store(out _);
        store.Start("SALE-1", 12.50m, "USD");

        var second = store.Start("SALE-2", 5m, "USD");

        Assert.Null(second.Sale);
    }

    [Fact]
    public void A_settled_sale_frees_the_lane()
    {
        var store = Store(out _);
        var first = store.Start("SALE-1", 12.50m, "USD");

        store.Settle(first.Sale!.Id, PendingSaleState.Approved, null, 12.50m, 0m, Guid.NewGuid());

        Assert.Null(store.AwaitingCard());
        Assert.NotNull(store.Start("SALE-2", 5m, "USD").Sale);
    }

    [Fact]
    public void A_result_stays_readable_so_the_till_can_collect_it()
    {
        var store = Store(out _);
        var started = store.Start("SALE-1", 12.50m, "USD");
        var reference = Guid.NewGuid();

        store.Settle(
            started.Sale!.Id, PendingSaleState.Approved, null, 8m, 4.50m, reference);
        var read = store.Find(started.Sale.Id);

        Assert.NotNull(read);
        Assert.Equal(PendingSaleState.Approved, read!.State);
        Assert.Equal(8m, read.ApprovedAmount);
        Assert.Equal(4.50m, read.OutstandingAmount);
        Assert.Equal(reference, read.PaymentReference);
    }

    [Fact]
    public void A_finished_sale_cannot_be_settled_again()
    {
        // A late second scan must not overwrite a result the till already read.
        var store = Store(out _);
        var started = store.Start("SALE-1", 12.50m, "USD");
        store.Settle(started.Sale!.Id, PendingSaleState.Approved, null, 12.50m, 0m, Guid.NewGuid());

        var again = store.Settle(
            started.Sale.Id, PendingSaleState.Declined, "nope", null, null, null);

        Assert.Null(again);
        Assert.Equal(PendingSaleState.Approved, store.Find(started.Sale.Id)!.State);
    }

    [Fact]
    public void An_unscanned_sale_lapses_and_says_why()
    {
        // The till must learn the sale died rather than be told it never existed.
        var store = Store(out var clock);
        var started = store.Start("SALE-1", 12.50m, "USD");

        clock.Advance(PendingSaleStore.Lifetime + TimeSpan.FromSeconds(1));

        var read = store.Find(started.Sale!.Id);
        Assert.NotNull(read);
        Assert.Equal(PendingSaleState.Expired, read!.State);
        Assert.Equal("no_card_presented", read.Reason);
        Assert.Null(store.AwaitingCard());
    }

    [Fact]
    public void A_lapsed_sale_frees_the_lane_for_the_next_one()
    {
        var store = Store(out var clock);
        store.Start("SALE-1", 12.50m, "USD");

        clock.Advance(PendingSaleStore.Lifetime + TimeSpan.FromSeconds(1));

        Assert.NotNull(store.Start("SALE-2", 5m, "USD").Sale);
    }
}

/// <summary>
/// A clock the test moves by hand. Ten lines beats a package reference for
/// something this small.
/// </summary>
internal sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset current = now;

    public override DateTimeOffset GetUtcNow() => current;

    public void Advance(TimeSpan by) => current = current.Add(by);
}
