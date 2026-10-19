using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Klant aanmaken vanuit Verkoop: validatie en normalisatie, idempotentie op requestId, gelijkende namen met expliciete
// bevestiging, bestaande standaardbetaaltermijn van de kantoorflow en een gelijktijdige herhaling.
public class CustomerServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static CreateOperationalCustomerDto Request(string name = "Groentehal Jansen", Guid? requestId = null, bool acknowledge = false) => new()
    {
        RequestId = requestId ?? Guid.NewGuid(), Name = name, ContactPerson = "  Piet Jansen ", Email = "piet@jansen.example", Phone = "0612345678", ShippingAddress = "Marktweg 1, Venlo", AcknowledgeSimilarNames = acknowledge
    };

    private async Task<ServiceResult<CustomerDto>> CreateAsync(CreateOperationalCustomerDto request, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new CustomerService(db).CreateAsync(request);
    }

    private async Task<int> SeedAsync(string name, int paymentTerms = 30)
    {
        await using var db = _factory.CreateContext();
        var customer = new Customer { Name = name, PaymentTermsDays = paymentTerms, ContactPerson = "", Email = null };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    [Fact]
    public async Task Create_Stores_Trimmed_Values_Blank_As_Null_And_Keeps_The_Office_Flow_Payment_Terms()
    {
        var request = Request();
        request.Phone = "   ";
        request.ShippingAddress = null;

        var result = await CreateAsync(request);

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal(("Groentehal Jansen", "Piet Jansen", "piet@jansen.example"), (result.Value.Name, result.Value.ContactPerson, result.Value.Email));
        Assert.Null(result.Value.Phone);
        Assert.Null(result.Value.ShippingAddress);
        Assert.Equal(CustomerService.DefaultPaymentTermsDays, result.Value.PaymentTermsDays);
        await using var db = _factory.CreateContext();
        var stored = await db.Customers.SingleAsync(c => c.Id == result.Value.Id);
        Assert.Equal(request.RequestId, stored.RequestId);
        Assert.Null(stored.Phone);
        Assert.Null(stored.ShippingAddress);
        Assert.Equal(14, stored.PaymentTermsDays);
    }

    [Theory]
    [InlineData("request-id", "requestId")]
    [InlineData("name-blank", "name")]
    [InlineData("name-long", "name")]
    [InlineData("contact-long", "contactPerson")]
    [InlineData("email-shape", "email")]
    [InlineData("email-long", "email")]
    [InlineData("phone-long", "phone")]
    [InlineData("address-long", "shippingAddress")]
    public async Task Create_Rejects_Invalid_Input_With_The_Field_Key(string invalidCase, string key)
    {
        var request = Request();
        switch (invalidCase)
        {
            case "request-id": request.RequestId = Guid.Empty; break;
            case "name-blank": request.Name = "   "; break;
            case "name-long": request.Name = new string('a', CustomerService.MaxNameLength + 1); break;
            case "contact-long": request.ContactPerson = new string('b', CustomerService.MaxContactPersonLength + 1); break;
            case "email-shape": request.Email = "geen mailadres"; break;
            case "email-long": request.Email = new string('c', CustomerService.MaxEmailLength) + "@x.nl"; break;
            case "phone-long": request.Phone = new string('1', CustomerService.MaxPhoneLength + 1); break;
            default: request.ShippingAddress = new string('d', CustomerService.MaxShippingAddressLength + 1); break;
        }

        var result = await CreateAsync(request);

        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(key, result.Errors.Keys);
        await using var db = _factory.CreateContext();
        Assert.Equal(0, await db.Customers.CountAsync());
    }

    [Fact]
    public async Task Identical_Replay_Returns_The_Existing_Customer_Even_With_Different_Blank_Spelling_And_Acknowledgement()
    {
        var request = Request();
        var first = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Created, first.Status);
        var retry = Request(requestId: request.RequestId, acknowledge: true);
        retry.ContactPerson = "Piet Jansen";
        retry.ShippingAddress = " Marktweg 1, Venlo  ";

        var result = await CreateAsync(retry);

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        Assert.Equal(first.Value.Id, result.Value.Id);
        await using var db = _factory.CreateContext();
        Assert.Equal(1, await db.Customers.CountAsync());
    }

    [Theory]
    [InlineData("name")]
    [InlineData("phone")]
    [InlineData("phone-blank-vs-value")]
    public async Task Replay_With_A_Different_Payload_Is_Conflict(string change)
    {
        var request = Request();
        if (change == "phone-blank-vs-value") request.Phone = null;
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(request)).Status);
        var retry = Request(requestId: request.RequestId);
        switch (change)
        {
            case "name": retry.Name = "Groentehal Janssen"; break;
            case "phone": retry.Phone = "0600000000"; break;
            default: retry.Phone = "0612345678"; break;
        }

        var result = await CreateAsync(retry);

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Contains("different customer", result.Message);
        await using var db = _factory.CreateContext();
        Assert.Equal(1, await db.Customers.CountAsync());
    }

    [Theory]
    [InlineData("groentehal jansen")]
    [InlineData("GROENTEHAL  JANSEN")]
    [InlineData("Groentehal-Jansen B.V.")]
    [InlineData("Jansen")]
    [InlineData("Gröentehal Jánsen")]
    public async Task Similar_Name_Without_Acknowledgement_Is_Conflict_With_The_Similar_Customers_And_Creates_Nothing(string name)
    {
        var existingId = await SeedAsync("Groentehal Jansen");
        await SeedAsync("Fruitpaleis Pietersen");

        var result = await CreateAsync(Request(name));

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        var body = result.ConflictBody;
        Assert.NotNull(body);
        var code = body.GetType().GetProperty("code")!.GetValue(body);
        var similar = Assert.IsAssignableFrom<IEnumerable<SimilarCustomerDto>>(body.GetType().GetProperty("similarCustomers")!.GetValue(body)).ToList();
        Assert.Equal(CustomerService.SimilarNamesCode, code);
        Assert.Equal(existingId, Assert.Single(similar).Id);
        Assert.Null(similar[0].ContactPerson); // lege waarde uit de database wordt null
        await using var db = _factory.CreateContext();
        Assert.Equal(2, await db.Customers.CountAsync());
    }

    [Fact]
    public async Task Acknowledged_Similar_Name_Creates_A_Second_Customer_And_Short_Or_Unrelated_Names_Never_Count_As_Similar()
    {
        await SeedAsync("Groentehal Jansen");
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(Request("Groentehal Jansen", acknowledge: true))).Status);
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(Request("AB"))).Status); // te kort om als "bevat" te tellen
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(Request("Fruitpaleis Pietersen"))).Status);
        await using var db = _factory.CreateContext();
        Assert.Equal(4, await db.Customers.CountAsync());
        Assert.Equal(2, await db.Customers.CountAsync(c => c.Name == "Groentehal Jansen"));
    }

    [Fact]
    public async Task Concurrent_Identical_Requests_Create_One_Customer()
    {
        var request = Request();
        ServiceResultStatus inner = default;

        var outer = await CreateAsync(request, new ConcurrentSaveInterceptor(async () => inner = (await CreateAsync(request)).Status));

        Assert.Equal(ServiceResultStatus.Created, inner);
        Assert.Equal(ServiceResultStatus.Ok, outer.Status);
        await using var db = _factory.CreateContext();
        Assert.Equal(1, await db.Customers.CountAsync());
    }

    [Theory]
    [InlineData("Groentehal Jansen", "groentehal jansen")]
    [InlineData("  Gröente-Hal  Jánsen B.V. ", "groente hal jansen b v")]
    [InlineData("", "")]
    public void NormalizeName_Folds_Case_Accents_Punctuation_And_Whitespace(string input, string expected) =>
        Assert.Equal(expected, CustomerService.NormalizeName(input));
}
