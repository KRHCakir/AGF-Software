using AgfAdmin.Models;
using AgfAdmin.Services;

namespace AgfAdmin.Tests.Services;

// Pure regels van relaties fase 2 (gespiegeld in de frontend): IBAN/BIC, btw-vorm per land, strikte e-mail met tikfoutsuggestie,
// vervaldatum per basis, rolafspraken (incasso, leveringsconditie, handelsvoorwaarden) en krediet.
public class RelationFinanceRulesTests
{
    [Theory]
    [InlineData("NL91ABNA0417164300")]
    [InlineData("nl91 abna 0417 1643 00")]
    [InlineData("DE89370400440532013000")]
    [InlineData("DE89 3704 0044 0532 0130 00")]
    [InlineData("BE68539007547034")]
    [InlineData("GB82WEST12345698765432")]
    public void Valid_Ibans_Pass_After_Normalization(string raw)
    {
        var normalized = BankAccountRules.NormalizeIban(raw);
        Assert.DoesNotContain(' ', normalized);
        Assert.Equal(normalized.ToUpperInvariant(), normalized);
        Assert.Equal(BankAccountRules.IbanProblem.None, BankAccountRules.CheckIban(normalized));
    }

    [Theory]
    [InlineData("NL91ABNA0417164301", BankAccountRules.IbanProblem.Checksum)]
    [InlineData("DE89370400440532013001", BankAccountRules.IbanProblem.Checksum)]
    [InlineData("NL91ABNA041716430", BankAccountRules.IbanProblem.Length)]
    [InlineData("BE6853900754703", BankAccountRules.IbanProblem.Length)]
    [InlineData("US12345678901234", BankAccountRules.IbanProblem.UnknownCountry)]
    [InlineData("1234ABNA0417164300", BankAccountRules.IbanProblem.Shape)]
    [InlineData("NL91-ABNA-0417164300", BankAccountRules.IbanProblem.Shape)]
    public void Bad_Ibans_Report_The_First_Problem(string raw, BankAccountRules.IbanProblem expected)
    {
        var normalized = BankAccountRules.NormalizeIban(raw);
        Assert.Equal(expected, BankAccountRules.CheckIban(normalized));
        Assert.False(string.IsNullOrEmpty(BankAccountRules.IbanMessage(expected, normalized)));
    }

    [Theory]
    [InlineData("ABNANL2A", true)]
    [InlineData("abnanl2a", true)]
    [InlineData("DEUTDEFF500", true)]
    [InlineData("ABNA NL 2A", true)]
    [InlineData("ABNANL2", false)]
    [InlineData("ABNANL2AX", false)]
    [InlineData("1BNANL2A", false)]
    [InlineData("ABNA1L2A", false)]
    public void Bic_Has_Eight_Or_Eleven_Characters_In_The_Bank_Country_Location_Branch_Shape(string raw, bool valid) =>
        Assert.Equal(valid, BankAccountRules.IsBic(BankAccountRules.NormalizeBic(raw)));

    [Theory]
    [InlineData("NL812345678B01", VatRules.VatProblem.None)]
    [InlineData("NL 8123.45.678-b01", VatRules.VatProblem.None)]
    [InlineData("NL812345678", VatRules.VatProblem.CountryPattern)]
    [InlineData("NL812345678B1", VatRules.VatProblem.CountryPattern)]
    [InlineData("DE123456789", VatRules.VatProblem.None)]
    [InlineData("DE12345678", VatRules.VatProblem.CountryPattern)]
    [InlineData("BE0123456789", VatRules.VatProblem.None)]
    [InlineData("BE1123456789", VatRules.VatProblem.None)]
    [InlineData("BE2123456789", VatRules.VatProblem.CountryPattern)]
    [InlineData("BE123456789", VatRules.VatProblem.CountryPattern)]
    [InlineData("FRXX123456789", VatRules.VatProblem.None)]
    [InlineData("FR12123456789", VatRules.VatProblem.None)]
    [InlineData("FR1212345678", VatRules.VatProblem.CountryPattern)]
    [InlineData("XI123456789", VatRules.VatProblem.None)]
    [InlineData("XI123456789012", VatRules.VatProblem.None)]
    [InlineData("XIGD123", VatRules.VatProblem.None)]
    [InlineData("XIHA567", VatRules.VatProblem.None)]
    [InlineData("XI12345", VatRules.VatProblem.CountryPattern)]
    [InlineData("EL123456789", VatRules.VatProblem.None)]
    [InlineData("GR123456789", VatRules.VatProblem.GreeceUsesEl)]
    [InlineData("ATU12345678", VatRules.VatProblem.None)]
    [InlineData("ESX1234567X", VatRules.VatProblem.None)]
    [InlineData("CHE123456789", VatRules.VatProblem.None)]   // geen EU-land: algemene vorm
    [InlineData("GB123456789", VatRules.VatProblem.None)]
    [InlineData("12345", VatRules.VatProblem.Shape)]
    public void Vat_Shape_Follows_The_Country_Pattern_With_A_Generic_Fallback(string raw, VatRules.VatProblem expected) =>
        Assert.Equal(expected, VatRules.Check(RelationRules.NormalizeVat(raw)));

    [Fact]
    public void Greek_Numbers_Get_A_Hint_To_Use_El()
    {
        Assert.Equal("EL123456789", VatRules.GreekSuggestion("GR123456789"));
        Assert.Contains("EL123456789", VatRules.Message(VatRules.VatProblem.GreeceUsesEl, "GR123456789"));
        Assert.Contains("NL123456789B01", VatRules.Message(VatRules.VatProblem.CountryPattern, "NL1"));
    }

    [Theory]
    [InlineData("jan@fruit.nl", true)]
    [InlineData("jan.de.vries+facturen@fruit-import.co.uk", true)]
    [InlineData("o'neil@example.ie", true)]
    [InlineData("jan@fruit", false)]
    [InlineData("jan@@fruit.nl", false)]
    [InlineData("jan@fr@uit.nl", false)]
    [InlineData("jan @fruit.nl", false)]
    [InlineData("jan@fruit.n", false)]
    [InlineData("jan@fruit.12", false)]
    [InlineData(".jan@fruit.nl", false)]
    [InlineData("jan.@fruit.nl", false)]
    [InlineData("ja..n@fruit.nl", false)]
    [InlineData("jan@-fruit.nl", false)]
    [InlineData("jan@fruit_import.nl", false)]
    [InlineData("jan(at)fruit.nl", false)]
    public void Strict_Email_Shape(string value, bool valid) => Assert.Equal(valid, EmailRules.IsStrict(value));

    [Theory]
    [InlineData("jan@gmial.com", "jan@gmail.com")]
    [InlineData("jan@GMAIL.CON", "jan@gmail.com")]
    [InlineData("info@hotmial.com", "info@hotmail.com")]
    [InlineData("a@outllok.com", "a@outlook.com")]
    [InlineData("a@ziggo.nll", "a@ziggo.nl")]
    [InlineData("a@yahooo.com", "a@yahoo.com")]
    [InlineData("a@gmail.com", null)]
    [InlineData("a@fruit.nl", null)]
    public void Known_Domain_Typos_Give_A_Suggestion_Not_An_Error(string value, string suggestion) =>
        Assert.Equal(suggestion, EmailRules.TypoSuggestion(value));

    [Theory]
    [InlineData("2026-10-15", 30, PaymentConditionBasis.InvoiceDate, "2026-11-14")]
    [InlineData("2026-10-15", 0, PaymentConditionBasis.InvoiceDate, "2026-10-15")]
    [InlineData("2026-10-15", 30, PaymentConditionBasis.EndOfMonth, "2026-11-30")]
    [InlineData("2026-10-31", 0, PaymentConditionBasis.EndOfMonth, "2026-10-31")]
    [InlineData("2026-01-31", 30, PaymentConditionBasis.EndOfMonth, "2026-03-02")]
    [InlineData("2027-02-10", 0, PaymentConditionBasis.EndOfMonth, "2027-02-28")]
    [InlineData("2028-02-10", 0, PaymentConditionBasis.EndOfMonth, "2028-02-29")]
    [InlineData("2028-02-10", 30, PaymentConditionBasis.EndOfMonth, "2028-03-30")]
    [InlineData("2027-12-20", 14, PaymentConditionBasis.EndOfMonth, "2028-01-14")]
    public void Due_Date_Follows_The_Basis(string invoice, int days, string basis, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), PaymentConditionRules.DueDate(DateOnly.Parse(invoice), days, basis));

    [Fact]
    public void Discount_Date_Counts_From_The_Invoice_Date_And_Utc_Variant_Keeps_The_Calendar_Day()
    {
        var condition = new PaymentCondition { Days = 30, Basis = PaymentConditionBasis.EndOfMonth, DiscountPercent = 2m, DiscountDays = 8 };
        Assert.Equal(new DateOnly(2026, 10, 23), PaymentConditionRules.DiscountDate(new DateOnly(2026, 10, 15), condition));
        Assert.Equal(new DateOnly(2026, 11, 30), PaymentConditionRules.DueDate(new DateOnly(2026, 10, 15), condition));
        Assert.Null(PaymentConditionRules.DiscountDate(new DateOnly(2026, 10, 15), new PaymentCondition { Days = 30, Basis = PaymentConditionBasis.InvoiceDate }));
        var utc = PaymentConditionRules.DueDate(new DateTime(2028, 2, 10, 0, 0, 0, DateTimeKind.Utc), 30, PaymentConditionBasis.EndOfMonth);
        Assert.Equal((new DateTime(2028, 3, 30), DateTimeKind.Utc), (utc, utc.Kind));
    }

    [Theory]
    [InlineData("30D", 30, "InvoiceDate", null, null, null)]
    [InlineData("EOM30", 30, "EndOfMonth", 2.0, 8, null)]
    [InlineData("30D", 30, "InvoiceDate", 2.0, null, "discountPercent")]
    [InlineData("30D", 30, "InvoiceDate", null, 8, "discountPercent")]
    [InlineData("30D", 30, "InvoiceDate", 0.0, 8, "discountPercent")]
    [InlineData("30D", 30, "InvoiceDate", 2.0, 31, "discountDays")]
    [InlineData("30D", 366, "InvoiceDate", null, null, "days")]
    [InlineData("30 D", 30, "InvoiceDate", null, null, "code")]
    [InlineData("30D", 30, "Weekly", null, null, "basis")]
    public void Payment_Condition_Validation(string code, int days, string basis, double? discount, int? discountDays, string errorKey)
    {
        var errors = new Dictionary<string, List<string>>();
        PaymentConditionRules.Validate(code, "nl", "en", days, basis, discount == null ? null : (decimal)discount.Value, discountDays, errors);
        if (errorKey == null) Assert.Empty(errors);
        else Assert.Contains(errorKey, errors.Keys);
    }

    private static readonly DateOnly Today = new(2026, 10, 20);

    private static (RoleTermsRules.Terms Terms, Dictionary<string, List<string>> Errors) Terms(RoleTermsRules.Terms raw)
    {
        var errors = new Dictionary<string, List<string>>();
        return (RoleTermsRules.NormalizeTerms(raw, "customer", Today, errors), errors);
    }

    [Fact]
    public void Direct_Debit_Needs_A_Mandate_Reference_And_A_Signing_Date_Not_In_The_Future_Other_Methods_Clear_Them()
    {
        var missing = Terms(new("DirectDebit", null, null, null, null, null, null, null, null));
        Assert.Contains("customer.mandateReference", missing.Errors.Keys);
        Assert.Contains("customer.mandateSignedOn", missing.Errors.Keys);
        var future = Terms(new("DirectDebit", "MND-1", Today.AddDays(1), null, null, null, null, null, null));
        Assert.Contains("customer.mandateSignedOn", future.Errors.Keys);
        var ok = Terms(new("DirectDebit", " MND-1 ", Today, null, null, null, null, null, null));
        Assert.Empty(ok.Errors);
        Assert.Equal(("MND-1", (DateOnly?)Today), (ok.Terms.MandateReference, ok.Terms.MandateSignedOn));
        var transfer = Terms(new(null, "MND-1", Today, null, null, null, null, null, null));
        Assert.Empty(transfer.Errors);
        Assert.Equal((PaymentMethods.BankTransfer, (string)null, (DateOnly?)null), (transfer.Terms.PaymentMethod, transfer.Terms.MandateReference, transfer.Terms.MandateSignedOn));
        Assert.Contains("customer.paymentMethod", Terms(new("Cheque", null, null, null, null, null, null, null, null)).Errors.Keys);
    }

    [Fact]
    public void Delivery_Term_Is_An_Incoterm_With_Place_Or_An_Own_Term_And_Other_Needs_A_Text()
    {
        var incoterm = Terms(new(null, null, null, "Incoterm", "fca", "Venlo", "weg", null, null));
        Assert.Empty(incoterm.Errors);
        Assert.Equal(("FCA", "Venlo", (string)null), (incoterm.Terms.Incoterm, incoterm.Terms.IncotermPlace, incoterm.Terms.DeliveryTermText));
        Assert.Contains("customer.incotermPlace", Terms(new(null, null, null, "Incoterm", "FCA", null, null, null, null)).Errors.Keys);
        Assert.Contains("customer.incoterm", Terms(new(null, null, null, "Incoterm", "FOT", "Venlo", null, null, null)).Errors.Keys);
        Assert.Contains("customer.deliveryTermText", Terms(new(null, null, null, "Other", null, null, " ", null, null)).Errors.Keys);
        var franco = Terms(new(null, null, null, "Franco", "FCA", "Venlo", "Franco Rotterdam", null, null));
        Assert.Empty(franco.Errors);
        Assert.Equal(((string)null, (string)null, "Franco Rotterdam"), (franco.Terms.Incoterm, franco.Terms.IncotermPlace, franco.Terms.DeliveryTermText));
        var none = Terms(new(null, null, null, null, "FCA", "Venlo", "x", null, null));
        Assert.Equal(((string)null, (string)null, (string)null), (none.Terms.Incoterm, none.Terms.IncotermPlace, none.Terms.DeliveryTermText));
        Assert.Contains("customer.deliveryTermKind", Terms(new(null, null, null, "Ex", null, null, null, null, null)).Errors.Keys);
    }

    [Fact]
    public void Trade_Conditions_Other_Needs_A_Text_And_Known_Kinds_Clear_It()
    {
        Assert.Contains("customer.tradeConditionsText", Terms(new(null, null, null, null, null, null, null, "Other", null)).Errors.Keys);
        var other = Terms(new(null, null, null, null, null, null, null, "Other", "GAFTA"));
        Assert.Equal(("Other", "GAFTA"), (other.Terms.TradeConditions, other.Terms.TradeConditionsText));
        var frugiventa = Terms(new(null, null, null, null, null, null, null, "Frugiventa", "x"));
        Assert.Equal(("Frugiventa", (string)null), (frugiventa.Terms.TradeConditions, frugiventa.Terms.TradeConditionsText));
        Assert.Contains("customer.tradeConditions", Terms(new(null, null, null, null, null, null, null, "Fruit", null)).Errors.Keys);
    }

    [Fact]
    public void Credit_Insurance_Needs_Insurer_And_Limit_Other_Needs_A_Name_Uninsured_Clears_The_Insurance_Fields()
    {
        Dictionary<string, List<string>> Errors(RoleTermsRules.Credit raw)
        {
            var errors = new Dictionary<string, List<string>>();
            RoleTermsRules.NormalizeCredit(raw, "customer", errors);
            return errors;
        }
        Assert.Contains("customer.creditLimit", Errors(new(-1m, false, null, null, null, null, null)).Keys);
        Assert.Contains("customer.creditLimit", Errors(new(1.001m, false, null, null, null, null, null)).Keys);
        var missing = Errors(new(1000m, true, null, null, null, null, null));
        Assert.Contains("customer.insurer", missing.Keys);
        Assert.Contains("customer.insuredLimit", missing.Keys);
        Assert.Contains("customer.insurerText", Errors(new(1000m, true, "Other", null, 500m, null, null)).Keys);
        Assert.Empty(Errors(new(1000m, true, "Atradius", null, 500m, "POL-1", Today)));
        var cleared = RoleTermsRules.NormalizeCredit(new(1000m, false, "Atradius", "x", 500m, "POL-1", Today), "customer", new());
        Assert.Equal(((string)null, (decimal?)null, (string)null, (DateOnly?)null), (cleared.Insurer, cleared.InsuredLimit, cleared.InsurerReference, cleared.InsuredUntil));
    }

    [Fact]
    public void Insurance_Expiry_And_Gap()
    {
        Assert.True(RoleTermsRules.InsuranceExpired(Today.AddDays(-1), Today));
        Assert.False(RoleTermsRules.InsuranceExpired(Today, Today));
        Assert.True(RoleTermsRules.InsuranceExpiresSoon(Today, Today));
        Assert.True(RoleTermsRules.InsuranceExpiresSoon(Today.AddDays(30), Today));
        Assert.False(RoleTermsRules.InsuranceExpiresSoon(Today.AddDays(31), Today));
        Assert.False(RoleTermsRules.InsuranceExpiresSoon(null, Today));
        Assert.Equal(400m, RoleTermsRules.InsuranceGap(1000m, true, 600m));
        Assert.Null(RoleTermsRules.InsuranceGap(1000m, true, 1000m));
        Assert.Null(RoleTermsRules.InsuranceGap(1000m, false, 600m));
        Assert.Null(RoleTermsRules.InsuranceGap(null, true, 600m));
    }

    [Theory]
    [InlineData("https://prod.osapiens.cloud/portal?x=%252F", true)]
    [InlineData("http://example.com", false)]
    [InlineData("/relative/path", false)]
    [InlineData("example.com", false)]
    public void Portal_Url_Must_Be_Absolute_Https(string url, bool valid) => Assert.Equal(valid, RelationRules.IsHttpsUrl(url));
}
