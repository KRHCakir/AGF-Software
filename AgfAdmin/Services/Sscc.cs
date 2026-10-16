namespace AgfAdmin.Services
{
    // Controle van een door de gebruiker gescande of ingevoerde GS1-SSCC: precies 18 ASCII-cijfers met een geldig
    // mod-10-controlecijfer. Er wordt niets genormaliseerd en deze applicatie genereert nooit zelf een SSCC.
    public static class Sscc
    {
        public const int Length = 18;

        public static bool IsValid(string value)
        {
            if (value == null || value.Length != Length || !value.All(c => c >= '0' && c <= '9'))
            {
                return false;
            }

            // Gewicht 3 en 1 afwisselend, van rechts naar links, beginnend direct links van het controlecijfer.
            var sum = 0;
            for (var i = Length - 2; i >= 0; i--)
            {
                var weight = (Length - 2 - i) % 2 == 0 ? 3 : 1;
                sum += (value[i] - '0') * weight;
            }

            var checkDigit = (10 - sum % 10) % 10;
            return value[Length - 1] - '0' == checkDigit;
        }
    }
}
