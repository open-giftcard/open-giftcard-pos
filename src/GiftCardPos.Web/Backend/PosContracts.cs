namespace GiftCardPos.Web.Backend;

/// <summary>
/// The subset of the platform contract this till binds to. Deliberately narrow:
/// a counter needs to take payment and release a hold, and nothing else. It
/// never reads a cardholder's identity, balance, or history.
/// </summary>
public sealed record PaymentProvision(
    Guid Id,
    string GiftCardPublicReference,
    decimal Amount,
    string Currency,
    string State,
    string StoreReference,
    string? PosTransactionReference,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? SettledAtUtc,
    decimal? ConfirmedAmount);

public sealed record PosAccessToken(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    Guid PosClientId,
    Guid PosTerminalId,
    string StoreReference);

/// <summary>
/// A refusal at a counter is normal, not exceptional. Carrying it as data keeps
/// the cashier-facing message deliberate instead of whatever an exception page
/// would have said.
/// </summary>
public sealed record PosResult<T>(T? Value, string? Error)
    where T : class
{
    public bool Ok => Error is null && Value is not null;
}

/// <summary>Factories, kept off the generic type so they stay easy to call.</summary>
public static class PosResult
{
    public static PosResult<T> Success<T>(T value)
        where T : class => new(value, null);

    public static PosResult<T> Failure<T>(string error)
        where T : class => new(null, error);
}

public sealed class PosOptions
{
    public const string SectionName = "Pos";

    /// <summary>Base address of the platform API.</summary>
    public string BackendBaseUrl { get; set; } = "http://localhost:5143";

    public string ClientCode { get; set; } = string.Empty;

    /// <summary>
    /// Supplied through user secrets or the environment, never committed. A real
    /// till would hold this in device secure storage.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    public string TerminalCode { get; set; } = string.Empty;

    public string Currency { get; set; } = "USD";
}
