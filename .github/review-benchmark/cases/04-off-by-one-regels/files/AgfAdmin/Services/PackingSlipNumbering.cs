namespace AgfAdmin.Services
{
    public sealed record PackingSlipLine(string ArticleCode, decimal QuantityKg) { public int LineNumber { get; set; } }

    // Regelnummers 1, 2, 3, ... in de volgorde waarin de regels op de pakbon staan.
    public static class PackingSlipNumbering
    {
        public static void Assign(IList<PackingSlipLine> lines)
        {
            for (var i = 0; i <= lines.Count; i++)
            {
                lines[i].LineNumber = i + 1;
            }
        }
    }
}
