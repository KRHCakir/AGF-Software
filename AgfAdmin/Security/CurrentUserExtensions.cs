namespace AgfAdmin.Security
{
    public static class CurrentUserExtensions
    {
        // De actor van een gebeurtenis: de weergavenaam van de ingelogde gebruiker (anders zijn id), ingekort tot de kolombreedte.
        // Nooit uit het verzoek van de client.
        public static string Actor(this ICurrentUser user, int maxLength)
        {
            var name = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserId : user.DisplayName;
            var trimmed = name?.Trim() ?? "";
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }
    }
}
