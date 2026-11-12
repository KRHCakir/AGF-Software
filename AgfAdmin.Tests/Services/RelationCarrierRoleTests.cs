using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;

namespace AgfAdmin.Tests.Services;

// Documenten deel 2: een relatie kan de rol vervoerder krijgen, bij het aanmaken of later. Zoeken op die rol vindt alleen vervoerders;
// het opslaan van de relatiekaart (die de rol niet meestuurt) laat de rol staan; een rol verdwijnt nooit.
public class RelationCarrierRoleTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    private async Task<T> RunAsync<T>(Func<RelationService, Task<T>> call)
    {
        await using var db = _factory.CreateContext();
        return await call(new RelationService(db));
    }

    [Fact]
    public async Task A_carrier_is_created_with_the_role_or_gets_it_later_and_keeps_it()
    {
        var created = await RunAsync(s => s.CreateAsync(new SaveRelationDto { RequestId = Guid.NewGuid(), Name = "Transport Zeeland B.V.", Carrier = true }));
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        Assert.True(created.Value.IsCarrier);
        var customer = await RunAsync(s => s.CreateAsync(new SaveRelationDto { RequestId = Guid.NewGuid(), Name = "Groothandel Noord", Customer = new SaveCustomerRoleDto() }));
        Assert.False(customer.Value.IsCarrier);

        // Alleen vervoerders bij zoeken op de rol.
        Assert.Equal(new[] { "Transport Zeeland B.V." }, (await RunAsync(s => s.ListAsync(RelationService.RoleCarrier, null, false, 1, 50))).Items.Select(i => i.Name));

        // Later de rol erbij; nog eens toevoegen verandert niets.
        Assert.Equal(ServiceResultStatus.Created, (await RunAsync(s => s.AddRoleAsync(customer.Value.Id, "carrier"))).Status);
        var again = await RunAsync(s => s.AddRoleAsync(customer.Value.Id, "carrier"));
        Assert.Equal((ServiceResultStatus.Ok, true), (again.Status, again.Value.IsCarrier));
        Assert.Equal(2, (await RunAsync(s => s.ListAsync(RelationService.RoleCarrier, null, false, 1, 50))).Total);

        // De relatiekaart opslaan zonder de rol: de rol blijft.
        var card = created.Value;
        var saved = await RunAsync(s => s.UpdateAsync(card.Id, new SaveRelationDto { ExpectedVersion = card.Version, Name = "Transport Zeeland", IsActive = true }));
        Assert.Equal((ServiceResultStatus.Ok, true, "Transport Zeeland"), (saved.Status, saved.Value.IsCarrier, saved.Value.Name));
    }

    [Fact]
    public async Task Replaying_a_create_without_the_carrier_role_keeps_working_as_before()
    {
        // De vingerafdruk van een aanvraag zonder vervoerder is dezelfde als vroeger: een herhaling geeft de eerste relatie terug.
        var request = new SaveRelationDto { RequestId = Guid.NewGuid(), Name = "Kwekerij Voorbeeld" };
        var first = await RunAsync(s => s.CreateAsync(request));
        var replay = await RunAsync(s => s.CreateAsync(request));
        Assert.Equal((first.Value.Id, false), (replay.Value.Id, replay.Value.IsCarrier));
        // Dezelfde requestId met de rol vervoerder erbij is een andere aanvraag.
        Assert.Equal(ServiceResultStatus.Conflict, (await RunAsync(s => s.CreateAsync(new SaveRelationDto { RequestId = request.RequestId, Name = "Kwekerij Voorbeeld", Carrier = true }))).Status);
    }
}
