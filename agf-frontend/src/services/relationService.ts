import { ApiError, getJson, postJson, putJson } from './apiClient';
import { ConfirmBankAccountRequest, CreditExposure, Relation, RelationList, RelationReference, RelationRoleFilter, RelationSettings, SaveRelationRequest, SimilarRelation, isCreditExposure, isRelation, isRelationList, isRelationReference, isRelationSettings, isSimilarRelationList } from '../types/relation';

// Relatiewerkplek (api/relations). POST is idempotent op requestId: 201 nieuw, 200 identieke herhaling; een 409 met code
// "similar-names" is een gewone uitkomst (gelijkende relaties), elke andere fout wordt als ApiError doorgegeven.

export const SIMILAR_NAMES_CODE = 'similar-names';

export type SaveRelationOutcome =
    | { outcome: 'created' | 'replayed' | 'updated'; relation: Relation }
    | { outcome: 'similar-names'; similar: SimilarRelation[] };

export async function listRelations(options: { role: RelationRoleFilter; query: string; includeInactive: boolean; page: number; pageSize: number }, signal?: AbortSignal): Promise<RelationList> {
    const params = new URLSearchParams({ role: options.role, query: options.query, includeInactive: String(options.includeInactive), page: String(options.page), pageSize: String(options.pageSize) });
    const json = await getJson(`/relations?${params.toString()}`, signal);
    if (!isRelationList(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de relaties.');
    return json;
}

export async function getRelation(id: number, signal?: AbortSignal): Promise<Relation> {
    const json = await getJson(`/relations/${id}`, signal);
    if (!isRelation(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de relatie.');
    return json;
}

export async function getRelationReference(signal?: AbortSignal): Promise<RelationReference> {
    const json = await getJson('/relations/reference', signal);
    if (!isRelationReference(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de keuzelijsten van relaties.');
    return json;
}

function similarFrom(caught: unknown): SimilarRelation[] | null {
    if (caught instanceof ApiError && caught.kind === 'http' && caught.status === 409 && typeof caught.body === 'object' && caught.body !== null) {
        const body = caught.body as Record<string, unknown>;
        if (body.code === SIMILAR_NAMES_CODE && isSimilarRelationList(body.similarRelations)) return body.similarRelations;
    }
    return null;
}

export async function createRelation(request: SaveRelationRequest, signal?: AbortSignal): Promise<SaveRelationOutcome> {
    try {
        const { status, json } = await postJson('/relations', request, signal);
        if (!isRelation(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het aanmaken van de relatie.');
        return { outcome: status === 200 ? 'replayed' : 'created', relation: json };
    } catch (caught) {
        const similar = similarFrom(caught);
        if (similar) return { outcome: 'similar-names', similar };
        throw caught;
    }
}

export async function updateRelation(id: number, request: SaveRelationRequest, signal?: AbortSignal): Promise<Relation> {
    const { json } = await putJson(`/relations/${id}`, request, signal);
    if (!isRelation(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het opslaan van de relatie.');
    return json;
}

// Tweede rol toevoegen zonder tweede onderneming (201 nieuw, 200 als de rol al bestond).
export async function addRelationRole(id: number, role: 'customer' | 'vendor', signal?: AbortSignal): Promise<Relation> {
    const { json } = await postJson(`/relations/${id}/roles/${role}`, {}, signal);
    if (!isRelation(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het toevoegen van de rol.');
    return json;
}

// Bankrekening bevestigen (met naam, op de verwachte versie). Al bevestigd = 200 zonder wijziging; 409 = kaart intussen gewijzigd.
export async function confirmBankAccount(id: number, bankAccountId: number, request: ConfirmBankAccountRequest, signal?: AbortSignal): Promise<Relation> {
    const { json } = await postJson(`/relations/${id}/bank-accounts/${bankAccountId}/confirm`, request, signal);
    if (!isRelation(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het bevestigen van de bankrekening.');
    return json;
}

// Bedrijfsinstelling: adres van de GLOBALG.A.P. Supply Chain Portal (handmatige GGN-controle).
export async function getRelationSettings(signal?: AbortSignal): Promise<RelationSettings> {
    const json = await getJson('/relations/settings', signal);
    if (!isRelationSettings(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de instellingen.');
    return json;
}

export async function updateRelationSettings(settings: RelationSettings, signal?: AbortSignal): Promise<RelationSettings> {
    const { json } = await putJson('/relations/settings', settings, signal);
    if (!isRelationSettings(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het opslaan van de instellingen.');
    return json;
}

// Kredietpositie van een klant (openstaand, limiet, verzekering); orders zonder prijs worden alleen geteld.
export async function getCustomerCredit(customerId: number, signal?: AbortSignal): Promise<CreditExposure> {
    const json = await getJson(`/customers/${customerId}/credit`, signal);
    if (!isCreditExposure(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de kredietpositie.');
    return json;
}
