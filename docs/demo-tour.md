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

## Een hoofdstuk schrijven

- Een hoofdstuk (`TourChapter`) heeft stappen (`TourStep`) met een route, `titleKey`, `whatKey` ("Nu doen we dit"),
  `whyKey` ("Waarom") en acties: `navigate`, `highlight`, `type`, `pick` (typen en de suggestie met Tab nemen), `select`,
  `click`, `wait`, `assert` en `remember` (een tekst onthouden, zoals het nieuwe ordernummer).
- Teksten mogen `{{tourRef}}`, `{{actor}}`, `{{today}}` en zelf onthouden variabelen bevatten.
- Een `assert` die niet klopt, stopt de rondleiding met "Deze stap kon niet uitgevoerd worden" in plaats van door te gaan.
- De voorcontrole (`precheck`) leest met gewone GET-aanroepen of alles klaarstaat en noemt anders wat er ontbreekt; zo
  is elk hoofdstuk los te starten.
- Teksten staan in `i18n/nl.json` en `i18n/en.json` (zelfde sleutels).
