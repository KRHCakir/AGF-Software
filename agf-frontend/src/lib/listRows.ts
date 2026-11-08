// Regels in lijsten (feedback 8 november): om en om wit en lichtgrijs, zodat je in een brede tabel ziet welke cellen bij één regel
// horen. Erover bewegen kleurt blauw, de gekozen regel is blauw. Een klik op de rij opent de regel, behalve een klik op een knop, link
// of veld in die rij (die doen hun eigen ding) en behalve als je tekst selecteert om te kopiëren. Toetsenbord: de knop of link in de
// eerste kolom blijft de manier om te openen. Geen imports: met node:test te toetsen.

export const ROW_PLAIN = 'bg-white';
export const ROW_STRIPE = 'bg-slate-100/70';
export const ROW_SELECTED = 'bg-blue-50';

// `index` telt vanaf 0: de eerste regel is wit, de tweede grijs, de derde wit, enzovoort.
export function listRowClass(index: number, options: { selected?: boolean; clickable?: boolean; selectedClass?: string } = {}): string {
    const { selected = false, clickable = false, selectedClass = ROW_SELECTED } = options;
    const background = selected ? selectedClass : index % 2 === 1 ? ROW_STRIPE : ROW_PLAIN;
    return `${background} ${clickable ? 'cursor-pointer hover:bg-blue-50' : 'hover:bg-blue-50/60'}`;
}

const INTERACTIVE = 'a, button, input, select, textarea, label, summary, [role="button"], [contenteditable="true"]';

interface ClosestTarget {
    closest(selector: string): unknown;
}

// Of een klik op (iets in) de rij de regel opent.
export function rowClickOpens(target: unknown, row: unknown, selectedText: string): boolean {
    if (selectedText.trim() !== '') return false;
    const element = target !== null && typeof target === 'object' && typeof (target as ClosestTarget).closest === 'function' ? target as ClosestTarget : null;
    const interactive = element?.closest(INTERACTIVE) ?? null;
    return interactive === null || interactive === row;
}

// onClick voor een <tr>: opent de regel volgens rowClickOpens.
export function rowClick(open: () => void) {
    return (event: { target: unknown; currentTarget: unknown }) => {
        const selection = typeof window === 'undefined' ? '' : window.getSelection()?.toString() ?? '';
        if (rowClickOpens(event.target, event.currentTarget, selection)) open();
    };
}
