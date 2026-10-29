#!/usr/bin/env bash
# Momentopname van een demo-database maken en terugzetten, om alles wat de rondleiding (of een demo) heeft aangemaakt in
# één keer weg te halen zonder de append-only-bewaking van de app te verzwakken.
#
#   scripts/demo-db-snapshot.sh save    <database> [bestand]
#   scripts/demo-db-snapshot.sh restore <database> [bestand]
#
# Alleen voor demo- en testdatabases: de naam moet "smoke", "demo" of "tour" bevatten. Stop de API die op de database
# draait vóór restore. Restore zet de hele database terug naar de momentopname: ook ander werk van na die momentopname
# verdwijnt. Standaardbestand: ~/.agf-demo-snapshots/<database>.dump.
set -euo pipefail

usage() {
    echo "Gebruik: $0 save|restore <database> [bestand]" >&2
    exit 2
}

[ $# -ge 2 ] || usage
action="$1"
database="$2"
file="${3:-$HOME/.agf-demo-snapshots/$database.dump}"

case "$database" in
    *smoke*|*demo*|*tour*) ;;
    *) echo "Geweigerd: '$database' lijkt geen demo- of testdatabase (naam moet smoke, demo of tour bevatten)." >&2; exit 1 ;;
esac

case "$action" in
    save)
        mkdir -p "$(dirname "$file")"
        pg_dump -Fc -d "$database" -f "$file"
        echo "Momentopname van $database opgeslagen in $file"
        ;;
    restore)
        [ -f "$file" ] || { echo "Geen momentopname gevonden: $file" >&2; exit 1; }
        echo "Dit zet $database volledig terug naar $file ($(date -r "$file" '+%Y-%m-%d %H:%M'))."
        echo "Alles wat daarna in $database is gedaan, verdwijnt. Stop eerst de API op deze database."
        read -r -p "Typ de databasenaam om door te gaan: " answer
        [ "$answer" = "$database" ] || { echo "Afgebroken." >&2; exit 1; }
        pg_restore --clean --if-exists --no-owner --single-transaction -d "$database" "$file"
        echo "$database is teruggezet naar de momentopname."
        ;;
    *) usage ;;
esac
