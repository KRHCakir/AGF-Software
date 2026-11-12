import type { SalesOrderTransport, SaveSalesOrderTransport, TransportMode } from '../types/salesOrderTransport';
import { formatDecimalInput, parseLocaleDecimal } from './decimal.ts';

// Het blok Transport op de verkooporder (documenten deel 2) als concept in het formulier: van en naar wat de API geeft en verwacht. Leeg
// laten = de standaard van de server. Laden op is één tijdstip (datum en tijd, in de tijd van de browser, zoals bij de laadbon). Geen React:
// met node:test te toetsen.

export const OTHER_ADDRESS = 'other';
export const DEFAULT_ADDRESS = '';

export interface TransportDraft {
    mode: TransportMode | '';
    carrierId: number | null;
    carrierName: string;
    loadingPlace: string;
    unloadingChoice: string;      // '' = standaard afleveradres, een adres-id, of 'other' (afwijkend adres)
    unloadingPlace: string;
    loadDate: string;             // jjjj-mm-dd of ''
    loadTime: string;             // uu:mm of ''
    unloadDate: string;
    temperature: string;
    loadingReference: string;
    unloadingReference: string;
    palletKind: string;
    remarks: string;
}

export type TransportProblem = 'mode' | 'carrier' | 'loadDate' | 'loadTime' | 'temperature';

const pad = (value: number) => String(value).padStart(2, '0');

export function draftFrom(transport: SalesOrderTransport | null): TransportDraft {
    const loadAt = transport?.loadAt ? new Date(transport.loadAt) : null;
    return {
        mode: transport?.mode ?? '',
        carrierId: transport?.carrierRelationId ?? null,
        carrierName: transport?.carrierName ?? '',
        loadingPlace: transport?.loadingPlace ?? '',
        unloadingChoice: transport?.unloadingPlace ? OTHER_ADDRESS : transport?.unloadingAddressId != null ? String(transport.unloadingAddressId) : DEFAULT_ADDRESS,
        unloadingPlace: transport?.unloadingPlace ?? '',
        loadDate: loadAt ? `${loadAt.getFullYear()}-${pad(loadAt.getMonth() + 1)}-${pad(loadAt.getDate())}` : '',
        loadTime: loadAt ? `${pad(loadAt.getHours())}:${pad(loadAt.getMinutes())}` : '',
        unloadDate: transport?.unloadDate ? transport.unloadDate.slice(0, 10) : '',
        temperature: transport?.temperatureC != null ? formatDecimalInput(transport.temperatureC, 1) : '',
        loadingReference: transport?.loadingReference ?? '',
        unloadingReference: transport?.unloadingReference ?? '',
        palletKind: transport?.palletKind ?? '',
        remarks: transport?.remarks ?? '',
    };
}

const text = (value: string) => (value.trim() === '' ? null : value.trim());

// "6.30", "630" en "6:30" worden 06:30; ongeldig = null.
export function normalizeTime(value: string): string | null {
    const match = /^(\d{1,2})[:.]?(\d{2})$/.exec(value.trim());
    if (!match) return null;
    const [hours, minutes] = [Number(match[1]), Number(match[2])];
    return hours < 24 && minutes < 60 ? `${pad(hours)}:${pad(minutes)}` : null;
}

export function requestFrom(draft: TransportDraft, version: number | null, hadLoadAt = false): { request: SaveSalesOrderTransport | null; problems: TransportProblem[] } {
    const problems: TransportProblem[] = [];
    if (draft.mode === '') problems.push('mode');
    if (draft.mode === 'Carrier' && draft.carrierId === null) problems.push('carrier');
    const time = draft.loadTime.trim() === '' ? null : normalizeTime(draft.loadTime);
    if (draft.loadDate !== '' && time === null) problems.push('loadTime');
    if (draft.loadDate === '' && draft.loadTime.trim() !== '') problems.push(time === null ? 'loadTime' : 'loadDate');
    let temperature: number | null = null;
    if (draft.temperature.trim() !== '') {
        const parsed = parseLocaleDecimal(draft.temperature, undefined, { maxDecimals: 1, allowZero: true, allowNegative: true });
        if (!parsed.ok || parsed.value < -30 || parsed.value > 30) problems.push('temperature');
        else temperature = parsed.value;
    }
    if (problems.length > 0 || draft.mode === '') return { request: null, problems };
    const other = draft.unloadingChoice === OTHER_ADDRESS;
    return {
        problems,
        request: {
            expectedVersion: version,
            clearLoadAt: hadLoadAt && draft.loadDate === '',
            mode: draft.mode,
            carrierRelationId: draft.mode === 'Carrier' ? draft.carrierId : null,
            loadingPlace: text(draft.loadingPlace),
            unloadingAddressId: !other && draft.unloadingChoice !== DEFAULT_ADDRESS ? Number(draft.unloadingChoice) : null,
            unloadingPlace: other ? text(draft.unloadingPlace) : null,
            loadAt: draft.loadDate !== '' && time !== null ? new Date(`${draft.loadDate}T${time}:00`).toISOString() : null,
            unloadDate: draft.unloadDate === '' ? null : draft.unloadDate,
            temperatureC: temperature,
            loadingReference: text(draft.loadingReference),
            unloadingReference: text(draft.unloadingReference),
            remarks: text(draft.remarks),
            palletKind: text(draft.palletKind),
        },
    };
}
