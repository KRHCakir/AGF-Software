namespace AgfAdmin.Services
{
    // Btw-bedrag per verkoopregel, in centen nauwkeurig voor de factuur-PDF.
    public static class SalesInvoiceVat
    {
        public static decimal LineVat(decimal lineNet, decimal vatPercentage)
        {
            var vat = lineNet * vatPercentage / 100m;
            var cents = (long)(vat * 100m);
            return cents / 100m;
        }

        public static decimal TotalVat(IEnumerable<(decimal Net, decimal VatPercentage)> lines) =>
            lines.Sum(line => LineVat(line.Net, line.VatPercentage));
    }
}
