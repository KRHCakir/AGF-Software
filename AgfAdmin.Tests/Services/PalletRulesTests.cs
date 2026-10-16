using AgfAdmin.Services;

namespace AgfAdmin.Tests.Services;

public class PalletRulesTests
{
    private static readonly string[] LogisticsStatuses = { "Expected", "InTransit", "Received", "Dispatched", "Delivered", "Cancelled" };
    private static readonly string[] QualityStatuses = { "Quarantine", "Approved", "Blocked", "Rejected" };

    // Expliciet uitgeschreven, los van de implementatie: alleen deze overgangen zijn toegestaan.
    private static readonly HashSet<(string, string)> AllowedLogistics = new()
    {
        ("Expected", "InTransit"),
        ("Expected", "Received"),
        ("Expected", "Cancelled"),
        ("InTransit", "Received"),
        ("InTransit", "Delivered"),
        ("Received", "Dispatched"),
        ("Dispatched", "Delivered")
    };

    private static readonly HashSet<(string, string)> AllowedQuality = new()
    {
        ("Quarantine", "Approved"),
        ("Quarantine", "Blocked"),
        ("Quarantine", "Rejected"),
        ("Approved", "Blocked"),
        ("Blocked", "Approved"),
        ("Blocked", "Rejected")
    };

    public static TheoryData<string, string, bool> AllLogisticsPairs()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var from in LogisticsStatuses)
        foreach (var to in LogisticsStatuses)
        {
            data.Add(from, to, AllowedLogistics.Contains((from, to)));
        }
        return data;
    }

    public static TheoryData<string, string, bool> AllQualityPairs()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var from in QualityStatuses)
        foreach (var to in QualityStatuses)
        {
            data.Add(from, to, AllowedQuality.Contains((from, to)));
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AllLogisticsPairs))]
    public void Logistics_Transition_Table_Is_Exact(string from, string to, bool allowed)
    {
        Assert.Equal(allowed, PalletRules.IsAllowedLogisticsTransition(from, to));
    }

    [Theory]
    [MemberData(nameof(AllQualityPairs))]
    public void Quality_Transition_Table_Is_Exact(string from, string to, bool allowed)
    {
        Assert.Equal(allowed, PalletRules.IsAllowedQualityTransition(from, to));
    }

    [Theory]
    [InlineData("expected", "InTransit")]
    [InlineData("Expected", "intransit")]
    [InlineData(null, "InTransit")]
    [InlineData("Expected", null)]
    [InlineData("Unknown", "Received")]
    public void Unknown_Or_Miscased_Logistics_Statuses_Are_Never_A_Transition(string from, string to)
    {
        Assert.False(PalletRules.IsAllowedLogisticsTransition(from, to));
    }

    [Theory]
    [InlineData("quarantine", "Approved")]
    [InlineData(null, "Approved")]
    [InlineData("Quarantine", "PartiallyApproved")]
    public void Unknown_Quality_Statuses_Are_Never_A_Transition(string from, string to)
    {
        Assert.False(PalletRules.IsAllowedQualityTransition(from, to));
    }

    [Theory]
    [InlineData("Expected", "Cancelled", true)]
    [InlineData("Expected", "InTransit", false)]
    [InlineData("Expected", "Received", false)]
    [InlineData("InTransit", "Received", false)]
    [InlineData("InTransit", "Delivered", false)]
    [InlineData("Received", "Dispatched", false)]
    [InlineData("Dispatched", "Delivered", false)]
    public void Only_Cancellation_Requires_A_Logistics_Reason(string from, string to, bool required)
    {
        Assert.Equal(required, PalletRules.LogisticsTransitionRequiresReason(from, to));
    }

    [Theory]
    [InlineData("Quarantine", "Approved", false)]
    [InlineData("Quarantine", "Blocked", true)]
    [InlineData("Quarantine", "Rejected", true)]
    [InlineData("Approved", "Blocked", true)]
    [InlineData("Blocked", "Approved", true)]
    [InlineData("Blocked", "Rejected", true)]
    public void Blocking_Rejecting_And_Lifting_A_Block_Require_A_Reason(string from, string to, bool required)
    {
        Assert.Equal(required, PalletRules.QualityTransitionRequiresReason(from, to));
    }

    [Theory]
    [InlineData("Approved", true)]
    [InlineData("Quarantine", false)]
    [InlineData("Blocked", false)]
    [InlineData("Rejected", false)]
    [InlineData("approved", false)]
    [InlineData(null, false)]
    public void Only_Approved_Quality_Is_Sellable(string status, bool sellable)
    {
        Assert.Equal(sellable, PalletRules.IsSellableQuality(status));
    }
}
