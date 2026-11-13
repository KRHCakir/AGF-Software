# E-mailadressen per relatie en een mailconcept met PDF (documenten deel 3)

Zodra er een document is (laadbon, transportopdracht), staat er meteen een **mailconcept** klaar: de juiste ontvangers, een onderwerp en een tekst uit een vast sjabloon, en de PDF als bijlage. Je **controleert het en klikt op Versturen**. **Er gaat nooit iets weg zonder die klik.** Geen AI: de tekst komt uit sjablonen.

## Wat er in de praktijk verandert

1. **E-mailadressen bij een relatie.** In de relatiekaart staat het blok **E-mailadressen voor documenten**. Het is optioneel en achteraf aan te passen.
   - Per regel: een naam, het adres, **Aan** of **Cc**, en de documenten waarvoor het geldt: Orderbevestiging, Laadbon, Transportopdracht, Factuur, Overig.
   - Een adres kan voor de **hele relatie** gelden, of voor **één afleveradres** (bijvoorbeeld de pakbon voor één winkel). Heeft een afleveradres zelf niemand voor een document, dan gelden de adressen van de relatie.
   - Tab gaat door de velden; Tab op het laatste veld van de laatste regel maakt een nieuwe regel.
   - Heeft iemand anders de lijst intussen gewijzigd, dan blijft je eigen lijst staan en zie je de opgeslagen lijst ernaast. Je kiest dan zelf: je eigen lijst bewaren of de opgeslagen lijst nemen.
   - Een ongeldig adres wordt geweigerd. Een bekende tikfout (gmial.com) geeft een hint met **Overnemen**.
2. **De loods** heeft haar eigen adressen, onder **Instellingen › E-mailadressen van de loods**. De laadbon gaat daarheen.
3. **Bij elk nieuw document een concept**, op de order in het blok **Te versturen**, en in de lijst **Verkoop › Te versturen**:
   - **laadbon** → de adressen van de loods; **transportopdracht** → de adressen van de vervoerder;
   - onderwerp en tekst uit het sjabloon. Daaronder staat één afsluiting: de handtekening uit Beheer, of zonder handtekening "Met vriendelijke groet" en de bedrijfsnaam;
   - alles is aan te passen; **Voorbeeld PDF** toont de bijlage;
   - **geen adres:** "Geen e-mailadres voor [relatie]: voeg er een toe", met een knop naar de relatie of de loods. Versturen kan dan niet.
   - **gewijzigde order:** een nieuwe versie met een nieuw concept, en "Gewijzigde versie, vorige is al verstuurd op [datum]".
   - **eigen transport / klant haalt op:** geen vervoerder, dus geen concept voor de transportopdracht.
4. **Versturen.**
   - Alleen na de klik. Meer dan 10 ontvangers vraagt een tweede klik, en per gebruiker gaan er hoogstens 10 mails per minuut weg.
   - Daarna staat er "Verzonden op … naar … (door …)". Het document is verstuurd, en de order toont naar wie.
   - Mislukt het, dan blijft het concept staan met de reden, en kan het opnieuw.
   - **Verwijderen** haalt alleen het concept weg; het document blijft.
5. **Nog niet ingesteld?** Dan zegt het scherm dat, en biedt het **PDF downloaden** en **Tekst kopiëren** om het zelf te mailen.

## Instellingen (Beheer)

- **Mail versturen (afzender):**
  - SMTP-server, poort, versleuteling (STARTTLS, SSL/TLS of geen), gebruiker, wachtwoord, van-adres, van-naam en handtekening;
  - het **wachtwoord** wordt versleuteld opgeslagen (ASP.NET Data Protection). Het komt nooit terug op het scherm, nooit in een log en nooit in de repo.
- **Sjablonen** per document: onderwerp en tekst als platte tekst, met invulvelden `{bedrijf}`, `{document}`, `{nummer}`, `{versie}`, `{order}`, `{partij}`, `{klant}`, `{laaddatum}` en `{datum}`. Met **Standaardtekst** zet je het vaste sjabloon terug. Zet geen groet onder een sjabloon: de afsluiting komt er altijd onder.

## SMTP nu, Microsoft 365 later

Versturen loopt via één dunne laag (`IMailSender`):
- nu met **SMTP** (MailKit);
- een **Microsoft 365-afzender via Graph** komt in de mailbox-ronde als tweede klasse naast SMTP. Het concept, het versturen en het vastleggen veranderen daarvoor niet.

Voor tests en de rondleiding is er een **nagebootste afzender** (`Mail:Sender = Fake`). Die verstuurt nooit iets over het netwerk.

## Rechten en vastleggen

- **Rechten:**
  - documenten en versturen vallen onder **Verkoop**;
  - de e-mailadressen bij een relatie onder **Stamgegevens**;
  - de mailinstellingen, de sjablonen en de adressen van de loods onder **Beheer**.
- **Wie** verstuurde komt uit de ingelogde gebruiker (`CurrentUser`), nooit van de client.
- **TraceabilityGuard regel 32:**
  - een mailconcept verdwijnt nooit (verwijderen zet het op Verwijderd);
  - een **verstuurd** concept verandert niet meer: wanneer, door wie, naar wie en het bericht-id liggen vast.

## Data

Migratie `AddDocumentMail` (62), met vier nieuwe tabellen:
- `DocumentRecipients`;
- `MailDrafts`;
- `MailSettings`;
- `MailTemplates`.

Bestaande relaties hebben nog geen e-mailadressen. Bevestigde orders van vóór deze ronde krijgen hun concept zodra je de lijst Te versturen of de order opent, behalve als het document al doorgegeven is.

## Bewust niet gebouwd

- Automatisch versturen zonder klik, en AI-teksten.
- De orderbevestiging naar de klant. De opzet kan het aan: de soort bestaat al bij de ontvangers.
- Inkomende mail, Microsoft 365 via Graph, en een opmaakeditor voor sjablonen.
- Zelf bijlagen toevoegen.
- Regels per afdeling (het plan-doc noemt ze); nu kies je per adres.

## Schermafbeeldingen (`docs/mailconcept-2026-11/`)

Gemaakt in Chrome op een lege testdatabase, met verzonnen gegevens en `.example`-adressen, en **Mailpit** als proefmailserver (geen echte mail).

| Bestand | Wat |
|---|---|
| `01-e-mailadressen-bij-de-vervoerder-desktop.png` | De vervoerder: Planning (Aan) en Kantoor (Cc) voor de transportopdracht |
| `02-te-versturen-twee-concepten-desktop.png` | Na bevestigen: concepten voor de laadbon (naar de loods) en de transportopdracht (naar de vervoerder) |
| `03-lijst-te-versturen-desktop.png` | Verkoop › Te versturen |
| `04-voorbeeld-pdf-desktop.png` | Het concept met het voorbeeld van de PDF |
| `05-verstuurd-desktop.png` | Beide verstuurd: wanneer, naar wie en door wie |
| `06-mailpit-inbox-desktop.png`, `07-mailpit-transportopdracht-desktop.png` | De twee mails in Mailpit, met de PDF als bijlage |
| `08-te-versturen-op-de-telefoon-390.png` | Het blok op 390 px, zonder horizontale scroll |
