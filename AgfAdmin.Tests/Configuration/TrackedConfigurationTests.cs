using Microsoft.Extensions.Configuration;

namespace AgfAdmin.Tests.Configuration;

// Bewaakt dat getrackte configuratie geen echte database-credentials bevat.
public class TrackedConfigurationTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Software-AGF.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    private static IConfiguration LoadJson(params string[] relativePath)
    {
        var path = Path.Combine(new[] { RepositoryRoot() }.Concat(relativePath).ToArray());
        Assert.True(File.Exists(path), $"Expected configuration file at {path}");

        return new ConfigurationBuilder()
            .AddJsonFile(path, optional: false, reloadOnChange: false)
            .Build();
    }

    [Fact]
    public void AppSettings_Does_Not_Contain_DefaultConnection()
    {
        var configuration = LoadJson("AgfAdmin", "appsettings.json");

        // Geen Assert.Null: die zou bij falen de (mogelijk geheime) waarde in de testoutput tonen.
        Assert.True(
            configuration.GetConnectionString("DefaultConnection") == null,
            "AgfAdmin/appsettings.json must not contain ConnectionStrings:DefaultConnection.");
    }

    [Fact]
    public void DevelopmentExample_Uses_Password_Placeholder()
    {
        var configuration = LoadJson("AgfAdmin", "appsettings.Development.json.example");
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var password = connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0].Trim().Equals("Password", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair[1].Trim())
            .SingleOrDefault();

        Assert.NotNull(password);
        Assert.StartsWith("<", password);
        Assert.EndsWith(">", password);
    }
}
