# Certificaten (deel 1)

Certificaten bij relaties: GGN (GLOBALG.A.P.), CoC, SKAL en de andere gangbare certificaten in de agf, met de producten waarvoor ze gelden, PDF's en foto's, en een waarschuwing vóór de einddatum. Deel 2 (certificaten op documenten en in de keten) volgt apart.

## Waar staat wat

| Plek | Wat |
|---|---|
| Relatiekaart › **Certificaten** | Per certificaat: soort, nummer, instantie, geldig van/tot met status, producten, gecontroleerd op (door wie: de ingelogde gebruiker), PDF/foto's als versies, actief/gearchiveerd. Opgeslagen met dezelfde klik als de relatie. |
| Instellingen › **Certificaten** | Dagen vooraf waarschuwen (standaard 30), eigen soorten, en de certificaten van het **eigen bedrijf**. |
| Startpagina | "Certificaten die bijna verlopen" (alleen als er iets is): verlopen en binnen de ingestelde dagen. |
| Stamgegevens › **Certificaten** | Dezelfde lijst, plus alle certificaten met filter op soort en status en zoeken op nummer of relatie. |
| Relaties | Kolom Certificaten (soorten met status), een badge bij bijna verlopen of verlopen, een filter, en zoeken vindt ook een certificaatnummer. |
| Inkooporder en ontvangst | "Klopt dit?" als het product niet in het GGN-certificaat van de leverancier staat, of als dat verlopen is of vóór de levering verloopt. |

## Regels

- **GGN**: 13 cijfers (spaties mogen). Het controlecijfer (zoals een GS1-GLN) wordt gecontroleerd; klopt het niet, dan is dat een waarschuwing, geen blokkade. Andere nummers (CoC, SKAL, …) zijn vrij, ook lang.
- **Producten**: op productniveau (avocado), niet per variant. Bij GGN verplicht; leeg = de waarschuwing "Vul in voor welke producten". Overgenomen oude GGN's staan op "producten nog invullen".
- **Status**: geldig tot en met de einddatum. *Verloopt binnenkort* bij hooguit het ingestelde aantal dagen (30 dagen = nog binnen de grens), *Verlopen* daarna. Zonder einddatum: geen status.
- **Meerdere** certificaten van dezelfde soort mogen (twee GGN's voor verschillende producten), maar niet twee keer hetzelfde nummer van dezelfde soort.
- **Nooit verwijderd**: een opgeslagen certificaat wordt gearchiveerd. Een PDF vervangen = een nieuwe versie toevoegen; de vorige blijft (guard regel 33).
- **Inkoopcontrole**: alleen als de leverancier een actief GGN-certificaat **met producten** heeft. Zonder GGN, of met alleen GGN's zonder producten: geen vraag. Ja legt per product vast wie bevestigde, wanneer en wat er stond (`CertificateConfirmations`, append-only). Bij aanpassen van een order alleen voor producten die nieuw op de order komen.
- **Ontvangst**: het GGN-voorstel per partij komt nu uit de actieve GGN-certificaten van de leverancier.

## Migratie 66 (`AddRelationCertificates`)

Nieuwe tabellen `RelationCertificates`, `RelationCertificateProducts`, `RelationCertificateFiles` en `CertificateConfirmations`, en twee bedrijfsinstellingen. Elk bestaand leveranciers-GGN (`VendorGgns`) wordt een certificaat GLOBALG.A.P. IFA (GGN) met nummer, producent, actief, volgorde, gecontroleerd op/door, geldig tot en GGN-label, gemarkeerd als "producten nog invullen". De tabel `VendorGgns` blijft staan.

## Niet in deel 1

Certificaten op documenten (laadbon, factuur, transportopdracht), de chain of custody over meerdere schakels, koppeling aan partijen, automatische controle bij certificeerders, e-mailmeldingen en AI-uitlezing van de PDF. Het GGN op de documenten en de AI-uitlezing kwamen in deel 2 (hieronder).

# Certificaten (deel 2)

Twee dingen: het **GGN van het product** per inkoopregel, tot op de laadbon en de transportopdracht; en een certificaat-PDF **door AI laten voorinvullen**.

## Het GGN van het product

Op de factuur van een leverancier staat het GGN van de teler van het product, bijna nooit dat van het bedrijf zelf. Daarom heeft elke inkoopregel een veld **GGN** (na de prijs; Tab: Product, Aantal, Prijs, GGN).

- **Voorstel**: heeft de leverancier een geldig GGN-certificaat (actief, op de verwachte leverdatum niet verlopen) waarin het product staat, dan stelt het systeem dat nummer voor. Eén treffer wordt voorgevuld, meer treffers geven een keuzelijst, geen treffer laat het veld leeg (`GET api/purchase-orders/ggn-proposals`). Zodra je het veld zelf aanraakt, volgt het het voorstel niet meer; een opgeslagen regel houdt haar GGN.
- **Altijd te overschrijven of leeg te maken**: een handelaar factureert het GGN van zijn teler, en dat staat niet in zijn eigen certificaat. Het scherm zegt dan "Niet in het GGN-certificaat van de leverancier".
- **13 cijfers** (spaties mogen); anders een melding bij het veld. Een afwijkend controlecijfer is een waarschuwing, geen blokkade.
- **Wijzigen**: bij een Open order met Aanpassen (auditregel `ggn`, ook na ontvangst), bij een definitieve order alleen met een correctie met reden. Guard regel 34 bewaakt dat.
- **Ontvangst**: het GGN van de inkoopregel staat vooraf ingevuld (anders, zoals eerst, de enige actieve GGN van de leverancier) en gaat mee naar de pallet of partij (`PalletContentLine.Ggn`).

## Op de documenten

| Document | Waar |
|---|---|
| Laadbon | per pallet of partij, onder de omschrijving ("GGN 4049928415684") |
| Transportopdracht | per orderregel; komt een regel uit meer partijen met verschillende GGN's, dan alle nummers achter elkaar (met komma's) |
| Verkoopfactuur | bestaat nog niet op main (alleen een plek in de lijst); komt er bij als die er is |

- Zonder GGN staat er niets (geen streepje, geen "n.v.t.").
- **Aan/uit per documentsoort** onder Instellingen › Laadbon en Instellingen › Transportopdracht ("GGN van het product per regel tonen"), standaard aan.
- In de **koptekst** het eigen CoC-nummer: het eerste actieve, niet verlopen certificaat van de soort *GLOBALG.A.P. Chain of Custody (CoC)* bij de eigen certificaten (Instellingen › Certificaten), in de volgorde van die lijst.
- Het GGN en de CoC staan in de **snapshot** van de versie: verandert er later iets, dan blijft een bestaande versie zoals ze was en komt er een nieuwe versie (zelfde regels als altijd). Lege velden tellen niet mee in de vingerafdruk.
- Nog steeds **geen prijzen** op de laadbon en de transportopdracht.

## Certificaat laten lezen (AI)

In het blok Certificaten (bij een opgeslagen relatie of bij het eigen bedrijf, zonder openstaande wijzigingen in het blok): **PDF of foto laten lezen (AI)**. Handmatig invullen blijft precies zoals het was.

- Het tweedelige scherm van stap 7a: het document links, het voorstel rechts (op een telefoon boven elkaar, het document in te klappen).
- Het voorstel: soort (onbekend: een eigen soort met de naam uit het document), nummer (GGN met de controle), houder met land, certificeringsinstantie, geldig van en tot met de status, de **producten van alle pagina's, ook uit een bijlage (Annex)** met de letterlijke tekst en de pagina, en de productielocaties (naam en land).
- Per product een voorgestelde koppeling met een eigen product (dezelfde naam, een vertaling zoals Lettuce = Sla, of de naam als woord in de tekst). Niet te koppelen: "niet gekoppeld"; zelf koppelen via het zoekveld of open laten. Er wordt **nooit** een product aangemaakt.
- **Geel** bij twijfel van de AI, een GGN dat niet klopt of een onleesbare datum; "niet gevonden" waar iets ontbreekt. Staat het certificaat duidelijk op naam van een ander bedrijf dan de relatie: "Dit certificaat staat op naam van X".
- **Goedkeuren** slaat op via het gewone opslaan van de certificaten (`PUT api/certificates/...` met `aiApproval`, geen tweede route). Het origineel wordt de volgende versie van het bestand (met hash); bij de versie staat "AI-voorstel goedgekeurd door [gebruiker] (model)". In `AiExtractions` staan model, versie, tokens, kosten in euro, de goedgekeurde waarden en wat de gebruiker aanpaste.
- Staat er al een certificaat met **hetzelfde nummer** bij deze relatie, dan is het voorstel een nieuwe versie daarvan (de keuze "als tweede certificaat" kan ook).
- **Handmatig verder**: het voorstel sluit en de waarden komen als gewone regel in het blok (opslaan zoals altijd). **Afwijzen**: er wordt niets opgeslagen.
- Dezelfde instelling als bij facturen (Beheer › AI-lezen: aan/uit, maandlimiet). Staat AI uit, dan zegt de knop dat; handmatig werkt altijd.
- Tests en de rondleiding gebruiken de nagebootste lezer (`DocumentAi:Reader = Demo`): altijd het voorbeeld van de eigenaar, Lettuce op pagina 1 en Arugula en Mizuna in de bijlage. Een verzonnen voorbeeld-PDF staat in `docs/certificaten-2026-11/voorbeeld-certificaat-met-annex.pdf` en `agf-frontend/public/demo/demo-certificaat.pdf`.

## Migraties 67 en 68

- **67 `AddProductGgn`**: kolom `SupplyCommitmentLines.Ggn` (13 tekens of leeg) en de instellingen `LoadingSlipShowGgn` en `TransportOrderShowGgn` (standaard aan). Bestaande regels krijgen geen GGN.
- **68 `AddCertificateAiReading`**: bij `AiExtractions` de relatie en het goedgekeurde certificaat, bij `RelationCertificateFiles` de AI-lezing; de controle "goedgekeurd is compleet" geldt nu per soort document (factuur of certificaat). Bestaande gegevens veranderen niet.

## Niet in deel 2

Automatische controle bij GLOBALG.A.P. of andere certificeerders, een overzicht van de chain of custody over meerdere schakels, certificaten aan klantcontracten koppelen, e-mailmeldingen, automatisch producten of relaties aanmaken, een lokaal AI-model en het GGN van het eigen bedrijf als regelnummer.
