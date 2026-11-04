using System.Text.Json;
using AgfAdmin.DTOs;

namespace AgfAdmin.Tests.Infrastructure;

// Het beschikbaarheidsoverzicht zonder de verkoopstand (WarehouseSupplyDto.Sales): voor tests die bewijzen dat de magazijnvoorraad niet
// verandert. De verkoopstand zelf verandert wel bij klantgebonden toewijzingen (die vraag telt niet als verkocht uit eigen voorraad).
public static class AvailabilitySnapshot
{
    public static string WithoutSales(IEnumerable<VariantAvailabilityDto> variants)
    {
        var copy = JsonSerializer.Deserialize<List<VariantAvailabilityDto>>(JsonSerializer.Serialize(variants))!;
        foreach (var row in copy.SelectMany(v => v.WarehouseSupply)) row.Sales = null;
        return JsonSerializer.Serialize(copy);
    }

    public static decimal Sold(IEnumerable<VariantAvailabilityDto> variants, string unit) =>
        variants.SelectMany(v => v.WarehouseSupply).Where(r => r.Unit == unit).Sum(r => r.Sales?.Sold ?? 0m);
}
