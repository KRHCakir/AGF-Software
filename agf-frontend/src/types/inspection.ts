import { isWeightMethod, type WeightMethod } from '../lib/weight';
import { PACKAGE_UNITS, PackageUnit, QualityStatus, SupplyRoute } from './availability';
import { isArrayOf, isNullableNumber, isNullableString, isNumber, isObject, isString } from './guards';

// Contract van GET /api/pallets/{id}, GET /api/pallets/{id}/audit-events en de response van
// POST .../quality-status (backend: AgfAdmin/DTOs/PalletDtos.cs). Alleen lezen, behalve de kwaliteitsactie.

export const QUALITY_STATUSES: readonly QualityStatus[] = ['Quarantine', 'Approved', 'Blocked', 'Rejected'];

export interface PalletDetailLine {
    id: number;
    palletId: number;
    supplyCommitmentLineId: number;
    supplyCommitmentId: number;
    route: SupplyRoute;
    customerId: number | null;
    productVariantId: number;
    productName: string | null;
    productVariantName: string | null;
    lotNumber: string;
    // Historische ontvangst; voorraad = ontvangst - afgeboekt door verzendingen.
    quantity: number;
    unit: PackageUnit;
    netWeightKg: number;
    // Weighed (gewogen) of Calculated (quantity × netKgPerPackage, etiket).
    weightMethod: WeightMethod;
    netKgPerPackage: number | null;
    // GGN zoals bij ontvangst vastgelegd (null = niet vastgelegd); zoals opgegeven, certificeringsstatus niet gecontroleerd.
    ggn: string | null;
    dispatchedQuantity: number;
    dispatchedNetWeightKg: number;
    onHandQuantity: number;
    onHandNetWeightKg: number;
    depleted: boolean;
    // Receipt of DispatchCorrection (teruggeboekt uit een gecorrigeerde verzending).
    origin: string;
    originCorrectionLineId: number | null;
    qualityStatus: QualityStatus;
    qualityReason: string | null;
    createdAt: string;
    version: number;
    palletVersion: number;
}

export interface PalletDetail {
    id: number;
    reference: string;
    sscc: string | null;
    logisticsStatus: string;
    locationContext: string;
    warehouseLocation: string | null;
    createdAt: string;
    version: number;
    // Minstens één regel en elke regel uitgeput; zegt niets over de fysieke pallet.
    depleted: boolean;
    // Historische ontvangst-kg (zonder teruggeboekte correctie-inhoud), apart van het fysieke contentNetKg.
    receivedNetKg: number;
    contentLines: PalletDetailLine[];
}

export interface PalletAuditEvent {
    id: number;
    palletId: number | null;
    palletContentLineId: number | null;
    eventType: string;
    oldValue: string | null;
    newValue: string | null;
    reason: string | null;
    actorReference: string | null;
    occurredAt: string;
    requestId: string | null;
}

export interface ChangeQualityStatusRequest {
    requestId: string;
    expectedContentLineVersion: number;
    toStatus: QualityStatus;
    actorReference: string;
    reason: string | null;
}

export function isQualityStatus(value: unknown): value is QualityStatus {
    return isString(value) && (QUALITY_STATUSES as readonly string[]).includes(value);
}

export function isPalletDetailLine(value: unknown): value is PalletDetailLine {
    return isObject(value)
        && isNumber(value.id)
        && isNumber(value.palletId)
        && isNumber(value.supplyCommitmentLineId)
        && isNumber(value.supplyCommitmentId)
        && (value.route === 'Warehouse' || value.route === 'DirectTrade')
        && isNullableNumber(value.customerId)
        && isNumber(value.productVariantId)
        && isNullableString(value.productName)
        && isNullableString(value.productVariantName)
        && isString(value.lotNumber)
        && isNumber(value.quantity)
        && isString(value.unit) && (PACKAGE_UNITS as readonly string[]).includes(value.unit)
        && isNumber(value.netWeightKg)
        && isWeightMethod(value.weightMethod)
        && isNullableNumber(value.netKgPerPackage)
        && isNullableString(value.ggn)
        && isNumber(value.dispatchedQuantity)
        && isNumber(value.dispatchedNetWeightKg)
        && isNumber(value.onHandQuantity)
        && isNumber(value.onHandNetWeightKg)
        && typeof value.depleted === 'boolean'
        && isString(value.origin)
        && (value.originCorrectionLineId === null || isNumber(value.originCorrectionLineId))
        && isQualityStatus(value.qualityStatus)
        && isNullableString(value.qualityReason)
        && isString(value.createdAt)
        && isNumber(value.version)
        && isNumber(value.palletVersion);
}

export function isPalletDetail(value: unknown): value is PalletDetail {
    return isObject(value)
        && isNumber(value.id)
        && isString(value.reference)
        && isNullableString(value.sscc)
        && isString(value.logisticsStatus)
        && isString(value.locationContext)
        && isNullableString(value.warehouseLocation)
        && isString(value.createdAt)
        && isNumber(value.version)
        && typeof value.depleted === 'boolean'
        && isNumber(value.receivedNetKg)
        && isArrayOf(value.contentLines, isPalletDetailLine);
}

function isAuditEvent(value: unknown): value is PalletAuditEvent {
    return isObject(value)
        && isNumber(value.id)
        && isNullableNumber(value.palletId)
        && isNullableNumber(value.palletContentLineId)
        && isString(value.eventType)
        && isNullableString(value.oldValue)
        && isNullableString(value.newValue)
        && isNullableString(value.reason)
        && isNullableString(value.actorReference)
        && isString(value.occurredAt)
        && isNullableString(value.requestId);
}

export function isPalletAuditEventList(value: unknown): value is PalletAuditEvent[] {
    return isArrayOf(value, isAuditEvent);
}
