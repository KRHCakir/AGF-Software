using AgfAdmin.Services;

namespace AgfAdmin.Tests.Services;

public class SsccTests
{
    [Theory]
    [InlineData("006141411234567890")] // GS1-voorbeeld (00) 0 0614141 123456789 0
    [InlineData("106141411234567897")]
    [InlineData("387123450000000012")]
    [InlineData("000000000000000000")]
    public void Valid_Gs1_Sscc_Is_Accepted(string sscc)
    {
        Assert.True(Sscc.IsValid(sscc));
    }

    [Theory]
    [InlineData("006141411234567891")]  // fout controlecijfer
    [InlineData("106141411234567890")]  // fout controlecijfer
    [InlineData("00614141123456789")]   // 17 cijfers
    [InlineData("0061414112345678900")] // 19 cijfers
    [InlineData("00614141123456789A")]
    [InlineData(" 006141411234567890")]
    [InlineData("006141411234567890 ")]
    [InlineData("00 6141411234567890")]
    [InlineData("(00)006141411234567890")]
    [InlineData("٠٠٦١٤١٤١١٢٣٤٥٦٧٨٩٠")]   // Arabisch-Indische cijfers: geen ASCII
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_Sscc_Is_Rejected(string sscc)
    {
        Assert.False(Sscc.IsValid(sscc));
    }
}
