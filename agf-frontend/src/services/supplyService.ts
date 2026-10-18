import { ApiError, getJson, postJson } from './apiClient';
import {
    CreateSupplyCommitmentRequest,
    SupplyCommitment,
    isSupplyCommitment,
    isSupplyCommitmentList,
} from '../types/supply';

// POST is idempotent via requestId: 201 = nieuw, 200 = exact hetzelfde verzoek was al opgeslagen.

export interface SaveResult<T> {
    value: T;
    replayed: boolean;
}

export async function createSupplyCommitment(request: CreateSupplyCommitmentRequest, signal?: AbortSignal): Promise<SaveResult<SupplyCommitment>> {
    const { status, json } = await postJson('/supply-commitments', request, signal);
    if (!isSupplyCommitment(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij het vastleggen van de aanvoer.');
    }
    return { value: json, replayed: status === 200 };
}

// Bronnen voor ontvangst: alleen open Warehouse-aanvoer. DirectTrade komt hier nooit in.
export async function listOpenWarehouseCommitments(signal?: AbortSignal): Promise<SupplyCommitment[]> {
    const json = await getJson('/supply-commitments?route=Warehouse&status=Expected', signal);
    if (!isSupplyCommitmentList(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij de geplande aanvoer.');
    }
    return json.filter((commitment) => commitment.route === 'Warehouse');
}
