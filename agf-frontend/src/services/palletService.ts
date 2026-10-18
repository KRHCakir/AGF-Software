import { ApiError, postJson } from './apiClient';
import { SaveResult } from './supplyService';
import {
    AddPalletContentLineRequest,
    CreateReceivedWarehousePalletRequest,
    PalletContentLineSummary,
    PalletSummary,
    isPalletContentLineSummary,
    isPalletSummary,
} from '../types/pallet';

// Pallet: idempotent via de unieke referentie (zelfde referentie + inhoud = 200).
// Inhoudsregel: idempotent via requestId. Kwaliteit zet de server altijd op Quarantine.

export async function createReceivedWarehousePallet(request: CreateReceivedWarehousePalletRequest, signal?: AbortSignal): Promise<SaveResult<PalletSummary>> {
    const { status, json } = await postJson('/pallets', request, signal);
    if (!isPalletSummary(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij het registreren van de pallet.');
    }
    return { value: json, replayed: status === 200 };
}

export async function addPalletContentLine(palletId: number, request: AddPalletContentLineRequest, signal?: AbortSignal): Promise<SaveResult<PalletContentLineSummary>> {
    const { status, json } = await postJson(`/pallets/${palletId}/content-lines`, request, signal);
    if (!isPalletContentLineSummary(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij het registreren van de palletinhoud.');
    }
    return { value: json, replayed: status === 200 };
}
