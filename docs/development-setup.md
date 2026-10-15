# Lokale ontwikkelomgeving

Deze handleiding beschrijft hoe je AGF Software lokaal start zonder dat er wachtwoorden of andere secrets in Git terechtkomen.

## Uitgangspunten

- Getrackte bestanden bevatten geen credentials. `AgfAdmin/appsettings.json` bevat geen connection string.
- De API weigert te starten zonder `ConnectionStrings:DefaultConnection` en meldt dan: `Connection string 'DefaultConnection' is required.`
- Lokale waarden staan in bestanden die Git negeert: `.env` en `AgfAdmin/appsettings.Development.json`. Je kunt ze ook als omgevingsvariabelen zetten.
- Productie levert de connection string via de omgeving (`ConnectionStrings__DefaultConnection`). Productiewaarden horen nooit in deze repository.

## Eerste installatie

1. **Docker-configuratie**

   ```sh
   cp .env.example .env
   ```

   Vervang `<lokaal-wachtwoord>` in `.env` door een eigen lokaal wachtwoord.

2. **API-configuratie.** Kies één van beide manieren.

   a. Een lokaal bestand dat Git negeert:

   ```sh
   cp AgfAdmin/appsettings.Development.json.example AgfAdmin/appsettings.Development.json
   ```

   Vul hetzelfde wachtwoord in als in `.env`.

   b. Of een omgevingsvariabele:

   ```sh
   export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=agfadmin;Username=postgres;Password=<lokaal-wachtwoord>"
   ```

3. **PostgreSQL starten**

   ```sh
   docker compose up -d
   ```

   Zonder `.env` stopt Docker Compose met een melding als `POSTGRES_PASSWORD is required; copy .env.example to .env`.

4. **Backend starten**

   ```sh
   dotnet run --project AgfAdmin
   ```

   De launch-profielen zetten `ASPNETCORE_ENVIRONMENT=Development`, zodat `appsettings.Development.json` wordt geladen. Bij het opstarten worden alleen EF Core-migraties toegepast. Er wordt nooit data verwijderd.

5. **Frontend starten**

   ```sh
   cd agf-frontend
   npm ci
   npm run dev
   ```

## Demo-data (optioneel)

Standaard wordt er geen demo-data aangemaakt. Demo-data komt er alleen als aan beide voorwaarden is voldaan:

- de omgeving is Development;
- `Development:SeedDemoData` staat expliciet op `true`.

Ook dan worden alleen lege tabellen gevuld. Bestaande data wordt nooit verwijderd of overschreven.

```sh
Development__SeedDemoData=true dotnet run --project AgfAdmin
```

Je kunt de waarde ook op `true` zetten in je lokale `appsettings.Development.json`.

## EF Core CLI (`dotnet ef`)

`dotnet ef` gebruikt de launch-profielen niet. Het draait standaard als Production en laadt dan `appsettings.Development.json` niet. Zonder connection string stopt het met de melding hierboven. Kies één van de volgende oplossingen:

```sh
# Development laden, zodat appsettings.Development.json wordt gelezen
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations has-pending-model-changes --project AgfAdmin/AgfAdmin.csproj

# Of de connection string direct meegeven
ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=agfadmin;Username=postgres;Password=<lokaal-wachtwoord>" \
  dotnet ef migrations has-pending-model-changes --project AgfAdmin/AgfAdmin.csproj
```

## Overstappen vanaf een eerdere versie

Eerdere versies hadden een database-wachtwoord in `AgfAdmin/appsettings.json` en `docker-compose.yml`. Daarnaast werd `AgfAdmin/appsettings.Development.json` door Git bijgehouden. Doe het volgende als je een bestaande checkout hebt:

1. **Vóór `git pull`:** heb je `AgfAdmin/appsettings.Development.json` lokaal aangepast? Maak dan eerst een kopie buiten de repository. Git verwijdert dit bestand bij de pull, omdat het nu genegeerd wordt. Met lokale wijzigingen weigert de pull.

   ```sh
   cp AgfAdmin/appsettings.Development.json ~/agf-appsettings.Development.json.bak
   ```

2. Na de pull volg je stap 1 en 2 van [Eerste installatie](#eerste-installatie). Zet eventuele eigen instellingen uit de kopie terug in het nieuwe, genegeerde bestand.

3. **Bestaand Docker-volume.** PostgreSQL gebruikt `POSTGRES_USER`, `POSTGRES_PASSWORD` en `POSTGRES_DB` alleen bij de **eerste** initialisatie van het volume `postgres_data`. Als je volume al bestaat, kun je kiezen uit drie mogelijkheden:
   - Zet in `.env` en in de connection string tijdelijk het wachtwoord waarmee het volume ooit is aangemaakt, en roteer het daarna, of
   - roteer het wachtwoord in de draaiende database en gebruik daarna overal het nieuwe wachtwoord:

     ```sh
     docker compose exec postgres psql -U postgres -c "ALTER USER postgres WITH PASSWORD '<nieuw-lokaal-wachtwoord>';"
     ```

   - Of verwijder het volume met `docker compose down -v`. **Daarmee is alle lokale data definitief weg.** Doe dit alleen als je die data niet nodig hebt.

## Wachtwoordrotatie

Het eerder getrackte database-wachtwoord staat nog in de Git-historie. Het verwijderen uit de huidige bestanden haalt het daar niet uit. Behandel dat wachtwoord als gelekt:

- gebruik het nergens anders dan in je eigen lokale Docker-database;
- vervang het overal waar het buiten de lokale ontwikkelomgeving is gebruikt, voordat er een niet-lokale deployment plaatsvindt;
- kies voor elke klantomgeving een eigen, sterk wachtwoord dat via de omgeving wordt aangeleverd.
