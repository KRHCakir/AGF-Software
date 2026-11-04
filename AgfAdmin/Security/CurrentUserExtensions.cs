namespace AgfAdmin.Security
{
    public static class CurrentUserExtensions
    {
        public const string UnknownActor = "Onbekend";

        // De actor van een gebeurtenis: de weergavenaam van de ingelogde gebruiker (anders zijn id, anders "Onbekend"),
        // ingekort tot de kolombreedte zonder een surrogaatpaar te splitsen. Nooit uit het verzoek van de client.
        public static string Actor(this ICurrentUser user, int maxLength)
        {
            var name = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserId : user.DisplayName;
            var trimmed = name?.Trim() ?? "";
            if (trimmed.Length == 0)
            {
                trimmed = UnknownActor;
            }
            if (trimmed.Length <= maxLength)
            {
                return trimmed;
            }
            var cut = maxLength;
            if (cut > 0 && char.IsHighSurrogate(trimmed[cut - 1]))
            {
                cut--;
            }
            return trimmed[..cut];
        }
    }
}
