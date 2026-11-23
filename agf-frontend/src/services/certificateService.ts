import { ApiError, getBlob, getJson, postForm, postJson, putJson } from './apiClient';
import { type AiReadingStatus, isAiReadingStatus } from '../types/aiReading';
import { type AiCertificateExtraction, isAiCertificateExtraction } from '../types/certificateAi';
import {
    type CertificateFile, type CertificateList, type CertificateOverview, type CertificateReference, type CertificateSettings, type SaveCertificates,
    isCertificateFile, isCertificateList, isCertificateOverview, isCertificateReference, isCertificateSettings,
} from '../types/certificate';

// Certificaten deel 1 (api/certificates): de soorten, de lijst van een relatie of het eigen bedrijf (opslaan vervangt de hele lijst, met de
// versie die de gebruiker zag), een PDF of foto toevoegen (altijd een nieuwe versie), het overzicht en de instellingen.
const list = (json: unknown): CertificateList => {
    if (!isCertificateList(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de certificaten.');
    return json;
};

// null = het eigen bedrijf.
const ownerPath = (relationId: number | null) => (relationId === null ? '/certificates/own' : `/certificates/relations/${relationId}`);

export async function getCertificateReference(signal?: AbortSignal): Promise<CertificateReference> {
    const json = await getJson('/certificates/reference', signal);
    if (!isCertificateReference(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de soorten certificaten.');
    return json;
}

export async function getCertificates(relationId: number | null, signal?: AbortSignal): Promise<CertificateList> {
    return list(await getJson(ownerPath(relationId), signal));
}

export async function saveCertificates(relationId: number | null, request: SaveCertificates, signal?: AbortSignal): Promise<CertificateList> {
    return list((await putJson(ownerPath(relationId), request, signal)).json);
}

export async function uploadCertificateFile(certificateId: number, file: File, signal?: AbortSignal): Promise<CertificateFile> {
    const form = new FormData();
    form.append('file', file, file.name);
    const { json } = await postForm(`/certificates/${certificateId}/files`, form, signal);
    if (!isCertificateFile(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het toevoegen van het bestand.');
    return json;
}

export function getCertificateFile(certificateId: number, fileId: number, signal?: AbortSignal): Promise<Blob> {
    return getBlob(`/certificates/${certificateId}/files/${fileId}`, signal);
}

// sort (feedback 22 november): een veld uit CERTIFICATE_SORT_KEYS (de server kent precies die velden), met asc of desc.
export async function getCertificateOverview(options: { query?: string; type?: string; status?: string; includeArchived?: boolean; page?: number; pageSize?: number; sort?: string; direction?: 'asc' | 'desc' }, signal?: AbortSignal): Promise<CertificateOverview> {
    const params = new URLSearchParams();
    if (options.page) params.set('page', String(options.page));
    if (options.pageSize) params.set('pageSize', String(options.pageSize));
    if (options.query) params.set('query', options.query);
    if (options.type) params.set('type', options.type);
    if (options.status) params.set('status', options.status);
    if (options.includeArchived) params.set('includeArchived', 'true');
    if (options.sort) { params.set('sort', options.sort); params.set('direction', options.direction ?? 'asc'); }
    const json = await getJson(`/certificates?${params.toString()}`, signal);
    if (!isCertificateOverview(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het overzicht van certificaten.');
    return json;
}

// De producten (niet de varianten) om uit te kiezen: een certificaat geldt op productniveau, zoals avocado.
export async function listProductOptions(signal?: AbortSignal): Promise<{ id: number; name: string }[]> {
    const json = await getJson('/products', signal);
    if (!Array.isArray(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de producten.');
    return json
        .filter((p): p is { id: number; name: string } => typeof p === 'object' && p !== null && typeof (p as { id: unknown }).id === 'number' && typeof (p as { name: unknown }).name === 'string')
        .map((p) => ({ id: p.id, name: p.name }))
        .sort((a, b) => a.name.localeCompare(b.name));
}

export async function getCertificateSettings(signal?: AbortSignal): Promise<CertificateSettings> {
    const json = await getJson('/certificates/settings', signal);
    if (!isCertificateSettings(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de instellingen van certificaten.');
    return json;
}

export async function saveCertificateSettings(settings: CertificateSettings, signal?: AbortSignal): Promise<CertificateSettings> {
    const { json } = await putJson('/certificates/settings', settings, signal);
    if (!isCertificateSettings(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het opslaan van de instellingen van certificaten.');
    return json;
}

// Certificaten deel 2 (api/certificates/ai): of AI-lezen aan staat (dezelfde instelling als bij facturen), een certificaat laten lezen bij
// een relatie (relationId) of het eigen bedrijf (null), het document bekijken en het voorstel afwijzen of handmatig verder gaan.
export async function getCertificateAiStatus(signal?: AbortSignal): Promise<AiReadingStatus> {
    const json = await getJson('/certificates/ai/status', signal);
    if (!isAiReadingStatus(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de status van AI-lezen.');
    return json;
}

export async function readCertificateDocument(file: File, relationId: number | null, signal?: AbortSignal): Promise<AiCertificateExtraction> {
    const form = new FormData();
    form.append('file', file, file.name);
    if (relationId !== null) form.append('relationId', String(relationId));
    const { json } = await postForm('/certificates/ai/extractions', form, signal);
    if (!isAiCertificateExtraction(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het lezen van het certificaat.');
    return json;
}

export function getCertificateAiDocument(id: number, signal?: AbortSignal): Promise<Blob> {
    return getBlob(`/certificates/ai/extractions/${id}/document`, signal);
}

export async function closeCertificateExtraction(id: number, outcome: 'Rejected' | 'Manual', signal?: AbortSignal): Promise<void> {
    await postJson(`/certificates/ai/extractions/${id}/close`, { outcome }, signal);
}
