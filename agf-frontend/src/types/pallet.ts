import { isWeightMethod, type WeightMethod } from '../lib/weight';
import { PACKAGE_UNITS, PackageUnit } from './availability';
import { isNumber, isNullableString, isObject, isString } from './guards';

// Contract van POST /api/pallets en POST /api/pallets/{id}/content-lines (backend: AgfAdmin/DTOs/PalletDtos.cs).
// Deze eerste kantoorflow maakt uitsluitend Warehouse + Received-pallets; kwaliteit is geen invoerveld (server: Quarantine).

export interface CreateReceivedWarehousePalletRequest {
    // Idempotentiesleutel: een identieke herhaling geeft dezelfde pallet terug, ook zonder eigen palletnummer.
    requestId: string;
    // Eigen palletnummer (optioneel); null laat de server een herkenbaar nummer (P-000123) genereren.
    reference: string | null;
    sscc: string | null;
    logisticsStatus: 'Received';
    locationContext: 'Warehouse';
    warehouseLocation: string | null;
    // Optioneel bij ontvangst; null blijft null in de audit (nooit een verzonnen naam).
    actorReference: string | null;
    reason: string | null;
}

export interface AddPalletContentLineRequest {
    requestId: string;
    supplyCommitmentLineId: number;
    lotNumber: string;
    quantity: number;
    unit: PackageUnit;
    // Weighed: netWeightKg is gewogen. Calculated: netKgPerPackage verplicht en netWeightKg = quantity × netKgPerPackage.
    weightMethod: WeightMethod;
    netKgPerPackage: number | null;
    netWeightKg: number;
    // GGN van deze partij (13 cijfers als tekst) zoals op de regel gekozen: voorstel uit het artikel, aangepast of leeggemaakt (null).
    ggn: string | null;
    actorReference: string | null;
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
    weightMethod: WeightMethod;
    netKgPerPackage: number | null;
    ggn: string | null;
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
        && isWeightMethod(value.weightMethod)
        && (value.netKgPerPackage === null || isNumber(value.netKgPerPackage))
        && isNullableString(value.ggn)
        && isString(value.qualityStatus);
}
