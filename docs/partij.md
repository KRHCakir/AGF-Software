# Partij als sleutel: I- en V-nummers, verkooppartij per levering, partij op de orderregel, kleur, herleidbaarheid

Feedback 22 november, prompt 5 van 5. Screenshots: [partij-2026-11](partij-2026-11/).

## Nummering met I- en V-

- **Nieuwe standaard:** `I-{JJ}{MM}{volgnr}` voor inkoop en `V-{JJ}{MM}{volgnr}` voor verkoop (bijvoorbeeld `I-2611040` en `V-2611040`; met 2 cijfers voor het volgnummer `I-261140`).
  - De migratie `AddLotNumberingIV` zet alleen de oude standaardformaten om: `{JJ}{MM}{volgnr}` en `5{JJ}{MM}{volgnr}` (de 5 vooraan vervalt).
  - Een zelf gekozen formaat blijft staan.
- **Bestaande nummers blijven zoals ze zijn**, want ze staan al op geprinte documenten. Nieuwe partijen krijgen het nieuwe formaat vanaf het moment dat de migratie draait.
- **Volgnummer:** loopt door in de periode. Na `2611030` komt `I-2611031`.
- **Uniek over inkoop en verkoop samen:** een eigen of gecorrigeerd nummer dat al bestaat als partij van de andere soort, wordt geweigerd met uitleg. Een voorstel slaat zo'n nummer over. Dit is guardregel 41.
- **Zoeken** zonder letter, streepjes of spaties vindt beide soorten: `261140` vindt `I-261140` en `V-261140`. Op Voorraad › Partijen staat onder de lijst hoeveel partijen van de andere soort ook passen, met een knop naar dat tabblad.

### Directe factuur

Een directe factuur is een verkoop zonder verkooporder, bijvoorbeeld contant aan de balie. De API is `POST /api/SalesInvoice`.

- **Eigen verkoop-partij:** de factuur krijgt het volgende V-nummer, of een eigen nummer dat nog nergens als partij bestaat.
- **Bestaand nummer:** van welke soort ook, geeft dat 409 `PARTIJ_TAKEN` met uitleg.
- **Regels:** elke factuurregel mag naar de inkoop-partij wijzen waar ze uit komt. Dat kan alleen een inkoop-partij zijn, en het ligt vast na aanmaken.
- **Geen scherm:** er is nog geen scherm voor de directe factuur (de verkoopfactuur komt in een latere ronde). De partij en haar herkomst staan wel op Voorraad › Partijen.

### Partij altijd verplicht

- **Bij ontvangst:** voorraad zonder partij kan niet meer ontstaan. Heeft een order nog geen partij, dan krijgt ze er bij de eerste ontvangst een:
  - met het getypte lotnummer, na de dubbelcontrole (een bestaand nummer geeft 409);
  - anders het voorgestelde I-nummer.
  - Alle regels van de order krijgen die partij. Een tweede lotnummer op dezelfde orderregel wordt geweigerd.
- **Werklijst Partij ontbreekt** (Voorraad › Partijen): voorraad zonder partij uit oude gegevens, met links naar pallet en inkooporder.
  - Er wordt niets verzonnen: de gebruiker koppelt de juiste partij via Partij › Inkooporder koppelen, met reden.
  - In de kopie van `agf_smoke` staat geen voorraad zonder partij.

## Verkooppartij per levering

Een verkooppartij is één levering aan één klant, niet één verkooporder.

- **Eerste levering:** elke order heeft een eerste levering (volgnummer 1) met het V-nummer van de order. Die ontstaat samen met de order, zoals het V-nummer al ontstond.
- **Deellevering:** de knop maakt een tweede levering met een nieuw V-nummer.
  - De gebruiker kiest welke gereserveerde pallets meegaan.
  - Wat nog niet gereserveerd is, gaat altijd mee met de nieuwste levering.
  - Al verzonden pallets blijven bij hun levering.
- **Laadbon en transportopdracht per levering:** de eerste levering houdt `LB-2026-0012` en `TO-2026-0012`; een deellevering krijgt `LB-2026-0012-2`.
  - Elke levering heeft haar eigen laden op, kenteken, versies, "opnieuw versturen" en mailconcepten.
  - Emballage gaat mee met de eerste levering.
  - Een order met één levering geeft precies dezelfde laadbon als vóór deze ronde, dus geen nieuwe versie.
- **Bestaande orders:** de migratie `AddSalesDeliveries` geeft elke order haar eerste levering met de verkoop-partij die ze al had, en hangt bestaande documenten aan die levering. Er verandert geen nummer en geen reservering.
- **Kosten** (P2) die op een verkoop-partij staan, blijven op die partij; dat is nu de eerste levering. Een deellevering begint zonder kosten, dus niets telt dubbel.
- **Transport:** de gegevens (vervoerder, losadres) zijn per order en gelden voor elke levering. Een andere vervoerder per levering is een latere stap.

## Partij op de orderregel

- **Kolom Partij** op de verkooporderregel, optioneel.
  - Leeg: bij het reserveren de oudste partij eerst (FIFO), zoals altijd. Een houdbaarheidsdatum bestaat nog niet; dan zou het FEFO zijn.
  - Ingevuld: reserveren (oudste partij eerst) neemt alleen uit die partij.
- **Partijkeuze:** typen of de lijst openen met pijltje omlaag.
  - Per partij: vrij aantal, maat, kleur, herkomst, locatie (met vak) en ontvangstdatum.
  - Enter kiest, Escape sluit.
- **Meer partijen op één regel:** op de orderregel reserveert Partij kiezen uit een gekozen partij. Zo kan één regel over meerdere partijen verdeeld worden.
- **Partij wijzigen bij het picken** (op een reservering):
  - De reservering wordt vrijgegeven en dezelfde hoeveelheid uit de gekozen partij gereserveerd (oudste pallet eerst), in één opslag.
  - Dat gebeurt met de bestaande auditregels van vrijgeven en reserveren, plus een orderregel "Partij gewijzigd: oud → nieuw".
  - De nieuwe reservering blijft bij dezelfde levering.
- Er is geen tweede reserveringsmodel: alles gaat via de bestaande reserveringen per palletregel.

## Kleur

- **Kleurenlijst per product:** onder Stamgegevens › Artikelen, bij het geopende artikel, gescheiden door komma's (bijvoorbeeld groen en geel, of 1 tot 6 voor ananas). Leeg betekent geen kleur voor dat product.
- **Kleur op de inkoop-partij:** het veld Kleur, bij de keuring of op het partijdetail. De kleur geldt voor de hele partij, niet per pallet.
- **Historie:** elke wijziging krijgt een historieregel met de oude en de nieuwe kleur (regel 43).
- **Waar te zien:** in de partijlijst, in de partijkeuze en in Beschikbaarheid per partij.

## Herleidbaarheid

- **Verkoop-partij** ("Waar komt partij … vandaan?"):
  - klant, order, levering en leverdatum (verzonden, anders laden op, anders de gevraagde leverdatum);
  - per regel de inkoop-partijen met leverancier en herkomst;
  - bij een directe factuur de factuurregels met hun inkoop-partij.
- **Inkoop-partij** ("Waar ging partij … naartoe?"):
  - ontvangen; geleverd per verkoop-partij (klant, colli, datum); teruggenomen; afgekeurd of afval; nog op voorraad;
  - **ontvangen = geleverd − teruggenomen + afgekeurd + op voorraad**, en een verschil staat in rood;
  - directe facturen staan apart, want zij boeken geen voorraad af.
- **Afdrukken en exporteren:** beide overzichten zijn af te drukken (ook als PDF via de afdrukdialoog) en als CSV te exporteren (puntkomma, getallen zoals in Nederland).

## Verdeel naar waarde

Bij het verdelen van kosten (P2) staat de knop **Verdeel naar waarde**.

- **Basis:** de inkoopwaarde in euro.
  - Inkoop-partij: de regelbedragen van haar inkooporders, gedeeld door de koers.
  - Verkoop-partij: de colli tegen hun inkoopprijs.
- **Afronding:** op centen, de restcent naar de grootste. Een partij zonder prijzen doet niet mee.

## Traceerbaarheid

| Regel | Wat er vastligt |
| --- | --- |
| 24 (uitgebreid) | Een verkoop-partij ontstaat met haar verkooporder, met haar directe factuur of met een nieuwe levering van haar order. Ze verandert nooit van order of factuur. |
| 31 (per levering) | Versies van laadbon en transportopdracht tellen per levering en soort. |
| 41 (nieuw) | Een nieuw of gewijzigd partijnummer deelt nooit de sleutel met een partij van de andere soort. De inkoop-partij van een factuurregel verandert nooit. |
| 42 (nieuw) | Een levering verdwijnt nooit en houdt order, volgnummer en partij. Een nieuwe levering krijgt een nieuwe verkoop-partij van dezelfde order en het volgende volgnummer. Een reservering verhuist alleen als ze actief is, en alleen met de auditregel. Een document hoort bij een levering van zijn order. |
| 43 (nieuw) | De gevraagde partij van een verkooporderregel ligt vast bij aanmaken en is een inkoop-partij. De kleur van een partij verandert alleen met een historieregel. |

## Migraties

| Migratie | Wat |
| --- | --- |
| `AddLotNumberingIV` | standaardformaten I- en V- (alleen de oude standaard omgezet); `Partijen.InvoiceId` en `InvoiceLine.PartijId` (leeg) |
| `AddSalesDeliveries` | tabel `SalesDeliveries`, één eerste levering per bestaande order met haar partij; `SalesOrderDocuments.SalesDeliveryId` (de eerste levering) en `StockReservations.SalesDeliveryId` (leeg) |
| `AddSalesLineLot` | `SalesOrderLines.PartijId` (leeg) |
| `AddLotColor` | `Products.ColorValues` en `Partijen.Color` (leeg); historie kent ColorChanged |
