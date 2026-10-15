using AgfAdmin.Configuration;
using Microsoft.Extensions.Configuration;

namespace AgfAdmin.Tests.Configuration;

public class DatabaseConfigurationTests
{
    private const string RequiredMessage = "Connection string 'DefaultConnection' is required.";

    private static IConfiguration BuildConfiguration(Dictionary<string, string> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    [Fact]
    public void Missing_DefaultConnection_Throws_Clear_Configuration_Error()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string>());

        var exception = Assert.Throws<InvalidOperationException>(
            () => DatabaseConfiguration.GetRequiredConnectionString(configuration));

        Assert.Equal(RequiredMessage, exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Blank_DefaultConnection_Throws_Clear_Configuration_Error(string value)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string>
        {
            ["ConnectionStrings:DefaultConnection"] = value
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => DatabaseConfiguration.GetRequiredConnectionString(configuration));

        Assert.Equal(RequiredMessage, exception.Message);
    }

    [Fact]
    public void DefaultConnection_Returns_Configured_Value()
    {
        const string connectionString = "Host=test-host;Database=test-db";
        var configuration = BuildConfiguration(new Dictionary<string, string>
        {
            ["ConnectionStrings:DefaultConnection"] = connectionString
        });

        var result = DatabaseConfiguration.GetRequiredConnectionString(configuration);

        Assert.Equal(connectionString, result);
    }
}
