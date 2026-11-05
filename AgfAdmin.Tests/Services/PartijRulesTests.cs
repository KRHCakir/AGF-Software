using AgfAdmin.Services;

namespace AgfAdmin.Tests.Services;

// Sleutel, formaat en periode van de partijnummering. Dezelfde gevallen staan in agf-frontend/src/lib/partijRules.test.ts.
public class PartijRulesTests
{
    [Theory]
    [InlineData("2610251", "2610251")]
    [InlineData("2610 251", "2610251")]
    [InlineData("2610-251", "2610251")]
    [InlineData(" 26-10 251 ", "2610251")]
    [InlineData("ab-12", "AB12")]
    [InlineData(" - ", null)]
    [InlineData(null, null)]
    public void NumberKey_Ignores_Case_Spaces_And_Hyphens(string number, string key) => Assert.Equal(key, PartijRules.NumberKey(number));

    [Fact]
    public void Two_Almost_Equal_Numbers_Keep_Different_Keys()
    {
        Assert.NotEqual(PartijRules.NumberKey("2610251"), PartijRules.NumberKey("2610252"));
        Assert.NotEqual(PartijRules.NumberKey("2610251"), PartijRules.NumberKey("26102510"));
        Assert.NotEqual(PartijRules.NumberKey("2610251"), PartijRules.NumberKey("2610.251"));
    }

    [Theory]
    [InlineData("{JJ}{MM}{volgnr}", 2026, 10, 251, "2610251")]
    [InlineData("{JJ}{MM}{volgnr}", 2026, 11, 1, "2611001")]
    [InlineData("{JJJJ}-{volgnr}", 2026, 11, 7, "2026-007")]
    [InlineData("P{JJ}/{volgnr}", 2027, 1, 1234, "P27/1234")]
    public void Format_Fills_The_Placeholders(string format, int year, int month, int sequence, string expected) =>
        Assert.Equal(expected, PartijRules.Format(format, new DateOnly(year, month, 15), sequence));

    [Theory]
    [InlineData("{JJ}{MM}")]                 // geen volgnummer
    [InlineData("{JJ}{volgnr}{volgnr}")]     // twee keer
    [InlineData("{JJ}{DD}{volgnr}")]         // onbekende plaatshouder
    [InlineData("{jj}{volgnr}")]             // hoofdlettergevoelig
    [InlineData("{JJ}{volgnr")]              // losse accolade
    [InlineData("")]
    [InlineData("   ")]
    public void Invalid_Formats_Are_Refused_With_A_Message(string format) => Assert.NotNull(PartijRules.FormatError(format));

    [Theory]
    [InlineData("{JJ}{MM}{volgnr}")]
    [InlineData("{JJJJ}-{volgnr}")]
    [InlineData("{volgnr}")]
    public void Valid_Formats_Are_Accepted(string format) => Assert.Null(PartijRules.FormatError(format));

    [Theory]
    [InlineData("Never", "")]
    [InlineData("Yearly", "2026")]
    [InlineData("Monthly", "2026-11")]
    public void Sequence_Period_Follows_The_Reset(string reset, string period) =>
        Assert.Equal(period, PartijRules.SequencePeriod(reset, new DateOnly(2026, 11, 4)));
}
