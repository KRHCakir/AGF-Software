import { isArrayOf, isNullableString, isNumber, isObject, isString } from './guards';

// Bevestigen en documenten van een verkooporder (documenten deel 1: de laadbon). Backend: AgfAdmin/DTOs/SalesOrderDocumentDtos.cs.
export interface SalesOrderDocument {
    id: number;
    kind: string;
    number: string;
    version: number;
    reason: string;
    createdAt: string;
    createdBy: string;
    sentAt: string | null;
    sentBy: string | null;
    latest: boolean;
    // Feedback 22 november: de levering van dit document en haar volgnummer; ontbreken bij een oudere server (= de eerste levering).
    salesDeliveryId?: number;
    deliverySequence?: number;
}

// Per levering: verkoop-partij, laden op, kenteken en of de laadbon of transportopdracht opnieuw moet worden doorgegeven.
export interface SalesDeliveryDocuments {
    salesDeliveryId: number;
    sequence: number;
    partijNumber: string | null;
    loadAt: string | null;
    licensePlate: string | null;
    needsResend: boolean;
    transportOrderNeedsResend: boolean;
}

export interface SalesOrderDocuments {
    salesOrderId: number;
    confirmedAt: string | null;
    confirmedBy: string | null;
    loadAt: string | null;
    licensePlate: string | null;
    // Er is een nieuwere versie van de laadbon dan de eerste en die is nog niet doorgegeven of verstuurd.
    needsResend: boolean;
    // Documenten deel 2: hetzelfde voor de transportopdracht, en of er transport is ingevuld (zonder transport geen opdracht).
    transportOrderNeedsResend?: boolean;
    hasTransport?: boolean;
    documents: SalesOrderDocument[];
    deliveries?: SalesDeliveryDocuments[];
}

// salesDeliveryId: de levering waarvoor laden op en kenteken gelden; weggelaten = de eerste levering (die van de order zelf).
export interface LoadingDetails { loadAt: string | null; licensePlate: string | null; salesDeliveryId?: number }

// warehouseAddress: naam en adres van de loods op de laadbon; leeg (null) = bedrijfsnaam en -adres.
// showGgn (certificaten deel 2): het GGN van het product per regel; ontbreekt bij een oudere server.
export interface LoadingSlipSettings { language: 'en' | 'nl'; showCustomer: boolean; showSupplier: boolean; warehouseAddress: string | null; showGgn?: boolean }

const isDocument = (value: unknown): value is SalesOrderDocument => isObject(value) && isNumber(value.id) && isString(value.kind) && isString(value.number)
    && isNumber(value.version) && isString(value.createdAt) && isString(value.createdBy) && isNullableString(value.sentAt) && typeof value.latest === 'boolean';

const isDeliveryDocuments = (value: unknown): value is SalesDeliveryDocuments => isObject(value) && isNumber(value.salesDeliveryId) && isNumber(value.sequence)
    && isNullableString(value.partijNumber) && isNullableString(value.loadAt) && isNullableString(value.licensePlate)
    && typeof value.needsResend === 'boolean' && typeof value.transportOrderNeedsResend === 'boolean';

export function isSalesOrderDocuments(value: unknown): value is SalesOrderDocuments {
    return isObject(value) && isNumber(value.salesOrderId) && isNullableString(value.confirmedAt) && isNullableString(value.licensePlate)
        && typeof value.needsResend === 'boolean' && isArrayOf(value.documents, isDocument)
        && (value.deliveries === undefined || isArrayOf(value.deliveries, isDeliveryDocuments));
}

export function isLoadingSlipSettings(value: unknown): value is LoadingSlipSettings {
    return isObject(value) && (value.language === 'en' || value.language === 'nl') && typeof value.showCustomer === 'boolean' && typeof value.showSupplier === 'boolean'
        && (value.warehouseAddress === undefined || isNullableString(value.warehouseAddress)) && (value.showGgn === undefined || typeof value.showGgn === 'boolean');
}
