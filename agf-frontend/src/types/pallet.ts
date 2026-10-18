import { PACKAGE_UNITS, PackageUnit } from './availability';
import { isNumber, isNullableString, isObject, isString } from './guards';

// Contract van POST /api/pallets en POST /api/pallets/{id}/content-lines (backend: AgfAdmin/DTOs/PalletDtos.cs).
// Deze eerste kantoorflow maakt uitsluitend Warehouse + Received-pallets; kwaliteit is geen invoerveld (server: Quarantine).

export interface CreateReceivedWarehousePalletRequest {
    reference: string;
    sscc: string | null;
    logisticsStatus: 'Received';
    locationContext: 'Warehouse';
    warehouseLocation: string | null;
    actorReference: string;
    reason: string | null;
}

export interface AddPalletContentLineRequest {
    requestId: string;
    supplyCommitmentLineId: number;
    lotNumber: string;
    quantity: number;
    unit: PackageUnit;
    netWeightKg: number;
    actorReference: string;
    reason: string | null;
}

export interface PalletSummary {
    id: number;
    reference: string;
    logisticsStatus: string;
    locationContext: string;
    warehouseLocation: string | null;
}

export interface PalletContentLineSummary {
    id: number;
    palletId: number;
    supplyCommitmentLineId: number;
    productVariantId: number;
    lotNumber: string;
    quantity: number;
    unit: PackageUnit;
    netWeightKg: number;
    qualityStatus: string;
}

export function isPalletSummary(value: unknown): value is PalletSummary {
    return isObject(value)
        && isNumber(value.id)
        && isString(value.reference)
        && isString(value.logisticsStatus)
        && isString(value.locationContext)
        && isNullableString(value.warehouseLocation);
}

export function isPalletContentLineSummary(value: unknown): value is PalletContentLineSummary {
    return isObject(value)
        && isNumber(value.id)
        && isNumber(value.palletId)
        && isNumber(value.supplyCommitmentLineId)
        && isNumber(value.productVariantId)
        && isString(value.lotNumber)
        && isNumber(value.quantity)
        && isString(value.unit) && (PACKAGE_UNITS as readonly string[]).includes(value.unit)
        && isNumber(value.netWeightKg)
        && isString(value.qualityStatus);
}
