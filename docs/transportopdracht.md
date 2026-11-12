# Transportopdracht voor de vervoerder (documenten deel 2)

Voor het transportbedrijf maakt het systeem een **transportopdracht**: wat er waar geladen en waar gelost wordt. Er staan **geen prijzen, bedragen of tarieven** op. Er wordt niets verstuurd; mailen komt in deel 3.

## Wat er in de praktijk verandert

1. **Blok Transport** op de verkooporder, onder Documenten. Je vult het alleen in als er transport is.
   - **Wie rijdt?** Je kiest een **vervoerder** of **eigen transport / klant haalt op**.
     - Een vervoerder is een relatie met de rol **vervoerder**. Je zoekt op naam of zoekcode.
     - Bestaat hij nog niet, dan maak je hem hier snel aan met alleen de naam. Adres en contact vul je later aan bij Relaties.
     - Lijkt de naam op een bestaande relatie, dan kies je die (hij krijgt de rol erbij) of maak je toch een nieuwe.
   - **Laaddatum en laadtijd.** Dit is dezelfde waarde als "Te laden op" op de laadbon. Een tweecijferig jaar mag (13-11-26), en een tijd als 6.30 of 630.
   - **Losdatum** (verwacht) en **temperatuur** in °C, bijvoorbeeld 2 of 2,5.
   - **Laadplaats:** leeg betekent de loods uit de instellingen, anders het bedrijfsadres.
   - **Losplaats:** leeg betekent het standaard afleveradres van de klant, anders haar hoofdadres. Je kunt ook een ander adres van de klant kiezen, of een **afwijkend adres** invullen.
   - **Laadreferentie** (loods), **losreferentie** (leeg betekent de klantreferentie), **soort pallet** (leeg betekent de standaard van het bedrijf) en **opmerkingen**.
2. **Bevestigen** maakt naast de laadbon ook de transportopdracht, versie 1, als er transport is ingevuld. Het nummer is **TO-2026-0012**: het VO-nummer met TO ervoor. Komt het transport pas na het bevestigen, dan verschijnt de opdracht zodra je het transport opslaat.
3. **Zonder transport** staat er "Geen transportopdracht: kies een vervoerder of eigen transport".
4. **Een wijziging** geeft een nieuwe versie en "Transportopdracht gewijzigd, opnieuw versturen", tot je op **Doorgegeven** klikt. Een wijziging kan zijn: het transport, de laadtijd, de regels of de emballage.
   - Dit geldt per document. Een andere temperatuur verandert alleen de transportopdracht; een andere laadtijd verandert beide.
   - De oude versie blijft bewaard.
5. In het blok Documenten staan **Bekijken, Downloaden en Afdrukken**, naast de laadbon.
6. **Instellingen › Transportopdracht:**
   - de taal van de kopteksten (Engels, of Nederlands);
   - de standaard soort pallet ("Europallet", bijvoorbeeld "Europallet + Corners + Straps").

   Dit geldt voor nieuwe versies.

## Het document (overgenomen uit het voorbeeld, de "Transport Order")

| Deel | Wat |
|---|---|
| Kop | **Transport Order** met nummer en versie; onze bedrijfsgegevens en logo; **Transporter** (de vervoerder met adres, of "Own transport / customer pick-up"); **Date**; **Our reference** (TO-nummer) |
| Please report with | **Order number**, **Loading reference**, **Loading date**, **Loading time**; daarnaast **Temperature**, **Unloading reference**, **Unloading date** |
| Plaatsen | **Loading place** (de loods, naam en adres) en **Unloading place** (de klant, naam en adres) |
| Tabel | Per orderregel: **Quantity**, **Description** (product · ras · verpakking), **Class**, **Origin**, **Pallets** (de soort), **Pallet space(s)** (twee decimalen, niet afgerond, colli gedeeld door colli per pallet zoals op de orderregel), **Gross kg** (het bruto van de regel, berekend of aangepast; anders aantal × het bruto per verpakking van het artikel) |
| Totaal | Colli, palletplaatsen en bruto kg, één keer aan het eind. Heeft een regel geen waarde, dan staat erbij dat het totaal alleen de regels met een waarde telt. |
| Emballage | "Packaging material going along": aantal en naam, zonder prijs |
| Onderaan | **Remarks** met de opmerkingen, een lijn om bij te schrijven, en **"Please report when loading."** |
| Opmaak | Zoals de laadbon: A4, zwart-wit leesbaar, afwisselende rijkleur, de kop van de tabel op elke pagina, Nederlandse getallen, en geen ligaturen, zodat zoeken en kopiëren werkt |

## Wat er al was en wat ik hergebruikte

- **Relaties:** de rollen klant en leverancier bestonden. **Vervoerder** is erbij als een rol die je alleen kunt toevoegen, nooit weghalen. Een vervoerder heeft geen eigen gegevens zoals betalingstermijnen nodig.
- **Loods:** er is geen voorraadlocatie met adres. De instelling **Loods op de laadbon** uit deel 1 is de standaard laadplaats.
- **Afleveradres van de klant:** de adressen van de relatie met doel Delivery of Main.
- **Documenten met versies** uit deel 1: snapshot, vingerafdruk, guardregel 31 (versies per soort document) en Doorgegeven.
- **Uit stap 2:** bruto kg op de orderregel, colli per pallet, en de klasse van het artikel.

## Data

Migratie `AddSalesOrderTransport` (61):
- `Relations.IsCarrier`;
- `CompanySettings.TransportOrderLanguage` (`en`) en `TransportOrderPalletKind` (`Europallet`);
- nieuwe tabel `SalesOrderTransports`, hooguit één per order.

Bestaande orders hebben geen transport, en bestaande relaties zijn geen vervoerder.

## Bewust niet gebouwd

- Prijzen of tarieven van de vervoerder, en de transportfactuur ("Invoice reference" op het voorbeeld).
- CMR, tracking en routeplanning.
- Mailen of een mailconcept: dat is deel 3, dat ook "verstuurd" zet.

## Schermafbeeldingen en PDF (`docs/transportopdracht-2026-11/`)

Gemaakt in Chrome op een lege testdatabase, met verzonnen gegevens:
- leveranciers Kwekerij Zonnegloed B.V. en Fruitimport Zuidkust;
- klant Groothandel Noorderhaven met DC Noord;
- vervoerder Transportbedrijf Zeewind.

| Bestand | Wat |
|---|---|
| `01-transport-leeg-desktop.png` | De order zonder transport: "Transport invullen" |
| `02-transport-invullen-desktop.png` | Een nieuwe vervoerder, laaddatum 13-11-26 en tijd 6.30, losdatum, 2 °C, laadreferentie en soort pallet |
| `03-transport-opgeslagen-desktop.png` | De samenvatting na opslaan |
| `04-bevestigd-transportopdracht-versie-1-desktop.png` | Bevestigd: de laadbon en de transportopdracht versie 1 |
| `05-transportopdracht-gewijzigd-versie-2-desktop.png` | 1,5 °C: de transportopdracht versie 2, "opnieuw versturen" (de laadbon blijft versie 1) |
| `06-op-de-telefoon-390.png` | Documenten en Transport op 390 px, zonder horizontale scroll |
| `07-transportopdracht-pdf-versie-2.png` | Pagina 1 van de PDF |
| `transportopdracht-versie-1.pdf`, `transportopdracht-versie-2.pdf` | De PDF's van de proef |
