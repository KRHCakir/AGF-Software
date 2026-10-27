namespace AgfAdmin.Services
{
    // Vervaldatum van een factuur. Bij "einde maand" telt de termijn vanaf de laatste dag van de factuurmaand.
    public static class DueDateCalculator
    {
        public static DateOnly DueDate(DateOnly invoiceDate, int termDays, bool fromEndOfMonth)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(termDays);

            var start = fromEndOfMonth
                ? new DateOnly(invoiceDate.Year, invoiceDate.Month, DateTime.DaysInMonth(invoiceDate.Year, invoiceDate.Month))
                : invoiceDate;

            return start.AddDays(termDays);
        }
    }
}
