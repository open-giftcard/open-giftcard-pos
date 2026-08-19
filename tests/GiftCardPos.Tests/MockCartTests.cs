using System.Globalization;
using GiftCardPos.Web.Cart;

namespace GiftCardPos.Tests;

public sealed class MockCartTests
{
    [Fact]
    public void The_total_is_the_sum_of_the_lines()
    {
        var expected = MockCart.Lines.Sum(line => line.Quantity * line.UnitPrice);

        Assert.Equal(expected, MockCart.Total);
        Assert.True(MockCart.Total > 0);
    }

    [Fact]
    public void Money_is_formatted_invariantly()
    {
        // A server locale must not change what the customer is told they owe.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // A culture whose decimal separator is a comma, so a culture-sensitive
            // format would be visibly wrong. Derived from the line rather than
            // hardcoded, because the assertion is about the formatting rule and
            // not about what happens to be in the demonstration basket.
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            var line = MockCart.Lines[0];
            var expected =
                line.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture) + " TRY";

            var formatted = line.FormatUnitPrice("TRY");

            Assert.Equal(expected, formatted);
            Assert.DoesNotContain(",", formatted, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Every_sale_gets_its_own_reference()
    {
        var references = Enumerable.Range(0, 50)
            .Select(_ => MockCart.NewTransactionReference())
            .ToArray();

        // Recorded for reconciliation only. It is deliberately not what prevents
        // a double charge, but a till that reused one would still be confusing.
        Assert.Equal(references.Length, references.Distinct(StringComparer.Ordinal).Count());
        Assert.All(references, reference => Assert.StartsWith("SALE-", reference, StringComparison.Ordinal));
    }
}
