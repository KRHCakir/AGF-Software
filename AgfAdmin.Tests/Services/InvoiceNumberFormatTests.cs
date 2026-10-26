using System.Globalization;

namespace AgfAdmin.Tests.Services;

// De factuur-PDF toont getallen in Nederlandse notatie, ook als de server in een andere cultuur draait.
public class InvoiceNumberFormatTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("")]
    public void Numbers_use_dutch_notation_independent_of_the_server_culture(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            Assert.Equal(["€ 42.900,00", "€ 12,50", "1.560", "2,5", "21%", "5,5%"], [InvoiceNumberFormat.Money(42900m), InvoiceNumberFormat.Money(12.5m),
                InvoiceNumberFormat.Quantity(1560m), InvoiceNumberFormat.Quantity(2.5m), InvoiceNumberFormat.Percentage(21.0m), InvoiceNumberFormat.Percentage(5.50m)]);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
