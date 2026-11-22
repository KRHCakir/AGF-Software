#!/usr/bin/env python3
"""Bouwt AgfAdmin/Data/Reference/goods-codes.json uit de officiële GN-dataset (SKOS) van het Publicatiebureau van de EU.

Gebruik (jaarlijks begin november, als de nieuwe GN-verordening voor 1 januari verschenen is):

    python3 -I scripts/goods-codes/build_cn_reference.py ESTAT-CN2026.rdf AgfAdmin/Data/Reference/goods-codes.json

Het RDF-bestand komt uit de dataset "Combined Nomenclature, 2026 (CN 2026)", distributie SKOS_CORE (RDF/XML):
https://data.europa.eu/data/datasets/combined-nomenclature-2026. Geen live API: de app leest alleen het JSON-bestand.

Wat erin komt: de hoofdstukken 07, 08, 09 en 20 volledig.
- entries: elke 8-cijferige GN-code van die hoofdstukken.
- hsHeadings: elke 6-cijferige GS/HS-onderverdeling (HS 2022) van die hoofdstukken.
De omschrijving is het hele pad vanaf de post van 4 cijfers, met " › " ertussen. Een regel als "-- niet fijngemaakt en niet gemalen"
wordt zo "Gember, saffraan, ... › gember › niet fijngemaakt en niet gemalen", zodat zoeken op "gember" ook de 8-cijferige codes vindt.
Bovenaan blijven de bron, de versie en de datum staan; synoniemen staan in een apart, handmatig bijgehouden bestand
(goods-code-synonyms.json).
"""
import json
import re
import sys
import xml.etree.ElementTree as ET
from datetime import date

CHAPTERS = ("07", "08", "09", "20")
RDF = "{http://www.w3.org/1999/02/22-rdf-syntax-ns#}"
SKOS = "{http://www.w3.org/2004/02/skos/core#}"
DC = "{http://purl.org/dc/elements/1.1/}"
XKOS = "{http://rdf-vocabulary.ddialliance.org/xkos#}"
LANG = "{http://www.w3.org/XML/1998/namespace}lang"
BASE = "http://data.europa.eu/xsp/cn2026/"


def clean(label):
    # Streepjes vooraan (niveau in de nomenclatuur) en dubbele spaties eruit; een afsluitende dubbele punt ook.
    text = re.sub(r"^[\s\-–]+", "", label or "")
    text = re.sub(r"\s+", " ", text).strip()
    return text.rstrip(":").strip()


def read(path):
    nodes = {}
    for _, element in ET.iterparse(path, events=("end",)):
        if element.tag != RDF + "Description":
            continue
        about = element.get(RDF + "about") or ""
        if not about.startswith(BASE):
            element.clear()
            continue
        identifier = element.findtext(DC + "identifier")
        if not identifier or not identifier[:2] in CHAPTERS or len(identifier) != 12:
            element.clear()
            continue
        labels = {"nl": None, "en": None}
        for alt in element.findall(SKOS + "altLabel"):
            lang = alt.get(LANG)
            if lang in labels and labels[lang] is None:
                labels[lang] = clean(alt.text)
        broader = element.find(SKOS + "broader")
        depth = element.findtext(XKOS + "depth")
        nodes[identifier] = {
            "id": identifier,
            "notation": (element.findtext(SKOS + "notation") or "").replace(" ", ""),
            "depth": int(depth) if depth else None,
            "nl": labels["nl"],
            "en": labels["en"],
            "parent": broader.get(RDF + "resource")[len(BASE):] if broader is not None else None,
        }
        element.clear()
    return nodes


def path_of(nodes, node, lang):
    parts = []
    current = node
    while current is not None and current["depth"] is not None and current["depth"] >= 3:
        if current[lang]:
            parts.append(current[lang])
        current = nodes.get(current["parent"])
    return " › ".join(reversed(parts))


def main(rdf_path, out_path):
    nodes = read(rdf_path)
    entries = []
    headings = []
    for node in sorted(nodes.values(), key=lambda n: n["id"]):
        # Echte codes eindigen op "80"; "10" zijn groeperingsregels zonder eigen code.
        if not node["id"].endswith("80"):
            continue
        code = node["notation"]
        item = {"code": code, "descriptionNl": path_of(nodes, node, "nl"), "descriptionEn": path_of(nodes, node, "en")}
        # Het niveau volgt uit de lengte van de code, niet uit de diepte: een post zonder onderverdeling ("0702 00") staat hoger.
        if len(code) == 8:
            entries.append(item)
        elif len(code) == 6:
            headings.append(item)
    # Een GS/HS-onderverdeling zonder eigen GN-splitsing staat in de dataset alleen als GN-code "xxxx xx 00" (zelfde omschrijving).
    known = {h["code"] for h in headings}
    for entry in entries:
        if entry["code"][:6] not in known and entry["code"].endswith("00"):
            headings.append({**entry, "code": entry["code"][:6]})
            known.add(entry["code"][:6])
    headings.sort(key=lambda h: h["code"])
    missing = sorted({e["code"][:6] for e in entries} - known)
    if missing:
        sys.exit(f"GS/HS-onderverdeling ontbreekt voor: {', '.join(missing)}")
    with open(out_path, encoding="utf-8") as handle:
        current = json.load(handle)
    source = current["source"]
    source["note"] = (
        f"Hoofdstukken {', '.join(CHAPTERS[:-1])} en {CHAPTERS[-1]} van de GN 2026 volledig: {len(entries)} GN-codes (8 cijfers) en "
        f"{len(headings)} GS/HS-onderverdelingen (6 cijfers, HS 2022), met de Nederlandse en Engelse omschrijving als pad vanaf de post. "
        "Gemaakt met scripts/goods-codes/build_cn_reference.py uit de SKOS-dataset van het Publicatiebureau. Codes uit andere hoofdstukken "
        "staan er niet in en mogen met de hand worden ingevuld (waarschuwing, geen blokkade). TARIC (10 cijfers): geen onderbouwde "
        "TARIC-referenties; die worden alleen na raadpleging van de officiële TARIC-databank met de hand ingevuld. Actualiseren: jaarlijks "
        "begin november het script draaien op de dataset van het nieuwe jaar."
    )
    source["retrievedAt"] = date.today().isoformat()
    source["chapters"] = list(CHAPTERS)
    out = {
        "source": source,
        "hsHeadingsNote": "GS/HS-onderverdelingen (6 cijfers, HS 2022) van dezelfde hoofdstukken, uit dezelfde dataset.",
        "hsHeadings": headings,
        "entries": entries,
    }
    with open(out_path, "w", encoding="utf-8") as handle:
        handle.write("{\n")
        handle.write('  "source": ' + json.dumps(source, ensure_ascii=False, indent=2).replace("\n", "\n  ") + ",\n")
        handle.write(f'  "hsHeadingsNote": {json.dumps(out["hsHeadingsNote"], ensure_ascii=False)},\n')
        for key in ("hsHeadings", "entries"):
            rows = ",\n".join("    " + json.dumps(row, ensure_ascii=False) for row in out[key])
            handle.write(f'  "{key}": [\n{rows}\n  ]' + (",\n" if key == "hsHeadings" else "\n"))
        handle.write("}\n")
    print(f"{len(entries)} GN-codes, {len(headings)} HS-onderverdelingen")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit("gebruik: build_cn_reference.py <ESTAT-CN2026.rdf> <goods-codes.json>")
    main(sys.argv[1], sys.argv[2])
