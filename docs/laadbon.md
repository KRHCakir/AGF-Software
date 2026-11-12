# Laadbon bij het bevestigen van een verkooporder (documenten deel 1)

Bevestig je een verkooporder, dan maakt het systeem meteen een **laadbon** voor de loods. Daarop staat alleen wat de loods nodig heeft om
te laden: geen prijzen, geen bedragen, geen btw. Er wordt niets verstuurd; mailen komt in deel 3.

## Wat er in de praktijk verandert

1. **Bevestigen** op de verkooporder, in het blok **Documenten**. Het scherm vraagt op de pagina naar:
   - **Te laden op:** datum en tijd. Leeg betekent de leverdatum, zonder tijd.
   - **Kenteken:** mag leeg blijven; de loods vult het dan met de hand in.

   Wie en wanneer bevestigde wordt vastgelegd (CurrentUser) en de laadbon is versie 1. Een order zonder regels kan niet bevestigd worden.
2. **Bekijken, Downloaden, Afdrukken** openen de PDF van precies die versie.
3. **Verandert de order daarna**, dan komt er een nieuwe versie (versie 2, 3, ...). Veranderingen zijn: een reservering, een toewijzing uit verwachte aanvoer, emballage, of laden op en het kenteken. De oude versie blijft bewaard en te openen. Op de order staat "Laadbon gewijzigd, opnieuw versturen" tot je op **Doorgegeven** klikt. In deel 3 gebeurt dat bij verzenden. Dezelfde inhoud geeft nooit een nieuwe versie.
4. Op de order staat de badge **Bevestigd**, en de keten-strip zegt "Bevestigd, laadbon klaar".
5. **Instellingen › Laadbon:**
   - de taal van de kopteksten (Engels, of Nederlands);
   - klantnaam op de bon (standaard aan);
   - kolom leverancier (standaard aan);
   - de loods op de bon: naam en adres van waar geladen wordt. Leeg betekent de bedrijfsnaam en het adres.

   Dit geldt voor nieuwe versies.

## De bon (overgenomen uit het voorbeeld, de "Inslaglijst")

| Deel | Wat |
|---|---|
| Titel | **Laadbon**, met nummer (LB-2026-0012, uit het VO-nummer) en versie |
| Kop | Bedrijfsnaam en logo; **Customer** (instelling); **Position** = het verkoop-partijnummer, met het VO-nummer; **Load on** (datum en tijd, Nederlandse tijd); **License plate** (of een lijn om in te vullen); **Warehouse** (de loods uit de instelling, anders bedrijfsnaam en adres); **Reference** (ordernummer van de klant) |
| Tabel | **Quantity** (colli), **Pallet** (twee decimalen, niet afgerond: colli gedeeld door colli per pallet, zoals op de orderregel), **Description**, **Variety** (ras), **Packaging** (inhoud), **Brand**, **Size**, **Origin** (landcode), **Net kg**, **Supplier** (leverancier en inkooppartij, instelling) |
| Rijen | Een rij per orderregel en per pallet of partij waar die al gereserveerd is; wat nog niet gereserveerd is een eigen rij ("not reserved yet") |
| Totaal | Totaal colli, pallets (twee decimalen) en netto kg, alleen aan het eind. Heeft een regel nog geen gewicht (nog niet gereserveerd), dan staat erbij dat het netto kg alleen dat van de regels met een gewicht is. |
| Emballage | "Packaging material going along": aantal en naam (Euro-pallet, Krat), zonder prijs |
| Onderaan | Regel voor opmerkingen; vakken voor naam chauffeur, handtekening en datum |
| Opmaak | A4, zwart-wit leesbaar, afwisselende rijkleur. De kop van de tabel herhaalt op elke pagina. Getallen in Nederlandse notatie (6.890 colli, 23,81 pallets), zoals het voorbeeld. Geen ligaturen, zodat zoeken en kopiëren in de PDF werkt. |

## Hoe versies werken

- Een versie is een **snapshot** van de gegevens op dat moment. De PDF wordt altijd uit die snapshot gemaakt, dus een oude versie ziet er later precies zo uit.
- **Nieuwe versies ontstaan op twee manieren.** Na elke geslaagde opdracht op een verkooporder (POST of PUT) controleert het systeem of de bon veranderd is. Ophalen van de documenten doet dezelfde controle, als vangnet.
- **Vingerafdruk:** dezelfde inhoud geeft nooit een nieuwe versie. Lege velden tellen niet mee. Komt er later een veld bij de bon, dan krijgt een bestaande bon zo geen onterechte nieuwe versie.
- **Doorgegeven** geldt voor precies de versie die je doorgaf. Verandert de order op hetzelfde moment, dan staat er meteen een nieuwere versie en vraagt de order weer om opnieuw versturen.
- **TraceabilityGuard regel 31:**
  - wie en wanneer bevestigde ligt vast;
  - een versie verandert of verdwijnt nooit; alleen "doorgegeven" wordt één keer gezet;
  - versies volgen elkaar op;
  - een document hoort bij een bevestigde order.

## Data

Migratie `AddSalesOrderConfirmationAndDocuments` (59):
- `SalesOrders`: `ConfirmedAt`, `ConfirmedBy`, `LoadAt`, `LicensePlate`;
- `CompanySettings`: `LoadingSlipLanguage` (`en`), `LoadingSlipShowCustomer` en `LoadingSlipShowSupplier` (aan);
- nieuwe tabel `SalesOrderDocuments`.

Migratie `AddLoadingSlipWarehouseAddress` (60): `CompanySettings.LoadingSlipWarehouseAddress`, maximaal 300 tekens, leeg toegestaan.

Bestaande orders zijn niet bevestigd en hebben geen document.

## Wat er al was en wat ik hergebruikte

- **Bevestigen:** een verkooporder was alleen Open of Geannuleerd; een bevestigingsstap bestond niet. Die is er nu bij, als aparte, eenvoudige stap naast reserveren en verzenden.
- **PDF:** QuestPDF, dat al gebruikt werd voor de factuur.
- **Artikelkenmerken uit stap 2:** product, variant (= ras), verpakking, merk, maat, landcode en colli per pallet.
- **Position:** de verkoop-partij uit P2.
- **Emballage:** de emballageregels stonden al op main.
- **Loods:** er was geen voorraadlocatie met naam en adres. Daarom is er nu één instelling voor de loods op de bon; leeg betekent de bedrijfsgegevens. Meer loodsen per bedrijf, met een loods per pallet, is een latere stap. De plek van een pallet in de loods ("Cel 3") staat op de pallet.

## Bewust niet gebouwd

- Prijzen of bedragen op de bon.
- Transportopdracht: deel 2.
- Mailen of een mailconcept: deel 3, dat ook "verstuurd" zet.
- Orderbevestiging naar de klant, CMR en factuur.
- Een digitale handtekening.
- Bewerken van de verkooporder zelf. Dat bestaat niet; wat verandert zijn reserveringen, toewijzingen en emballage.

## Schermafbeeldingen en PDF (`docs/laadbon-2026-11/`)

Gemaakt in Chrome op een lege testdatabase, met verzonnen gegevens:
- leveranciers Kwekerij Zonnegloed B.V. en Fruitimport Zuidkust;
- klant Groothandel Noorderhaven;
- Avocado Hass 4 kg doos en Blauwe bes Duke 12x125 gr.

| Bestand | Wat |
|---|---|
| `01-bevestigen-desktop.png` | De order na reserveren, nog niet bevestigd: knop Bevestigen |
| `02-te-laden-op-en-kenteken-desktop.png` | De vraag op de pagina: te laden op en kenteken |
| `03-laadbon-versie-1-desktop.png` | Bevestigd: de laadbon versie 1 met Bekijken, Downloaden en Afdrukken |
| `04-laadbon-gewijzigd-versie-2-desktop.png` | Na een extra reservering (versie 2) en een ander kenteken (versie 3): "Laadbon gewijzigd, opnieuw versturen" en de eerdere versies |
| `05-documenten-op-de-telefoon-390.png` | Hetzelfde blok op 390 px (geen horizontale scroll) |
| `06-laadbon-pdf-versie-3.png` | Pagina 1 van de PDF |
| `laadbon-versie-1.pdf`, `laadbon-versie-3.pdf` | De PDF's van de proef. Versie 1 heeft 300 bessen gereserveerd; de andere 84 staan als "not reserved yet", en het totaal zegt dat het netto kg alleen dat van de regels met een gewicht is. Versie 3 heeft 384 bessen en het nieuwe kenteken. |
