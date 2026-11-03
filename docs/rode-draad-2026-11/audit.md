# Stroomaudit rode draad (2 november 2026)

Uitgevoerd op `main` 8e75299 (met de fixes #113–#117), lokaal: API op `agf_chain_audit_test` (vers via
`scripts/receipt-test-data.sh`), frontend op :3001, Chrome via Playwright.

De keten is één keer helemaal doorlopen met de rondleiding:

1. inkooporder aanmaken;
2. factuurnummer koppelen;
3. ontvangen;
4. keuren, met een deel afgekeurd;
5. beschikbaarheid;
6. verkooporder;
7. reserveren op de pallet;
8. verzenden;
9. afwijkende ontvangst.

Daarna is elk scherm geopend en vastgelegd: screenshot, kop, eerste zin, links en knoppen. De screenshots staan in `audit/`.

Voorbeeld uit de audit:

- **Inkooporder:** IO-2026-0003 (Nature's Pride, Avocado Hass 20 colli).
- **Ontvangst:** pallet P-000001; 15 colli goedgekeurd, 5 colli afgekeurd (terugsturen).
- **Verkoop:** verkooporder #1 (Lokaal Fruit B.V.), 4 colli gereserveerd op P-000001 en verzonden.

## Menu en schermen vóór deze PR

Het menu bestond uit drie delen:

- **Bovenbalk:** Start · Verkoop · Inkoop · Inkoopopdrachten · Aanvoer · Keuring · Beschikbaarheid · Artikelen · Relaties · Rapporten · Instellingen · Help.
- **Zijbalk "Werkplekken":** Nieuwe verkooporder · Aanvoer plannen · Pallet ontvangen · Keuring · Beschikbaarheid.
- **Zijbalk "Oude flows":** Bestellingen (oud) · Voorraad (oud) · Facturen (oud) · Tracking (niet actief).

| Menu-item | Route | Wat het scherm doet | Koppeling naar de volgende stap | Waar je vastloopt |
|---|---|---|---|---|
| Start | `/dashboard` | Vier tegels (Verkoop, Aanvoer, Keuring, Beschikbaarheid) en de oude flows | tegels | geen inkooporders of inkoopfacturen op Start |
| Verkoop | `/sales-orders` (`?orderId=`) | Verkooporders: lijst links, detail rechts; reserveren per regel ("Voorraad kiezen en reserveren"), "Verzenden" in de kop, aflevering bevestigen, corrigeren | geen | de verkooporder toont pallet en partij, maar niet van welke inkooporder het komt; verzenden staat in de kop vóór reserveren per regel |
| Inkoop | `/purchase-invoices`, `/new` | Inkoopfacturen: lijst en invoeren (kop, regels, totalen) | "Hoort bij order IO-…" als het factuurnummer al aan een order hangt | de factuur staat los van order en ontvangst (plan: PR 2/3); de menunaam "Inkoop" zegt niet dat het om facturen gaat |
| Inkoopopdrachten | `/purchase-orders` (`?id=`, `?query=`) | Inkooporders: status, regels, ontvangsten en keuring, factuurnummers, afkeuringen, historie | "+ Nieuwe inkooporder" (naar `/supply#plan`), "Naar keuring" (algemene keuringslijst) | geen knop "Ontvangen"; geen link naar de verkooporders die van deze pallets leveren; pallets zijn geen link; filter "Open" verbergt volledig ontvangen orders (IO-2026-0003 zag je alleen via de link) |
| Aanvoer › Aanvoer plannen | `/supply#plan`, bestemming Eigen magazijn | Inkooporder aanmaken (IO-nummer) | na opslaan: "Pallet ontvangen voor deze aanvoer" | eerst bestemming en modus kiezen; "aanvoer" is hier gewoon een inkooporder |
| Aanvoer › Pallet ontvangen | `/supply#receive` | Pallets registreren tegen een open inkooporder (blind, met afwijkingscontrole); nieuw = quarantaine | na boeken: "Bekijk beschikbaarheid", "Naar keuring voor deze pallet" (`/inspection?palletId=`), "Nog een pallet" | geen |
| Aanvoer › Directe handel | `/supply`, bestemming Directe handel | Klantgebonden aanvoer (DirectTrade) vastleggen: rechtstreeks naar de klant, nooit voorraad | toewijzen gebeurt op de verkooporder | verstopt achter een keuze op dezelfde pagina als Aanvoer plannen |
| Keuring | `/inspection` (`?palletId=`, `?search=`) | Goedkeuren, blokkeren, afkeuren per inhoudsregel | na goedkeuren: melding met link "Bekijk beschikbaarheid" (gefilterd op het artikel) | zoeken op inkoopordernummer kan niet (wel pallet, product, partij) |
| Beschikbaarheid | `/availability` (`?route=`, `?productVariantId=`, `?focus=`) | Alleen kijken: voorraad en verwachte aanvoer per variant en eenheid | geen | niets "gebeurt" daarna; verkopen vanaf hier kan niet (plan: deel 2) |
| Artikelen | `/articles` | Artikelbeheer, opschonen herkomst en maat/klasse | "Oude productpagina" (`/products`) | geen |
| Relaties | `/relations` | Klanten en leveranciers als één relatie met rollen | geen | geen |
| Rapporten | `/reports` | Engelstalige trace forward/backward op de oude zendingen en facturen, plus snelcodes | geen | hoort bij de oude flow (oude datamodellen), niet bij de nieuwe keten |
| Instellingen | `/settings` | Betalingscondities, keuzelijsten, adressen | geen | geen |
| Help | `/help` | "Nog niet beschikbaar" | geen | geen |
| Bestellingen (oud) | `/orders` | De oude bestelflow met legacy-voorraad | "Naar Verkoop" | geen |
| Voorraad (oud) | `/inventory` | Oude batchvoorraad | "Naar Beschikbaarheid" | geen |
| Facturen (oud) | `/incoming-invoices` | Oude inkoopfactuurcontrole | geen | lege kop |
| (niet in menu) | `/invoices` | Oude verkoopfacturen | "Naar Verkoop" | alleen via een link bereikbaar |
| (niet in menu) | `/products` | Oude productcatalogus | geen | alleen via Artikelen |
| Tracking | `/tracking` | Placeholder "nog niet beschikbaar" | geen | geen |

## De "Oude bestelflow (niet gesynchroniseerd)"

Dat is `/orders` ("Bestellingen (oude flow)"): de eerste verkoopmodule.

- **Wat het is:** bestellingen met regels op de oude `Inventory`-batches, met daarnaast de oude facturen (`/invoices`) en oude voorraad (`/inventory`).
- **Waar de link stond:** in de paarse balk bovenaan Verkoop, en in de zijbalk onder "Oude flows".
- **Koppeling met de nieuwe keten:** er wordt niets met de nieuwe verkooporders, pallets of reserveringen gesynchroniseerd.

## Welke koppelingen er via de data al zijn (voor de keten-strip)

| Van | Naar | Hoe | Al in het scherm? |
|---|---|---|---|
| Inkooporder | Ontvangsten, pallets | `PalletContentLine.SupplyCommitmentLineId` → orderregel; inkooporder-detail `receipts` | ja (lijst op de order) |
| Inkooporder | Keuring | `PalletContentLine.QualityStatus` per ontvangen regel | ja (badge per regel) |
| Inkooporder | Voorraad | `OnHandQuantity` per ontvangen regel; Beschikbaarheid filtert op `productVariantId` | deels |
| Inkooporder | Verkooporders | `StockReservation.SupplyCommitmentLineId` (ook bij DirectTrade-toewijzingen) en `SalesOrderLineId`; status Active (gereserveerd) of Consumed (verzonden) | **nee**; het detail-API gaf alleen het gereserveerde aantal per pallet |
| Verkooporder | Inkooporder, partij | reservering: `SupplyCommitmentId`, `LotNumber`, pallet | partij en pallet ja; inkoopordernummer **nee** (alleen het id) |
| Verkooporder | Levering | verzendingen en afleverbevestigingen op de order | ja |
| Inkooporder | Inkoopfactuur (document) | alleen factuurnummer (`InvoiceRef`), geen koppeling aan het factuurdocument | nee; plan PR 2 |
| Levering | Verkoopfactuur | bestaat niet | plan PR 4a |

Gevolg voor deze ronde: de strip heeft twee kleine leesuitbreidingen van bestaande API's nodig, zonder nieuwe tabel.

- **Inkooporder-detail:** de verkooporders die van deze order reserveren of verzonden hebben.
- **Reservering:** het inkoopordernummer.

Inkoopfactuur en verkoopfactuur blijven grijs ("nog niet beschikbaar").

## Aannames uit de opdracht, getoetst

| Aanname | Klopt? |
|---|---|
| "Aanvoer plannen" maakt een inkooporder | Ja: het IO-nummer komt daar vandaan; het scherm heet nu "Inkooporder aanmaken" |
| "Aanvoer" betekent ook klantgebonden aanvoer | Ja: dat was de keuze "Directe handel" op dezelfde pagina; die heeft nu een eigen menu-item "Klantgebonden aanvoer" |
| Er is een pagina Partijen | **Nee**: niet in het menu gezet |
| Er is een pagina Leveringen | **Nee**: leveringen staan op de verkooporder; niet in het menu gezet |
| Er is een pagina Verkoopfacturen | **Nee** (alleen de oude `/invoices`, onder "Oud") |
| Na ontvangst bestaat "Naar keuring" | **Ja**, al ("Naar keuring voor deze pallet") |
| Na goedkeuren bestaat "Bekijk voorraad" | **Deels**: een tekstlink "Bekijk beschikbaarheid" in de melding |
| Op de inkooporder bestaat "Ontvangen" | **Nee** |
