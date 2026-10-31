using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;

namespace AgfAdmin.Tests.Services;

// Herkomst van de order: optioneel op de kop, afgeleid van de regels in lijst en detail.
public sealed partial class ReceiptWeightDeviationTests
{
    // ---------------- Herkomst van de order ----------------

    [Fact]
    public async Task The_order_header_origin_is_optional_and_the_order_origin_follows_the_lines()
    {
        await ArrangeAsync(headerOrigin: null);
        var mixed = await CreateOrderAsync("  ",
            new CreateSupplyCommitmentLineDto { ProductVariantId = _peru, ExpectedQuantity = 5m, ExpectedUnit = "colli" },
            new CreateSupplyCommitmentLineDto { ProductVariantId = _chile, ExpectedQuantity = 5m, ExpectedUnit = "colli" },
            new CreateSupplyCommitmentLineDto { ProductVariantId = _peru, ExpectedQuantity = 2m, ExpectedUnit = "colli" });

        Assert.Equal(("", "Chili, Peru"), (mixed.Origin, mixed.LinesOrigin));
        await using var db = _factory.CreateContext();
        var service = new PurchaseOrderService(db);
        var list = (await service.ListAsync(null, "all", null, 1, 50)).Value.Items;
        Assert.Equal("Peru", list.Single(i => i.Id == _orderId).LinesOrigin);
        Assert.Equal("Chili, Peru", list.Single(i => i.Id == mixed.Id).LinesOrigin);
        var detail = (await service.GetAsync(_orderId)).Value;
        Assert.Equal(("", "Peru"), (detail.Origin, detail.LinesOrigin));
    }

    [Fact]
    public void Lines_origin_is_unique_sorted_and_empty_without_origins()
    {
        Assert.Equal("Peru", SupplyRules.LinesOrigin(new[] { "Peru", " Peru " }));
        Assert.Equal("Chili, Peru, Zuid-Afrika", SupplyRules.LinesOrigin(new[] { "Zuid-Afrika", "Peru", "Chili", null, "" }));
        Assert.Null(SupplyRules.LinesOrigin(new string[] { null, " " }));
    }
}
