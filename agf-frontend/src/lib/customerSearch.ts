// Klant zoeken in Verkoop: op zoekcode, naam, plaats en btw-nummer, hoofdletter- en accentongevoelig, vanaf 1 teken.
// Volgorde: exacte zoekcode eerst, dan namen die met de zoekterm beginnen, dan alles wat de term ergens bevat; binnen een
// groep op naam. Inactieve klanten zijn niet kiesbaar voor een nieuwe order (de server weigert ze ook).
// Pure module (node:test); geen imports.

export const MAX_CUSTOMER_SUGGESTIONS = 8;

export interface SearchableCustomer {
    id: number;
    name: string;
    searchCode: string | null;
    city?: string | null;
    vatNumber?: string | null;
    isActive: boolean;
}

function normalize(value: string): string {
    return value.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
}

// Btw-nummers worden zonder spaties en punten vergeleken ("NL 8001.23.456.B01" vindt "nl800123").
const compact = (value: string) => normalize(value).replace(/[\s.]/g, '');

// 0 = exacte zoekcode, 1 = naam begint met, 2 = bevat (naam, zoekcode, plaats of btw-nummer); null = geen treffer.
export function customerRank(customer: SearchableCustomer, query: string): 0 | 1 | 2 | null {
    const term = normalize(query);
    if (term === '') return null;
    const code = customer.searchCode === null ? '' : normalize(customer.searchCode);
    if (code !== '' && code === term) return 0;
    const name = normalize(customer.name);
    if (name.startsWith(term)) return 1;
    if (name.includes(term) || code.includes(term) || normalize(customer.city ?? '').includes(term)) return 2;
    const vat = compact(customer.vatNumber ?? '');
    return vat !== '' && compact(term) !== '' && vat.includes(compact(term)) ? 2 : null;
}

export function searchCustomers<T extends SearchableCustomer>(customers: readonly T[], query: string): T[] {
    return customers
        .filter((customer) => customer.isActive)
        .map((customer) => ({ customer, rank: customerRank(customer, query) }))
        .filter((entry): entry is { customer: T; rank: 0 | 1 | 2 } => entry.rank !== null)
        .sort((a, b) => a.rank - b.rank || a.customer.name.localeCompare(b.customer.name))
        .map((entry) => entry.customer);
}

// Omschrijving in de lijst en in het gekozen veld: "Lommerse Groothandel · Barendrecht".
export function describeCustomer(customer: SearchableCustomer): string {
    return customer.city ? `${customer.name} · ${customer.city}` : customer.name;
}
