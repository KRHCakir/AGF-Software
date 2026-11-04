using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Nummering per bedrijf (voorstel, volgnummer per jaar/maand, gelijktijdig aanmaken), dubbelcontrole, modus Eigen, correctie met reden
// en historie, verwijderen alleen zonder koppelingen en een bestaande order koppelen.
public class PartijServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new() { Calendar = new FixedBusinessCalendar(new DateOnly(2026, 10, 27)) };

    public void Dispose() => _factory.Dispose();

    private async Task SettingsAsync(string mode = "Auto", string format = "{JJ}{MM}{volgnr}", string reset = "Yearly")
    {
        await using var db = _factory.CreateContext();
        var result = await new PartijService(db).UpdateSettingsAsync(new PartijSettingsDto { Mode = mode, Format = format, SequenceReset = reset });
        Assert.Equal(ServiceResultStatus.Ok, result.Status);
    }

    private async Task<ServiceResult<PartijSummaryDto>> CreateAsync(string number = null, Guid? requestId = null)
    {
        await using var db = _factory.CreateContext();
        return await new PartijService(db).CreateAsync(new CreatePartijDto { RequestId = requestId ?? Guid.NewGuid(), Number = number });
    }

    private void Today(int year, int month, int day) => _factory.Calendar = new FixedBusinessCalendar(new DateOnly(year, month, day));

    [Fact]
    public async Task Suggestion_Follows_The_Default_Format_And_Counts_Up()
    {
        Assert.Equal("2610001", (await CreateAsync()).Value.Number);
        Assert.Equal("2610002", (await CreateAsync()).Value.Number);
        await using var db = _factory.CreateContext();
        var next = await new PartijService(db).NextNumberAsync();
        Assert.Equal("2610003", next.Value.Suggestion);
        // Live voorbeeld voor een nog niet opgeslagen formaat.
        Assert.Equal("2026-003", (await new PartijService(db).NextNumberAsync("{JJJJ}-{volgnr}")).Value.Suggestion);
    }

    [Fact]
    public async Task Sequence_Resets_Per_Year()
    {
        await SettingsAsync(format: "{JJJJ}-{volgnr}");
        Assert.Equal("2026-001", (await CreateAsync()).Value.Number);
        Assert.Equal("2026-002", (await CreateAsync()).Value.Number);
        Today(2027, 1, 2);
        Assert.Equal("2027-001", (await CreateAsync()).Value.Number);
    }

    [Fact]
    public async Task Sequence_Resets_Per_Month_Or_Never()
    {
        await SettingsAsync(reset: "Monthly");
        Assert.Equal("2610001", (await CreateAsync()).Value.Number);
        Today(2026, 11, 3);
        Assert.Equal("2611001", (await CreateAsync()).Value.Number);

        // Nooit opnieuw: één doorlopende reeks; 2611001 bestaat al (met de hand of uit de maandreeks) en wordt overgeslagen.
        await SettingsAsync(reset: "Never");
        Assert.Equal("2611002", (await CreateAsync()).Value.Number);
    }

    [Fact]
    public async Task Never_Reset_Keeps_Counting_Across_Months_And_Skips_Numbers_Taken_By_Hand()
    {
        await SettingsAsync(reset: "Never");
        Assert.Equal("2610001", (await CreateAsync()).Value.Number);
        // Een eigen nummer dat toevallig het volgende voorstel is: de teller schuift niet op, het voorstel slaat het over.
        Assert.Equal("2610 002", (await CreateAsync("2610 002")).Value.Number);
        Assert.Equal("2610003", (await CreateAsync()).Value.Number);
        Today(2026, 11, 3);
        Assert.Equal("2611004", (await CreateAsync()).Value.Number);
    }

    [Fact]
    public async Task Invalid_Format_Is_Refused_With_A_Clear_Message()
    {
        await using var db = _factory.CreateContext();
        var service = new PartijService(db);
        var missing = await service.UpdateSettingsAsync(new PartijSettingsDto { Mode = "Auto", Format = "{JJ}{MM}", SequenceReset = "Yearly" });
        Assert.Equal(ServiceResultStatus.Validation, missing.Status);
        Assert.Contains("{volgnr}", missing.Errors["format"].Single());
        var unknown = await service.UpdateSettingsAsync(new PartijSettingsDto { Mode = "Auto", Format = "{JJ}{DD}{volgnr}", SequenceReset = "Yearly" });
        Assert.Contains("{DD}", unknown.Errors["format"].Single());
        Assert.Equal(ServiceResultStatus.Validation, (await service.NextNumberAsync("{JJ}")).Status);
        Assert.Equal("{JJ}{MM}{volgnr}", (await service.GetSettingsAsync()).Format);
    }

    [Theory]
    [InlineData("2610251")]
    [InlineData("2610 251")]
    [InlineData("2610-251")]
    [InlineData(" 2610251 ")]
    public async Task Duplicate_Check_Uses_The_Key_And_Never_Creates_A_Second_Lot(string typed)
    {
        var first = await CreateAsync("2610251");
        var again = await CreateAsync(typed);

        Assert.Equal(ServiceResultStatus.Conflict, again.Status);
        var body = Assert.IsType<PartijExistsConflictDto>(again.ConflictBody);
        Assert.Equal("PARTIJ_EXISTS", body.Code);
        Assert.Equal(first.Value.Id, body.Partij.Id);
        Assert.Contains("2610251", body.Message);
        await using var db = _factory.CreateContext();
        Assert.Equal(1, await db.Partijen.CountAsync());
        Assert.Equal(first.Value.Id, (await new PartijService(db).FindByNumberAsync(typed)).Id);
    }

    [Fact]
    public async Task Own_Mode_Needs_A_Typed_Number()
    {
        await SettingsAsync(mode: "Own");
        var missing = await CreateAsync();
        Assert.Equal(ServiceResultStatus.Validation, missing.Status);
        Assert.Contains("number", missing.Errors.Keys);
        await using (var db = _factory.CreateContext())
        {
            Assert.Null((await new PartijService(db).NextNumberAsync()).Value.Suggestion);
        }
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync("PA-77")).Status);
    }

    [Fact]
    public async Task Create_Is_Idempotent_On_The_Request_Id_And_Records_The_Signed_In_User()
    {
        var requestId = Guid.NewGuid();
        var first = await CreateAsync(requestId: requestId);
        var retry = await CreateAsync(requestId: requestId);
        Assert.Equal(ServiceResultStatus.Ok, retry.Status);
        Assert.Equal(first.Value.Id, retry.Value.Id);
        Assert.Equal(CurrentUser.Default.DisplayName, first.Value.CreatedBy);
    }

    [Fact]
    public async Task Two_Simultaneous_Suggestions_Get_Two_Different_Numbers()
    {
        // De eerste schrijver heeft zijn voorstel al gekozen (2610001); vóór zijn save bezet een gelijktijdige schrijver hetzelfde
        // volgnummer. De unieke index weigert de eerste, die het opnieuw probeert met het volgende nummer.
        await using var db = _factory.CreateContextBeforeGuard(async () => Assert.Equal("2610001", (await CreateAsync()).Value.Number));
        var result = await new PartijService(db).CreateAsync(new CreatePartijDto { RequestId = Guid.NewGuid() });

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal("2610002", result.Value.Number);
        await using var verify = _factory.CreateContext();
        Assert.Equal(new[] { "2610001", "2610002" }, await verify.Partijen.OrderBy(p => p.Number).Select(p => p.Number).ToListAsync());
    }

    [Fact]
    public async Task Correction_Needs_A_Reason_Writes_An_Audit_Event_And_Checks_For_Duplicates()
    {
        var partij = (await CreateAsync("2610251")).Value;
        await CreateAsync("2610261");
        await using var db = _factory.CreateContext();
        var service = new PartijService(db);

        Assert.Equal(ServiceResultStatus.Validation, (await service.CorrectNumberAsync(partij.Id, new CorrectPartijNumberDto { Number = "2610252" })).Status);
        var duplicate = await service.CorrectNumberAsync(partij.Id, new CorrectPartijNumberDto { Number = "2610-261", Reason = "Typefout" });
        Assert.Equal(ServiceResultStatus.Conflict, duplicate.Status);
        Assert.IsType<PartijExistsConflictDto>(duplicate.ConflictBody);

        var corrected = await service.CorrectNumberAsync(partij.Id, new CorrectPartijNumberDto { Number = "2610252", Reason = "Typefout bij invoer" });
        Assert.Equal(ServiceResultStatus.Ok, corrected.Status);
        Assert.Equal("2610252", corrected.Value.Number);
        var audit = await db.PartijAuditEvents.AsNoTracking().SingleAsync();
        Assert.Equal(("NumberCorrected", "2610251", "2610252", "Typefout bij invoer"), (audit.EventType, audit.OldValue, audit.NewValue, audit.Reason));
        Assert.Equal(CurrentUser.Default.DisplayName, audit.ActorReference);
    }

    [Fact]
    public async Task Lot_Can_Only_Be_Deleted_Without_Links()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var linked = (await CreateAsync("2610251")).Value;
        var loose = (await CreateAsync("2610252")).Value;
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        await PartijTestData.LinkOrderAsync(_factory, order.Id, linked.Id);

        await using var verify = _factory.CreateContext();
        var service = new PartijService(verify);
        Assert.Equal(ServiceResultStatus.Conflict, (await service.DeleteAsync(linked.Id)).Status);
        Assert.Equal(ServiceResultStatus.Ok, (await service.DeleteAsync(loose.Id)).Status);
        Assert.Equal(new[] { "2610251" }, await verify.Partijen.Select(p => p.Number).ToListAsync());
        Assert.Equal("Deleted", (await verify.PartijAuditEvents.SingleAsync()).EventType);
    }
}
