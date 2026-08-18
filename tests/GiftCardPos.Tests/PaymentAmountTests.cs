using GiftCardPos.Web.Pages;

namespace GiftCardPos.Tests;

public sealed class PaymentAmountTests
{
    [Theory]
    [InlineData("739.40", 739.40)]
    [InlineData("739,40", 739.40)]
    [InlineData("739", 739.00)]
    public void Amount_accepts_dot_comma_or_whole_number(
        string input,
        decimal expected)
    {
        Assert.True(PaymentModel.TryParseAmount(input, out var amount));
        Assert.Equal(expected, amount);
    }

    [Theory]
    [InlineData(739.4, "739.40")]
    [InlineData(739, "739.00")]
    public void Amount_is_always_rendered_with_two_decimal_places(
        decimal input,
        string expected)
    {
        Assert.Equal(expected, PaymentModel.FormatAmount(input));
    }
}
