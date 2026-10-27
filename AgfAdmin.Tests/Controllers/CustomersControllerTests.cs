using AgfAdmin.Controllers;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Controllers;

// POST /api/customers: 201, 200 bij identieke herhaling, 400 met veldsleutels, 409 met gelijkende klanten; GET ongewijzigd.
public class CustomersControllerTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<IActionResult> Call(Func<CustomersController, Task<IActionResult>> action)
    {
        await using var db = _factory.CreateContext();
        return await action(new CustomersController(db, new CustomerService(db), new RelationService(db)));
    }

    private static CreateOperationalCustomerDto Request(Guid requestId, string name = "DEMO Klant C", bool acknowledge = false) =>
        new() { RequestId = requestId, Name = name, ContactPerson = "Chris", Email = null, Phone = "06-1", ShippingAddress = null, AcknowledgeSimilarNames = acknowledge };

    [Fact]
    public async Task Create_Returns_201_Then_200_On_Replay_And_409_On_Changed_Payload()
    {
        var requestId = Guid.NewGuid();
        var created = Assert.IsType<ObjectResult>(await Call(c => c.CreateCustomer(Request(requestId))));
        Assert.Equal(201, created.StatusCode);
        var dto = Assert.IsType<CustomerDto>(created.Value);
        Assert.Equal(("DEMO Klant C", "Chris", 14), (dto.Name, dto.ContactPerson, dto.PaymentTermsDays));

        var replay = Assert.IsType<OkObjectResult>(await Call(c => c.CreateCustomer(Request(requestId))));
        Assert.Equal(dto.Id, Assert.IsType<CustomerDto>(replay.Value).Id);

        Assert.IsType<ConflictObjectResult>(await Call(c => c.CreateCustomer(Request(requestId, name: "DEMO Klant D"))));
        await using var db = _factory.CreateContext();
        Assert.Equal(1, await db.Customers.CountAsync());
    }

    [Fact]
    public async Task Create_Returns_400_With_Field_Keys_And_409_With_Similar_Customers_Until_Acknowledged()
    {
        var bad = Assert.IsType<BadRequestObjectResult>(await Call(c => c.CreateCustomer(new CreateOperationalCustomerDto { RequestId = Guid.NewGuid(), Name = " ", Email = "x" })));
        var problem = Assert.IsType<ValidationProblemDetails>(bad.Value);
        Assert.Contains("name", problem.Errors.Keys);
        Assert.Contains("email", problem.Errors.Keys);

        Assert.Equal(201, Assert.IsType<ObjectResult>(await Call(c => c.CreateCustomer(Request(Guid.NewGuid())))).StatusCode);
        var conflict = Assert.IsType<ConflictObjectResult>(await Call(c => c.CreateCustomer(Request(Guid.NewGuid(), name: "demo klant c"))));
        var body = conflict.Value!;
        Assert.Equal(CustomerService.SimilarNamesCode, body.GetType().GetProperty("code")!.GetValue(body));
        var similar = Assert.IsAssignableFrom<IEnumerable<SimilarCustomerDto>>(body.GetType().GetProperty("similarCustomers")!.GetValue(body));
        Assert.Equal("DEMO Klant C", Assert.Single(similar).Name);

        Assert.Equal(201, Assert.IsType<ObjectResult>(await Call(c => c.CreateCustomer(Request(Guid.NewGuid(), name: "demo klant c", acknowledge: true)))).StatusCode);
        // GET blijft de volledige klantenlijst op naam geven (sinds de relatiewerkplek met zoekcode en actief-vlag erbij).
        var list = Assert.IsType<OkObjectResult>(await Call(c => c.GetCustomers()));
        Assert.Equal(2, Assert.IsAssignableFrom<System.Collections.IEnumerable>(list.Value).Cast<object>().Count());
    }

    [Fact]
    public async Task List_Gives_The_City_Of_The_Main_Address_For_The_Customer_Search()
    {
        var created = Assert.IsType<CustomerDto>(Assert.IsType<ObjectResult>(await Call(c => c.CreateCustomer(Request(Guid.NewGuid(), name: "Lommerse Groothandel")))).Value);
        await using (var db = _factory.CreateContext())
        {
            var relationId = await db.Customers.Where(c => c.Id == created.Id).Select(c => c.RelationId).SingleAsync();
            db.RelationAddresses.Add(new RelationAddress { RelationId = relationId, Purpose = AddressPurpose.Delivery, City = "Venlo", CountryCode = "NL", IsActive = true, SortOrder = 0 });
            db.RelationAddresses.Add(new RelationAddress { RelationId = relationId, Purpose = AddressPurpose.Main, City = "Barendrecht", CountryCode = "NL", IsActive = true, SortOrder = 1 });
            await db.SaveChangesAsync();
        }
        var list = Assert.IsType<OkObjectResult>(await Call(c => c.GetCustomers()));
        var item = Assert.Single(Assert.IsAssignableFrom<System.Collections.IEnumerable>(list.Value).Cast<object>());
        Assert.Equal("Barendrecht", item.GetType().GetProperty("City")!.GetValue(item));
    }
}
