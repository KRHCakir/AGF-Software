using AgfAdmin.Controllers;
using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Tests.Controllers;

// HTTP-vertaling van relaties fase 2: betalingscondities (201/200/400/409/204), krediet per klant (200/404) en bankbevestiging.
public class RelationFinanceControllerTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<IActionResult> Conditions(Func<PaymentConditionsController, Task<IActionResult>> action)
    {
        await using var db = _factory.CreateContext();
        return await action(new PaymentConditionsController(new PaymentConditionService(db)));
    }

    private async Task<IActionResult> Customers(Func<CustomersController, Task<IActionResult>> action)
    {
        await using var db = _factory.CreateContext();
        return await action(new CustomersController(db, new CustomerService(db), new RelationService(db)));
    }

    private async Task<IActionResult> Relations(Func<RelationsController, Task<IActionResult>> action)
    {
        await using var db = _factory.CreateContext();
        return await action(new RelationsController(new RelationService(db)));
    }

    [Fact]
    public async Task Payment_Condition_Endpoints_Map_To_Http_And_Delete_In_Use_Is_409_With_A_Code()
    {
        var request = new SavePaymentConditionDto { RequestId = Guid.NewGuid(), Code = "30D", DescriptionNl = "30 dagen", DescriptionEn = "30 days", Days = 30, Basis = "InvoiceDate" };
        var created = Assert.IsType<CreatedAtActionResult>(await Conditions(c => c.Create(request)));
        var id = Assert.IsType<PaymentConditionDto>(created.Value).Id;
        Assert.IsType<OkObjectResult>(await Conditions(c => c.Create(request)));
        Assert.IsType<BadRequestObjectResult>(await Conditions(c => c.Create(new SavePaymentConditionDto { RequestId = Guid.NewGuid(), Code = "30d", DescriptionNl = "x", DescriptionEn = "x", Days = 1, Basis = "InvoiceDate" })));

        var relation = Assert.IsType<CreatedAtActionResult>(await Relations(r => r.Create(new SaveRelationDto { RequestId = Guid.NewGuid(), Name = "Klant", Customer = new SaveCustomerRoleDto { PaymentConditionId = id } })));
        var customerId = Assert.IsType<RelationDto>(relation.Value).Customer!.CustomerId;
        var inUse = Assert.IsType<ConflictObjectResult>(await Conditions(c => c.Delete(id)));
        Assert.Contains("payment-condition-in-use", System.Text.Json.JsonSerializer.Serialize(inUse.Value));

        var spare = Assert.IsType<CreatedAtActionResult>(await Conditions(c => c.Create(new SavePaymentConditionDto { RequestId = Guid.NewGuid(), Code = "60D", DescriptionNl = "60", DescriptionEn = "60", Days = 60, Basis = "EndOfMonth" })));
        Assert.IsType<NoContentResult>(await Conditions(c => c.Delete(Assert.IsType<PaymentConditionDto>(spare.Value).Id)));
        Assert.IsType<NotFoundObjectResult>(await Conditions(c => c.Delete(999)));

        var credit = Assert.IsType<OkObjectResult>(await Customers(c => c.GetCredit(customerId)));
        Assert.Equal((0m, true), (Assert.IsType<CreditExposureDto>(credit.Value).TotalOutstanding, ((CreditExposureDto)credit.Value).IsComplete));
        Assert.IsType<NotFoundObjectResult>(await Customers(c => c.GetCredit(999)));
    }

    [Fact]
    public async Task Bank_Confirmation_Endpoint_Returns_409_On_A_Stale_Version_And_404_For_A_Foreign_Account()
    {
        var created = Assert.IsType<CreatedAtActionResult>(await Relations(r => r.Create(new SaveRelationDto
        {
            RequestId = Guid.NewGuid(), Name = "Bank", ActorReference = "Jan", BankAccounts = new() { new() { Iban = "NL91ABNA0417164300", AccountHolder = "Bank BV" } }
        })));
        var dto = Assert.IsType<RelationDto>(created.Value);
        var bankId = dto.BankAccounts[0].Id!.Value;
        Assert.IsType<ConflictObjectResult>(await Relations(r => r.ConfirmBankAccount(dto.Id, bankId, new ConfirmBankAccountDto { ExpectedVersion = dto.Version + 5, ActorReference = "Piet" })));
        Assert.IsType<NotFoundObjectResult>(await Relations(r => r.ConfirmBankAccount(dto.Id, bankId + 100, new ConfirmBankAccountDto { ExpectedVersion = dto.Version, ActorReference = "Piet" })));
        var ok = Assert.IsType<OkObjectResult>(await Relations(r => r.ConfirmBankAccount(dto.Id, bankId, new ConfirmBankAccountDto { ExpectedVersion = dto.Version, ActorReference = "Piet" })));
        Assert.Equal("Confirmed", Assert.IsType<RelationDto>(ok.Value).BankAccounts[0].ConfirmationStatus);
        Assert.IsType<BadRequestObjectResult>(await Relations(r => r.UpdateSettings(new RelationSettingsDto { GlobalGapPortalUrl = "ftp://x" })));
    }
}
