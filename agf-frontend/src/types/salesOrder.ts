import { isWeightMethod, type WeightMethod } from '../lib/weight';
import { PACKAGE_UNITS, PackageUnit, QualityStatus } from './availability';
import { hasConsistentDeliveryState } from '../lib/deliveryState';
import { isArrayOf, isNullableString, isNumber, isObject, isString, isNullableNumber } from './guards';
import { isQualityStatus } from './inspection';

// Exact het contract van /api/sales-orders en /api/stock-reservations (backend: AgfAdmin/DTOs/SalesOrderDtos.cs).
// Getallen zijn decimals uit de backend; de frontend rekent er niet mee, behalve vergelijken en aftrekken voor weergave.

export type ReservationStatus = 'Active' | 'Released' | 'Consumed' | 'Corrected';

export const RESERVATION_STATUSES: readonly ReservationStatus[] = ['Active', 'Released', 'Consumed', 'Corrected'];

export interface CreateSalesOrderLineRequest {
    productVariantId: number;
    quantity: number;
    unit: PackageUnit;
    // Optioneel, beide of geen: quantity = palletCount × packagesPerPallet; quantity blijft de bron voor alle controles.
    palletCount: number | null;
    packagesPerPallet: number | null;
}

export interface CreateSalesOrderRequest {
    requestId: string;
    customerId: number;
    customerReference: string | null;
    // Interne opmerking bij deze order: alleen intern zichtbaar, telt mee in de replayvergelijking.
    internalNote: string | null;
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
    // GGN van de verzonden partij (uit de inhoudsregel; null = niet vastgelegd).
    ggn: string | null;
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
    // De actieve afleverbevestiging (zonder intrekking), of null.
    delivery: DeliveryConfirmation | null;
    // Alle bevestigingen met hun intrekking, op volgnummer.
    deliveryHistory: DeliveryConfirmation[];
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
    // Alleen bij een teruggenomen DirectTrade-verzending (Corrected); verzenddatum, kg en partij hierboven blijven historie.
    directTradeCorrectionId: number | null;
    // Alleen bij een Consumed reservering waarvan de verzending als afgeleverd is bevestigd.
    deliveryConfirmationId: number | null;
    deliveredOn: string | null;
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
    // Oorspronkelijke palletinvoer ("9 pallets van 120 dozen"); null bij gewone invoer.
    palletCount: number | null;
    packagesPerPallet: number | null;
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
    // Bevestigd afgeleverd in de eigen eenheid van de regel; afgeleverd = bevestigd afgeleverd == gevraagd.
    deliveredQuantity: number;
    delivered: boolean;
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
    // Interne opmerking (alleen interne werkplekken); ontbreekt in oudere antwoorden en is dan null.
    internalNote: string | null;
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
    // Afgeleid: elke regel volledig verzonden én bevestigd afgeleverd (per regel en eenheid).
    delivered: boolean;
    lines: SalesOrderLine[];
}

// Inhoudsregel die voor een orderregel te reserveren is: zelfde variant + eenheid, Approved, Received + Warehouse, route Warehouse.
export interface ReservableStockLine {
    palletContentLineId: number;
    palletId: number;
    palletReference: string;
    warehouseLocation: string | null;
    lotNumber: string;
    // GGN van de partij zoals bij ontvangst vastgelegd (null = niet vastgelegd).
    ggn: string | null;
    unit: PackageUnit;
    // Historische ontvangst; voorraad = ontvangst - afgeboekt; beschikbaar = voorraad - Active gereserveerd.
    quantity: number;
    netWeightKg: number;
    // Calculated: een reservering moet exact quantity × netKgPerPackage kg zijn; de UI leidt de kg dan af.
    weightMethod: WeightMethod;
    netKgPerPackage: number | null;
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
        && (value.directTradeCorrectionId === null || isNumber(value.directTradeCorrectionId))
        && (value.deliveryConfirmationId === null || isNumber(value.deliveryConfirmationId))
        && isNullableString(value.deliveredOn)
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
        && isNullableNumber(value.palletCount)
        && isNullableNumber(value.packagesPerPallet)
        && isNumber(value.reservedQuantity)
        && isNumber(value.reservedNetWeightKg)
        && isNumber(value.allocatedQuantity)
        && isNumber(value.dispatchedQuantity)
        && isNumber(value.directTradeShippedQuantity)
        && isNumber(value.deliveredQuantity)
        && typeof value.delivered === 'boolean'
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
        && (value.internalNote === undefined || isNullableString(value.internalNote))
        && isString(value.requestedDeliveryDate)
        && isString(value.actorReference)
        && isString(value.createdAt)
        && isNumber(value.version)
        && isNullableString(value.cancelledAt)
        && isNullableString(value.cancelReason)
        && isNullableString(value.cancelledBy)
        && typeof value.fulfilled === 'boolean'
        && typeof value.delivered === 'boolean'
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
        && isNullableString(value.ggn)
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
        && (value.delivery === null || isDeliveryConfirmation(value.delivery))
        && isArrayOf(value.deliveryHistory, isDeliveryConfirmation)
        && hasConsistentDeliveryState(value.delivery as DeliveryConfirmation | null, value.deliveryHistory as DeliveryConfirmation[])
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
        && isNullableString(value.ggn)
        && isUnit(value.unit)
        && isNumber(value.quantity)
        && isNumber(value.netWeightKg)
        && isWeightMethod(value.weightMethod)
        && (value.netKgPerPackage === null || isNumber(value.netKgPerPackage))
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
    // Waar zolang er een actieve afleverbevestiging is (zie delivery).
    deliveryRecorded: boolean;
    delivery: DeliveryConfirmation | null;
    deliveryHistory: DeliveryConfirmation[];
    // Gevuld zodra de verzending administratief is teruggenomen; de verzending zelf blijft ongewijzigd.
    correction: DirectTradeShipmentCorrection | null;
    lines: DirectTradeShipmentLine[];
}

// Terugnemen van een volledige DirectTrade-verzending die niet heeft plaatsgevonden: alle toewijzingen met hun verwachte versies.
export interface CorrectDirectTradeShipmentRequest {
    requestId: string;
    expectedOrderVersion: number;
    actorReference: string;
    reason: string;
    shipmentDidNotTakePlace: true;
    lines: CreateSalesDispatchLineRequest[];
}

export interface DirectTradeShipmentCorrectionLine {
    id: number;
    directTradeShipmentLineId: number;
    stockReservationId: number;
    supplyCommitmentLineId: number;
    salesOrderLineId: number;
    quantity: number;
    unit: PackageUnit;
    netWeightKg: number;
}

export interface DirectTradeShipmentCorrection {
    id: number;
    requestId: string;
    directTradeShipmentId: number;
    salesOrderId: number;
    reason: string;
    actorReference: string;
    shipmentDidNotTakePlaceConfirmed: boolean;
    // Taalonafhankelijke code en vaste (Nederlandse) verklaringstekst zoals vastgelegd.
    confirmationCode: string;
    confirmationStatement: string;
    correctedAt: string;
    lines: DirectTradeShipmentCorrectionLine[];
}

function isDirectTradeShipmentCorrectionLine(value: unknown): value is DirectTradeShipmentCorrectionLine {
    return isObject(value) && isNumber(value.id) && isNumber(value.directTradeShipmentLineId) && isNumber(value.stockReservationId)
        && isNumber(value.supplyCommitmentLineId) && isNumber(value.salesOrderLineId) && isNumber(value.quantity) && isUnit(value.unit) && isNumber(value.netWeightKg);
}

export function isDirectTradeShipmentCorrection(value: unknown): value is DirectTradeShipmentCorrection {
    return isObject(value) && isNumber(value.id) && isString(value.requestId) && isNumber(value.directTradeShipmentId) && isNumber(value.salesOrderId)
        && isString(value.reason) && isString(value.actorReference) && value.shipmentDidNotTakePlaceConfirmed === true
        && isString(value.confirmationCode) && isString(value.confirmationStatement) && isString(value.correctedAt)
        && isArrayOf(value.lines, isDirectTradeShipmentCorrectionLine);
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
        && isString(value.actorReference) && isString(value.recordedAt) && typeof value.deliveryRecorded === 'boolean'
        && (value.delivery === null || isDeliveryConfirmation(value.delivery))
        && isArrayOf(value.deliveryHistory, isDeliveryConfirmation)
        && hasConsistentDeliveryState(value.delivery as DeliveryConfirmation | null, value.deliveryHistory as DeliveryConfirmation[])
        && value.deliveryRecorded === (value.delivery !== null)
        && (value.correction === null || isDirectTradeShipmentCorrection(value.correction))
        && isArrayOf(value.lines, isDirectTradeShipmentLine);
}

export function isDirectTradeShipmentList(value: unknown): value is DirectTradeShipment[] {
    return isArrayOf(value, isDirectTradeShipment);
}

// Afleverbevestiging van één volledige verzending (magazijn of DirectTrade).
export type ShipmentKind = 'Warehouse' | 'DirectTrade';

export interface ConfirmDeliveryRequest {
    requestId: string;
    expectedOrderVersion: number;
    actorReference: string;
    // Kalenderdatum yyyy-MM-dd: niet vóór de verzenddatum en niet na vandaag (Europe/Amsterdam).
    deliveredOn: string;
    receiptReference: string | null;
    deliveredInFull: true;
}

export interface DeliveryConfirmation {
    id: number;
    requestId: string;
    shipmentKind: ShipmentKind;
    salesDispatchId: number | null;
    directTradeShipmentId: number | null;
    salesOrderId: number;
    deliveredOn: string;
    receiptReference: string | null;
    actorReference: string;
    deliveredInFullConfirmed: boolean;
    confirmationCode: string;
    confirmationStatement: string;
    recordedAt: string;
    sequence: number;
    // Gevuld als deze bevestiging is ingetrokken; dan telt ze nergens meer mee (zie lib/deliveryState).
    retraction: DeliveryRetraction | null;
}

export interface RetractDeliveryRequest {
    requestId: string;
    expectedOrderVersion: number;
    actorReference: string;
    reason: string;
    recordedInError: true;
}

export interface DeliveryRetraction {
    id: number;
    requestId: string;
    shipmentDeliveryConfirmationId: number;
    shipmentKind: ShipmentKind;
    salesDispatchId: number | null;
    directTradeShipmentId: number | null;
    salesOrderId: number;
    reason: string;
    actorReference: string;
    recordedInErrorConfirmed: boolean;
    confirmationCode: string;
    confirmationStatement: string;
    recordedAt: string;
}

export function isDeliveryRetraction(value: unknown): value is DeliveryRetraction {
    return isObject(value) && isNumber(value.id) && isString(value.requestId) && isNumber(value.shipmentDeliveryConfirmationId)
        && (value.shipmentKind === 'Warehouse' || value.shipmentKind === 'DirectTrade')
        && (value.salesDispatchId === null || isNumber(value.salesDispatchId)) && (value.directTradeShipmentId === null || isNumber(value.directTradeShipmentId))
        && isNumber(value.salesOrderId) && isString(value.reason) && isString(value.actorReference) && value.recordedInErrorConfirmed === true
        && isString(value.confirmationCode) && isString(value.confirmationStatement) && isString(value.recordedAt);
}

export function isDeliveryConfirmation(value: unknown): value is DeliveryConfirmation {
    return isObject(value) && isNumber(value.id) && isString(value.requestId)
        && (value.shipmentKind === 'Warehouse' || value.shipmentKind === 'DirectTrade')
        && (value.salesDispatchId === null || isNumber(value.salesDispatchId)) && (value.directTradeShipmentId === null || isNumber(value.directTradeShipmentId))
        && isNumber(value.salesOrderId) && isString(value.deliveredOn) && isNullableString(value.receiptReference) && isString(value.actorReference)
        && value.deliveredInFullConfirmed === true && isString(value.confirmationCode) && isString(value.confirmationStatement) && isString(value.recordedAt)
        && isNumber(value.sequence) && value.sequence >= 1
        && (value.retraction === null || (isDeliveryRetraction(value.retraction) && value.retraction.shipmentDeliveryConfirmationId === value.id));
}

