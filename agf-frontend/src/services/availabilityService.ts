import { ApiConfigError, getApiBaseUrl } from './apiConfig';
import {
    AvailabilityDrillDown,
    AvailabilityOverview,
    PackageUnit,
    SupplyRoute,
    isAvailabilityDrillDown,
    isAvailabilityOverview,
} from '../types/availability';

// Alleen-lezen client voor de Availability API. Geen schrijfacties.

export type AvailabilityErrorKind = 'config' | 'network' | 'http' | 'shape' | 'aborted';

export class AvailabilityApiError extends Error {
    readonly kind: AvailabilityErrorKind;
    readonly status: number | null;
    readonly fieldErrors: Record<string, string[]>;

    constructor(kind: AvailabilityErrorKind, message: string, status: number | null = null, fieldErrors: Record<string, string[]> = {}) {
        super(message);
        this.name = 'AvailabilityApiError';
        this.kind = kind;
        this.status = status;
        this.fieldErrors = fieldErrors;
    }
}

export function toAvailabilityError(error: unknown): AvailabilityApiError {
    if (error instanceof AvailabilityApiError) {
        return error;
    }
    return new AvailabilityApiError('network', error instanceof Error ? error.message : 'Onbekende fout');
}

// Uit ValidationProblemDetails ({ errors: { veld: [meldingen] } }) of { message } van de backend.
function readErrorBody(body: unknown): { message: string | null; fieldErrors: Record<string, string[]> } {
    if (typeof body !== 'object' || body === null) {
        return { message: null, fieldErrors: {} };
    }
    const record = body as Record<string, unknown>;
    const fieldErrors: Record<string, string[]> = {};
    if (typeof record.errors === 'object' && record.errors !== null) {
        for (const [field, messages] of Object.entries(record.errors as Record<string, unknown>)) {
            if (Array.isArray(messages)) {
                fieldErrors[field] = messages.filter((m): m is string => typeof m === 'string');
            }
        }
    }
    const message = typeof record.message === 'string' ? record.message : typeof record.title === 'string' ? record.title : null;
    return { message, fieldErrors };
}

async function getJson(path: string, signal?: AbortSignal): Promise<unknown> {
    let baseUrl: string;
    try {
        baseUrl = getApiBaseUrl();
    } catch (error) {
        throw new AvailabilityApiError('config', error instanceof ApiConfigError ? error.message : 'API-adres niet ingesteld.');
    }

    let response: Response;
    try {
        response = await fetch(`${baseUrl}${path}`, { signal, cache: 'no-store', headers: { Accept: 'application/json' } });
    } catch (error) {
        if (signal?.aborted) {
            throw new AvailabilityApiError('aborted', 'Verzoek afgebroken.');
        }
        throw new AvailabilityApiError('network', error instanceof Error ? error.message : 'Netwerkfout');
    }

    if (!response.ok) {
        let body: unknown = null;
        try {
            body = await response.json();
        } catch {
            body = null;
        }
        const { message, fieldErrors } = readErrorBody(body);
        throw new AvailabilityApiError('http', message ?? `HTTP ${response.status}`, response.status, fieldErrors);
    }

    try {
        const json: unknown = await response.json();
        return json;
    } catch {
        throw new AvailabilityApiError('shape', 'Antwoord is geen geldige JSON.');
    }
}

export async function getAvailabilityOverview(signal?: AbortSignal): Promise<AvailabilityOverview> {
    const json = await getJson('/availability', signal);
    if (!isAvailabilityOverview(json)) {
        throw new AvailabilityApiError('shape', 'Onverwacht antwoord van de server.');
    }
    return json;
}

export interface VariantDrillDownQuery {
    route: SupplyRoute;
    unit: PackageUnit | null;
    customerId: number | null;
    includeClosed: boolean;
}

// Een bekende eenheid gaat als filter mee naar de API. Alleen bij een onbekende eenheid (unit: null, legacy-bron)
// laat de service de parameter weg en filtert daarna exact op unit === null.
export async function getAvailabilityVariant(
    productVariantId: number,
    query: VariantDrillDownQuery,
    signal?: AbortSignal,
): Promise<AvailabilityDrillDown> {
    const params = new URLSearchParams({ route: query.route });
    if (query.unit !== null) {
        params.set('unit', query.unit);
    }
    if (query.route === 'DirectTrade' && query.customerId !== null) {
        params.set('customerId', String(query.customerId));
    }
    if (query.includeClosed) {
        params.set('includeClosed', 'true');
    }

    const json = await getJson(`/availability/variants/${productVariantId}?${params.toString()}`, signal);
    if (!isAvailabilityDrillDown(json)) {
        throw new AvailabilityApiError('shape', 'Onverwacht antwoord van de server.');
    }

    if (query.unit !== null) {
        return json;
    }
    return {
        ...json,
        sourceLines: json.sourceLines.filter((line) => line.unit === null),
        // Palletinhoud heeft altijd een expliciete eenheid en hoort dus nooit bij een rij zonder eenheid.
        contentLines: [],
    };
}
