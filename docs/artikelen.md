# Artikelen (feedback 22 november)

Deze pagina beschrijft wat er sinds de feedback van 22 november voor artikelen geldt. Screenshots met alleen verzonnen DEMO-gegevens
staan in [artikelen-2026-11](artikelen-2026-11/).

## Maat is optioneel

- Een artikel, een nieuwe regel en een ontvangst kunnen zonder maat (gember heeft geen maat). Een lege maat toont niets, ook niet op de
  laadbon of de transportopdracht.
- De handelsklasse blijft verplicht (Extra, I of II).
- Sleutel van een artikel: productsoort, ras, herkomst, maat, verpakking, klasse en merk. "Geen maat" en "maat 13" zijn twee artikelen;
  twee keer "geen maat" met verder alles gelijk is een dubbel. Artikelen weigert zo'n dubbel en noemt het bestaande artikel (ook een
  gearchiveerd artikel: zet dat terug).
- Migratie `ClearSizeNotApplicable`: de oude verplichte waarde "n.v.t." wordt echt leeg (alleen bij de artikelen; vastgelegde
  ontvangsten en documenten blijven zoals ze zijn).
- De werklijst "Maat en klasse opschonen" heet nu "Klasse opschonen".

## Herkomst is een landcode

- Op het artikel was de herkomst al een land uit de vaste lijst (ISO 3166-1 alfa-2). Nu ook de herkomst in de kop van de
  inkooporder: `SupplyCommitments.OriginCountryCode`, met de Nederlandse naam als tekst.
- De landkiezer zoekt op code, Nederlandse en Engelse naam en eigen snelkoppelingen, en toont de code met de naam in de taal van het
  scherm: "PE Peru". Documenten tonen de code, zoals de laadbon al deed.
- Migratie `OriginCountryOnPurchaseOrder`: elke oude tekst gaat eerst ongewijzigd naar `OriginLegacyText` (audit, guardregel 35).
  Daarna wordt alleen een eenduidige tekst omgezet (precies de code, de Nederlandse of de Engelse naam). Wat niet eenduidig is ("Peru
  (DEMO)", "EU"), blijft staan en komt in de werklijst "Herkomst controleren (inkooporders)" onder Stamgegevens › Artikelen.
- Een open order krijg je via Aanpassen een land. Bij een definitieve order verandert de kop niet meer (guardregel 29), dus daar blijft
  de oude tekst staan; de regel in de werklijst is dan alleen ter informatie.

## HS-code zoeken op naam

- Oorzaak van "gember vindt niets": de meegeleverde lijst was een demonstratielijst van 34 GN-codes en 25 HS-posten; post 0910
  (gember) stond er niet in. Het zoeken op naam werkte wel, er was alleen niets te vinden.
- Nu: de hoofdstukken 07, 08, 09 en 20 van de Gecombineerde Nomenclatuur 2026 volledig (602 GN-codes en 234 HS-onderverdelingen),
  gemaakt met `scripts/goods-codes/build_cn_reference.py` uit de SKOS-dataset "Combined Nomenclature, 2026" van het Publicatiebureau
  van de EU (https://data.europa.eu/data/datasets/combined-nomenclature-2026). Geen live API: de app leest alleen
  `AgfAdmin/Data/Reference/goods-codes.json`.
- Zoeken op code met of zonder punten en spaties ("0910", "0910 11", "0910.11.00"), op de Nederlandse en Engelse omschrijving (het hele
  pad vanaf de post, zodat "gember" ook "0910 11 00 niet fijngemaakt en niet gemalen" vindt), en op een paar synoniemen
  (`goods-code-synonyms.json`, bijvoorbeeld paprika, blauwe bes, limoen, kaki). Hoofdletters en accenten tellen niet.
- Een code mag altijd met de hand. Staat een volledige code niet in de lijst, dan waarschuwt het formulier; opslaan blijft mogelijk.
- Actualiseren: jaarlijks begin november het script draaien op de dataset van het nieuwe jaar.

## Btw-tarief op het artikel

- Bij het aanmaken van een artikel is het btw-tarief verplicht: 9% (onbewerkt, de standaard), 21% (bewerkt) of 0%.
- Een Dienst (handling, transport enzovoort, onder Instellingen › Kostenartikelen) is standaard 21%.
- Emballage houdt haar eigen, al verplichte btw-percentage uit de emballage-ronde; daar verandert niets.
- Op een nieuwe inkoopfactuurregel is het tarief van het gekozen artikel of de Dienst het voorstel, alleen als de regel nog het
  standaardtarief (of het tarief van het vorige artikel) had. Een met de hand gewijzigd tarief en een AI-voorstel blijven staan.
- Migratie `AddArticleVat`: bestaande artikelen 9%, bestaande Dienst-artikelen 21%. Wat zonder tarief blijft, staat in "Btw nog
  invullen" onder Stamgegevens › Artikelen.
- Niet in deze ronde: btw afleiden uit het land van de relatie (verlegd, export); dat komt in de btw-ronde.

## Artikel kiezen en nieuw artikel in een zijpaneel

- Op de inkooporder en de verkooporder opent **Artikel toevoegen** een lijst van bestaande artikelen. Zoeken op snelcode, naam, herkomst
  (ook de landcode) en maat. Enter of klik zet het artikel direct op de lege laatste regel of een nieuwe regel; de cursor staat dan op
  Aantal. Ook kiezen in de regel zelf springt naar Aantal.
- **Nieuw artikel** (onder de lijst, per regel, of Alt+A op de inkooporder) opent een zijpaneel over de order met de minimale velden:
  product, herkomst, klasse, btw-tarief, eenheid en kg per colli. **Meer velden** toont de rest. Opslaan maakt het artikel aan via
  dezelfde route als Artikelen, sluit het paneel en zet het artikel op de regel waar de gebruiker was.
- Escape of Annuleren sluit het paneel; is er al iets ingevuld, dan eerst de vraag "Weggooien?".
- Het paneel is geen navigatie: de order blijft open en ongewijzigd en de vraag bij weggaan (fixes 5b) komt niet. Verlaat je daarna de
  nog niet opgeslagen order echt, dan komt die vraag wel.
- Op 390 px vult het paneel het scherm.

## Rondleiding

- Stamgegevens: een stap Btw-tarief (9% staat al klaar).
- Inkooporder aanmaken: het artikel wordt gekozen via Artikel toevoegen en de lijst.
