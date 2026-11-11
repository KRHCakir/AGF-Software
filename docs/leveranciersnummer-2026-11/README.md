# Ordernummer leverancier als hoofdnummer (november 2026)

Gemaakt in Chrome op `agf_supref_screens`, een kopie van `agf_deviation_clean`. Daarop zijn vier orders bij Nature's Pride gemaakt: `45-21`, `45 21` (hetzelfde nummer, anders geschreven), een lang nummer en een order zonder leveranciersnummer. `TEST-MAN-BR8` is een bestaande order met een al ingevulde leveranciersreferentie.

| Bestand | Wat je ziet |
|---|---|
| `01-aanmaken-ordernummer-leverancier.png` | Inkooporder aanmaken: Ordernummer leverancier direct onder de leverancier, met uitleg; Partij en Lot van de leverancier daarna |
| `02-opgeslagen-hoofdnummer.png` | Opgeslagen: het ordernummer van de leverancier groot, het IO-nummer klein als intern nummer |
| `03-waarschuwing-zelfde-nummer.png` | `45 21` bij dezelfde leverancier: waarschuwing met een link naar `45-21`; opslaan kan gewoon |
| `04-opgeslagen-zonder-leveranciersnummer.png` | Zonder leveranciersnummer: het IO-nummer is het hoofdnummer, zonder intern-label |
| `05-lijst.png` | Inkooporderlijst: hoofdnummer met grijs IO-nummer, het lange nummer afgekapt, de bestaande order met haar oude referentie, kolom Partij, filter Ordernummer leverancier |
| `06-lijst-zoeken-4521.png` | Zoeken op `4521` vindt `45-21`, `45 21` en het lange nummer |
| `07-lijst-filter-ordernummer-leverancier.png` | Filter Ordernummer leverancier (`bevestiging`) |
| `08-detail-lang-nummer.png` | Orderdetail: het lange nummer volledig in de titel, IO-nummer eronder, keten-strip met het hoofdnummer |
| `09-lijst-390.png` | 390 px: kaarten met het hoofdnummer bovenaan, lang nummer afgekapt, niets loopt over |
| `10-detail-390.png` | 390 px: het lange nummer breekt af in de titel, het IO-nummer eronder |
| `11-aanmaken-390.png` | 390 px: Leverancier, Ordernummer leverancier en de waarschuwing |
| `12-ontvangst-zoeken-4521.png` | Ontvangst: orders met het hoofdnummer en het IO-nummer; zoeken op `4521` |
