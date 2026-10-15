namespace AgfAdmin.Configuration
{
    public static class DatabaseConfiguration
    {
        private const string ConnectionStringName = "DefaultConnection";

        public static string GetRequiredConnectionString(IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is required.");
            }

            return connectionString;
        }
    }
}
