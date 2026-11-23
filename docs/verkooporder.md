# Verkooporder als inkooporder, leveringsconditie per order en dienstregels

Feedback 22 november, prompt 3 van 5. Screenshots: [verkooporder-2026-11](verkooporder-2026-11/).

## De orderregel

Verkooporder en inkooporder gebruiken dezelfde regelcomponent (`PurchaseOrderLines`). Van links naar rechts staan deze kolommen:

| Kolom | Herkomst |
|---|---|
| Product | met snelcode |
| Merk, Land, Maat | uit het artikel |
| Bruto kg | uit het artikel; aan te passen met "Bruto aanpassen" (Alt+B) |
| Netto kg | uit het artikel |
| Aantal | eenheid en pallets komen uit het artikel |
| Inhoud | uit het artikel |
| Prijs | per eenheid van de regel |
| Korting % | leeg is geen korting |
| Btw | voorstel uit het artikel, per regel aan te passen |
| Bedrag | berekend |

- **Bedrag:** aantal × prijs min de korting, op centen. Zonder korting is dat precies aantal × prijs, zoals voorheen.
- **Totalen:** de totalenrij staat onder dezelfde kolommen. Daaronder staan totaal excl. btw, de btw per tarief (met het bedrag waarover) en totaal incl. btw.
- **Btw per tarief:** de btw wordt per regel op centen berekend en daarna per tarief opgeteld, net als op de factuur. Daardoor geeft een factuur die de regels overneemt dezelfde bedragen.
- **Tab-volgorde:** Product, Aantal, Prijs, Korting, Btw. Bij inkoop volgt daarna nog GGN.
- **Nieuwe regel:** Tab op het laatste veld van de laatste regel maakt een nieuwe regel.
- **Verkooporder:** behoudt per regel de stand (Voorraad · Verwacht · Vrij), de keuze "Uit voorraad / Uit verwachte aanvoer", splitsen, reserveren en de herkomst per stuk.
- **Verkooporderdetail:** toont de regels in dezelfde indeling, met totalen en btw per tarief. Reserveren gebeurt daaronder per regel, zoals altijd.
- **Bestaande regels** blijven zoals ze zijn opgeslagen:
  - zonder korting;
  - zonder btw-tarief, en zo'n regel wordt bij de totalen apart genoemd;
  - verkoopregels zonder prijs.

## Leveringsconditie per order

- **Velden:** verkooporder en inkooporder hebben het veld Leveringsconditie: Incoterm met plaats, Franco, Af kwekerij of een eigen omschrijving.
- **Voorinvulling:** het veld neemt de conditie van de klant of leverancier over. Een aanpassing geldt alleen voor de order; de relatie verandert niet.
- **Verkooporder:** de conditie is ook na het opslaan aan te passen ("Aanpassen" in de kop), zolang de order open is. Dat wordt een regel in de historie met oud en nieuw.
- **Inkooporder:** de conditie wordt aangepast met Aanpassen, zolang de order Open is. Een definitieve order houdt haar conditie.
- **Documenten:** laadbon en transportopdracht tonen de conditie niet. Daarom verandert een bestaand document hierdoor ook niet.

## Dienstregels

- **Wat:** handling of transport die aan de klant wordt doorberekend. Dat is een aparte regel met een Dienst-artikel en het btw-tarief van dat artikel (standaard 21%, per regel aan te passen).
- **Niet:** de regel telt niet mee in voorraad, pallets of kg, en staat niet op de laadbon of de transportopdracht.
- **Wel:** de regel telt mee in de totalen en de btw per tarief, en komt straks op de factuur.
- **Verschil met eigen kosten:** handling die het bedrijf zelf betaalt, hoort als kost bij de partij.
- **Emballage** werkt zoals voorheen.

## Traceerbaarheid

| Regel | Wat er vastligt |
|---|---|
| 29 (versterkt) | Korting, btw en leveringsconditie van een inkooporder veranderen alleen met een Edit-auditregel. |
| 36 | Prijs, korting en btw van een opgeslagen verkooporderregel veranderen nooit. |
| 37 | De leveringsconditie van een order verandert alleen met een auditregel: bij een verkooporder `DeliveryTermChanged`, bij een inkooporder een Edit. |
| 38 | Een dienstregel heeft een Dienst-artikel en verandert of verdwijnt nooit. |

## Nog niet

- **Verkoopfactuur:** bestaat nog niet voor de nieuwe verkooporder. De bedragen en de btw per tarief zijn zo gemaakt dat de factuur ze straks kan overnemen; de test "order tot factuur" laat zien dat ze op de cent gelijk zijn.
- **Btw uit het land van de klant** (verlegd, export): komt in de btw-ronde.
- **Partij kiezen op de regel:** komt in de partij-ronde.
- **Orderbevestiging aan de klant:** valt buiten deze ronde.
