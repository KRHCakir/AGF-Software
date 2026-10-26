namespace AgfAdmin.DTOs
{
    // GET api/customers/{id}/credit: openstaand krediet van een klant (EUR), zie CreditExposureService voor de definities.
    public sealed class CreditExposureDto
    {
        public int CustomerId { get; set; }
        public DateOnly AsOf { get; set; }
        public decimal OpenInvoicesAmount { get; set; }
        public int OpenInvoicesCount { get; set; }
        public decimal DeliveredNotInvoicedAmount { get; set; }
        public int DeliveredNotInvoicedCount { get; set; }
        public decimal OpenOrdersAmount { get; set; }
        public int OpenOrdersCount { get; set; }
        public decimal TotalOutstanding { get; set; }
        // Nieuwe verkooporders zonder prijs: alleen geteld, niet in het totaal; dan is de uitkomst onvolledig.
        public int UnpricedSalesOrdersCount { get; set; }
        public string UnpricedNote { get; set; }
        public bool IsComplete { get; set; }
        public decimal? CreditLimit { get; set; }
        public decimal OverLimitAmount { get; set; }
        public bool IsOverLimit { get; set; }
        public bool IsInsured { get; set; }
        public string Insurer { get; set; }
        public decimal? InsuredLimit { get; set; }
        public DateOnly? InsuredUntil { get; set; }
        // Interne limiet min verzekerde limiet, alleen als de verzekerde limiet lager is.
        public decimal? InsuranceGap { get; set; }
        public bool InsuranceExpired { get; set; }
        public bool InsuranceExpiresWithin30Days { get; set; }
    }

    // Niet-blokkerende kredietwaarschuwing in het antwoord op POST api/sales-orders (null als er geen limiet is of die niet is overschreden).
    public sealed class SalesOrderCreditWarningDto
    {
        public decimal CreditLimit { get; set; }
        public decimal Outstanding { get; set; }
        public decimal Difference { get; set; }
        public bool IsComplete { get; set; }
        public int UnpricedSalesOrdersCount { get; set; }
        public string Message { get; set; }
    }
}
