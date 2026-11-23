# Sorteren op kolomkoppen (feedback 22 november)

Boven elke lijst klik je op een kolomtitel om te sorteren. Screenshots met alleen verzonnen DEMO-gegevens staan in
[sorteren-2026-11](sorteren-2026-11/).

## Zo werkt het

- Eén klik sorteert oplopend, nog een klik aflopend, een derde klik zet de standaardvolgorde van de lijst terug.
- Het pijltje naast de titel toont de richting: ▲ oplopend, ▼ aflopend, ↕ niet gesorteerd. Voor een schermlezer staat `aria-sort`
  op de gesorteerde kolom.
- De kop is een knop: bereikbaar met Tab, te bedienen met Enter en spatie.
- Een kolom met twee nummers heeft een tweede, kleine knop onder de titel. Bij inkooporders is dat "Intern nummer" onder
  "Ordernr"; bij verkooporders "Klant" en "Partij"; bij verkoop-partijen "Klant".
- Op een smal scherm (390 px) staan sommige lijsten als kaarten. Daar staat boven de kaarten de keuzelijst **Sorteren op** met
  dezelfde velden in beide richtingen, en Standaard.
- De keuze wordt per scherm onthouden in deze browser (`localStorage`, sleutel `agf_sort_<scherm>`). Zonder opslag (privévenster,
  geblokkeerde opslag) werkt sorteren gewoon, alleen zonder onthouden.

## Volgorde per soort

| Soort | Volgorde | Voorbeelden |
| --- | --- | --- |
| Tekst | Alfabetisch; hoofdletters en accenten tellen niet | leverancier, product, klant |
| Getal en bedrag | Als getal; negatief (tekort) eerst bij oplopend | colli, netto kg, bedrag in euro, vrij te verkopen |
| Datum | Als datum | besteld, verwacht, leverdatum, geldig tot |
| Nummer | Natuurlijk: 2 voor 10, ook met letters ervoor zoals I- of V- | ordernummer, partijnummer, factuurnummer, certificaatnummer |
| Status | In de volgorde van het proces, niet alfabetisch | Besteld, Deels ontvangen, Ontvangen, Afgekeurd, Geannuleerd |

Een lege waarde staat altijd onderaan, in beide richtingen. Gelijke waarden houden de standaardvolgorde.

## Op de server of in de browser

Lijsten die op de server bladeren, sorteren op de server, over alle rijen na de filters, en pas daarna per pagina:

| Lijst | API | Toegestane velden (`sort`) |
| --- | --- | --- |
| Inkooporders | `GET /api/purchase-orders` | number, orderNumber, vendor, partij, orderedOn, expected, status, packages, netKg, amount, invoices |
| Partijen (inkoop en verkoop) | `GET /api/partijen` | number, vendor, product, purchased, received, onHand, sold, ordered, status, salesOrder, customer, sources, createdAt |
| Relaties | `GET /api/relations` | code, name, role, status, place, contact |
| Certificaten | `GET /api/certificates` | relation, type, number, products, validUntil, status |

- `direction` is `asc` of `desc`.
- Een ander veld of een andere richting geeft 400 met de toegestane velden; de server neemt nooit een vrije veldnaam aan.
- Zonder `sort` blijft de standaardvolgorde.
- De velden staan in `AgfAdmin/Services/ListSortFields.cs` en de vergelijking in `ListSort.cs`, gelijk aan `lib/listSort.ts` op de
  website.

De andere lijsten hebben alles al in de browser en sorteren daar. De inkoopfacturen tonen de 200 nieuwste facturen (zoeken vindt
oudere); er wordt binnen die 200 gesorteerd.

## Welke lijsten

| Scherm | Lijst | Hoe |
| --- | --- | --- |
| Inkoop › Inkooporders | Orderlijst | Kolomkoppen (server); op 390 px Sorteren op |
| Inkoop › Keuring | Pallets (kaarten) | Sorteren op: pallet, ontvangen, locatie, product |
| Inkoop › Inkoopfacturen | Facturen | Kolomkoppen |
| Voorraad › Beschikbaarheid | Verkoopweergave, compact, alle kolommen, klantgebonden aanvoer | Kolomkoppen, per weergave onthouden; verkoopweergave op 390 px Sorteren op |
| Voorraad › Partijen | Inkoop- en verkoop-partijen | Kolomkoppen (server); op 390 px Sorteren op |
| Verkoop › Verkooporders | Orderlijst | Kolomkoppen |
| Verkoop › Verkooporders | Werklijst "Verkocht, wacht op voorraad" | Sorteren op |
| Verkoop › Te versturen | Mailconcepten | Sorteren op |
| Stamgegevens › Relaties | Relatielijst | Kolomkoppen (server); de kolom Certificaten sorteert niet (meerdere per relatie) |
| Stamgegevens › Artikelen | Artikellijst | Kolomkoppen |
| Stamgegevens › Artikelen | Werklijsten Btw nog invullen, Herkomst controleren, Klasse opschonen | Sorteren op |
| Stamgegevens › Certificaten | Certificaten | Kolomkoppen (server) |

## Niet gesorteerd

- **Ontvangst** (`/receipts`) en **Klantgebonden aanvoer** (`/direct-trade`) zijn invoerschermen zonder lijst. De ontvangen pallets
  staan in Keuring.
- **Verkoopfacturen**: er is nog geen lijst in de nieuwe werkplek.
- Regels binnen één order, factuur of pallet: dat zijn invoer- en detailtabellen in de volgorde van de regels.
- De oude pagina's (Bestellingen, Voorraad en Facturen (oud), Producten): die worden niet meer uitgebreid.
- Instellingen (kostenartikelen, betalingscondities, valuta): korte beheerlijsten.

## Rondleiding

Het hoofdstuk Inkooporder wijst de kolomkop Leverancier aan en legt het sorteren uit. Het klikt niet: een klik zou de sortering in
de browser van de gebruiker onthouden.
