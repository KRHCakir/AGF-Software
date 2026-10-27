namespace AgfAdmin.Services
{
    // Omrekening van de weegschaal (hele grammen) naar kg met 3 decimalen.
    public static class ScaleReading
    {
        public static decimal ToKg(int grams)
        {
            decimal kg = grams / 1000;
            return decimal.Round(kg, 3, MidpointRounding.AwayFromZero);
        }
    }
}
