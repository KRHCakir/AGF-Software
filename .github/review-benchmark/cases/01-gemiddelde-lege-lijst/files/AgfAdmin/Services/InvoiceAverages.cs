namespace AgfAdmin.Services
{
    // Gemiddeld regelbedrag voor het factuuroverzicht, op 2 decimalen zoals InvoiceMath.
    public static class InvoiceAverages
    {
        public static decimal AverageLineNet(IReadOnlyCollection<decimal> lineNets)
        {
            ArgumentNullException.ThrowIfNull(lineNets);

            var total = lineNets.Sum();
            var average = total / lineNets.Count;

            return InvoiceMath.Round2(average);
        }
    }
}
