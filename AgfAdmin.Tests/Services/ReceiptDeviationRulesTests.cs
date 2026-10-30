using AgfAdmin.Services;
using static AgfAdmin.Services.ReceiptDeviationRules;

namespace AgfAdmin.Tests.Services;

public sealed class ReceiptDeviationRulesTests
{
    private static VariantFacts Hass(int id = 1, string origin = "Peru", string size = "18", string packaging = "4kg Box", string unit = "colli",
        string tradeClass = "I", string brand = "Verita", string name = "Hass", int product = 1) =>
        new(id, product, "Avocado", name, origin, size, packaging, unit, tradeClass, brand);

    [Fact]
    public void Same_Variant_Has_No_Differences() => Assert.Empty(DifferingKinds(Hass(), Hass()));

    [Theory]
    [InlineData("Zuid-Afrika", "18", "4kg Box", "colli", "I", "Verita", "Origin")]
    [InlineData("Peru", "16", "4kg Box", "colli", "I", "Verita", "Size")]
    [InlineData("Peru", "18", "10kg Crate", "colli", "I", "Verita", "Packaging")]
    [InlineData("Peru", "18", "4kg Box", "box", "I", "Verita", "Packaging")]
    [InlineData("Peru", "18", "4kg Box", "colli", "II", "Verita", "TradeClass")]
    [InlineData("Peru", "18", "4kg Box", "colli", "I", "Other", "Brand")]
    [InlineData("PE", "8", "4kg Box", "colli", "I", "Verita", "Origin,Size")]
    // Spaties aan de randen en leeg = geen waarde tellen niet; alle kenmerken gelijk maar een andere variant: Other.
    [InlineData(" Peru ", "18", "4kg Box", "colli", "I", "Verita", "Other")]
    // Hoofdletters tellen wel: exact vergelijken, nooit "bijna gelijk".
    [InlineData("peru", "18", "4kg Box", "colli", "I", "Verita", "Origin")]
    public void Differences_Are_Exact(string origin, string size, string packaging, string unit, string tradeClass, string brand, string expected)
    {
        var kinds = DifferingKinds(Hass(), Hass(2, origin, size, packaging, unit, tradeClass, brand));
        Assert.Equal(expected, string.Join(",", kinds));
    }

    [Fact]
    public void With_a_country_on_both_sides_only_the_country_code_counts()
    {
        var peru = Hass() with { Origin = "Peru", OriginCountryCode = "PE" };
        Assert.Equal(new[] { KindOther }, DifferingKinds(peru, Hass(2) with { Origin = "PE", OriginCountryCode = "PE" }));
        Assert.Equal(new[] { KindOrigin }, DifferingKinds(peru, Hass(2) with { Origin = "Chili", OriginCountryCode = "CL" }));
        // Een oude variant zonder land: dan de tekst, exact zoals voorheen.
        Assert.Equal(new[] { KindOrigin }, DifferingKinds(peru, Hass(2) with { Origin = "PE", OriginCountryCode = null }));
    }

    [Fact]
    public void Null_And_Empty_Are_The_Same_Value() =>
        Assert.Equal(new[] { KindOther }, DifferingKinds(Hass(brand: null), Hass(2, brand: "  ")));

    [Theory]
    [InlineData("Hass", 1, true)]
    [InlineData(" Hass", 1, true)]
    [InlineData("Fuerte", 1, false)]
    [InlineData("Hass.", 1, false)]
    [InlineData("Hass", 2, false)]
    public void Same_Article_Means_Same_Product_And_Variety(string name, int product, bool same) =>
        Assert.Equal(same, IsSameArticle(Hass(), Hass(2, name: name, product: product)));

    [Theory]
    [InlineData(100, 0, 10, 10)]
    [InlineData(100, 40, 10, 10)]
    [InlineData(100, 95, 10, 5)]
    [InlineData(100, 100, 10, 0)]
    [InlineData(100, 120, 10, 0)]
    public void Reduction_Never_Goes_Below_What_Was_Received(decimal expected, decimal received, decimal quantity, decimal reduced) =>
        Assert.Equal(reduced, ReducedQuantity(expected, received, quantity));

    [Fact]
    public void Reduce_Scales_Kg_And_Redistributes_Pallets()
    {
        var after = Reduce(new LineShape(100m, 400m, 420m, LineWeightRules.Calculated, 2m, 48m, 4m), 10m);
        Assert.Equal(new LineShape(90m, 360m, 378m, LineWeightRules.Calculated, 1m, 48m, 42m), after);
    }

    [Fact]
    public void Reduce_Redistributes_An_Older_Broken_Pallet_Count_Over_Whole_Pallets()
    {
        var after = Reduce(new LineShape(100m, null, null, null, 2.5m, 40m, null), 10m);
        Assert.Equal((2m, 40m, 10m), (after.PalletCount, after.PackagesPerPallet, after.LoosePackages));
    }

    [Fact]
    public void Reduce_Keeps_Loose_Empty_When_It_Was_Empty_And_Comes_Out_Even()
    {
        var after = Reduce(new LineShape(96m, null, null, null, 2m, 48m, null), 48m);
        Assert.Equal(new LineShape(48m, null, null, null, 1m, 48m, null), after);
    }

    [Theory]
    [InlineData(81, 2, 40.5, 1)]    // gebroken verpakkingen per pallet (oudere regel): weergave leeg
    [InlineData(100, 2, 48, 60)]    // minder dan één pallet over: weergave leeg
    public void Reduce_Clears_A_Pallet_Display_That_No_Longer_Fits(decimal expected, decimal pallets, decimal perPallet, decimal reduced)
    {
        var loose = expected - pallets * perPallet;
        var after = Reduce(new LineShape(expected, null, null, null, pallets, perPallet, loose == 0m ? null : loose), reduced);
        Assert.Equal((expected - reduced, (decimal?)null, (decimal?)null, (decimal?)null), (after.ExpectedQuantity, after.PalletCount, after.PackagesPerPallet, after.LoosePackages));
    }

    [Fact]
    public void Reduce_Rounds_Kg_To_Three_Decimals()
    {
        var after = Reduce(new LineShape(3m, 10m, null, LineWeightRules.Entered, null, null, null), 1m);
        Assert.Equal(6.667m, after.NetKg);
    }

    [Fact]
    public void Nothing_To_Reduce_Leaves_The_Line_As_It_Is()
    {
        var line = new LineShape(100m, 400m, null, LineWeightRules.Entered, 2m, 48m, 4m);
        Assert.Same(line, Reduce(line, 0m));
    }
}
