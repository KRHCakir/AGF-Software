import type { Certificate, CertificateFile, CertificateStatus, SaveCertificate } from '../types/certificate';
import { CUSTOM_TYPE, GGN_TYPE } from '../types/certificate.ts';
import { isDateOnly } from './calendarDate.ts';
import { isGgn, normalizeGgn } from './ggn.ts';

// Regels van het blok "Certificaten" (certificaten deel 1): van en naar de API, met de controles van de server (soort, nummer, GGN van
// 13 cijfers, datums, dubbel nummer) en de waarschuwingen die niet blokkeren (GGN zonder producten, controlecijfer). De status volgt
// uit de einddatum, net als op de server. Geen React: met node:test te toetsen.

export interface CertificateRow {
    key: string;
    id: number | null;
    typeCode: string;
    customTypeName: string;
    number: string;
    certificationBody: string;
    validFrom: string;
    validUntil: string;
    productIds: number[];
    producerName: string;
    hasGgnLabel: boolean;
    checkedOn: string;
    checkedBy: string | null;
    isActive: boolean;
    // Uit de oude GGN-velden overgenomen zonder producten (komt van de server; nieuwe rijen: false).
    productsPending: boolean;
    files: CertificateFile[];
    // Gekozen bestanden voor een certificaat dat nog niet is opgeslagen: na opslaan gaan ze erbij (elk een versie).
    pendingFiles: File[];
}

export interface CertificateProblem { field: string; code: string }

let counter = 0;
const newKey = () => `cert-${++counter}`;

export function emptyCertificateRow(typeCode = ''): CertificateRow {
    return {
        key: newKey(), id: null, typeCode, customTypeName: '', number: '', certificationBody: '', validFrom: '', validUntil: '', productIds: [],
        producerName: '', hasGgnLabel: false, checkedOn: '', checkedBy: null, isActive: true, productsPending: false, files: [], pendingFiles: [],
    };
}

export function rowsFromCertificates(certificates: Certificate[]): CertificateRow[] {
    return certificates.map((c) => ({
        key: newKey(), id: c.id, typeCode: c.typeCode, customTypeName: c.customTypeName ?? '', number: c.number, certificationBody: c.certificationBody ?? '',
        validFrom: c.validFrom ?? '', validUntil: c.validUntil ?? '', productIds: c.products.map((p) => p.id), producerName: c.producerName ?? '',
        hasGgnLabel: c.hasGgnLabel, checkedOn: c.checkedOn ?? '', checkedBy: c.checkedBy, isActive: c.isActive, productsPending: c.productsPending, files: c.files, pendingFiles: [],
    }));
}

// Een nieuwe rij waarin geen enkel bewerkbaar veld is ingevuld is leeg (bijvoorbeeld net toegevoegd met Tab) en gaat niet mee.
export function isBlankRow(row: CertificateRow): boolean {
    return row.id === null && row.typeCode === '' && row.number.trim() === '' && row.customTypeName.trim() === '' && row.certificationBody.trim() === ''
        && row.validFrom.trim() === '' && row.validUntil.trim() === '' && row.checkedOn.trim() === '' && row.producerName.trim() === ''
        && row.productIds.length === 0 && !row.hasGgnLabel && row.pendingFiles.length === 0;
}

// Tab op het laatste veld van de laatste rij geeft een nieuwe rij, maar alleen als die laatste rij al iets bevat.
export function tabAddsRow(rows: CertificateRow[], key: string): boolean {
    const last = rows[rows.length - 1];
    return last !== undefined && last.key === key && !isBlankRow(last);
}

const optional = (value: string): string | null => (value.trim() === '' ? null : value.trim());

// Het GGN-controlecijfer (zoals een GS1-GLN): gewichten 3 en 1 van rechts. Spiegel van CertificateRules.HasValidCheckDigit.
export function hasValidGgnCheckDigit(ggn: string): boolean {
    if (!isGgn(ggn)) return false;
    let sum = 0;
    for (let i = 0; i < 12; i++) {
        const digit = Number(ggn[11 - i]);
        sum += i % 2 === 0 ? digit * 3 : digit;
    }
    return (10 - (sum % 10)) % 10 === Number(ggn[12]);
}

export function normalizeNumber(typeCode: string, raw: string): string {
    return typeCode === GGN_TYPE ? normalizeGgn(raw) : raw.trim();
}

export const numberKey = (value: string) => value.trim().toLowerCase().replace(/[^\p{L}\p{N}]/gu, '');

// De opdracht voor de API, of de fouten (zelfde regels als de server). Lege nieuwe rijen gaan niet mee.
export function buildCertificatesPayload(rows: CertificateRow[], today: string): { ok: true; value: SaveCertificate[] } | { ok: false; errors: CertificateProblem[] } {
    const errors: CertificateProblem[] = [];
    const seen = new Set<string>();
    const value: SaveCertificate[] = [];
    for (const row of rows) {
        if (isBlankRow(row)) continue;
        const k = (field: string) => `certificates.${row.key}.${field}`;
        if (row.typeCode === '') errors.push({ field: k('typeCode'), code: 'type_required' });
        const customName = row.typeCode === CUSTOM_TYPE ? optional(row.customTypeName) : null;
        if (row.typeCode === CUSTOM_TYPE && customName === null) errors.push({ field: k('customTypeName'), code: 'custom_type_required' });
        if (customName !== null && customName.length > 80) errors.push({ field: k('customTypeName'), code: 'too_long' });
        const number = normalizeNumber(row.typeCode, row.number);
        if (number === '') errors.push({ field: k('number'), code: 'number_required' });
        else if (row.typeCode === GGN_TYPE && !isGgn(number)) errors.push({ field: k('number'), code: 'ggn_13_digits' });
        else if (number.length > 100) errors.push({ field: k('number'), code: 'too_long' });
        else {
            const key = `${row.typeCode}|${(customName ?? '').toLowerCase()}|${numberKey(number)}`;
            if (seen.has(key)) errors.push({ field: k('number'), code: 'duplicate' });
            seen.add(key);
        }
        for (const field of ['validFrom', 'validUntil', 'checkedOn'] as const) {
            if (row[field] !== '' && !isDateOnly(row[field])) errors.push({ field: k(field), code: 'invalid_date' });
        }
        if (isDateOnly(row.validFrom) && isDateOnly(row.validUntil) && row.validFrom > row.validUntil) errors.push({ field: k('validUntil'), code: 'end_before_start' });
        if (isDateOnly(row.checkedOn) && row.checkedOn > today) errors.push({ field: k('checkedOn'), code: 'check_future' });
        if (row.certificationBody.trim().length > 200) errors.push({ field: k('certificationBody'), code: 'too_long' });
        value.push({
            id: row.id, typeCode: row.typeCode, customTypeName: customName, number, certificationBody: optional(row.certificationBody),
            validFrom: optional(row.validFrom), validUntil: optional(row.validUntil), productIds: [...new Set(row.productIds)], producerName: optional(row.producerName),
            hasGgnLabel: row.hasGgnLabel, checkedOn: optional(row.checkedOn), isActive: row.isActive,
        });
    }
    return errors.length > 0 ? { ok: false, errors } : { ok: true, value };
}

// Waarschuwingen die niet blokkeren, live in het blok: een GGN zonder producten, en een GGN waarvan het controlecijfer niet klopt.
export function certificateWarnings(row: CertificateRow): string[] {
    if (row.typeCode !== GGN_TYPE) return [];
    const warnings: string[] = [];
    if (row.productIds.length === 0) warnings.push(row.productsPending ? 'products_pending' : 'products_missing');
    const number = normalizeGgn(row.number);
    if (isGgn(number) && !hasValidGgnCheckDigit(number)) warnings.push('ggn_check_digit');
    return warnings;
}

const dayNumber = (iso: string) => Math.round(Date.UTC(Number(iso.slice(0, 4)), Number(iso.slice(5, 7)) - 1, Number(iso.slice(8, 10))) / 86_400_000);

export function daysLeft(validUntil: string, today: string): number | null {
    return isDateOnly(validUntil) && isDateOnly(today) ? dayNumber(validUntil) - dayNumber(today) : null;
}

// Geldig tot en met de einddatum; "verloopt binnenkort" bij hooguit `warningDays` dagen. Spiegel van CertificateRules.Status.
export function certificateStatus(validUntil: string, today: string, warningDays: number): CertificateStatus {
    const left = daysLeft(validUntil, today);
    if (left === null) return 'NoEndDate';
    if (left < 0) return 'Expired';
    return left <= warningDays ? 'ExpiringSoon' : 'Valid';
}

export const needsAttention = (status: CertificateStatus) => status === 'Expired' || status === 'ExpiringSoon';

// Is er iets veranderd ten opzichte van de opgeslagen lijst (dan gaat het mee met Opslaan)?
export function certificatesChanged(rows: CertificateRow[], stored: CertificateRow[]): boolean {
    const shape = (list: CertificateRow[]) => JSON.stringify(list.filter((r) => !isBlankRow(r)).map((r) => [
        r.id, r.typeCode, r.customTypeName.trim(), r.number.trim(), r.certificationBody.trim(), r.validFrom, r.validUntil, [...r.productIds].sort((a, b) => a - b),
        r.producerName.trim(), r.hasGgnLabel, r.checkedOn, r.isActive,
    ]));
    return shape(rows) !== shape(stored) || rows.some((r) => r.pendingFiles.length > 0);
}
