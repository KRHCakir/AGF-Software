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

Certificaten op documenten (laadbon, factuur, transportopdracht), de chain of custody over meerdere schakels, koppeling aan partijen, automatische controle bij certificeerders, e-mailmeldingen en AI-uitlezing van de PDF.
