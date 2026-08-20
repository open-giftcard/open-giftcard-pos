using GiftCardPos.Web.Pages;

namespace GiftCardPos.Tests;

/// <summary>
/// The cashier types the amount, so parsing it is the point where a mistyped or
/// locale-shifted figure becomes a wrong charge.
/// </summary>
public sealed class AmountEntryTests
{
    [Theory]
    [InlineData("12.50", 12.50)]
    [InlineData("0.01", 0.01)]
    [InlineData(" 7 ", 7)]
    [InlineData("1000.0000", 1000)]
    public void A_plain_decimal_amount_is_accepted(string entered, double expected)
    {
        Assert.True(IndexModel.TryParseAmount(entered, out var amount));
        Assert.Equal((decimal)expected, amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1.23456")]
    public void An_amount_that_is_not_a_positive_payable_figure_is_refused(string? entered)
    {
        Assert.False(IndexModel.TryParseAmount(entered, out var amount));
        Assert.Equal(0m, amount);
    }

    [Fact]
    public void A_comma_is_refused_rather_than_reinterpreted()
    {
        // "12,50" means twelve fifty to most of Europe and one thousand two
        // hundred and fifty to a machine reading it as a group separator. A till
        // must not guess which the cashier meant: refusing asks them again,
        // charging the wrong one takes money.
        Assert.False(IndexModel.TryParseAmount("12,50", out _));
    }
}
