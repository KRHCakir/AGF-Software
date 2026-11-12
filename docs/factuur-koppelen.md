# Inkoopfactuur koppelen aan de bon (stap 4b)

Een inkoopfactuur factureert nu echt de regels van een inkooporder (de "bon"). Daarvoor bestond de koppeling alleen als verwijzing:

- **Factuur ontvangen** op de order legde nummer, datum en bedrag vast, zonder inkoopfactuur.
- **Hoort bij order …** in Inkoopfactuur invoeren was alleen een melding; opslaan legde niets vast.
- **Verwijzing ontvangst** op de factuur is vrije tekst.

Na verversen hing er dus niets aan elkaar.

## Wat er in de praktijk verandert

1. **Factuur invoeren vanaf de order.** De knop opent Inkoopfactuur invoeren met de leverancier en de open regels van die bon:
   artikel, eenheid, het aantal dat nog open staat en de prijs op de bon. Ook vanuit Inkoopfactuur invoeren zelf kan dat: na de
   leverancier staat "Koppelen aan inkooporder (bon)", met zoeken op ordernummer leverancier, IO-nummer, partij en factuurnummer.
   "Hoort bij order …" heeft nu ook "Regels overnemen".
2. **Alleen aanpassen wat afwijkt.** Artikel en eenheid van een regel uit de bon liggen vast. Onder elke regel staat wat de bon
   zegt (aantal × prijs = bedrag), wat al gefactureerd en nog open is, en het verschil in kleur:
   "Bon 13,50, factuur 12,50: verschil −1,00 per colli, −264,00 totaal". Te veel is oranje, deels grijs.
3. **Eenmaal per factuur de vraag** (bij opslaan, op de pagina):
   - **Bon aanpassen aan de factuur** (aanbevolen). De prijs op de orderregel wordt de factuurprijs, met een auditregel (oud, nieuw,
     wie, "n.a.v. factuur …"). Dat kan alleen bij een open magazijnorder.
   - **Bon laten staan en het verschil vastleggen.** Het verschil blijft zichtbaar op de factuur en op de order, voor de latere
     matchcontrole of een creditnota.

   Meer factureren dan besteld of ontvangen (met eerdere facturen erbij) vraagt "Toch opslaan". Er wordt nooit stilletjes iets aangepast.
4. **De order wordt definitief** zodra een factuur gekoppeld is (stap 4a, via Invoice). Een order die al definitief was blijft dat;
   de prijs verander je daar alleen met Correctie (met reden).
5. **Terugvinden.** De order toont haar facturen (link, bedrag, verschil) en per regel wat gefactureerd en open is. De lijst toont
   Gefactureerd / Deels / Niet, met een filter. De keten-strip heeft de stap Factuur met de facturen als link. Het factuurdetail toont
   de orders en per regel de bon tegenover de factuur. Zoeken op factuurnummer vindt de order, en zoeken op ordernummer de factuur.

Een factuur zonder bon (een kostenfactuur, P2) werkt zoals altijd. Een dubbele factuur (zelfde leverancier en nummer) blijft geweigerd.

## Regels

| Regel | Waar |
|---|---|
| Een factuurregel factureert hoogstens één orderregel, van een order van dezelfde leverancier, niet geannuleerd. Artikel en eenheid zijn die van de orderregel. | `PurchaseInvoiceOrderLinks` |
| Waartegen gefactureerd wordt: wat ontvangen is zodra er iets ontvangen is, anders wat besteld is. Afgewezen facturen tellen niet mee. | `PurchaseInvoiceOrderLinkRules.InvoiceBasis` |
| De factuurprijs per eenheid is de prijs min de regelkorting, op 2 decimalen. Een bon zonder prijs telt als verschil: de factuur zet dan de prijs. | `InvoiceUnitPrice`, `HasPriceDifference` |
| Gefactureerd: Niet (geen factuurregel), Deels, of Gefactureerd (elke regel tot de basis). Een oude "Factuur ontvangen" zonder regels telt als gefactureerd. | `BillingStatus` |
| Koppeling, prijs op de bon en de keuze bij een verschil liggen vast na opslaan. | TraceabilityGuard regel 30 |
| Bon aanpassen en definitief maken zijn aparte saves in dezelfde transactie. | regel 29 (stap 4a) |

## Data

Migratie `AddPurchaseInvoiceOrderLinks` (58) voegt alleen lege kolommen toe:

- `IncomingInvoiceLines.SupplyCommitmentLineId`, met een FK die verwijderen tegenhoudt;
- `IncomingInvoiceLines.OrderUnitPrice`;
- `IncomingInvoices.OrderPriceDecision` (`AdjustOrder` of `RecordDifference`).

Bestaande facturen en orders blijven zoals ze zijn. Bij een gekoppelde factuur legt de order ook een verwijzing vast, zoals Factuur ontvangen, met de factuur erbij. Staat dat nummer er al, dan blijft die verwijzing ongewijzigd (append-only).

## Bewust niet gebouwd

- AI-voorstellen die orderregels koppelen. De AI-goedkeuring met een orderkoppeling maakt de order wel definitief.
- Verkoopfacturen en creditnota's als document.
- De volledige matchcontrole met werklijst (PR 3 van de keten).
- Export naar Exact en periodeafsluiting.
- Een factuur later aanpassen: een inkoopfactuur wordt nog niet bewerkt.

## Schermafbeeldingen (`docs/factuur-koppelen-2026-11/`)

Gemaakt in Chrome op een lege testdatabase, alleen met DEMO-TOUR-gegevens. De order is Avocado Hass, 264 colli × 13,50 = 3.564,00.

| Bestand | Wat |
|---|---|
| `01-order-factuur-invoeren-desktop.png` | De order met **Factuur invoeren** en **Creditnota ontvangen** |
| `02-factuur-uit-de-bon-*.png` | De factuur, voorgevuld uit de bon (264 colli, 13,50) |
| `03-verschil-in-kleur-*.png` | Factuurprijs 12,50: "verschil −1,00 per colli, −264,00 totaal" in rood |
| `04-eenmaal-de-vraag-*.png` | De vraag bij opslaan: bon aanpassen of verschil vastleggen |
| `05-order-definitief-met-factuur-*.png` | De order na Bon aanpassen: definitief, prijs 12,50, de factuur in het blok Facturen, de auditregel "n.a.v. factuur" |
| `06-factuurdetail-met-order-desktop.png` | Een factuur met dezelfde bedragen (geen vraag), met de gekoppelde order |
| `07-orderlijst-gefactureerd-desktop.png` | De orderlijst met de badges Definitief en Gefactureerd |

`*` = `desktop` (1440 px) en `390` (telefoon). Op 390 px scrolt de pagina niet horizontaal; alleen de regeltabel scrolt binnen haar kader, zoals voorheen.
