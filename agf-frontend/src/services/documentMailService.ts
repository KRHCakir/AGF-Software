import { ApiError, getJson, putJson } from './apiClient';
import { type DocumentRecipients, type SaveDocumentRecipients, isDocumentRecipients } from '../types/documentMail';

// E-mailadressen voor documenten (documenten deel 3): van een relatie (api/relations/{id}/document-recipients) en van de loods
// (api/settings/warehouse-recipients). Opslaan vervangt de hele lijst, met de versie die de gebruiker zag.
const recipients = (json: unknown): DocumentRecipients => {
    if (!isDocumentRecipients(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de e-mailadressen voor documenten.');
    return json;
};

export async function getRelationRecipients(relationId: number, signal?: AbortSignal): Promise<DocumentRecipients> {
    return recipients(await getJson(`/relations/${relationId}/document-recipients`, signal));
}

export async function saveRelationRecipients(relationId: number, request: SaveDocumentRecipients, signal?: AbortSignal): Promise<DocumentRecipients> {
    return recipients((await putJson(`/relations/${relationId}/document-recipients`, request, signal)).json);
}

export async function getWarehouseRecipients(signal?: AbortSignal): Promise<DocumentRecipients> {
    return recipients(await getJson('/settings/warehouse-recipients', signal));
}

export async function saveWarehouseRecipients(request: SaveDocumentRecipients, signal?: AbortSignal): Promise<DocumentRecipients> {
    return recipients((await putJson('/settings/warehouse-recipients', request, signal)).json);
}
