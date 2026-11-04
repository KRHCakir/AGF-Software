# Rechten per gebied

Dit document beschrijft hoe de API weet wie er werkt en wat die persoon mag. Het legt ook uit hoe je later een medewerker een gebied ontzegt.

## Wie is de gebruiker

- **Eén plek bepaalt wie de gebruiker is:** `AgfAdmin/Security/DefaultUserAuthenticationHandler.cs`.
- **Er is nog geen echte aanmelding.** Elk verzoek komt van de ingestelde standaardgebruiker uit `appsettings.json`:

  ```json
  "Identity": { "DefaultUser": { "Id": "admin", "DisplayName": "Admin", "Roles": [ "Admin" ] } }
  ```

- **Services lezen de gebruiker uit `ICurrentUser`.** Ze vragen nooit een naam aan de client. Het schermpje rechtsboven leest dezelfde gebruiker via `GET /api/me`.
- **Een echte aanmelding vervangt alleen de handler.** Bijvoorbeeld een cookie of token met een eigen gebruikerstabel. De nieuwe handler zet dezelfde claims:

| Claim | Betekenis |
|---|---|
| `ClaimTypes.NameIdentifier` | gebruikers-id |
| `ClaimTypes.Name` | weergavenaam; deze komt in elke actor-kolom |
| `ClaimTypes.Role` | rollen |
| `agf:permission` | één claim per gebied dat de gebruiker heeft |

## Gebieden

Elk gebied is een constante in `AgfAdmin/Security/Permissions.cs`. Het is ook een autorisatiebeleid met dezelfde naam op de controllers van dat gebied.

| Gebied | Endpoints |
|---|---|
| `Inkoop` | inkooporders, aanvoer (`supply-commitments`), inkoopfacturen, inkomende facturen, afwijkingen bij ontvangst |
| `Voorraad` | beschikbaarheid, pallets en ontvangst, voorraad (oud) |
| `Keuring` | goedkeuren, blokkeren, afkeuren en vrijgeven (`pallets/{id}/content-lines/{lineId}/quality-status`). Daarnaast is ook `Voorraad` nodig, omdat dat het beleid van de hele pallet-controller is. |
| `Verkoop` | verkooporders, reserveringen, verzendingen, klanten, verkoop en facturen (oud), rapporten (oud) |
| `Stamgegevens` | artikelen, producten, landen, relaties, betalingscondities. **Lezen mag iedereen**, want deze lijsten staan in elk formulier. Wijzigen vraagt `Stamgegevens`. |
| `Beheer` | instellingen. **Lezen mag iedereen** (standaarden in formulieren). Wijzigen vraagt `Beheer`. |
| `Verkoopcijfers` | gevoelige verkoopgegevens; zie hieronder. Staat nog op geen enkel endpoint. |

Een endpoint zonder eigen beleid, zoals het dashboard en `/api/me`, vraagt alleen een aangemelde gebruiker.

## Nu: iedereen mag alles

`Identity:DefaultUser:Permissions` is niet ingesteld. Daardoor krijgt de standaardgebruiker **alle** gebieden, en verandert er voor niemand iets.

## Later: een medewerker een gebied ontzeggen

1. **Geef de aanmelding per gebruiker alleen de gebieden mee die hij mag,** als `agf:permission`-claims. Bijvoorbeeld een orderpicker met `Voorraad` en `Keuring`, maar zonder `Verkoop`.
2. **De endpoints hoeven niet te veranderen.** Een verzoek buiten zijn gebieden krijgt `403 Forbidden`.
3. **Het beheerscherm hoeft alleen die lijst per gebruiker op te slaan.** Dat scherm is nog niet gebouwd.

Om het zonder aanmelding te proberen, zet je in de configuratie een beperkte standaardgebruiker:

```json
"Identity": { "DefaultUser": { "DisplayName": "Piet", "Permissions": [ "Inkoop", "Voorraad" ] } }
```

Dit doet de test `AgfAdmin.Tests/Security/AreaAuthorizationTests.cs` ook.

## Verkoopgegevens apart markeren

Prijzen, marges en het partijresultaat zijn gevoeliger dan de verkooporder zelf. Daar is het gebied `Verkoopcijfers` voor, bovenop `Verkoop`. Het kan op twee manieren:

1. **Op een eigen endpoint** (bijvoorbeeld een margeoverzicht):

   ```csharp
   [Authorize(Policy = Permissions.SalesFigures)]
   ```

2. **Velden in een bestaand antwoord.** De service vraagt `ICurrentUser.Has(Permissions.SalesFigures)` en laat de prijs- en margevelden leeg als de gebruiker dat gebied niet heeft.

Zo kan een medewerker orders maken zonder verkoopgegevens te zien.

## Keuring: twee namen

Bij een keuring liggen twee personen vast:

- **De beoordelaar:** wie het product in de loods fysiek keurde. Die wordt per keuring gekozen.
- **De ingelogde gebruiker:** het account waarmee het gebeurde.

Op een gedeelde tablet kunnen dat verschillende mensen zijn.
