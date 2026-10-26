import { ApiConfigError, getApiBaseUrl } from './apiConfig';

// Gedeelde, getypeerde HTTP-client voor de nieuwe domein-API's. Antwoorden komen binnen als `unknown`
// en worden door de aanroeper met een type-guard gecontroleerd. Geen terugvaladres, geen `any`.

export type ApiErrorKind = 'config' | 'network' | 'http' | 'shape' | 'aborted';

export class ApiError extends Error {
    readonly kind: ApiErrorKind;
    readonly status: number | null;
    readonly fieldErrors: Record<string, string[]>;
    // Het ruwe JSON-foutlichaam van de server (alleen bij HTTP-fouten); nooit tekstueel geïnterpreteerd, alleen op vorm.
    readonly body: unknown;

    constructor(kind: ApiErrorKind, message: string, status: number | null = null, fieldErrors: Record<string, string[]> = {}, body: unknown = null) {
        super(message);
        this.name = 'ApiError';
        this.kind = kind;
        this.status = status;
        this.fieldErrors = fieldErrors;
        this.body = body;
    }

    // Definitieve afwijzing door de server (400/404/409): er is niets opgeslagen.
    // Bij netwerk- en vormfouten is de uitkomst onbekend en mag een verzoek alleen ongewijzigd opnieuw.
    get isDefinitiveRejection(): boolean {
        return this.kind === 'http' && this.status !== null && this.status >= 400 && this.status < 500;
    }
}

export function toApiError(error: unknown): ApiError {
    if (error instanceof ApiError) {
        return error;
    }
    return new ApiError('network', error instanceof Error ? error.message : 'Onbekende fout');
}

export interface JsonResponse {
    status: number;
    json: unknown;
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

async function request(method: 'GET' | 'POST' | 'PUT' | 'DELETE', path: string, body: unknown, signal?: AbortSignal): Promise<JsonResponse> {
    let baseUrl: string;
    try {
        baseUrl = getApiBaseUrl();
    } catch (error) {
        throw new ApiError('config', error instanceof ApiConfigError ? error.message : 'API-adres niet ingesteld.');
    }

    let response: Response;
    try {
        response = await fetch(`${baseUrl}${path}`, {
            method,
            signal,
            cache: 'no-store',
            headers: method === 'GET' || method === 'DELETE'
                ? { Accept: 'application/json' }
                : { Accept: 'application/json', 'Content-Type': 'application/json' },
            body: method === 'GET' || method === 'DELETE' ? undefined : JSON.stringify(body),
        });
    } catch (error) {
        if (signal?.aborted) {
            throw new ApiError('aborted', 'Verzoek afgebroken.');
        }
        throw new ApiError('network', error instanceof Error ? error.message : 'Netwerkfout');
    }

    if (!response.ok) {
        let errorBody: unknown = null;
        try {
            errorBody = await response.json();
        } catch {
            errorBody = null;
        }
        const { message, fieldErrors } = readErrorBody(errorBody);
        throw new ApiError('http', message ?? `HTTP ${response.status}`, response.status, fieldErrors, errorBody);
    }

    // 204 No Content (verwijderen): geen lichaam.
    if (response.status === 204) {
        return { status: 204, json: null };
    }

    try {
        const json: unknown = await response.json();
        return { status: response.status, json };
    } catch {
        throw new ApiError('shape', 'Antwoord is geen geldige JSON.');
    }
}

export async function getJson(path: string, signal?: AbortSignal): Promise<unknown> {
    return (await request('GET', path, undefined, signal)).json;
}

export function postJson(path: string, body: unknown, signal?: AbortSignal): Promise<JsonResponse> {
    return request('POST', path, body, signal);
}

export function putJson(path: string, body: unknown, signal?: AbortSignal): Promise<JsonResponse> {
    return request('PUT', path, body, signal);
}

export function deleteJson(path: string, signal?: AbortSignal): Promise<JsonResponse> {
    return request('DELETE', path, undefined, signal);
}
