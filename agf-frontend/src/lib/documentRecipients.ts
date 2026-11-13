import type { DocumentKind, DocumentRecipient, RecipientRole } from '../types/documentMail';
import { emailTypoSuggestion, isStrictEmail } from './emailRules.ts';

// Regels in het blok "E-mailadressen voor documenten" (documenten deel 3): van en naar de API, met de controles van de server (strikte
// vorm, minstens één document, geen dubbel adres voor hetzelfde bereik). Een lege regel telt niet mee. Geen React: met node:test te toetsen.

export interface RecipientRow {
    key: string;
    id: number | null;
    name: string;
    email: string;
    role: RecipientRole;
    kinds: DocumentKind[];
    scope: string;              // '' = de hele relatie, anders het id van het afleveradres
}

export type RecipientProblem = { index: number; field: 'email' | 'kinds'; code: 'invalid' | 'duplicate' | 'kinds' };

let counter = 0;
export const newRowKey = () => `r${++counter}`;

export function emptyRow(kinds: DocumentKind[] = []): RecipientRow {
    return { key: newRowKey(), id: null, name: '', email: '', role: 'To', kinds, scope: '' };
}

export function rowsFrom(recipients: readonly DocumentRecipient[]): RecipientRow[] {
    return recipients.map((r) => ({ key: newRowKey(), id: r.id, name: r.name ?? '', email: r.email, role: r.role, kinds: [...r.kinds], scope: r.relationAddressId === null ? '' : String(r.relationAddressId) }));
}

export const isBlankRow = (row: RecipientRow) => row.name.trim() === '' && row.email.trim() === '';

export function recipientProblems(rows: readonly RecipientRow[]): RecipientProblem[] {
    const problems: RecipientProblem[] = [];
    const seen = new Set<string>();
    rows.forEach((row, index) => {
        if (isBlankRow(row)) return;
        const email = row.email.trim();
        if (!isStrictEmail(email)) problems.push({ index, field: 'email', code: 'invalid' });
        else if (seen.has(`${email.toLowerCase()}|${row.scope}`)) problems.push({ index, field: 'email', code: 'duplicate' });
        seen.add(`${email.toLowerCase()}|${row.scope}`);
        if (row.kinds.length === 0) problems.push({ index, field: 'kinds', code: 'kinds' });
    });
    return problems;
}

export function recipientsFrom(rows: readonly RecipientRow[]): DocumentRecipient[] {
    return rows.filter((row) => !isBlankRow(row)).map((row) => ({
        id: row.id, name: row.name.trim() === '' ? null : row.name.trim(), email: row.email.trim(), role: row.role, kinds: [...row.kinds], relationAddressId: row.scope === '' ? null : Number(row.scope),
    }));
}

// Veranderd ten opzichte van wat er stond (dan pas opslaan): de regels zonder lege, zoals ze naar de server gaan.
export const recipientsChanged = (rows: readonly RecipientRow[], stored: readonly RecipientRow[]) => JSON.stringify(recipientsFrom(rows)) !== JSON.stringify(recipientsFrom(stored));

export const typoHint = (row: RecipientRow): string | null => (row.email.trim() === '' ? null : emailTypoSuggestion(row.email.trim()));
