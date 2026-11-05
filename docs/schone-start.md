# Schone start: leeg beginnen met een nieuwe database

Je kunt verder met een **lege** omgeving: je voert zelf je relaties, artikelen en orders in, zoals dat straks in het echt gaat.
Daarvoor maak je een **nieuwe** database. De oude databases (`agf_smoke`, `agf_deviation_clean` en de andere) blijven
onveranderd staan als back-up. Er wordt niets verwijderd, geleegd of overschreven.

## Wat er wel en niet in de nieuwe database komt

| Blijft (referentiegegevens) | Waarom |
|---|---|
| Het schema en alles wat de migraties zelf vullen | Zonder schema werkt niets. |
| De landenlijst (249 landen) | Bij het artikel kies je de herkomst uit deze lijst. |
| De standaard betaalcondities (14 en 30 dagen) | De relatieschermen stellen ze voor. |
| Met `--settings-from`: je bedrijfsinstellingen, met de partijnummering | Zo blijven je nummerformaat en je keuzes voor de inkoopfactuur gelijk. |
| Met `--settings-from`: je eigen betaalcondities | Bijvoorbeeld "30 dagen na einde maand, 2% korting". |
| Met `--settings-from`: je eigen snelkoppelingen voor landen | Een eigen afkorting die in de landkiezer het land vindt. |
| De standaardgebruiker (Admin) | Die staat in de instellingen van de API (`Identity:DefaultUser`), niet in de database. Er verandert dus niets aan. |

Gaat niet mee:
- relaties (en daarmee ook de kredietinstellingen per klant, want die horen bij de relatie);
- artikelen en varianten;
- inkooporders, ontvangsten, pallets, keuringen en partijen;
- reserveringen, verkooporders en verzendingen;
- inkoopfacturen;
- de oude bestelflow;
- de historie van al die testgegevens.

Een betaalconditie of snelkoppeling met een testkenmerk in de tekst (`SMOKE-`, `DEMO-TOUR-`, `E2E-`) wordt overgeslagen.

Er zijn geen standaardbedrijven, magazijnen, kostensoorten of btw-tabellen in de database. Eenheden, verpakkingen en de klassen
Extra, I en II zitten in de code. Je hebt dus niets anders nodig.

## Stap 1: de nieuwe database maken

Kies een naam, bijvoorbeeld `agf_start`. Voer in de map van de repository uit:

```bash
scripts/new-clean-db.sh agf_start --settings-from agf_smoke
```

- Het script **weigert** een naam die al bestaat. Een bestaande database raakt het nooit aan.
- Het weigert ook als poort 5296 (of `CLEAN_DB_PORT`) al in gebruik is, en stopt als de nieuwe database na het opstarten niet alle migraties heeft.
- Het gebruikt `PGHOST`, `PGPORT`, `PGUSER` en `PGPASSWORD` (standaard localhost:5432 en je eigen gebruikersnaam) voor alle stappen, ook voor de tijdelijke API.
- Het maakt de database aan, laat de migraties lopen (de API start daarvoor kort op poort 5296) en neemt met `--settings-from`
  je instellingen over. Uit `agf_smoke` wordt daarbij alleen **gelezen**.
  Heeft het bedrijf de standaardcondities 14D of 30D aangepast, dan gelden in de nieuwe database die aangepaste waarden.
- Daarna controleert het dat alleen de referentietabellen gevuld zijn, en toont het per tabel het aantal rijen.
- Zonder `--settings-from` krijg je de standaardinstellingen. Die stel je later zelf in onder Instellingen.

## Stap 2: de API en de frontend op de nieuwe database zetten

De demo draait met de API op poort 5197 en de frontend op poort 3000. De frontend vindt de API via
`NEXT_PUBLIC_API_URL=http://localhost:5197/api` in `agf-frontend/.env.local`. Laat je de API op dezelfde poort draaien, dan hoeft
aan de frontend niets te veranderen.

1. Stop de API die nu draait (Ctrl+C in dat terminalvenster).
2. Start de API op de nieuwe database:

   ```bash
   ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=agf_start;Username=$(whoami)" \
     ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5197 \
     dotnet run --project AgfAdmin --no-launch-profile
   ```

3. Herlaad http://localhost:3000 met Cmd+Shift+R. Op de startpagina staat nu de kaart **Beginnen**: eerst relaties, dan
   artikelen, dan de eerste inkooporder. De kaart verdwijnt vanzelf zodra er relaties en artikelen zijn.

Wil je de oude en de nieuwe omgeving naast elkaar? Start dan de API op een andere poort, bijvoorbeeld 5195, met
`Cors__Origins__0=http://localhost:3004` erbij. Start daarnaast een tweede frontend:
`NEXT_PUBLIC_API_URL=http://localhost:5195/api npx next dev -p 3004` in `agf-frontend`.

## Terug naar de oude database

Stop de API en start hem opnieuw met `Database=agf_smoke` (of een andere oude database) in de verbindingsreeks. Er is niets
weg: de oude database staat er precies zoals je hem liet. Je kunt zo vaak wisselen als je wilt.

## Zelf bewaren

De oude databases blijven staan zolang jij ze niet weggooit. Een extra kopie maak je met:

```bash
pg_dump -Fc -d agf_smoke -f ~/agf_smoke_$(date +%Y%m%d).dump
```

Terugzetten in een nieuwe database: `createdb agf_smoke_terug && pg_restore -d agf_smoke_terug --no-owner ~/agf_smoke_<datum>.dump`.

## Testgegevens blijven beschikbaar, maar alleen als je ze vraagt

- **Opstarten:** de API maakt bij het opstarten nooit testgegevens aan. De oude demo-data komt er alleen met de omgeving
  Development én `Development__SeedDemoData=true` (zie `docs/development-setup.md`). Het script zet die instelling expliciet uit.
- **Ontvangst- en afwijkingstests:** `scripts/receipt-test-data.sh <naam-met-test-of-clean>` bouwt een aparte testdatabase met een
  kleine vaste set: twee leveranciers, een klant, tien artikelen en twee orders.
- **Rondleiding:** de Demo-assistent maakt in elk hoofdstuk zijn eigen voorbeeldgegevens, herkenbaar aan `DEMO-TOUR-` in de
  naam of referentie. Ze komen er dus alleen als je de rondleiding afspeelt. Doe dat bij voorkeur niet op de database waar je echte
  gegevens in zet. Zie `docs/demo-tour.md`.
