using GiftCardPos.Web.Backend;

namespace GiftCardPos.Tests;

/// <summary>
/// The till has to decide which of the two credential forms it was handed
/// before it calls the platform, because the API accepts exactly one. Getting
/// this wrong at a counter looks like a card that "does not work".
/// </summary>
public sealed class CredentialFormatTests
{
    [Theory]
    [InlineData("123456789012")]
    [InlineData("1234 5678 9012")]
    [InlineData("1234-5678-9012")]
    [InlineData("  1234 5678 9012  ")]
    public void A_twelve_digit_code_is_recognised_however_it_is_typed(string typed) =>
        Assert.True(PosApiClient.LooksNumeric(typed));

    [Theory]
    // Eleven and thirteen digits are not the numeric form.
    [InlineData("12345678901")]
    [InlineData("1234567890123")]
    // The opaque QR credential, which a scanner types verbatim.
    [InlineData("019c05986700700080000000001a.pQ7Zx1k-3fB2mYw8LrTn5CvJ0aHdEsGtUiOpQwErTyU")]
    [InlineData("")]
    [InlineData("not-a-credential")]
    public void Anything_else_is_treated_as_the_opaque_credential(string typed) =>
        Assert.False(PosApiClient.LooksNumeric(typed));

    [Fact]
    public void Separators_are_stripped_before_the_code_reaches_the_platform() =>
        Assert.Equal("123456789012", PosApiClient.Normalize(" 1234-5678 9012 "));

    [Fact]
    public void A_digit_string_that_only_looks_numeric_after_stripping_is_still_rejected() =>
        // Letters survive normalisation, so this stays the opaque form.
        Assert.False(PosApiClient.LooksNumeric("1234 5678 901X"));
}
