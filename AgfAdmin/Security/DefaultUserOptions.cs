namespace AgfAdmin.Security
{
    // De ingestelde standaardgebruiker zolang er geen echte aanmelding is (configuratie `Identity:DefaultUser`). Alle verzoeken
    // komen van deze gebruiker; zijn weergavenaam komt in elke actor-kolom. Permissions leeg of niet ingesteld = alle gebieden;
    // Roles niet ingesteld = Admin. Lijsten blijven zonder beginwaarde: de configuratie voegt een lijst anders bij de beginwaarde.
    public sealed class DefaultUserOptions
    {
        public const string Section = "Identity:DefaultUser";

        public string Id { get; set; } = "admin";
        public string DisplayName { get; set; } = "Admin";
        public string[] Roles { get; set; }
        public string[] Permissions { get; set; }
    }
}
