using AgfAdmin.Services;

namespace AgfAdmin.Tests.Services;

public class AvailabilityRulesTests
{
    private static readonly string[] LogisticsStatuses = { "Expected", "InTransit", "Received", "Dispatched", "Delivered", "Cancelled" };
    private static readonly string[] Locations = { "Supplier", "Warehouse", "Crossdock", "InTransit", "Customer", "External" };

    // Expliciet uitgeschreven, los van de implementatie.
    private static string ExpectedCategory(string status, string location) => status switch
    {
        "Expected" => "expectedOnPallets",
        "InTransit" => "inTransit",
        "Received" => location == "Warehouse" ? "warehouse" : "receivedOutsideWarehouse",
        _ => "closed" // Dispatched, Delivered, Cancelled: niet meer in ons bezit of nooit gekomen
    };

    public static TheoryData<string, string, string> AllStatusLocationPairs()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var status in LogisticsStatuses)
        foreach (var location in Locations)
        {
            data.Add(status, location, ExpectedCategory(status, location));
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AllStatusLocationPairs))]
    public void Every_Status_And_Location_Maps_To_Exactly_One_Category(string status, string location, string category)
    {
        Assert.Equal(category, AvailabilityRules.Classify(status, location));
    }

    [Theory]
    [InlineData("received", "Warehouse")]
    [InlineData(null, "Warehouse")]
    [InlineData("Shipped", "Warehouse")]
    public void Unknown_Status_Has_No_Category(string status, string location)
    {
        Assert.Null(AvailabilityRules.Classify(status, location));
    }

    [Theory]
    [InlineData("warehouse", "Approved", "Warehouse", true)]
    [InlineData("warehouse", "Quarantine", "Warehouse", false)]
    [InlineData("warehouse", "Blocked", "Warehouse", false)]
    [InlineData("warehouse", "Rejected", "Warehouse", false)]
    [InlineData("warehouse", "Approved", "DirectTrade", false)]
    [InlineData("receivedOutsideWarehouse", "Approved", "Warehouse", false)]
    [InlineData("inTransit", "Approved", "Warehouse", false)]
    [InlineData("expectedOnPallets", "Approved", "Warehouse", false)]
    [InlineData("closed", "Approved", "Warehouse", false)]
    public void Only_Approved_Warehouse_Route_Content_In_The_Warehouse_Is_Available_Now(string category, string quality, string route, bool available)
    {
        Assert.Equal(available, AvailabilityRules.IsAvailableNow(category, quality, route));
    }

    [Theory]
    [InlineData("Expected", true)]
    [InlineData("PartiallyReceived", true)]
    [InlineData("Received", false)]
    [InlineData("Delivered", false)]
    [InlineData("Cancelled", false)]
    public void Only_Expected_And_PartiallyReceived_Commitments_Have_Open_Expected_Supply(string status, bool open)
    {
        Assert.Equal(open, AvailabilityRules.IsOpenCommitmentStatus(status));
    }
}
