import { ApiError, getBlob, getJson, postForm, postJson, putJson } from './apiClient';
import { type AiExtraction, type AiReadingStatus, isAiExtraction, isAiReadingStatus } from '../types/aiReading';

// /api/purchase-invoices/ai: of AI-lezen aan staat, een document laten lezen (het voorstel), het document bekijken, en het voorstel
// afwijzen of handmatig verder gaan. Goedkeuren gaat via het gewone opslaan van de inkoopfactuur (aiExtractionId in de opdracht).

export async function getAiReadingStatus(signal?: AbortSignal): Promise<AiReadingStatus> {
    const json = await getJson('/purchase-invoices/ai/status', signal);
    if (!isAiReadingStatus(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de status van AI-lezen.');
    return json;
}

export async function readInvoiceDocument(file: File, signal?: AbortSignal): Promise<AiExtraction> {
    const form = new FormData();
    form.append('file', file, file.name);
    const { json } = await postForm('/purchase-invoices/ai/extractions', form, signal);
    if (!isAiExtraction(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het lezen van het document.');
    return json;
}

export function getAiDocument(id: number, signal?: AbortSignal): Promise<Blob> {
    return getBlob(`/purchase-invoices/ai/extractions/${id}/document`, signal);
}

export async function closeAiExtraction(id: number, outcome: 'Rejected' | 'Manual', signal?: AbortSignal): Promise<void> {
    await postJson(`/purchase-invoices/ai/extractions/${id}/close`, { outcome }, signal);
}

// De foutcode van een geweigerde of mislukte lezing (AI_OFF, AI_UNAVAILABLE, ...) of van een onbruikbaar bestand (file: empty, ...).
export function aiErrorCode(error: ApiError): string {
    const body = error.body as Record<string, unknown> | null;
    if (body && typeof body.code === 'string') return body.code;
    const file = error.fieldErrors.file?.[0];
    return file ? `file_${file}` : error.kind === 'network' ? 'AI_NETWORK' : 'AI_FAILED';
}

// Beheer: AI-lezen aan/uit (aanzetten alleen met de bevestiging dat de klant het weet) en de maandlimiet.
export interface AiReadingSettings { enabled: boolean; enabledBy: string | null; enabledAt: string | null; monthlyLimitEur: number }

const isSettings = (value: unknown): value is AiReadingSettings => typeof value === 'object' && value !== null
    && typeof (value as AiReadingSettings).enabled === 'boolean' && typeof (value as AiReadingSettings).monthlyLimitEur === 'number';

export async function getAiReadingSettings(signal?: AbortSignal): Promise<AiReadingSettings> {
    const json = await getJson('/settings/ai-reading', signal);
    if (!isSettings(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de instelling AI-lezen.');
    return json;
}

export async function updateAiReadingSettings(request: { enabled: boolean; customerInformed: boolean; monthlyLimitEur: number }, signal?: AbortSignal): Promise<AiReadingSettings> {
    const { json } = await putJson('/settings/ai-reading', request, signal);
    if (!isSettings(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de instelling AI-lezen.');
    return json;
}

// Een bijlage van een factuur (het originele document) als bestand.
export function getInvoiceAttachment(invoiceId: number, attachmentId: number, signal?: AbortSignal): Promise<Blob> {
    return getBlob(`/purchase-invoices/${invoiceId}/attachments/${attachmentId}`, signal);
}
