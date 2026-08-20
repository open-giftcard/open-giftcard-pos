using GiftCardPos.Web.LocalApi;

namespace GiftCardPos.Tests;

/// <summary>
/// Keeps the integration samples honest.
///
/// The samples are not compiled or run by this build, on purpose: they are
/// examples to copy, not a library with a compatibility promise. That leaves one
/// realistic way for them to go wrong, which is the API moving while nobody
/// updates them, so an integrator copies something that cannot work. This
/// asserts the routes they name still exist.
///
/// It does not check that the samples are correct, only that they are not
/// obviously stale. Correctness is what the examples' own comments and the
/// README are for.
/// </summary>
public sealed class ExampleRoutesTests
{
    private static readonly string[] SampleFiles =
    [
        "README.md",
        "take-payment.sh",
        "take_payment.py",
        "TakePayment.cs",
    ];

    /// <summary>Every route the local API actually serves.</summary>
    public static TheoryData<string> ServedRoutes() => new()
    {
        $"{LocalSaleApi.RouteBase}/sale/payment",
        $"{LocalSaleApi.RouteBase}/sale/start",
    };

    [Theory]
    [MemberData(nameof(ServedRoutes))]
    public void The_samples_name_routes_that_still_exist(string route)
    {
        var samples = ReadSamples();

        Assert.True(
            samples.Any(sample => sample.Value.Contains(route, StringComparison.Ordinal)),
            $"No sample mentions '{route}'. If the API moved, the samples in " +
            "examples/ are now telling integrators to call something that does " +
            "not exist.");
    }

    [Fact]
    public void The_samples_do_not_name_a_route_the_api_no_longer_serves()
    {
        // Catches a rename in the other direction: a sample still pointing at
        // the old path after the API moved.
        var served = new[]
        {
            $"{LocalSaleApi.RouteBase}/sale/payment",
            $"{LocalSaleApi.RouteBase}/sale/start",
            $"{LocalSaleApi.RouteBase}/sale/",
        };

        foreach (var (name, text) in ReadSamples())
        {
            var index = 0;
            while ((index = text.IndexOf("/local/", index, StringComparison.Ordinal)) >= 0)
            {
                var mentioned = text[index..];
                Assert.True(
                    served.Any(route => mentioned.StartsWith(route, StringComparison.Ordinal)),
                    $"{name} names a /local/ route the API does not serve: " +
                    mentioned[..Math.Min(48, mentioned.Length)]);
                index++;
            }
        }
    }

    [Fact]
    public void Every_sample_warns_that_indeterminate_is_not_declined()
    {
        // The one mistake that charges a customer twice. A sample that omits it
        // is worse than no sample, because it will be copied.
        foreach (var (name, text) in ReadSamples())
        {
            Assert.True(
                text.Contains("indeterminate", StringComparison.OrdinalIgnoreCase),
                $"{name} never mentions the indeterminate outcome.");
        }
    }

    [Fact]
    public void Every_sample_explains_that_the_sale_reference_is_the_idempotency_key()
    {
        foreach (var (name, text) in ReadSamples())
        {
            Assert.True(
                text.Contains("idempotency", StringComparison.OrdinalIgnoreCase),
                $"{name} never explains that the sale reference is the idempotency key.");
        }
    }

    private static KeyValuePair<string, string>[] ReadSamples()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !Directory.Exists(Path.Combine(directory, "examples")))
        {
            directory = Directory.GetParent(directory)?.FullName;
        }

        Assert.NotNull(directory);
        var examples = Path.Combine(directory!, "examples");

        return SampleFiles
            .Select(file => new KeyValuePair<string, string>(
                file,
                File.ReadAllText(Path.Combine(examples, file))))
            .ToArray();
    }
}
