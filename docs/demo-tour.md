# Demo-rondleiding met autopilot

De Demo-assistent (rechtsonder in de app) heeft een tab **Rondleiding**. Met de autopilot neemt de assistent je mee door een hele flow:
hij kiest en vult zelf in, en vertelt bij elke stap **wat** hij doet en **waarom**. Alles is een vast script
(`agf-frontend/src/lib/tour/`), er zit geen AI in. De rondleiding is bedoeld voor de eigenaar zelf (leren, demo geven), niet voor
klanten.

## Afspraak voor nieuwe functies

**Elke nieuwe functie die een scherm toevoegt of wijzigt, neemt het bijbehorende hoofdstuk van de rondleiding mee: het script in
`lib/tour/`, de `data-tour`-attributen op de schermelementen en de test (`e2e/tour.spec.ts`).** Dat geldt voor alle hoofdstukken
hieronder, ook Stamgegevens, Inkoopfactuur invoeren en De rode draad. Al gepland:
- mixpallets;
- verkoopfactuur en creditfactuur;
- periodeafsluiting;
- de opzoeking van een adres op postcode en huisnummer (zodra die er is, krijgt Stamgegevens die stap).

## Hoofdstukken

| # | Hoofdstuk | Wat de rondleiding doet |
|---|---|---|
| 1 | Kennismaken met de schermen | Een rondje langs de vier menugroepen (Inkoop, Voorraad, Verkoop, Stamgegevens). Alleen kijken. |
| 2 | Stamgegevens | Een leverancier en een klant met adres (met de hand ingevuld). Een artikel met herkomst uit de landenlijst ("PE" vindt Peru), verplichte maat en klasse, en een berekend gewicht. |
| 3 | Inkooporder aanmaken | Met Tab door de regel (artikel, aantal, eenheid, kg) en een datum met tweecijferig jaar (`07-11-26` wordt `07-11-2026`). Daarbij: de voorgestelde partij, de partij van de leverancier en de leveranciersreferentie. Daarna de factuur koppelen en de partij terugvinden onder Voorraad › Partijen. |
| 4 | Ontvangst en keuring | Blind invullen wat er aankomt, en keuren met **Wie beoordeelt** per keuring (beoordelaar én ingelogde gebruiker in de historie). Daarna een deel afkeuren met reden en besluit, en kijken wat verkoopbaar is. |
| 5 | Afwijking bij ontvangst | De helft komt uit een ander land en is 5% zwaarder. Bestaat die variant nog niet, dan maakt de ontvangst hem aan. Daarna "Klopt dit?" met herkomst en gewicht boven 2%, en de afwijking op de order. De voorraad staat op de exacte variant. |
| 6 | Voorraad en verkopen voor aankomst | Beschikbaarheid (Voorraad, Verwacht, Verkocht, Vrij te verkopen). Met **Verkopen** meer verkopen dan er is: Tekort, geen blokkade. Daarna ontvangen en keuren, en via de werklijst "Verkocht, wacht op voorraad" reserveren met oudste partij eerst. |
| 7 | Verkooporder, reserveren en verzenden | VO-nummer, klantreferentie en de voorgestelde verkoop-partij (naast het VO-nummer). Reserveren op een pallet met per stuk de inkooporder, partij en pallet, daarna verzenden. |
| 8 | De rode draad | De keten-strip op inkooporder, ontvangst en verkooporder, en het partijdetail onder Voorraad › Partijen. |
| 9 | Inkoopfactuur invoeren | De factuur van de leverancier, met een regel waarvan het bedrag `120,00` wordt, en de totalen. |
| 10 | Kosten verdelen over partijen | Een factuur van derden zonder inkooporder: kostenartikel Handling, € 5.000. Daarna "Aan welke partij hoort dit?": € 500 op de verkoop-partij van de rondleiding, de rest blijft algemene kosten. Tot slot het blok Kosten op die partij. Op een lege database maakt het hoofdstuk eerst een verkoop aan. |
| 11 | Verkoopfacturen | Nog een lege plek: "Komt zodra facturen klaar zijn". |

Wie iets vastlegt, volgt altijd uit de ingelogde gebruiker; er is geen veld "Medewerker" meer. Alleen bij Keuring kiest de rondleiding
expliciet de beoordelaar ("Demo-assistent"), zoals een gebruiker dat per keuring doet. Na afloop staat de eigen onthouden beoordelaar
weer terug.

## Eigen voorbeeldgegevens, ook op een lege database

De rondleiding werkt met haar **eigen** voorbeeldgegevens, herkenbaar aan `DEMO-TOUR-` plus een volgnummer:
- leverancier `DEMO-TOUR-… Kwekerij` (zoekcode `DEMO-TOUR-…-LEV`);
- klant `DEMO-TOUR-… Groothandel` (zoekcode `DEMO-TOUR-…-KLANT`);
- artikel Avocado Hass uit Peru, maat 18, klasse I, met merk en snelcode `DEMO-TOUR-…`;
- in de referentievelden van orders en facturen.

Ze gebruikt nooit bestaande relaties of artikelen van de eigenaar.

Elk hoofdstuk controleert eerst met gewone GET-aanroepen wat er al is. Ontbreekt er iets, dan maakt het hoofdstuk het eerst zelf aan,
met precies de stappen (en teksten) van Stamgegevens, Inkooporder aanmaken, Ontvangst en Verkooporder. Wat al bestaat, wordt
overgeslagen; na afloop staat in het paneel hoeveel stappen er zijn overgeslagen. Zo is elk hoofdstuk los te starten op een lege
database (zie `docs/schone-start.md`). Op een lege database maakt De rode draad bijvoorbeeld eerst een leverancier, klant, artikel,
inkooporder, ontvangst en verkoop aan.

Speel de rondleiding bij voorkeur niet af op de database waarin je je echte gegevens zet: de DEMO-TOUR-gegevens blijven daar staan.

## Aanzetten

Twee frontend-instellingen, allebei standaard uit:

| Instelling | Wat |
|---|---|
| `NEXT_PUBLIC_DEMO_ASSISTANT=true` | Toont de Demo-assistent (rondleiding langs de schermen en order zoeken, alleen lezen). |
| `NEXT_PUBLIC_DEMO_TOUR=true` | Maakt van de tab Rondleiding de autopilot, die op de demo-omgeving ook gegevens invult. |

Voor de demo-server op poort 3000 zet je beide regels in `agf-frontend/.env.local` (die staat niet in git) en herstart je de
dev-server. Het zijn `NEXT_PUBLIC_`-instellingen: ze worden bij het bouwen in de frontend gezet. Op productie staan ze niet in de
omgeving en zijn ze dus uit. Zonder `NEXT_PUBLIC_DEMO_TOUR` blijft de rondleiding alleen navigeren en uitleggen. De rode draad werkt
dan alleen als er al een demo-order met verkoop is.

## Veiligheid

- Schrijvende hoofdstukken werken alleen met `NEXT_PUBLIC_DEMO_TOUR=true`. Gebruik dat alleen op een demo-omgeving, nooit tegen
  echte klantgegevens.
- Invullen gaat via echte toetsenbord- en invoer-events, zodat dezelfde zoekers, controles en API-regels gelden als bij een gebruiker.
  De rondleiding roept zelf geen schrijvende API aan; de voorcontrole en de tussentijdse controles (`lookup`) lezen alleen (GET).
- Wat de rondleiding vastlegt, staat op naam van de ingelogde gebruiker. Bij de keuring is de beoordelaar "Demo-assistent".
- Navigeren kan alleen naar vaste routes (`isAllowedDemoHref`), nooit naar externe URL's.
- Elementen worden alleen gevonden via `data-tour="…"` (en eventueel `data-tour-key`), nooit via CSS-klassen of tekst.

## Opruimen

Afkeuringen, factuurkoppelingen en andere sporen zijn bewust append-only. Die bewaking wordt niet verzwakt en er is geen
verwijderfunctie. Opruimen gaat met een momentopname van de demo-database:

```bash
# vóór de demo of de rondleiding
scripts/demo-db-snapshot.sh save agf_smoke
# erna: API op die database stoppen, dan
scripts/demo-db-snapshot.sh restore agf_smoke
```

Het script weigert databases waarvan de naam geen `smoke`, `demo` of `tour` bevat, en vraagt bij restore om de databasenaam ter
bevestiging. Restore zet de hele database terug: ook ander werk van na de momentopname verdwijnt. Wil je een schone demo, maak dan
gewoon een nieuwe lege database met `scripts/new-clean-db.sh` en speel de rondleiding daarop af.

## E2E-tests (`npm run test:tour`)

Playwright speelt elk hoofdstuk af door het echte paneel (Kijk mee en Doe zelf). Het controleert:
- dat elke stap voorbijkomt of bewust is overgeslagen;
- dat geen controle faalt;
- dat de eigen beoordelaar terugkomt;
- wat er via de API in het systeem staat.

De test zet zelf niets klaar. Draai hem altijd tegen een aparte testdatabase, nooit tegen `agf_smoke`, en wel op twee soorten
databases:

1. **Lege database:** `scripts/new-clean-db.sh agf_tour_empty_test --settings-from agf_smoke`, en `TOUR_EMPTY_DATABASE=true` bij de
   test. Het eerste schrijvende hoofdstuk (De rode draad, los gestart) moet dan alles zelf aanmaken, zonder één stap over te slaan.
2. **Gevulde database:** een kopie van de demo, bijvoorbeeld
   `pg_dump -Fc -d agf_smoke -f /tmp/demo.dump && createdb agf_tour_filled_test && pg_restore -d agf_tour_filled_test --no-owner /tmp/demo.dump`.

Per database:
1. **API** op de testdatabase (poort 5198, CORS voor de testfrontend):
   `ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=<testdatabase>;Username=…" ASPNETCORE_URLS=http://localhost:5198 Cors__Origins__0=http://localhost:3001 dotnet run --project AgfAdmin --no-launch-profile`
2. **Frontend** met de rondleiding aan (poort 3001, naast de demo op 3000):
   `NEXT_PUBLIC_API_URL=http://localhost:5198/api NEXT_PUBLIC_DEMO_ASSISTANT=true NEXT_PUBLIC_DEMO_TOUR=true npx next dev -p 3001`
3. **Test** in `agf-frontend`: `TOUR_TEST_DATABASE=<testdatabase> [TOUR_EMPTY_DATABASE=true] npm run test:tour`. Dat duurt ongeveer 12
   minuten op een lege database.

De configuratie (`playwright.tour.config.ts`) draait eerst het project `tour` (`tour.spec.ts`, de rondleiding). Daarna komt het
project `flows`:
- `date-field.spec.ts`: het datumveld, ook `04-11-26`;
- `keyboard-lines.spec.ts`: een orderregel met alleen Tab, pijlen en Enter;
- `sales-chain.spec.ts`: verkopen gekoppeld aan voorraad.

Die flows maken hun eigen vaste testgegevens (`e2e/fixtures.ts`: "Nature's Pride", "Lokaal Fruit B.V.", Avocado Hass) als die
ontbreken. Zo begint de rondleiding op een lege database echt leeg.

Standaard gebruikt de test de geïnstalleerde Google Chrome. Zonder Chrome: `npx playwright install chromium` en
`TOUR_BROWSER_CHANNEL=chromium`. Andere adressen: `TOUR_BASE_URL` en `TOUR_API_URL`. Meekijken: `TOUR_HEADED=true`. De set zit (nog)
niet in de standaard-CI: hij heeft Postgres, de API en een draaiende frontend nodig.

## Een hoofdstuk schrijven

- **Opbouw:** een hoofdstuk (`TourChapter`) heeft stappen (`TourStep`). Elke stap heeft een route, `titleKey`, `whatKey` ("Nu doen we
  dit"), `whyKey` ("Waarom") en acties:
  - `navigate`, `highlight`, `type`, `select`, `click` en `wait`;
  - `pick`: typen en de suggestie met Tab nemen;
  - `assert`: controleren wat er staat;
  - `remember`: een tekst onthouden, zoals het nieuwe ordernummer;
  - `lookup`: halverwege opnieuw lezen met GET-aanroepen.
- **Variabelen:** teksten mogen `{{tourRef}}`, `{{actor}}`, `{{today}}` en gevonden of onthouden variabelen bevatten.
- **`when` op een actie:** de actie wordt overgeslagen als die tekst na invullen leeg is, bijv. Klasse of Merk die het scherm bij dit
  artikel niet vraagt.
- **`when` op een stap:** de hele stap wordt overgeslagen. Zo komen voorwaarden in een hoofdstuk: `prerequisite(stappen, prefix, '{{needVendor}}')`
  (in `compose.ts`) neemt de stappen van een ander hoofdstuk over, met eigen ids. De voorcontrole zet de vlaggen `needVendor`,
  `needCustomer`, `needArticle`, `needOrder`, `needStock` en `needSale`.
- **`lookup`-acties** lopen vóór het navigeren naar de route van de stap, zodat de route een gevonden waarde mag bevatten
  (`/availability?productVariantId={{variantId}}`). Een `data-tour`-naam komt nooit uit een variabele, alleen de sleutel.
- **Fouten:** een `assert` die niet klopt, stopt de rondleiding met "Deze stap kon niet uitgevoerd worden" in plaats van door te gaan.
- **Voorcontrole:** de `precheck` leest met gewone GET-aanroepen wat er is. Een hoofdstuk dat zelf niets schrijft maar eerst iets moet
  aanmaken (De rode draad op een lege database), geeft `writes: true` terug.
- **Teksten** staan in `i18n/nl.json` en `i18n/en.json`, met dezelfde sleutels.
