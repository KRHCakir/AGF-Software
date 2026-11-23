// Sorteren op kolomkoppen (feedback 22 november, prompt 2). Klikken op een kop sorteert oplopend, nog eens aflopend, een derde keer
// terug naar de standaardvolgorde van de lijst. Per soort waarde de juiste volgorde:
// - tekst op Nederlandse volgorde, hoofdletters en accenten tellen niet;
// - getallen en bedragen als getal, datums als datum;
// - nummers (partij-, order- en factuurnummers) natuurlijk: 2 voor 10, ook met letters ervoor zoals I- of V-.
// Een lege waarde staat altijd onderaan, in beide richtingen. Gelijke waarden houden de standaardvolgorde (stabiel sorteren).
// Geen imports: met node:test te toetsen.

export type SortKind = 'text' | 'number' | 'date' | 'natural';
export type SortDirection = 'asc' | 'desc';
export interface SortState { key: string; direction: SortDirection }
export type SortValue = string | number | null | undefined;

// Een sorteerbare kolom: de soort en hoe je de waarde uit een regel haalt. Datums als ISO-tekst (2026-11-22 of met tijd) of als getal.
export interface SortColumn<Row> {
    kind: SortKind;
    value: (row: Row) => SortValue;
}
export type SortColumns<Row> = Readonly<Record<string, SortColumn<Row>>>;

const textCollator = new Intl.Collator('nl', { sensitivity: 'base', numeric: false });
const naturalCollator = new Intl.Collator('nl', { sensitivity: 'base', numeric: true });

// De volgende stand na een klik op kolom `key`: andere kolom of standaard → oplopend, oplopend → aflopend, aflopend → standaard (null).
export function nextSort(current: SortState | null, key: string): SortState | null {
    if (current === null || current.key !== key) return { key, direction: 'asc' };
    return current.direction === 'asc' ? { key, direction: 'desc' } : null;
}

// Voor aria-sort op de kop: alleen de gesorteerde kolom krijgt een richting.
export function ariaSort(current: SortState | null, key: string): 'ascending' | 'descending' | undefined {
    if (current === null || current.key !== key) return undefined;
    return current.direction === 'asc' ? 'ascending' : 'descending';
}

function isEmpty(value: SortValue): boolean {
    return value === null || value === undefined || (typeof value === 'string' && value.trim() === '') || (typeof value === 'number' && Number.isNaN(value));
}

function dateValue(value: string | number): number {
    if (typeof value === 'number') return value;
    const parsed = Date.parse(value);
    return Number.isNaN(parsed) ? Number.NaN : parsed;
}

// Vergelijkt twee niet-lege waarden oplopend.
export function compareValues(kind: SortKind, a: string | number, b: string | number): number {
    switch (kind) {
        case 'number': return Number(a) - Number(b);
        case 'date': return dateValue(a) - dateValue(b);
        case 'natural': return naturalCollator.compare(String(a).trim(), String(b).trim());
        default: return textCollator.compare(String(a).trim(), String(b).trim());
    }
}

// Sorteert een kopie van `rows` volgens `state`; zonder stand (of met een onbekende kolom) blijft de standaardvolgorde.
export function sortRows<Row>(rows: readonly Row[], columns: SortColumns<Row>, state: SortState | null): Row[] {
    const column = state === null ? undefined : columns[state.key];
    if (state === null || column === undefined) return [...rows];
    const sign = state.direction === 'asc' ? 1 : -1;
    return rows
        .map((row, index) => ({ row, index, value: column.value(row) }))
        .sort((x, y) => {
            const xEmpty = isEmpty(x.value);
            const yEmpty = isEmpty(y.value);
            if (xEmpty || yEmpty) return xEmpty === yEmpty ? x.index - y.index : xEmpty ? 1 : -1;
            const compared = compareValues(column.kind, x.value as string | number, y.value as string | number);
            return compared === 0 || Number.isNaN(compared) ? x.index - y.index : sign * compared;
        })
        .map((entry) => entry.row);
}

// ---------------- Onthouden per scherm ----------------

export const SORT_STORAGE_PREFIX = 'agf_sort_';

interface StorageLike {
    getItem(key: string): string | null;
    setItem(key: string, value: string): void;
    removeItem(key: string): void;
}

// Leest een bewaarde stand; alleen een bekende kolom en richting tellen, al het andere is "standaard".
export function parseSort(raw: string | null, allowed: readonly string[]): SortState | null {
    if (raw === null) return null;
    const [key, direction] = raw.split(':');
    if (!allowed.includes(key) || (direction !== 'asc' && direction !== 'desc')) return null;
    return { key, direction };
}

export function serializeSort(state: SortState): string {
    return `${state.key}:${state.direction}`;
}

// Opslag kan ontbreken of weigeren (privévenster, geblokkeerde opslag): dan werkt sorteren gewoon voor deze pagina.
export function readStoredSort(storage: () => StorageLike | undefined, screen: string, allowed: readonly string[]): SortState | null {
    try {
        return parseSort(storage()?.getItem(SORT_STORAGE_PREFIX + screen) ?? null, allowed);
    } catch {
        return null;
    }
}

export function storeSort(storage: () => StorageLike | undefined, screen: string, state: SortState | null): void {
    try {
        const target = storage();
        if (target === undefined) return;
        if (state === null) target.removeItem(SORT_STORAGE_PREFIX + screen);
        else target.setItem(SORT_STORAGE_PREFIX + screen, serializeSort(state));
    } catch {
        // Niet onthouden is geen fout.
    }
}

// ---------------- Sorteren op de server ----------------

// Queryparameters voor een lijst die op de server bladert: alleen een kolom uit de toegestane lijst van die API.
export function sortParams(state: SortState | null): { sort?: string; direction?: SortDirection } {
    return state === null ? {} : { sort: state.key, direction: state.direction };
}

// De keuzelijst "Sorteren op" (390 px, kaarten): "" = standaard, anders "kolom:richting".
export function sortOptionValue(state: SortState | null): string {
    return state === null ? '' : serializeSort(state);
}
