using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;

namespace AgfAdmin.Tests.Services;

// Fixes 5a: gewichten als hele kilo's (of met één decimaal als het bedrijf dat instelt). Afronden alleen bij het tonen, halve kilo's
// omhoog; het totaal op een document is de som van de getoonde regels; een document van vóór de instelling houdt zijn oude weergave en
// de instelling wijzigen maakt nooit een nieuwe versie.
public class WeightDisplayTests : IDisposable
{
    static WeightDisplayTests() => QuestPDF.Settings.License = LicenseType.Community;

    private readonly SqliteAppDbContextFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public void Weights_are_whole_kilos_by_default_half_kilos_up_and_never_two_dots()
    {
        Assert.Equal(new[] { "2.273", "2.272", "1", "0", "15.600", "1.234.568" },
            new[] { 2272.5m, 2272.499m, 0.5m, 0.499m, 15600m, 1234567.5m }.Select(kg => WeightDisplay.Kg(kg, 0)).ToArray());
        Assert.Equal(new[] { "2.272,5", "2.272,5", "2.272,6", "15.600,0" }, new[] { 2272.5m, 2272.549m, 2272.55m, 15600m }.Select(kg => WeightDisplay.Kg(kg, 1)).ToArray());
        Assert.Equal("–", WeightDisplay.Kg(null, 0));
        // Een document van vóór de instelling: de oude weergave, niet afgerond.
        Assert.Equal("2.272,5", WeightDisplay.Kg(2272.5m, null));
        Assert.Equal("1,125", WeightDisplay.Kg(1.125m, null));
        // Wat er ook in staat: nooit een getal met twee punten (2.272.5) of een punt als decimaalteken.
        foreach (var kg in new[] { 2272.5m, 0.5m, 999.5m, 1000.4m, 1234567.891m })
        {
            foreach (int? decimals in new int?[] { 0, 1, null })
            {
                var text = WeightDisplay.Kg(kg, decimals);
                Assert.Matches(@"^\d{1,3}(\.\d{3})*(,\d{1,3})?$", text);
            }
        }
        // Een waarde buiten 0 en 1 valt terug op hele kilo's.
        Assert.Equal("2.273", WeightDisplay.Kg(2272.5m, 5));
    }

    [Fact]
    public void A_total_is_the_sum_of_the_shown_rows_so_it_adds_up_on_paper()
    {
        // 10,5 + 10,5 + 0,4: echt 21,4; getoond 11 + 11 + 0 = 22.
        Assert.Equal(22m, WeightDisplay.DisplayedTotal(new decimal?[] { 10.5m, 10.5m, 0.4m, null }, 0));
        Assert.Equal(21.4m, WeightDisplay.DisplayedTotal(new decimal?[] { 10.5m, 10.5m, 0.4m, null }, 1));
        Assert.Null(WeightDisplay.DisplayedTotal(new decimal?[] { null }, 0));
    }

    [Fact]
    public async Task The_setting_is_0_by_default_can_be_1_and_refuses_anything_else()
    {
        await using var db = _factory.CreateContext();
        var service = new WeightDisplayService(db);
        Assert.Equal(0, (await service.GetAsync()).Decimals);
        Assert.Equal(ServiceResultStatus.Validation, (await service.UpdateAsync(new WeightDisplaySettingsDto { Decimals = 2 })).Status);
        Assert.Equal(ServiceResultStatus.Validation, (await service.UpdateAsync(new WeightDisplaySettingsDto { Decimals = -1 })).Status);
        Assert.Equal(ServiceResultStatus.Validation, (await service.UpdateAsync(null)).Status);
        Assert.Equal(1, (await service.UpdateAsync(new WeightDisplaySettingsDto { Decimals = 1 })).Value.Decimals);
        Assert.Equal(1, (await service.GetAsync()).Decimals);
        Assert.Equal(0, (await service.UpdateAsync(new WeightDisplaySettingsDto { Decimals = 0 })).Value.Decimals);
    }

    [Fact]
    public async Task A_new_loading_slip_records_the_setting_and_changing_it_later_makes_no_new_version()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (orderId, _, _) = await SalesTestData.SeedOrderAsync(_factory, seed);
        await using (var db = _factory.CreateContext()) await new LoadingSlipService(db).ConfirmAsync(orderId, new AgfAdmin.DTOs.ConfirmSalesOrderDto());
        await using (var db = _factory.CreateContext())
        {
            var stored = Assert.Single(db.SalesOrderDocuments.Where(d => d.SalesOrderId == orderId && d.Kind == SalesOrderDocumentRules.KindLoadingSlip));
            Assert.Equal(0, LoadingSlipBuilder.Deserialize(stored.SnapshotJson).WeightDecimals);
        }

        await using (var db = _factory.CreateContext()) await new WeightDisplayService(db).UpdateAsync(new WeightDisplaySettingsDto { Decimals = 1 });
        await using (var db = _factory.CreateContext()) await new LoadingSlipService(db).DocumentsAsync(orderId);
        await using (var db = _factory.CreateContext())
        {
            var stored = Assert.Single(db.SalesOrderDocuments.Where(d => d.SalesOrderId == orderId && d.Kind == SalesOrderDocumentRules.KindLoadingSlip));
            Assert.Equal(0, LoadingSlipBuilder.Deserialize(stored.SnapshotJson).WeightDecimals);
        }
    }

    private static LoadingSlipSnapshot Slip(int? weightDecimals)
    {
        var rows = new List<LoadingSlipRow>
        {
            new(505m, "colli", 2m, "Avocado", "Hass", "4 kg box", "Westfalia", "18", "PE", 2272.5m, null, "P-000012"),
            new(3m, "colli", 0.1m, "Mango", "Kent", "4 kg box", null, "8", "PE", 10.5m, null, "P-000013"),
            new(3m, "colli", 0.1m, "Mango", "Kent", "4 kg box", null, "9", "PE", 10.5m, null, "P-000014"),
        };
        return new LoadingSlipSnapshot("en", true, false, "Kwekerij Voorbeeld B.V.", null, null, null, "Groothandel Noord", "261140", "VO-2026-0012", null,
            new DateTime(2026, 11, 19, 0, 0, 0, DateTimeKind.Utc), null, null, rows, new List<LoadingSlipPackagingRow>(),
            rows.Sum(r => r.Quantity), 2.2m, rows.Sum(r => r.NetKg), new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc), "Jan", weightDecimals);
    }

    private static string Text(LoadingSlipSnapshot slip)
    {
        var document = new SalesOrderDocument { Number = "LB-2026-0012", Version = 1, CreatedAt = new DateTime(2026, 11, 18, 9, 5, 0, DateTimeKind.Utc) };
        using var pdf = PdfDocument.Open(LoadingSlipPdf.Render(document, slip));
        return string.Join("\n", pdf.GetPages().Select(p => p.Text));
    }

    [Fact]
    public void The_loading_slip_shows_whole_kilos_and_a_total_that_adds_up_and_an_older_slip_keeps_its_own_notation()
    {
        var whole = Text(Slip(0));
        Assert.Contains("2.273", whole);
        Assert.DoesNotContain("2.272,5", whole);
        Assert.DoesNotContain("2.272.5", whole);
        // 2.273 + 11 + 11 = 2.295 (de echte som is 2.293,5).
        Assert.Contains("2.295", whole);
        Assert.DoesNotContain("2.293,5", whole);

        var one = Text(Slip(1));
        Assert.Contains("2.272,5", one);
        Assert.Contains("2.293,5", one);

        var older = Text(Slip(null));
        Assert.Contains("2.272,5", older);
        Assert.Contains("2.293,5", older);
    }

    [Fact]
    public void Changing_the_setting_never_makes_a_new_version_and_a_stored_slip_without_the_field_still_reads()
    {
        Assert.Equal(LoadingSlipBuilder.Hash(Slip(null)), LoadingSlipBuilder.Hash(Slip(0)));
        Assert.Equal(LoadingSlipBuilder.Hash(Slip(0)), LoadingSlipBuilder.Hash(Slip(1)));
        // Het gewicht zelf telt wel mee.
        var heavier = Slip(0);
        heavier.Rows[0] = heavier.Rows[0] with { NetKg = 2273m };
        Assert.NotEqual(LoadingSlipBuilder.Hash(Slip(0)), LoadingSlipBuilder.Hash(heavier));

        // Een bon die is bewaard vóór dit veld bestond: geen WeightDecimals in de JSON, dus de oude weergave.
        var stored = LoadingSlipBuilder.Serialize(Slip(null)).Replace(",\"weightDecimals\":null", "");
        Assert.DoesNotContain("weightDecimals", stored);
        Assert.Null(LoadingSlipBuilder.Deserialize(stored).WeightDecimals);
        Assert.Equal(1, LoadingSlipBuilder.Deserialize(LoadingSlipBuilder.Serialize(Slip(1))).WeightDecimals);
    }
}
