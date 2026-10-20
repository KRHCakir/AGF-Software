import { PACKAGE_UNITS, PackageUnit, QualityStatus } from './availability';
import { isArrayOf, isNullableString, isNumber, isObject, isString } from './guards';
import { isQualityStatus } from './inspection';

// Exact het contract van /api/sales-orders en /api/stock-reservations (backend: AgfAdmin/DTOs/SalesOrderDtos.cs).
// Getallen zijn decimals uit de backend; de frontend rekent er niet mee, behalve vergelijken en aftrekken voor weergave.

export type ReservationStatus = 'Active' | 'Released' | 'Consumed' | 'Corrected';

export const RESERVATION_STATUSES: readonly ReservationStatus[] = ['Active', 'Released', 'Consumed', 'Corrected'];

export interface CreateSalesOrderLineRequest {
    productVariantId: number;
    quantity: number;
    unit: PackageUnit;
}

export interface CreateSalesOrderRequest {
    requestId: string;
    customerId: number;
    customerReference: string | null;
    requestedDeliveryDate: string;
    actorReference: string;
    lines: CreateSalesOrderLineRequest[];
}

// Hoeveelheid, eenheid en netto-kg zijn altijd expliciet; de UI leidt nooit kg af uit dozen of maat.
export interface CreateStockReservationRequest {
    requestId: string;
    palletContentLineId: number;
    quantity: number;
    unit: PackageUnit;
    netWeightKg: number;
    actorReference: string;
    reason: string | null;
}

// Toewijzen van verwachte, klantgebonden DirectTrade-aanvoer: alleen hoeveelheid en eenheid, nooit kg; met expliciete bevestiging.
export interface CreateDirectTradeAllocationRequest {
    requestId: string;
    supplyCommitmentLineId: number;
    quantity: number;
    unit: PackageUnit;
    acknowledgeExpectedSupply: boolean;
    actorReference: string;
    reason: string | null;
}

// DirectTrade-verzending: hele Active toewijzingen met de werkelijke hoeveelheid, eenheid, netto kg (ingevoerd, nooit afgeleid) en partij.
export interface CreateDirectTradeShipmentLineRequest {
    stockReservationId: number;
    expectedReservationVersion: number;
    quantity: number;
    unit: PackageUnit;
    netWeightKg: number;
    lotNumber: string;
    lotDeviationReason: string | null;
}

export interface CreateDirectTradeShipmentRequest {
    requestId: string;
    expectedOrderVersion: number;
    actorReference: string;
    // Kalenderdatum yyyy-MM-dd, niet na vandaag in Europe/Amsterdam (backend handhaaft).
    shippedOn: string;
    reference: string | null;
    lines: CreateDirectTradeShipmentLineRequest[];
}

export interface ReleaseStockReservationRequest {
    requestId: string;
    expectedReservationVersion: number;
    actorReference: string;
    reason: string;
}

// Annuleren van een Open order: alle Active reserveringen (ook on hold) worden door de server in dezelfde transactie vrijgegeven.
export interface CancelSalesOrderRequest {
    requestId: string;
    expectedOrderVersion: number;
    actorReference: string;
    reason: string;
}

// Verzenden: gekozen Active reserveringen in hun geheel verbruiken; hoeveelheid, eenheid en kg komen uit de reservering.
export interface CreateSalesDispatchLineRequest {
    stockReservationId: number;
    expectedReservationVersion: number;
}

export interface CreateSalesDispatchRequest {
    requestId: string;
    expectedOrderVersion: number;
    actorReference: string;
    reference: string | null;
    lines: CreateSalesDispatchLineRequest[];
}

export interface SalesDispatchLine {
    id: number;
    stockReservationId: number;
    salesOrderLineId: number;
    palletContentLineId: number;
    palletId: number;
    palletReference: string | null;
    lotNumber: string | null;
    productName: string | null;
    productVariantName: string | null;
    quantity: number;
    unit: PackageUnit;
    netWeightKg: number;
    stockMovementId: number;
}

// Administratieve correctie van een hele verzending: goederen hebben het magazijn niet verlaten (door de gebruiker bevestigd).
export interface CorrectSalesDispatchRequest {
    requestId: string;
    expectedOrderVersion: number;
    actorReference: string;
    reason: string;
    goodsNeverLeftWarehouse: true;
    lines: CreateSalesDispatchLineRequest[];
}

export interface SalesDispatchCorrectionLine {
    id: number;
    salesDispatchLineId: number;
    stockReservationId: number;
    restoredPalletContentLineId: number;
    palletId: number;
    palletReference: string | null;
    lotNumber: string | null;
    quantity: number;
    unit: PackageUnit;
    netWeightKg: number;
    restoredQualityStatus: QualityStatus | null;
    stockMovementId: number;
}

export interface SalesDispatchCorrection {
    id: number;
    requestId: string;
    salesDispatchId: number;
    salesOrderId: number;
    reason: string;
    actorReference: string;
    goodsNeverLeftWarehouseConfirmed: boolean;
    correctedAt: string;
    lines: SalesDispatchCorrectionLine[];
}

export interface SalesDispatch {
    id: number;
    requestId: string;
    salesOrderId: number;
    reference: string | null;
    actorReference: string;
    dispatchedAt: string;
    // Gevuld zodra de verzending administratief is gecorrigeerd; de verzending zelf blijft ongewijzigd.
    correction: SalesDispatchCorrection | null;
    lines: SalesDispatchLine[];
}

export interface SalesOrderAuditEvent {
    id: number;
    salesOrderId: number;
    eventType: string;
    oldValue: string | null;
    newValue: string | null;
    reason: string | null;
    actorReference: string | null;
    occurredAt: string;
    requestId: string | null;
}

export interface StockReservation {
    id: number;
    requestId: string | null;
    salesOrderId: number;
    salesOrderLineId: number;
    // Alleen bij source Physical; een DirectTrade-toewijzing (verwachte, klantgebonden aanvoer) staat nooit op een pallet.
    palletContentLineId: number | null;
    palletId: number | null;
    palletReference: string | null;
    warehouseLocation: string | null;
    // Partij van de inhoudsregel (Physical) of van de bronregel (DirectTrade).
    lotNumber: string | null;
    quantity: number;
    unit: PackageUnit;
    // Gemeten netto kg; null bij een toewijzing: er is geen gemeten kg en de UI leidt er nooit een af.
    netWeightKg: number | null;
    source: string;
    // Bron (toezegging); bij DirectTrade met leverancier, herkomst, bestemming en verwachte leverdatum.
    supplyCommitmentLineId: number;
    supplyCommitmentId: number;
    vendorName: string | null;
    origin: string | null;
    destination: string | null;
    expectedAt: string | null;
    // Alleen bij een verzonden (Consumed) DirectTrade-toewijzing. Verzonden betekent niet afgeleverd: er is (nog) geen afleverbevestiging.
    directTradeShipmentId: number | null;
    shippedOn: string | null;
    shippedNetWeightKg: number | null;
    shippedLotNumber: string | null;
    status: ReservationStatus;
    createdAt: string;
    releasedAt: string | null;
    releaseReason: string | null;
    version: number;
    // Actuele kwaliteit van de inhoudsregel, rechtstreeks uit de server; nooit afgeleid uit de reserveringsstatus.
    contentQualityStatus: QualityStatus | null;
    // Active op inhoud die niet (meer) Approved is: wacht op vrijgave of herkeuring.
    onHold: boolean;
    // Alleen bij Consumed: wanneer en met welke verzending.
    consumedAt: string | null;
    dispatchId: number | null;
    dispatchReference: string | null;
    // Alleen bij Corrected (eindtoestand na een verzendcorrectie): telt niet als gereserveerd of verzonden.
    correctedAt: string | null;
    correctionId: number | null;
}

export interface SalesOrderLine {
    id: number;
    productVariantId: number;
    productName: string | null;
    productVariantName: string | null;
    quantity: number;
    unit: PackageUnit;
    // Gereserveerd = Active Physical (magazijnvoorraad).
    reservedQuantity: number;
    reservedNetWeightKg: number;
    // Toegewezen = Active DirectTrade: verwachte, klantgebonden aanvoer. Geen voorraad, geen kg, nooit "gereed".
    allocatedQuantity: number;
    // Verzonden (Consumed): telt niet meer als gereserveerd en komt nooit terug als vrije voorraad.
    dispatchedQuantity: number;
    dispatchedNetWeightKg: number;
    // Deel van verzonden dat als DirectTrade (leverancier -> klant) is verzonden.
    directTradeShippedQuantity: number;
    // Open = gevraagd - gereserveerd - toegewezen - verzonden; vervuld = verzonden == gevraagd.
    openQuantity: number;
    fulfilled: boolean;
    version: number;
    reservations: StockReservation[];
}

export interface SalesOrder {
    id: number;
    requestId: string;
    customerId: number;
    customerName: string | null;
    status: string;
    customerReference: string | null;
    requestedDeliveryDate: string;
    actorReference: string;
    createdAt: string;
    version: number;
    // Alleen gevuld bij status Cancelled; rechtstreeks uit de server, nooit afgeleid.
    cancelledAt: string | null;
    cancelReason: string | null;
    cancelledBy: string | null;
    // Afgeleid: elke regel volledig verzonden; de status blijft Open.
    fulfilled: boolean;
    lines: SalesOrderLine[];
}

// Inhoudsregel die voor een orderregel te reserveren is: zelfde variant + eenheid, Approved, Received + Warehouse, route Warehouse.
export interface ReservableStockLine {
    palletContentLineId: number;
    palletId: number;
    palletReference: string;
    warehouseLocation: string | null;
    lotNumber: string;
    unit: PackageUnit;
    // Historische ontvangst; voorraad = ontvangst - afgeboekt; beschikbaar = voorraad - Active gereserveerd.
    quantity: number;
    netWeightKg: number;
    dispatchedQuantity: number;
    dispatchedNetWeightKg: number;
    onHandQuantity: number;
    onHandNetWeightKg: number;
    reservedQuantity: number;
    reservedNetWeightKg: number;
    availableQuantity: number;
    availableNetWeightKg: number;
    qualityStatus: QualityStatus;
    contentLineVersion: number;
}

function isUnit(value: unknown): value is PackageUnit {
    return isString(value) && (PACKAGE_UNITS as readonly string[]).includes(value);
}

function isReservationStatus(value: unknown): value is ReservationStatus {
    return isString(value) && (RESERVATION_STATUSES as readonly string[]).includes(value);
}

export function isStockReservation(value: unknown): value is StockReservation {
    return isObject(value)
        && isNumber(value.id)
        && isNullableString(value.requestId)
        && isNumber(value.salesOrderId)
        && isNumber(value.salesOrderLineId)
        && (value.palletContentLineId === null || isNumber(value.palletContentLineId))
        && (value.palletId === null || isNumber(value.palletId))
        && isNullableString(value.palletReference)
        && isNullableString(value.warehouseLocation)
        && isNullableString(value.lotNumber)
        && isNumber(value.quantity)
        && isUnit(value.unit)
        && (value.netWeightKg === null || isNumber(value.netWeightKg))
        && isString(value.source)
        && isNumber(value.supplyCommitmentLineId)
        && isNumber(value.supplyCommitmentId)
        && isNullableString(value.vendorName)
        && isNullableString(value.origin)
        && isNullableString(value.destination)
        && isNullableString(value.expectedAt)
        && (value.directTradeShipmentId === null || isNumber(value.directTradeShipmentId))
        && isNullableString(value.shippedOn)
        && (value.shippedNetWeightKg === null || isNumber(value.shippedNetWeightKg))
        && isNullableString(value.shippedLotNumber)
        && isReservationStatus(value.status)
        && isString(value.createdAt)
        && isNullableString(value.releasedAt)
        && isNullableString(value.releaseReason)
        && isNumber(value.version)
        && (value.contentQualityStatus === null || isQualityStatus(value.contentQualityStatus))
        && typeof value.onHold === 'boolean'
        && isNullableString(value.consumedAt)
        && (value.dispatchId === null || isNumber(value.dispatchId))
        && isNullableString(value.dispatchReference)
        && isNullableString(value.correctedAt)
        && (value.correctionId === null || isNumber(value.correctionId));
}

function isSalesOrderLine(value: unknown): value is SalesOrderLine {
    return isObject(value)
        && isNumber(value.id)
        && isNumber(value.productVariantId)
        && isNullableString(value.productName)
        && isNullableString(value.productVariantName)
        && isNumber(value.quantity)
        && isUnit(value.unit)
        && isNumber(value.reservedQuantity)
        && isNumber(value.reservedNetWeightKg)
        && isNumber(value.allocatedQuantity)
        && isNumber(value.dispatchedQuantity)
        && isNumber(value.directTradeShippedQuantity)
        && isNumber(value.dispatchedNetWeightKg)
        && isNumber(value.openQuantity)
        && typeof value.fulfilled === 'boolean'
        && isNumber(value.version)
        && isArrayOf(value.reservations, isStockReservation);
}

export function isSalesOrder(value: unknown): value is SalesOrder {
    return isObject(value)
        && isNumber(value.id)
        && isString(value.requestId)
        && isNumber(value.customerId)
        && isNullableString(value.customerName)
        && isString(value.status)
        && isNullableString(value.customerReference)
        && isString(value.requestedDeliveryDate)
        && isString(value.actorReference)
        && isString(value.createdAt)
        && isNumber(value.version)
        && isNullableString(value.cancelledAt)
        && isNullableString(value.cancelReason)
        && isNullableString(value.cancelledBy)
        && typeof value.fulfilled === 'boolean'
        && isArrayOf(value.lines, isSalesOrderLine);
}

function isSalesDispatchLine(value: unknown): value is SalesDispatchLine {
    return isObject(value)
        && isNumber(value.id)
        && isNumber(value.stockReservationId)
        && isNumber(value.salesOrderLineId)
        && isNumber(value.palletContentLineId)
        && isNumber(value.palletId)
        && isNullableString(value.palletReference)
        && isNullableString(value.lotNumber)
        && isNullableString(value.productName)
        && isNullableString(value.productVariantName)
        && isNumber(value.quantity)
        && isUnit(value.unit)
        && isNumber(value.netWeightKg)
        && isNumber(value.stockMovementId);
}

function isSalesDispatchCorrectionLine(value: unknown): value is SalesDispatchCorrectionLine {
    return isObject(value)
        && isNumber(value.id)
        && isNumber(value.salesDispatchLineId)
        && isNumber(value.stockReservationId)
        && isNumber(value.restoredPalletContentLineId)
        && isNumber(value.palletId)
        && isNullableString(value.palletReference)
        && isNullableString(value.lotNumber)
        && isNumber(value.quantity)
        && isUnit(value.unit)
        && isNumber(value.netWeightKg)
        && (value.restoredQualityStatus === null || isQualityStatus(value.restoredQualityStatus))
        && isNumber(value.stockMovementId);
}

export function isSalesDispatchCorrection(value: unknown): value is SalesDispatchCorrection {
    return isObject(value)
        && isNumber(value.id)
        && isString(value.requestId)
        && isNumber(value.salesDispatchId)
        && isNumber(value.salesOrderId)
        && isString(value.reason)
        && isString(value.actorReference)
        && typeof value.goodsNeverLeftWarehouseConfirmed === 'boolean'
        && isString(value.correctedAt)
        && isArrayOf(value.lines, isSalesDispatchCorrectionLine);
}

export function isSalesDispatch(value: unknown): value is SalesDispatch {
    return isObject(value)
        && isNumber(value.id)
        && isString(value.requestId)
        && isNumber(value.salesOrderId)
        && isNullableString(value.reference)
        && isString(value.actorReference)
        && isString(value.dispatchedAt)
        && (value.correction === null || isSalesDispatchCorrection(value.correction))
        && isArrayOf(value.lines, isSalesDispatchLine);
}

export function isSalesDispatchList(value: unknown): value is SalesDispatch[] {
    return isArrayOf(value, isSalesDispatch);
}

export function isSalesOrderAuditEvent(value: unknown): value is SalesOrderAuditEvent {
    return isObject(value)
        && isNumber(value.id)
        && isNumber(value.salesOrderId)
        && isString(value.eventType)
        && isNullableString(value.oldValue)
        && isNullableString(value.newValue)
        && isNullableString(value.reason)
        && isNullableString(value.actorReference)
        && isString(value.occurredAt)
        && isNullableString(value.requestId);
}

export function isSalesOrderAuditEventList(value: unknown): value is SalesOrderAuditEvent[] {
    return isArrayOf(value, isSalesOrderAuditEvent);
}

export function isSalesOrderList(value: unknown): value is SalesOrder[] {
    return isArrayOf(value, isSalesOrder);
}

export function isReservableStockLine(value: unknown): value is ReservableStockLine {
    return isObject(value)
        && isNumber(value.palletContentLineId)
        && isNumber(value.palletId)
        && isString(value.palletReference)
        && isNullableString(value.warehouseLocation)
        && isString(value.lotNumber)
        && isUnit(value.unit)
        && isNumber(value.quantity)
        && isNumber(value.netWeightKg)
        && isNumber(value.dispatchedQuantity)
        && isNumber(value.dispatchedNetWeightKg)
        && isNumber(value.onHandQuantity)
        && isNumber(value.onHandNetWeightKg)
        && isNumber(value.reservedQuantity)
        && isNumber(value.reservedNetWeightKg)
        && isNumber(value.availableQuantity)
        && isNumber(value.availableNetWeightKg)
        && isQualityStatus(value.qualityStatus)
        && isNumber(value.contentLineVersion);
}

export function isReservableStockList(value: unknown): value is ReservableStockLine[] {
    return isArrayOf(value, isReservableStockLine);
}

// Klantgebonden DirectTrade-bronregel die aan een orderregel kan worden toegewezen (GET …/direct-trade-supply):
// zelfde klant als de order, zelfde variant en eenheid, toezegging in een toewijsbare status. Verwachte aanvoer, geen voorraad.
export interface DirectTradeSupplyLine {
    supplyCommitmentLineId: number;
    supplyCommitmentId: number;
    customerId: number;
    customerName: string | null;
    vendorName: string | null;
    origin: string | null;
    destination: string | null;
    commitmentStatus: string;
    expectedAt: string | null;
    lotNumber: string | null;
    unit: PackageUnit;
    expectedQuantity: number;
    // Opgegeven gewicht van de HELE bronregel; nooit het gewicht van een (deel)toewijzing.
    declaredExpectedNetWeightKg: number | null;
    allocatedQuantity: number;
    remainingQuantity: number;
    allocatedToThisLineQuantity: number;
    sourceLineVersion: number;
}

export function isDirectTradeSupplyLine(value: unknown): value is DirectTradeSupplyLine {
    return isObject(value)
        && isNumber(value.supplyCommitmentLineId)
        && isNumber(value.supplyCommitmentId)
        && isNumber(value.customerId)
        && isNullableString(value.customerName)
        && isNullableString(value.vendorName)
        && isNullableString(value.origin)
        && isNullableString(value.destination)
        && isString(value.commitmentStatus)
        && isNullableString(value.expectedAt)
        && isNullableString(value.lotNumber)
        && isUnit(value.unit)
        && isNumber(value.expectedQuantity)
        && (value.declaredExpectedNetWeightKg === null || isNumber(value.declaredExpectedNetWeightKg))
        && isNumber(value.allocatedQuantity)
        && isNumber(value.remainingQuantity)
        && isNumber(value.allocatedToThisLineQuantity)
        && isNumber(value.sourceLineVersion);
}

export function isDirectTradeSupplyList(value: unknown): value is DirectTradeSupplyLine[] {
    return isArrayOf(value, isDirectTradeSupplyLine);
}

export interface DirectTradeShipmentLine {
    id: number;
    stockReservationId: number;
    salesOrderLineId: number;
    supplyCommitmentLineId: number;
    supplyCommitmentId: number;
    vendorId: number;
    // Snapshots bij verzending.
    vendorName: string;
    origin: string;
    destination: string | null;
    productName: string | null;
    productVariantName: string | null;
    quantity: number;
    unit: PackageUnit;
    netWeightKg: number;
    lotNumber: string;
    sourceLotNumber: string | null;
    lotDeviationReason: string | null;
}

export interface DirectTradeShipment {
    id: number;
    requestId: string;
    salesOrderId: number;
    customerId: number;
    customerName: string;
    shippedOn: string;
    reference: string | null;
    actorReference: string;
    recordedAt: string;
    // Altijd false: het systeem kent (nog) geen afleverbevestiging.
    deliveryRecorded: boolean;
    lines: DirectTradeShipmentLine[];
}

function isDirectTradeShipmentLine(value: unknown): value is DirectTradeShipmentLine {
    return isObject(value)
        && isNumber(value.id) && isNumber(value.stockReservationId) && isNumber(value.salesOrderLineId)
        && isNumber(value.supplyCommitmentLineId) && isNumber(value.supplyCommitmentId) && isNumber(value.vendorId)
        && isString(value.vendorName) && isString(value.origin) && isNullableString(value.destination)
        && isNullableString(value.productName) && isNullableString(value.productVariantName)
        && isNumber(value.quantity) && isUnit(value.unit) && isNumber(value.netWeightKg)
        && isString(value.lotNumber) && isNullableString(value.sourceLotNumber) && isNullableString(value.lotDeviationReason);
}

export function isDirectTradeShipment(value: unknown): value is DirectTradeShipment {
    return isObject(value)
        && isNumber(value.id) && isString(value.requestId) && isNumber(value.salesOrderId) && isNumber(value.customerId)
        && isString(value.customerName) && isString(value.shippedOn) && isNullableString(value.reference)
        && isString(value.actorReference) && isString(value.recordedAt) && value.deliveryRecorded === false
        && isArrayOf(value.lines, isDirectTradeShipmentLine);
}

export function isDirectTradeShipmentList(value: unknown): value is DirectTradeShipment[] {
    return isArrayOf(value, isDirectTradeShipment);
}

