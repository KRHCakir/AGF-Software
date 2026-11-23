# Ontvangen met één knop, orderregel splitsen, onderweg en laadadres, Direct trade eruit

Feedback 22 november, prompt 4 van 5. Screenshots: [ontvangst-2026-11](ontvangst-2026-11/).

## Ontvangen in één scherm

- **Ontvangst zonder order:** de open inkooporders, eerst wat vandaag of eerder verwacht werd. Zoeken gaat op leverancier, ordernummer, partij en product. Een order kiezen opent **Dit wordt verwacht** (`/receipts?order=…`).
- **Dit wordt verwacht:** per regel product, partij, besteld, al ontvangen en nog te ontvangen.
- **Ontvangen:** per regel één knop. Het aantal staat op wat nog open is; een afwijkend aantal typ je in het veld (ook meer dan besteld). Enter in het aantal ontvangt de regel. Bovenaan staat **Alles ontvangen**.
- **Gewicht:** volgt uit de orderregel (kg per verpakking van de regel, anders van het artikel). Alleen als de order geen kg kent, vraagt het scherm het netto kg.
- **Partij:** de ontvangst neemt de partij van de orderregel over. Zonder partij is het lot dat van de regel, dan dat van de leverancier, anders het ordernummer.
- **Status:** de voorraad start in quarantaine, zoals altijd.
- **Optioneel per regel:**
  - **Palletnummers:** het aantal pallets en per pallet een nummer, getypt of gescand (Enter springt naar het volgende nummer). Een leeg nummer maakt het systeem (P-000123). Het aantal wordt in hele verpakkingen over de pallets verdeeld.
  - **Keuren / afwijzen:** de bestaande keuring van die pallet, ook later te doen.
- **Geen controlevragen meer:** een ander product, een andere maat of herkomst wordt niet meer bij ontvangst gevraagd. Komt er iets anders, dan pas je de orderregel aan (zie hieronder).
- **GGN-melding:** de melding uit certificaten 1 is een regel op het scherm in plaats van een venster. Ontvangen legt vast wie hem zag; bij de inkooporder blijft de vraag zoals hij was.
- **Herhalen:** een herhaling met dezelfde aanvraag maakt niets dubbel; na een fout gaat een herhaling verder waar hij stopte.

API: `GET /api/receipts/orders/{id}` en `POST /api/receipts/orders/{id}/lines/{lineId}` (beleid Voorraad). De pallets en regels maakt de bestaande `PalletService`, met dezelfde regels, audit en idempotentie.

## Iets anders ontvangen: de orderregel splitsen

- **Aanpassen:** de orderregel pas je aan bij Inkooporder › Aanpassen, zolang de order Open is.
- **Splitsen:** krijgt een deels ontvangen regel een ander artikel (product, maat of herkomst), dan splitst het systeem bij opslaan.
  - Het ontvangen deel blijft op de oude regel.
  - Het open deel wordt een nieuwe regel met het nieuwe artikel en dezelfde partij.
  - Het formulier stelt het open deel voor als aantal en meldt dat de regel splitst.
- **Restant:** bij opslaan vraagt een venster wat er met het restant van de oude regel gebeurt:
  - **Vervalt** (standaard): de oude regel verwacht voortaan precies wat ontvangen is. Het verschil staat als vervallen restant op de regel en telt niet meer in Verwacht of in de totalen.
  - **Wordt nog verwacht:** de oude regel blijft open naast de nieuwe.
- **Niets telt dubbel:** de totalen van de order zijn de som van de actieve regels. Zie de test met de order van 18 november (mango Brazilië, deels vervangen door mango Peru).
- **Vast:** ontvangen hoeveelheden veranderen nooit.
- **Zonder ontvangst:** een regel waaraan nog niets ontvangen is, krijgt gewoon het andere artikel. Een regel waaraan iets anders hangt (bijvoorbeeld een reservering) houdt haar artikel.

## Orderoverzicht per regel

- **Inkooporderlijst:** bij Deels ontvangen staat ook **x van y regels ontvangen · n colli open**. Met **Regels tonen** klap je een order uit tot een overzicht per regel. Dat wordt pas bij openen geladen.
- **Ontvangst per regel:** op het detail van de inkooporder staat per regel besteld, ontvangen, open, partij en status (Besteld, Deels ontvangen, Ontvangen, Klaar met vervallen rest). Het blok is open bij een deels ontvangen order en blijft compact, ook bij 30 regels.
- **Labels:** gesplitste regels en een vervallen restant krijgen een label op de regel.

## Locatiesoort, onderweg en laadadres

- **Voorraadlocaties** (Instellingen):
  - Een locatie heeft een naam, een soort, een adres en een land. De soorten zijn Eigen loods, Leverancier, Cross-dock, Externe opslag en Onderweg / direct naar klant.
  - De **eigen loods** is altijd aanwezig (het adres uit de laadbon-instellingen) en is geen rij. Een pallet of order zonder locatie ligt in de eigen loods. Zo blijven alle bestaande pallets en orders Eigen loods, zonder dat er een rij verandert.
  - Een locatie wordt nooit verwijderd; je zet haar inactief. Haar soort ligt vast zodra een pallet of order haar gebruikt.
- **Inkooporder › Locatie en adressen:**
  - **Ontvangstlocatie:** standaard de eigen loods.
  - **Laadadres:** leeg als de leverancier levert. **Wij halen op** vult het adres van de leverancier.
  - **Afleveradres:** leeg is de eigen loods. Bij een locatie Onderweg kies je een klant en neemt het veld haar afleveradres over.
  - Wijzigingen staan met oud en nieuw in de historie. Een definitieve order houdt ze.
- **Directe handel:**
  - Een gewone inkooporder met als ontvangstlocatie een locatie van de soort Onderweg en als afleveradres de klant.
  - Op die locatie heet de knop **Afgeleverd**.
  - De voorraad telt mee in Beschikbaarheid met de locatie erbij ("120 · Onderweg naar Griekenland"). Hij wordt verkocht en geleverd zoals elke andere voorraad.
- **Laadadres van een levering:** standaard de locatie van de gereserveerde pallets, als die allemaal op dezelfde locatie (niet de eigen loods) liggen; anders de loods uit de instellingen. Per levering blijft het aan te passen (Transport › Laadplaats). De transportopdracht gebruikt dat adres.

## Direct trade eruit

- **Menu:** Direct trade staat niet meer in het menu. `/direct-trade` is een archief (alleen lezen) van de klantgebonden aanvoer van vroeger, te vinden via Start.
- **Verkooporderregel:** je kunt geen nieuwe klantgebonden toewijzing meer starten. Bestaande toewijzingen, verzendingen, terugnames en afleverbevestigingen blijven zichtbaar en werken zoals altijd.
- **Niets verwijderd:** tabellen, API en gegevens blijven staan.
- **In `agf_smoke`:** 20 klantgebonden inkooporders (allemaal Verwacht), 43 toewijzingen, 24 verzendingen en 5 terugnames. Omzetten naar gewone orders is niet gedaan en is niet nodig om ze te lezen. Of ze omgezet moeten worden, beslist de eigenaar.

## Traceerbaarheid

| Regel | Wat er vastligt |
| --- | --- |
| 39 (nieuw) | een gesplitste regel ontstaat alleen met een Edit-auditregel en wijst naar een deels ontvangen regel van dezelfde order; het vervallen restant daalt nooit en stijgt alleen met precies de verlaging van het verwachte aantal; aantal, eenheid, netto kg en gewichtsmethode van een ontvangen palletregel veranderen nooit |
| 40 (nieuw) | een voorraadlocatie wordt nooit verwijderd; de locatie van een pallet wordt alleen bij aanmaken gezet; de soort van een gebruikte locatie verandert nooit |
| 29 (versterkt) | ontvangstlocatie, laadadres en afleveradres horen bij de kop: alleen met Edit, een definitieve order houdt ze |

Regel 18 (afwijkende ontvangst) is ongewijzigd: splitsen gebruikt een eigen veld (`SplitFromLineId`), niet de vervangende regel van een afwijking.

## Migraties

| Migratie | Wat |
| --- | --- |
| `AddPurchaseLineSplit` | `SupplyCommitmentLines.SplitFromLineId` en `CancelledQuantity` (leeg) |
| `AddStockLocations` | tabel `StockLocations`, `Pallets.StockLocationId`, `SupplyCommitments.ReceiveLocationId`, `LoadingAddress` en `DeliveryAddress` (leeg) |

Alleen nieuwe kolommen en een nieuwe tabel; er verandert geen bestaande rij.
