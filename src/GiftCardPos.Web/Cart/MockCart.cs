using System.Globalization;

namespace GiftCardPos.Web.Cart;

public sealed record CartLine(string Name, int Quantity, decimal UnitPrice)
{
    public decimal Total => Quantity * UnitPrice;

    public string FormatUnitPrice(string currency) => Money(UnitPrice, currency);

    public string FormatTotal(string currency) => Money(Total, currency);

    internal static string Money(decimal amount, string currency) =>
        string.Create(CultureInfo.InvariantCulture, $"{amount:0.00} {currency}");
}

/// <summary>
/// A fixed basket standing in for a real point-of-sale integration.
///
/// This exists to make the payment journey demonstrable end to end. It is not a
/// retail system: there is no catalogue, no stock, no pricing rules, no tax, and
/// no receipt printer. Everything financial still happens on the platform, which
/// is the part actually being shown.
/// </summary>
public static class MockCart
{
    public static IReadOnlyList<CartLine> Lines { get; } =
    [
        new CartLine("Milk, 1 L", 2, 3.45m),
        new CartLine("Wholemeal bread", 1, 2.20m),
        new CartLine("Eggs, box of 10", 1, 8.99m),
        new CartLine("Olive oil, 750 ml", 1, 24.90m),
        new CartLine("Filter coffee, 250 g", 2, 15.475m),
    ];

    public static decimal Total => Lines.Sum(line => line.Total);

    public static string FormatTotal(string currency) => CartLine.Money(Total, currency);

    /// <summary>
    /// A per-sale reference the platform records for reconciliation. It is
    /// deliberately not what prevents a double charge: the server-issued
    /// credential is (ADR-018).
    /// </summary>
    public static string NewTransactionReference() =>
        "SALE-" + DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
        + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
}
