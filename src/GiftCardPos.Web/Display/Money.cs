using System.Globalization;

namespace GiftCardPos.Web.Display;

/// <summary>
/// How an amount is written on a till screen and a receipt.
///
/// Invariant on purpose. The figure is read aloud to a customer and compared
/// against another till's screen, so it must not change shape because the server
/// happens to run under a different culture than the shop.
///
/// This outlived the demonstration basket it used to live inside: formatting
/// money is presentation and belongs here, whereas the basket was a fake
/// supermarket and is gone.
/// </summary>
internal static class Money
{
    public static string Format(decimal amount, string currency) =>
        string.Create(CultureInfo.InvariantCulture, $"{amount:0.00} {currency}");
}
