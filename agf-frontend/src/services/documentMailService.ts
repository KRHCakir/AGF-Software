import { ApiError, getJson, putJson } from './apiClient';
import { type DocumentRecipients, type MailSettings, type MailTemplate, type MailTemplates, type SaveDocumentRecipients, isDocumentRecipients, isMailSettings, isMailTemplates } from '../types/documentMail';

// E-mailadressen voor documenten (documenten deel 3): van een relatie (api/relations/{id}/document-recipients) en van de loods
// (api/settings/warehouse-recipients). Opslaan vervangt de hele lijst, met de versie die de gebruiker zag. De afzender (api/settings/mail)
// en de sjablonen (api/settings/mail-templates) onder Beheer.
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

export async function getMailSettings(signal?: AbortSignal): Promise<MailSettings> {
    const json = await getJson('/settings/mail', signal);
    if (!isMailSettings(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de mailinstellingen.');
    return json;
}

export async function saveMailSettings(settings: MailSettings, signal?: AbortSignal): Promise<MailSettings> {
    const { json } = await putJson('/settings/mail', settings, signal);
    if (!isMailSettings(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de mailinstellingen.');
    return json;
}

export async function getMailTemplates(signal?: AbortSignal): Promise<MailTemplates> {
    const json = await getJson('/settings/mail-templates', signal);
    if (!isMailTemplates(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de mailsjablonen.');
    return json;
}

export async function saveMailTemplate(template: Pick<MailTemplate, 'kind' | 'subject' | 'body'>, signal?: AbortSignal): Promise<MailTemplates> {
    const { json } = await putJson('/settings/mail-templates', template, signal);
    if (!isMailTemplates(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de mailsjablonen.');
    return json;
}
