namespace AgfAdmin.Security
{
    // De ingelogde gebruiker aan de serverkant: wie iets keurt, verkoopt, reserveert of ontvangt. De actor van elke gebeurtenis
    // komt hiervandaan en nooit uit het verzoek van de client.
    public interface ICurrentUser
    {
        string UserId { get; }
        string DisplayName { get; }
        IReadOnlyList<string> Roles { get; }
        IReadOnlyList<string> Permissions { get; }
        bool Has(string permission);
    }

    public sealed class CurrentUser : ICurrentUser
    {
        public CurrentUser(string userId, string displayName, IEnumerable<string> roles, IEnumerable<string> permissions)
        {
            UserId = userId;
            DisplayName = displayName;
            Roles = roles.ToArray();
            Permissions = permissions.ToArray();
        }

        public string UserId { get; }
        public string DisplayName { get; }
        public IReadOnlyList<string> Roles { get; }
        public IReadOnlyList<string> Permissions { get; }
        public bool Has(string permission) => Permissions.Contains(permission, StringComparer.Ordinal);

        // De standaardgebruiker zoals DefaultUserOptions die zonder configuratie geeft: voor code die buiten een verzoek draait
        // (tests, seed) en dezelfde gebruiker moet zien als de API zonder aanmelding.
        public static CurrentUser FromOptions(DefaultUserOptions options) => new(
            options.Id, options.DisplayName, options.Roles is { Length: > 0 } roles ? roles.Distinct() : new[] { "Admin" },
            options.Permissions is { Length: > 0 } permissions ? permissions.Distinct() : Security.Permissions.All);

        public static readonly CurrentUser Default = FromOptions(new DefaultUserOptions());
    }
}
