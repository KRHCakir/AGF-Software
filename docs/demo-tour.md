# Demo-rondleiding met autopilot

De Demo-assistent (rechtsonder in de app) heeft een tab **Rondleiding**. Met de autopilot neemt de assistent je mee door
een hele flow: hij kiest en vult zelf in, en vertelt bij elke stap **wat** hij doet en **waarom**. Alles is een vast
script (`agf-frontend/src/lib/tour/`), er zit geen AI in. De rondleiding is bedoeld voor de eigenaar zelf (leren, demo
geven), niet voor klanten.

## Afspraak voor nieuwe functies

**Elke nieuwe functie die een scherm toevoegt of wijzigt, neemt het bijbehorende hoofdstuk van de rondleiding mee:
het script in `lib/tour/`, de `data-tour`-attributen op de schermelementen en de test.** Al gepland: inkoop-afwijkingen
en mixpallets, verkoopfactuur, creditfactuur en periodeafsluiting.

## Aanzetten

Twee frontend-instellingen, allebei standaard uit:

| Instelling | Wat |
|---|---|
| `NEXT_PUBLIC_DEMO_ASSISTANT=true` | Toont de Demo-assistent (rondleiding langs de schermen en order zoeken, alleen lezen). |
| `NEXT_PUBLIC_DEMO_TOUR=true` | Maakt van de tab Rondleiding de autopilot, die op de demo-omgeving ook gegevens invult. |

Voor de demo-server op poort 3000: zet beide regels in `agf-frontend/.env.local` (die staat niet in git) en herstart de
dev-server. Het zijn `NEXT_PUBLIC_`-instellingen: ze worden bij het bouwen in de frontend gezet. Op productie staan ze
niet in de omgeving en zijn ze dus uit; zonder `NEXT_PUBLIC_DEMO_TOUR` blijft de rondleiding alleen navigeren en uitleggen.

## Veiligheid

- Schrijvende hoofdstukken werken alleen met `NEXT_PUBLIC_DEMO_TOUR=true`. Gebruik dat alleen op een demo-omgeving,
  nooit tegen echte klantgegevens.
- Invullen gaat via echte toetsenbord- en invoer-events, zodat dezelfde zoekers, controles en API-regels gelden als bij
  een gebruiker. De rondleiding roept zelf geen schrijvende API aan; alleen de voorcontrole leest (GET).
- Tijdens een hoofdstuk staat de medewerker op **Demo-assistent** (audit en historie). Na afloop, bij Stop of bij een fout
  komt de eigen naam terug.
- Alles wat de rondleiding aanmaakt, krijgt `DEMO-TOUR-` plus een volgnummer in de vrije referentievelden
  (leveranciersreferentie, klantreferentie), zodat het terug te vinden is.
- Navigeren kan alleen naar vaste routes (`isAllowedDemoHref`), nooit naar externe URL's.
- Elementen worden alleen gevonden via `data-tour="…"` (en eventueel `data-tour-key`), nooit via CSS-klassen of tekst.

## Opruimen

Afkeuringen, factuurkoppelingen en andere sporen zijn bewust append-only; die bewaking wordt niet verzwakt en er is
geen verwijderfunctie. Opruimen gaat met een momentopname van de demo-database:

```bash
# vóór de demo of de rondleiding
scripts/demo-db-snapshot.sh save agf_smoke
# erna: API op die database stoppen, dan
scripts/demo-db-snapshot.sh restore agf_smoke
```

Het script weigert databases waarvan de naam geen `smoke`, `demo` of `tour` bevat, en vraagt bij restore om de
databasenaam ter bevestiging. Restore zet de hele database terug: ook ander werk van na de momentopname verdwijnt.

## E2E-tests (`npm run test:tour`)

Playwright speelt elk hoofdstuk af door het echte paneel (Kijk mee en Doe zelf) en controleert dat alle stappen
voorbijkomen, dat geen controle faalt, dat de eigen gebruikersnaam terugkomt en wat er via de API in het systeem staat.
Altijd tegen een aparte testdatabase, nooit tegen `agf_smoke`.

1. Testdatabase maken, bijvoorbeeld als kopie van een dump van de demo:
   `pg_dump -Fc -d agf_smoke -f /tmp/demo.dump && createdb agf_tour_test && pg_restore -d agf_tour_test --no-owner /tmp/demo.dump`.
   Leg er meteen een momentopname van vast (`scripts/demo-db-snapshot.sh save agf_tour_test`), dan kun je na een run
   terug. De test maakt "Nature's Pride" (leverancier) en een klant op "Lo" via de gewone API aan als ze ontbreken;
   Avocado Hass met snelcode, eenheid en kg per verpakking moet al bestaan.
2. API op de testdatabase (poort 5198, CORS voor de testfrontend):
   `ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=agf_tour_test;Username=…" ASPNETCORE_URLS=http://localhost:5198 Cors__Origins__0=http://localhost:3001 dotnet run --project AgfAdmin --no-launch-profile`
3. Frontend met de rondleiding aan (poort 3001, naast de demo op 3000):
   `NEXT_PUBLIC_API_URL=http://localhost:5198/api NEXT_PUBLIC_DEMO_ASSISTANT=true NEXT_PUBLIC_DEMO_TOUR=true npx next dev -p 3001`
4. In `agf-frontend`: `TOUR_TEST_DATABASE=agf_tour_test npm run test:tour` (ongeveer 2 minuten).

Standaard gebruikt de test de geïnstalleerde Google Chrome. Zonder Chrome: `npx playwright install chromium` en
`TOUR_BROWSER_CHANNEL=chromium`. Andere adressen: `TOUR_BASE_URL` en `TOUR_API_URL`; meekijken: `TOUR_HEADED=true`.
De set zit (nog) niet in de standaard-CI: hij heeft Postgres, de API en een draaiende frontend nodig en duurt een paar
minuten.

## Een hoofdstuk schrijven

- Een hoofdstuk (`TourChapter`) heeft stappen (`TourStep`) met een route, `titleKey`, `whatKey` ("Nu doen we dit"),
  `whyKey` ("Waarom") en acties: `navigate`, `highlight`, `type`, `pick` (typen en de suggestie met Tab nemen), `select`,
  `click`, `wait`, `assert` en `remember` (een tekst onthouden, zoals het nieuwe ordernummer).
- Teksten mogen `{{tourRef}}`, `{{actor}}`, `{{today}}` en zelf onthouden variabelen bevatten.
- Een `assert` die niet klopt, stopt de rondleiding met "Deze stap kon niet uitgevoerd worden" in plaats van door te gaan.
- De voorcontrole (`precheck`) leest met gewone GET-aanroepen of alles klaarstaat en noemt anders wat er ontbreekt; zo
  is elk hoofdstuk los te starten.
- Teksten staan in `i18n/nl.json` en `i18n/en.json` (zelfde sleutels).
