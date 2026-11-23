// De leveringsconditie van een order (feedback 22 november), spiegel van AgfAdmin/Services/DeliveryTermRules.cs: geen | Incoterm (Incoterms
// 2020 + plaats verplicht) | Franco / AfKwekerij (toelichting optioneel) | Other (omschrijving verplicht). Voorgevuld uit de klant of
// leverancier en per order aan te passen; de relatie verandert er niet door. Pure module zonder React (node:test).

import { DELIVERY_TERM_KINDS, type DeliveryTermKind, INCOTERMS_2020, MAX_DELIVERY_TERM_TEXT_LENGTH, MAX_INCOTERM_PLACE_LENGTH } from './roleTermsRules.ts';

export interface DeliveryTerm {
    deliveryTermKind: string | null;
    incoterm: string | null;
    incotermPlace: string | null;
    deliveryTermText: string | null;
}

// Zoals het formulier haar bijhoudt; '' = niet gekozen.
export interface DeliveryTermDraft {
    kind: string;
    incoterm: string;
    place: string;
    text: string;
}

export const emptyDeliveryTerm = (): DeliveryTermDraft => ({ kind: '', incoterm: '', place: '', text: '' });

export function deliveryTermDraftOf(term: Partial<DeliveryTerm> | null | undefined): DeliveryTermDraft {
    return { kind: term?.deliveryTermKind ?? '', incoterm: term?.incoterm ?? '', place: term?.incotermPlace ?? '', text: term?.deliveryTermText ?? '' };
}

export type DeliveryTermError = { field: 'deliveryTerm.deliveryTermKind' | 'deliveryTerm.incoterm' | 'deliveryTerm.incotermPlace' | 'deliveryTerm.deliveryTermText';
    code: 'invalid' | 'incoterm' | 'incoterm_place' | 'delivery_text' | 'too_long' };

// Naar de payload (altijd meegestuurd, ook leeg = geen conditie): velden die bij de soort niet horen gaan leeg mee.
export function deliveryTermPayload(draft: DeliveryTermDraft): { ok: true; value: DeliveryTerm } | { ok: false; errors: DeliveryTermError[] } {
    const errors: DeliveryTermError[] = [];
    const kind = draft.kind.trim();
    const text = (value: string, max: number, field: DeliveryTermError['field']) => {
        const trimmed = value.trim();
        if (trimmed.length > max) errors.push({ field, code: 'too_long' });
        return trimmed === '' ? null : trimmed;
    };
    if (kind === '') return { ok: true, value: { deliveryTermKind: null, incoterm: null, incotermPlace: null, deliveryTermText: null } };
    if (!(DELIVERY_TERM_KINDS as readonly string[]).includes(kind)) return { ok: false, errors: [{ field: 'deliveryTerm.deliveryTermKind', code: 'invalid' }] };
    let incoterm: string | null = null;
    let place: string | null = null;
    let description: string | null = null;
    if (kind === 'Incoterm') {
        incoterm = draft.incoterm.trim().toUpperCase() || null;
        if (incoterm === null || !(INCOTERMS_2020 as readonly string[]).includes(incoterm)) errors.push({ field: 'deliveryTerm.incoterm', code: 'incoterm' });
        place = text(draft.place, MAX_INCOTERM_PLACE_LENGTH, 'deliveryTerm.incotermPlace');
        if (place === null) errors.push({ field: 'deliveryTerm.incotermPlace', code: 'incoterm_place' });
    } else {
        description = text(draft.text, MAX_DELIVERY_TERM_TEXT_LENGTH, 'deliveryTerm.deliveryTermText');
        if (kind === 'Other' && description === null) errors.push({ field: 'deliveryTerm.deliveryTermText', code: 'delivery_text' });
    }
    return errors.length > 0 ? { ok: false, errors } : { ok: true, value: { deliveryTermKind: kind as DeliveryTermKind, incoterm, incotermPlace: place, deliveryTermText: description } };
}

export function sameDeliveryTerm(a: DeliveryTermDraft, b: DeliveryTermDraft): boolean {
    const left = deliveryTermPayload(a);
    const right = deliveryTermPayload(b);
    return left.ok && right.ok ? JSON.stringify(left.value) === JSON.stringify(right.value) : JSON.stringify(a) === JSON.stringify(b);
}

// Voor op het scherm: "FCA Venlo", "Franco · vanaf 10 pallets", "Eigen omschrijving: …"; null = geen conditie.
export function describeDeliveryTerm(term: Partial<DeliveryTerm> | null | undefined, t: (key: string) => string): string | null {
    if (!term?.deliveryTermKind) return null;
    if (term.deliveryTermKind === 'Incoterm') return `${term.incoterm ?? ''} ${term.incotermPlace ?? ''}`.trim();
    if (term.deliveryTermKind === 'Other') return term.deliveryTermText ?? t('relations_delivery_Other');
    const name = t(`relations_delivery_${term.deliveryTermKind}`);
    return term.deliveryTermText ? `${name} · ${term.deliveryTermText}` : name;
}
