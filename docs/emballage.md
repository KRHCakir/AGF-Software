# Emballage (fust) op de verkooporder

Pallets en kratten staan op papier en op een orderbevestiging als gewone regel naast de goederen, bijvoorbeeld "Pallet wegwerp, 1 stuk à 7,50". Dat kan nu ook in het systeem.

## Wat nu kan

**Emballage-artikelen** staan bij **Artikelen › Emballage**, naast de goederen.
- Standaard zijn er Pallet wegwerp, Euro-pallet en Krat. Je maakt er zelf bij, zoals Fust CBL of Doos. Bestond er al een kostenartikel met dezelfde naam, dan heet de standaard-emballage "Krat (emballage)".
- Per artikel leg je een **standaardprijs per stuk** vast (excl. btw, mag leeg) en een **btw-percentage**. De standaard is 9%: verpakking die met groente en fruit meegaat, volgt het tarief van de goederen. Wordt emballage los geleverd of anders belast, dan pas je het per artikel aan; vraag dat bij twijfel na bij de boekhouder.
- Een emballage-artikel heeft geen maat, klasse, herkomst of voorraad.
  - Het staat niet in Beschikbaarheid, FIFO, Verwacht of Tekort.
  - Het staat ook niet in "Maat en klasse opschonen" en is niet te reserveren.
- Verwijderen kan niet, want orderregels en factuurregels verwijzen ernaar. Een artikel dat je niet meer gebruikt, maak je inactief.

**Emballageregels op een nieuwe verkooporder** staan onder de goederenregels.
- Per regel kies je een emballage-artikel, een heel aantal en een prijs per stuk. De prijs is voorgevuld met de standaardprijs en mag je aanpassen. Een gratis ruil, zoals Euro-pallets ruilen, krijgt 0,00. Zonder prijs mag ook; die vult de factuur later in.
- **Pallet toevoegen** zet een regel Pallet wegwerp neer.
  - Het aantal is een voorstel: de pallets van de goederenregels opgeteld en naar boven afgerond. Dat is hetzelfde getal als "= x pallets" bij elke regel. 110 colli kiwi met 120 per pallet wordt 1 pallet.
  - Het voorstel komt er alleen na een klik en blijft aanpasbaar. Er komt nooit vanzelf een regel bij.
- De btw komt van het artikel en staat bij het opslaan vast op de regel.
- Tab loopt per regel van artikel naar aantal, prijs en de verwijderknop, net als bij de goederenregels, en daarna naar de volgende regel.
- Een order met **alleen emballage** mag ook. In de orderlijst heet die "Alleen emballage"; hij telt niet als order met goederen en wordt dus nooit "verzonden" of "afgeleverd".

Een emballageregel doet niets met de voorraad. Er is geen reservering, geen controle op "Uit voorraad", geen invloed op Vrij te verkopen en geen splitsen. Reserveren en verzenden gaan alleen over de goederenregels, en de emballage staat als apart blok op de order.

**Zoeken** op een emballage-artikel ("euro-pal") vindt de orders waar het op staat.

**Inkoopzijde.** Brengt een leverancier pallets of kratten in rekening, dan kies je op de inkoopfactuur het emballage-artikel in de groep **Emballage**. Dat is een kostenregel zoals Handling of Transport: algemene kosten, of verdeeld over een partij (zie Partij P2).

## Vastgelegd

- Emballage-artikelen staan in dezelfde tabel als de kostenartikelen (`CostArticles`), met soort `Packaging`. De naam is uniek over beide soorten. Kostensoorten beheer je bij Instellingen, emballage bij Artikelen.
- Emballageregels staan in een eigen tabel, `SalesOrderPackagingLines`, met artikel, aantal, prijs per stuk en btw.
  - Een goederenregel heeft nog geen prijs. Daarom is er geen tweede prijssysteem: de prijs bestaat alleen op de emballageregel.
  - Prijzen voor goederenregels komen met de verkoopfactuur.
- Guard-regel 27:
  - een emballageregel heeft altijd een emballage-artikel;
  - een opgeslagen emballageregel verandert nooit en wordt nooit verwijderd;
  - de soort van een artikel verandert nooit.
- Rechten: emballage-artikelen vallen onder `Stamgegevens` (lezen mag iedereen) en orderregels onder `Verkoop`. De maker van een artikel komt uit `CurrentUser`.

## Voor de verkoopfactuur (PR 4a)

De factuur neemt de emballageregels van de order over, met artikel, aantal, prijs per stuk en btw. Daarvoor moet 4a nog:

1. `SalesOrder.PackagingLines` lezen en per regel een factuurregel maken.
2. De omschrijving en het btw-percentage van de orderregel gebruiken, niet die van het artikel: die kunnen intussen veranderd zijn.
3. Een regel zonder prijs bij het factureren laten aanvullen, of de factuur weigeren tot er een prijs is.
4. Vastleggen welke emballageregels gefactureerd zijn. Dat kan met een eigen koppeltabel, zodat regel 27 (de orderregel verandert nooit) blijft gelden.

## Bewust niet gebouwd

- **Saldo per klant of relatie.** Hoeveel pallets en kratten er nog buiten staan, wacht op het antwoord van de vader.
- **Retour- of ruilverwerking** en **statiegeld terugboeken.** Een gratis ruil is nu alleen een regel van 0,00.
- **Poolpartijen**, zoals CHEP of IFCO.
- **Pallets of kratten in de voorraad of in Beschikbaarheid.**
- **Pakbon en CMR.** Het aantal pallets daarop komt in een latere ronde. In deze ronde is niets aan de pakbon of CMR veranderd.
- **Emballage op een bestaande order zetten.** Een verkooporder heeft nog geen bewerkscherm voor regels, dus emballage komt er bij het aanmaken op.

## Hoe een saldo-administratie later kan aansluiten

Een saldo is een optelsom van bewegingen per klant en artikel, met de soorten uitgeleverd, retour en gecorrigeerd. Een beweging is append-only en heeft een actor uit `CurrentUser`, een datum en een bron.
- Een emballageregel op een verkooporder kan bij verzending een beweging "uitgeleverd" geven.
- Een retourbon geeft "retour".
- Een correctie heeft altijd een reden.

De emballageregel blijft wat hij nu is: wat er op de order en de factuur staat. Het saldo wordt een eigen tabel ernaast, zodat het nooit de factuur of de voorraad verandert.
