// Certificaten deel 2: van het AI-voorstel naar een regel van het blok Certificaten, en terug naar de lijst die bij Goedkeuren wordt
// opgeslagen (via het gewone opslaan, met aiApproval). Hetzelfde nummer bij deze relatie: standaard een nieuwe versie van dat certificaat.
// Een product zonder koppeling blijft "niet gekoppeld"; er wordt nooit een product aangemaakt. Pure module zonder React (node:test).

import type { AiCertificateExtraction, AiCertificateProposal } from '../types/certificateAi';
import { CUSTOM_TYPE } from '../types/certificate.ts';
import { type CertificateRow, emptyCertificateRow } from './certificateRules.ts';

// Een product zoals in het document, met de gekozen koppeling (null = niet gekoppeld).
export interface ProductLink { text: string; page: number | null; productId: number | null; suggested: boolean }

export interface AiDraft {
    row: CertificateRow;
    links: ProductLink[];
    // Het bestaande certificaat met hetzelfde nummer dat een nieuwe versie krijgt, of null (een nieuw certificaat).
    targetId: number | null;
}

export function draftFromProposal(proposal: AiCertificateProposal): AiDraft {
    const row = emptyCertificateRow(proposal.typeCode ?? '');
    return {
        row: {
            ...row,
            customTypeName: proposal.typeCode === CUSTOM_TYPE ? proposal.customTypeName ?? '' : '',
            number: proposal.number ?? '',
            certificationBody: proposal.certificationBody ?? '',
            validFrom: proposal.validFrom ?? '',
            validUntil: proposal.validUntil ?? '',
            // De houder van het certificaat is de producent bij een GGN (het veld van certificaten deel 1).
            producerName: proposal.holderName ?? '',
        },
        links: proposal.products.map((p) => ({ text: p.text, page: p.page, productId: p.productId, suggested: p.productId !== null })),
        targetId: proposal.sameNumberCertificateId,
    };
}

export const linkedProductIds = (links: readonly ProductLink[]): number[] => [...new Set(links.flatMap((l) => (l.productId === null ? [] : [l.productId])))];

// De lijst om op te slaan: de opgeslagen certificaten met het goedgekeurde erbij (nieuw achteraan), of met het bestaande certificaat
// bijgewerkt (nieuwe versie: id, bestanden en controle blijven). index = de plek van het goedgekeurde certificaat.
export function approvalRows(stored: readonly CertificateRow[], draft: AiDraft): { rows: CertificateRow[]; index: number } {
    const fields = { ...draft.row, productIds: linkedProductIds(draft.links), productsPending: false };
    const target = draft.targetId === null ? -1 : stored.findIndex((r) => r.id === draft.targetId);
    if (target < 0) return { rows: [...stored, { ...fields, id: null, isActive: true }], index: stored.length };
    const rows = stored.map((r, i) => (i !== target ? r : {
        ...r, typeCode: fields.typeCode, customTypeName: fields.customTypeName, number: fields.number, certificationBody: fields.certificationBody,
        validFrom: fields.validFrom, validUntil: fields.validUntil, productIds: fields.productIds, producerName: fields.producerName || r.producerName,
        productsPending: false, isActive: true,
    }));
    return { rows, index: target };
}

// Hoe een veld gemarkeerd wordt: geel bij twijfel of een fout in het nummer, "niet gevonden" als het ontbreekt.
export type FieldMark = 'doubt' | 'missing' | null;
export function fieldMark(extraction: Pick<AiCertificateExtraction, 'problems'>, field: string): FieldMark {
    const codes = extraction.problems.filter((p) => p.field === field).map((p) => p.code);
    if (codes.includes('missing')) return 'missing';
    return codes.some((c) => c === 'uncertain' || c === 'ggnInvalid' || c === 'ggnCheckDigit' || c === 'invalidDate' || c === 'holderMismatch' || c === 'unmatched') ? 'doubt' : null;
}

export const problemCodes = (extraction: Pick<AiCertificateExtraction, 'problems'>, field: string): string[] =>
    extraction.problems.filter((p) => p.field === field).map((p) => p.code);
