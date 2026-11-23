using AgfAdmin.Services;

namespace AgfAdmin.DTOs
{
    // Totalen van een order met prijs, korting en btw (feedback 22 november), voor inkoop en verkoop gelijk (OrderLineMath.Summary).
    // Bedragen in de valuta van de order. Regels zonder prijs en regels zonder btw-tarief worden apart geteld.
    public sealed class OrderAmountsDto
    {
        public decimal? Net { get; set; }
        public List<OrderVatRateDto> VatRates { get; set; } = new();
        public decimal? Vat { get; set; }
        public decimal? Gross { get; set; }
        public int LinesWithoutPrice { get; set; }
        public int LinesWithoutVat { get; set; }

        public static OrderAmountsDto From(OrderLineMath.Summary summary) => new()
        {
            Net = summary.Net,
            VatRates = summary.VatRates.Select(r => new OrderVatRateDto { Percentage = r.Percentage, Net = r.Net, Vat = r.Vat }).ToList(),
            Vat = summary.Vat,
            Gross = summary.Gross,
            LinesWithoutPrice = summary.LinesWithoutPrice,
            LinesWithoutVat = summary.LinesWithoutVat,
        };
    }

    public sealed class OrderVatRateDto
    {
        public decimal Percentage { get; set; }
        public decimal Net { get; set; }
        public decimal Vat { get; set; }
    }
}
