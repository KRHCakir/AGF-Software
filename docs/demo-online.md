# De demo online zetten

Een vaste demo-omgeving op een kleine server, bereikbaar op `https://demo.<jouw-domein>`. De klant kan er thuis en wanneer hij wil mee
oefenen; je MacBook mag uit. Jij bepaalt wanneer er een nieuwe versie op komt.

Zo zit het in elkaar:

```
klant (browser)
   │  https://demo.<jouw-domein>
   ▼
Cloudflare  ── Access: e-mailadres invullen, code per mail, dan pas binnen
   │  (Cloudflare Tunnel: de server belt zelf naar Cloudflare; er staan geen poorten open)
   ▼
server bij Hetzner (Docker Compose)
   cloudflared ─► web (website, /api) ─► api ─► db (PostgreSQL, alleen intern)
                                              backup (dagelijks, 7 dagen)
```

De app heeft nog geen eigen login: iedereen die door Cloudflare Access komt, werkt als de standaardgebruiker (Admin). Access is daarom
de toegangspoort; een login met gebruikers is een aparte ronde.

## 1. Wat je nodig hebt en wat het kost

| Wat | Waarvoor | Ongeveer |
|---|---|---|
| Server bij Hetzner Cloud, 2 vCPU en **minstens 4 GB** geheugen | de demo zelf | € 4 tot € 8 per maand |
| Een domeinnaam (bijvoorbeeld `jouwbedrijf.nl`) | het vaste adres | € 10 tot € 15 per jaar |
| Cloudflare, gratis plan (met Zero Trust, gratis tot 50 gebruikers) | tunnel en toegangspoort | € 0 |
| Optioneel: een **aparte** API-sleutel bij Anthropic, met een eigen bestedingslimiet | de AI-factuurlezer in de demo | een paar cent per factuur; de demo stopt bij € 5 per maand |

Prijzen veranderen: controleer ze vooraf op de sites zelf. Reken de eerste keer op ongeveer anderhalf uur.

## 2. De server aanmaken (Hetzner)

1. Maak een account op hetzner.com en open de Cloud Console. Maak een project, bijvoorbeeld `agf-demo`.
2. Maak op je Mac een SSH-sleutel (als je er nog geen hebt): `ssh-keygen -t ed25519 -C "agf-demo"`. Je publieke sleutel staat in
   `~/.ssh/id_ed25519.pub`.
3. Maak eerst een **firewall** (Firewalls › Create Firewall), naam `agf-demo-fw`:
   - Inkomend: alleen **TCP 22 (SSH) vanaf je eigen IP-adres** (zoek je IP op via "wat is mijn ip"). Verwijder alle andere inkomende
     regels, ook ICMP.
   - Uitgaand: laat alles toe (nodig voor updates, Docker en de tunnel naar Cloudflare).
   - Verandert je IP thuis, pas dan de regel aan; via de console in het dashboard kun je er altijd nog in.
4. Maak de server (Servers › Add Server):
   - Locatie: Falkenstein of Nürnberg (Duitsland) of Helsinki (Finland).
   - Image: **Ubuntu 24.04**.
   - Type: gedeelde vCPU met **minstens 4 GB** geheugen (x86 of Arm werkt allebei).
   - SSH-sleutel: plak de inhoud van `~/.ssh/id_ed25519.pub`. Geen wachtwoord.
   - Firewall: `agf-demo-fw`.
5. Log in en werk de server bij:

```bash
ssh root@<ip-van-de-server>
apt update && apt upgrade -y
# 2 GB swap: het bouwen van de images vraagt tijdelijk meer geheugen dan 4 GB
fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
echo '/swapfile none swap sw 0 0' >> /etc/fstab
```

## 3. Docker installeren en de code ophalen

```bash
# Docker (officiële pakketbron van Docker)
apt install -y ca-certificates curl git
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" > /etc/apt/sources.list.d/docker.list
apt update && apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
docker compose version
```

De code haal je op met een **alleen-lezen deploy-sleutel** (de server kan dan niets naar GitHub schrijven):

```bash
ssh-keygen -t ed25519 -f ~/.ssh/agf_deploy -N "" -C "agf-demo-server"
cat ~/.ssh/agf_deploy.pub
```

Zet die sleutel in GitHub: repository › Settings › Deploy keys › Add deploy key, titel `agf-demo-server`, **"Allow write access" uit**.
Daarna:

```bash
printf 'Host github.com\n  IdentityFile ~/.ssh/agf_deploy\n  IdentitiesOnly yes\n' >> ~/.ssh/config
git clone git@github.com:KRHCakir/AGF-Software.git /opt/agf-demo
cd /opt/agf-demo
cp deploy/.env.demo.example deploy/.env && chmod 600 deploy/.env
openssl rand -hex 24      # dit wordt het databasewachtwoord
nano deploy/.env          # vul POSTGRES_PASSWORD in; de rest volgt in stap 4
```

`deploy/.env` staat niet in de repo en komt niet in een image. Zet er nooit echte klantgegevens of de sleutels van het echte systeem in.

Handig voor de rest van deze handleiding (eenmalig):

```bash
echo "alias demo='docker compose -f /opt/agf-demo/deploy/docker-compose.demo.yml --env-file /opt/agf-demo/deploy/.env'" >> ~/.bashrc && source ~/.bashrc
```

## 4. Cloudflare: domein, tunnel en toegangspoort

De menunamen in het Cloudflare-dashboard veranderen af en toe; zoek bij twijfel op de vetgedrukte woorden.

1. **Domein toevoegen:** maak een account op cloudflare.com, kies **Add a domain**, vul je domein in en kies het **Free**-plan.
   Zet bij je domeinregistrar de twee nameservers die Cloudflare noemt. Wacht tot het domein op "Active" staat (meestal binnen een uur).
2. **Zero Trust:** open **Zero Trust** in het dashboard, kies een teamnaam (bijvoorbeeld `jouwbedrijf`) en het gratis plan. Je
   team-domein is dan `jouwbedrijf.cloudflareaccess.com`.
3. **Tunnel maken:** Zero Trust › **Networks › Tunnels › Create a tunnel** › type **Cloudflared**, naam `agf-demo`. Kies bij de
   installatie **Docker** en kopieer alleen het lange token (de tekst na `--token`). Zet het in `deploy/.env` bij `CLOUDFLARE_TUNNEL_TOKEN`.
4. **Publieke hostnaam** (in dezelfde tunnel, tab **Public Hostname**): subdomein `demo`, je domein, service-type **HTTP**, URL
   **`web:3000`**. Dat is de website-container binnen Docker; de server hoeft daarvoor geen poort open te zetten.
5. **Toegangspoort (Access):** Zero Trust › **Access › Applications › Add an application** › **Self-hosted**:
   - Naam `AGF demo`, domein `demo.<jouw-domein>`.
   - **Session duration:** bijvoorbeeld 24 uur (daarna vraagt Cloudflare opnieuw een code).
   - Policy: naam `Toegestane mensen`, Action **Allow**, Include **Emails**: de e-mailadressen van de klant en van jezelf. Alleen die
     adressen krijgen een code.
   - Login method: **One-time PIN** (code per mail).
   - Bewaar de toepassing en kopieer daarna de **Application Audience (AUD) Tag** (bij de toepassing, tab Overview of Basic information).
     Zet hem in `deploy/.env` bij `CF_ACCESS_AUD`, en je team-domein bij `CF_ACCESS_TEAM_DOMAIN`. Dan controleert de API zelf ook nog
     het jeton van Cloudflare (een extra laag; zonder deze twee werkt de toegangspoort van Cloudflare ook, maar dan zonder die controle).
6. Optioneel, voor een externe bewaking (bijvoorbeeld een gratis uptime-dienst): maak een tweede Access-toepassing voor
   `demo.<jouw-domein>/health` met een policy **Bypass** voor iedereen. `/health` laat alleen zien dat de website draait.

## 5. Starten, controleren en het adres delen

```bash
cd /opt/agf-demo
scripts/demo-reset.sh          # de eerste keer: bouwt de images (10 tot 20 minuten), maakt agf_demo_<datum> en vult hem
demo ps                        # db, api, web, cloudflared en backup: "Up", de eerste drie "(healthy)"
```

Wil je de AI-factuurlezer in de demo, zet dan eerst een aparte demo-sleutel in `AGF_DEMO_ANTHROPIC_API_KEY` en gebruik
`scripts/demo-reset.sh --ai-aan`. Doe dat alleen als de klant weet dat geüploade documenten naar een externe AI-dienst gaan.

Controleer in een privévenster van je browser: `https://demo.<jouw-domein>` → de inlogpagina van Cloudflare → e-mailadres → code →
de app met de **gele demo-balk** bovenaan ("Demo-omgeving: gebruik alleen verzonnen gegevens. Gegevens kunnen worden teruggezet.").

Een voorbeeld voor WhatsApp:

> Hoi! Je kunt de AGF-software nu zelf proberen op https://demo.jouwbedrijf.nl . Vul je e-mailadres in (klant@voorbeeld.nl); je krijgt
> dan een code per mail. Gebruik alleen verzonnen gegevens; ik kan de demo altijd terugzetten. Rechtsonder zit de knop Demo-assistent
> met een rondleiding.

## 6. Een nieuwe versie zetten, terugdraaien en resetten

**Nieuwe versie** (bijvoorbeeld na een merge op `main`):

```bash
cd /opt/agf-demo
git tag -f demo-vorige                  # onthoud de versie die nu draait
git fetch origin && git checkout --detach origin/main
demo up -d --build                      # bouwt opnieuw en herstart; de API migreert de demo-database als dat nodig is
demo ps
```

**Terugdraaien** naar de vorige versie:

```bash
git checkout --detach demo-vorige && demo up -d --build
```

Bracht de nieuwe versie een migratie mee, dan start de oude versie mogelijk niet op de al gemigreerde database. Doe na het terugdraaien
dan een reset (hieronder): een nieuwe database met de oude versie. De gegevens zijn toch verzonnen.

**Resetten** (de klant wil opnieuw beginnen, of de gegevens zijn rommelig):

```bash
scripts/demo-reset.sh                   # nieuwe database agf_demo_<datum>, gevuld met de DEMO-gegevens
scripts/demo-reset.sh agf_demo_20261120_2   # tweede reset op dezelfde dag: kies zelf een nieuwe naam
```

Er wordt niets gewist: het script maakt een **nieuwe** database, vult die (`scripts/demo-seed.sh`), zet `AGF_DEMO_DATABASE` in
`deploy/.env` om (de vorige versie blijft als `deploy/.env.vorige`) en herstart de containers. De oude database blijft staan. Het script
weigert een naam die niet met `agf_demo` begint of die al bestaat. Terug naar de vorige: zet de oude naam terug in `deploy/.env` en doe
`demo up -d`.

De demo-gegevens: DEMO Groothandel B.V. met twee leveranciers, twee klanten, een vervoerder en de loods (met e-mailadressen op
`.example`, die niet bestaan), drie artikelen met snelcodes (DEMO-AVO18, DEMO-MANGO8, DEMO-CIT4), drie inkooporders waarvan één
ontvangen en gekeurd, een verkooporder met reservering en vervoerder (klaar om te **Bevestigen**: dan komen de laadbon, de
transportopdracht en de mailconcepten) en een inkoopfactuur.

## 7. Beheer

**Logs bekijken**

```bash
demo logs --tail 100 api            # of web, db, cloudflared, backup
demo logs -f api                    # meelezen; stoppen met Ctrl+C
```

**Back-up terugzetten.** Elke nacht om 03:00 (`AGF_DEMO_BACKUP_TIME`) maakt de container `backup` een kopie van de huidige
demo-database in `deploy/backups/` (7 dagen bewaard, `AGF_DEMO_BACKUP_DAYS`). Terugzetten gaat altijd naar een **nieuwe** database:

```bash
ls deploy/backups/
demo exec -T db createdb -U agf -T template0 agf_demo_terug_20261120
demo exec -T db pg_restore -U agf -d agf_demo_terug_20261120 --no-owner < deploy/backups/agf_demo_20261115_0900.dump
nano deploy/.env                    # AGF_DEMO_DATABASE=agf_demo_terug_20261120
demo up -d
```

Dit is geen productieback-up (niet versleuteld, niet buiten de server); alleen voor het geval de klant iets belangrijks invoert.

**Oude demo-databases opruimen.** Na een reset noemt het script de demo-databases die ouder zijn dan `AGF_DEMO_KEEP_DAYS` dagen. Opruimen
doe je zelf, en alleen een database die je zeker niet meer nodig hebt en die niet in `AGF_DEMO_DATABASE` staat:

```bash
grep AGF_DEMO_DATABASE deploy/.env                                   # deze NIET opruimen
demo exec -T db psql -U agf -d postgres -c '\l agf_demo*'
demo exec -T db dropdb -U agf agf_demo_20261101                     # één oude, met de hand
```

**Sleutels en wachtwoorden vervangen**

- Tunnel-token: Cloudflare › de tunnel › token vernieuwen, in `deploy/.env` zetten en `demo up -d cloudflared`.
- AI-sleutel: nieuwe demo-sleutel bij Anthropic, de oude intrekken, in `deploy/.env` zetten en `demo up -d api`.
- Databasewachtwoord: eerst in de database (`demo exec db psql -U agf -d postgres -c "ALTER USER agf PASSWORD 'nieuw-wachtwoord'"`),
  dan hetzelfde in `deploy/.env`, dan `demo up -d`. Alleen `deploy/.env` aanpassen is niet genoeg.
- Wie er in mag: pas de lijst met e-mailadressen aan in de Access-policy. Iemand er meteen uit: Zero Trust › Users › de gebruiker ›
  Revoke sessions.

**De server uitzetten**

- Tijdelijk stoppen: `demo down` (de gegevens blijven in de Docker-volumes staan; `demo up -d` start alles weer).
- Een uitgezette server kost bij Hetzner nog steeds geld. Wil je niets meer betalen: kopieer eerst de back-ups
  (`scp root@<ip>:/opt/agf-demo/deploy/backups/*.dump .`) en verwijder dan de server in de Cloud Console.
- Onderhoud: werk de server eens per maand bij met `apt update && apt upgrade -y` (en `reboot` als daarom gevraagd wordt; de containers
  starten daarna vanzelf weer).

## 8. Wat de klant wel en niet moet doen

- Alleen **verzonnen gegevens** gebruiken: iedereen die toegang heeft, ziet dezelfde gegevens.
- Gegevens kunnen worden **teruggezet**; zet er niets in wat bewaard moet blijven.
- Mail gaat **nooit echt weg**: versturen werkt in het scherm en toont "Verzonden (demo, niets echt verstuurd)".
- **AI-factuurlezer** (als die aan staat): alleen verzonnen of geanonimiseerde facturen uploaden. Er is een limiet per dag (alle
  bezoekers samen) en per maand; daarna staat er "Voor de demo is de limiet voor vandaag bereikt" en werkt handmatig invoeren gewoon.
- Er is nog geen login per persoon: alles wordt vastgelegd als de standaardgebruiker.
- De rondleiding (knop **Demo-assistent** rechtsonder) mag gewoon gebruikt worden; ze maakt eigen gegevens met DEMO-TOUR- ervoor. Het
  hoofdstuk "Factuur uploaden" werkt in de demo niet: dat gebruikt de nagebootste AI-lezer, en die staat in de demo bewust uit.

## Eerst lokaal proberen (optioneel)

Op je Mac met Docker Desktop, zonder Cloudflare. De website staat dan alleen op je eigen computer.

```bash
cp deploy/.env.demo.example deploy/.env.local-test && chmod 600 deploy/.env.local-test
# vul POSTGRES_PASSWORD in, en bij CLOUDFLARE_TUNNEL_TOKEN iets willekeurigs (de tunnel start lokaal niet)
AGF_DEMO_ENV_FILE=deploy/.env.local-test AGF_DEMO_COMPOSE_EXTRA=deploy/docker-compose.local.yml scripts/demo-reset.sh
open http://localhost:3080
```

## Wat de demo-modus afdwingt

`AGF_DEMO_ENVIRONMENT=true` (API) en `NEXT_PUBLIC_DEMO_ENVIRONMENT=true` (website, bij de build) staan vast in de Compose-opzet:

- De API start **alleen op een database waarvan de naam met `agf_demo` begint**, en weigert anders met een melding. Zo draait de demo nooit
  op `agf_smoke` of een echte database.
- Mail: alleen de **nagebootste afzender**; de SMTP-afzender wordt niet gebouwd, ook niet als er SMTP-instellingen staan.
- AI-lezen: alleen met de aparte demo-sleutel, met een **daglimiet** (`AGF_DEMO_AI_DAILY_LIMIT`, standaard 20) en een **maandplafond**
  (`AGF_DEMO_AI_MONTHLY_LIMIT_EUR`, standaard € 5) bovenop de instelling onder Beheer. Geen nepgegevens: zonder sleutel staat er dat het
  niet beschikbaar is.
- De demo-balk op elk scherm; zoekmachines worden geweerd (`noindex`, `X-Robots-Tag`, `robots.txt`).
- Met `CF_ACCESS_AUD` en `CF_ACCESS_TEAM_DOMAIN` neemt de API alleen verzoeken aan met een geldig Access-jeton. De demo-balk toont het
  e-mailadres waarmee de klant binnenkwam.
- De browser praat alleen met de website (`/api` wordt intern doorgestuurd naar de API); CORS staat geen andere herkomst toe.

Bewust niet gebouwd: een echte login met gebruikers, meerdere omgevingen of klanten, een productie-opzet, versleutelde back-ups naar
buiten de server, en echte mailverzending vanuit de demo.
