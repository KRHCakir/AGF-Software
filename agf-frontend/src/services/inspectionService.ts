import { ApiError, getJson, postJson } from './apiClient';
import { getSupplyCommitment } from './supplyService';
import { lineHistory, receiptRegisteredAt } from '../lib/qualityTransitions';
import { isAvailabilityDrillDown, isAvailabilityOverview } from '../types/availability';
import {
    ChangeQualityStatusRequest,
    PalletAuditEvent,
    PalletDetail,
    PalletDetailLine,
    isPalletAuditEventList,
    isPalletDetail,
    isPalletDetailLine,
} from '../types/inspection';

// Keuring per inhoudsregel, uitsluitend via bestaande endpoints. Lezen: pallet, audit, aanvoer en beschikbaarheid.
// Schrijven: alleen POST .../quality-status. Audit wordt nooit door de UI geschreven of gewijzigd.

export interface CommitmentContext {
    vendorName: string | null;
    origin: string | null;
}

export interface InspectionPallet {
    pallet: PalletDetail;
    // Uit pallet-events (PalletCreated/LogisticsStatusChanged); null als niet af te leiden.
    receiptRegisteredAt: string | null;
    commitments: Record<number, CommitmentContext>;
    // Alle audit-events van de pallet; per regel wordt strikt op palletContentLineId gefilterd.
    events: PalletAuditEvent[];
}

export function isReceivedInWarehouse(pallet: PalletDetail): boolean {
    return pallet.logisticsStatus === 'Received' && pallet.locationContext === 'Warehouse';
}

export function historyFor(context: InspectionPallet, line: PalletDetailLine): PalletAuditEvent[] {
    return lineHistory(context.events, line.id);
}

export async function getPalletDetail(palletId: number, signal?: AbortSignal): Promise<PalletDetail> {
    const json = await getJson(`/pallets/${palletId}`, signal);
    if (!isPalletDetail(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij de pallet.');
    }
    return json;
}

async function getPalletAuditEvents(palletId: number, signal?: AbortSignal): Promise<PalletAuditEvent[]> {
    const json = await getJson(`/pallets/${palletId}/audit-events`, signal);
    if (!isPalletAuditEventList(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij de audit-historie.');
    }
    return json;
}

// Eén pallet rechtstreeks, met alleen de audit- en aanvoercontext die bij die pallet hoort.
export async function loadPalletContext(palletId: number, signal?: AbortSignal): Promise<InspectionPallet> {
    const [pallet, events] = await Promise.all([getPalletDetail(palletId, signal), getPalletAuditEvents(palletId, signal)]);
    const commitmentIds = Array.from(new Set(pallet.contentLines.map((line) => line.supplyCommitmentId)));
    const commitmentList = await Promise.all(commitmentIds.map((id) => getSupplyCommitment(id, signal)));
    const commitments: Record<number, CommitmentContext> = {};
    for (const commitment of commitmentList) {
        commitments[commitment.id] = { vendorName: commitment.vendorName, origin: commitment.origin };
    }
    return { pallet, receiptRegisteredAt: receiptRegisteredAt(events), commitments, events };
}

// Wachtrij zonder lijst-endpoint: overzicht -> per variant de drill-down van categorie "warehouse"
// (= Received + Warehouse) -> per gevonden pallet de echte palletgegevens met versies.
export async function loadInspectionQueue(signal?: AbortSignal): Promise<InspectionPallet[]> {
    const overview = await getJson('/availability?route=Warehouse', signal);
    if (!isAvailabilityOverview(overview)) {
        throw new ApiError('shape', 'Onverwacht antwoord van het beschikbaarheidsoverzicht.');
    }
    const variantIds = overview.variants
        .filter((variant) => variant.warehouseSupply.some((supply) =>
            supply.warehouse.quarantine.quantity > 0 || supply.warehouse.approved.quantity > 0 || supply.warehouse.blocked.quantity > 0))
        .map((variant) => variant.productVariantId);

    const drillDowns = await Promise.all(variantIds.map(async (id) => {
        const json = await getJson(`/availability/variants/${id}?route=Warehouse&category=warehouse`, signal);
        if (!isAvailabilityDrillDown(json)) {
            throw new ApiError('shape', 'Onverwacht antwoord van de beschikbaarheidsdetails.');
        }
        return json;
    }));

    const palletIds = Array.from(new Set(drillDowns.flatMap((drillDown) => drillDown.contentLines
        .filter((line) => line.category === 'warehouse' && line.route === 'Warehouse' && line.qualityStatus !== 'Rejected')
        .map((line) => line.palletId))));

    const pallets = await Promise.all(palletIds.map((id) => loadPalletContext(id, signal)));
    return pallets.filter((context) => isReceivedInWarehouse(context.pallet));
}

export async function changeQualityStatus(palletId: number, lineId: number, request: ChangeQualityStatusRequest, signal?: AbortSignal): Promise<PalletDetailLine> {
    const { json } = await postJson(`/pallets/${palletId}/content-lines/${lineId}/quality-status`, request, signal);
    if (!isPalletDetailLine(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij de kwaliteitswijziging.');
    }
    return json;
}

export interface AvailableNow {
    quantity: number;
    netWeightKg: number;
}

// Na een geslaagde keuring: het actuele "Nu beschikbaar (vóór reserveringen)" voor die variant + eenheid,
// rechtstreeks uit de API. null als er (nog) geen magazijnaanbod in die eenheid is.
export async function getAvailableNow(productVariantId: number, unit: string, signal?: AbortSignal): Promise<AvailableNow | null> {
    const json = await getJson(`/availability?route=Warehouse&productVariantId=${productVariantId}`, signal);
    if (!isAvailabilityOverview(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord van het beschikbaarheidsoverzicht.');
    }
    const supply = json.variants
        .filter((variant) => variant.productVariantId === productVariantId)
        .flatMap((variant) => variant.warehouseSupply)
        .find((entry) => entry.unit === unit);
    return supply ? supply.availableBeforeReservations : null;
}
