import { isArrayOf, isNullableNumber, isNullableString, isNumber, isObject, isString } from './guards';
import { PaymentConditionSummary, isPaymentConditionSummary } from './paymentCondition';

// Contract van api/relations (backend: AgfAdmin/DTOs/RelationDtos.cs). Eén onderneming die klant, leverancier of beide is;
// rollen houden hun bestaande Customer-/Vendor-ID's.

export const ADDRESS_PURPOSES = ['Main', 'Billing', 'Delivery'] as const;
export type AddressPurpose = (typeof ADDRESS_PURPOSES)[number];
export const CHANNEL_KINDS = ['Phone', 'Email'] as const;
export type ChannelKind = (typeof CHANNEL_KINDS)[number];
export type RelationRoleFilter = 'all' | 'customer' | 'vendor';

export interface RelationListItem {
    id: number;
    searchCode: string | null;
    name: string;
    tradeName: string | null;
    customerId: number | null;
    vendorId: number | null;
    isActive: boolean;
    city: string | null;
    countryCode: string | null;
    email: string | null;
    phone: string | null;
    primaryContactName: string | null;
    vatNumber: string | null;
    version: number;
}

export interface RelationList {
    items: RelationListItem[];
    total: number;
    page: number;
    pageSize: number;
}

export interface RelationAddress {
    id: number | null;
    purpose: AddressPurpose;
    label: string | null;
    line1: string | null;
    line2: string | null;
    postalCode: string | null;
    city: string | null;
    region: string | null;
    countryCode: string | null;
    isDefault: boolean;
    isActive: boolean;
}

export interface ContactChannel {
    id: number | null;
    kind: ChannelKind;
    label: string | null;
    value: string;
}

export interface RelationContact {
    id: number | null;
    name: string;
    department: string | null;
    jobTitle: string | null;
    isPrimary: boolean;
    isActive: boolean;
    note: string | null;
    channels: ContactChannel[];
}

// Handmatige certificaatcontrole: wie wanneer heeft gekeken (beide of geen van beide), geldigheid volgens het certificaat en of
// de producent het GGN-label mag voeren. Een controle is geen garantie; de ontvangst waarschuwt bij verlopen of nooit gecontroleerd.
export interface VendorGgn {
    id: number | null;
    ggn: string;
    producerName: string | null;
    isActive: boolean;
    checkedOn: string | null;
    checkedBy: string | null;
    certificateValidUntil: string | null;
    hasGgnLabel: boolean;
}

// Afspraken die klant- en leveranciersrol gemeen hebben (betalingsconditie, betaalwijze/mandaat, leverings- en handelsvoorwaarden).
// paymentTermsDays is een alleen-lezen spiegel van de dagen van de conditie.
export interface RoleTerms {
    paymentConditionId: number | null;
    paymentCondition: PaymentConditionSummary | null;
    paymentTermsDays: number;
    paymentMethod: string | null;
    mandateReference: string | null;
    mandateSignedOn: string | null;
    deliveryTermKind: string | null;
    incoterm: string | null;
    incotermPlace: string | null;
    deliveryTermText: string | null;
    tradeConditions: string | null;
    tradeConditionsText: string | null;
}

export interface CustomerRole extends RoleTerms {
    customerId: number;
    currency: string | null;
    creditLimit: number | null;
    isInsured: boolean;
    insurer: string | null;
    insurerText: string | null;
    insuredLimit: number | null;
    insurerReference: string | null;
    insuredUntil: string | null;
}

export interface VendorRole extends RoleTerms { vendorId: number; defaultCurrency: string | null; ggns: VendorGgn[] }

export const BANK_CONFIRMATION_STATUSES = ['Unconfirmed', 'Confirmed'] as const;
export type BankConfirmationStatus = (typeof BANK_CONFIRMATION_STATUSES)[number];

// Bankrekening van de relatie. Precies één van iban / otherAccountNumber; nooit verwijderd, alleen inactief. Elke nieuwe of
// gewijzigde rekening is Unconfirmed tot iemand haar (met naam) bevestigt; confirmedAt/By zijn alleen uitvoer.
export interface RelationBankAccount {
    id: number | null;
    iban: string | null;
    otherAccountNumber: string | null;
    bic: string | null;
    accountHolder: string;
    currency: string;
    isDefault: boolean;
    isActive: boolean;
    confirmationStatus: BankConfirmationStatus;
    confirmedAt: string | null;
    confirmedBy: string | null;
}

export interface RelationBankAccountEvent {
    id: number;
    bankAccountId: number;
    kind: string;
    actorReference: string;
    occurredAt: string;
    oldIban: string | null;
    newIban: string | null;
    oldAccountHolder: string | null;
    newAccountHolder: string | null;
    oldOtherAccountNumber: string | null;
    newOtherAccountNumber: string | null;
}

// Waarschuwing op de kaart (staand) of in het antwoord op opslaan (eenmalig, e-mail). Vertaald op code, nooit op de tekst.
export interface RelationWarning { code: string; field: string | null; message: string; suggestion: string | null }

export interface RelationUsage {
    salesOrders: number;
    legacyOrders: number;
    directTradeCommitments: number;
    purchaseInvoices: number;
    supplyCommitments: number;
    defaultsInUse: string[];
}

export interface Relation {
    id: number;
    requestId: string | null;
    searchCode: string | null;
    name: string;
    tradeName: string | null;
    preferredLanguage: string | null;
    website: string | null;
    email: string | null;
    phone: string | null;
    vatNumber: string | null;
    vatVerificationStatus: string;
    vatVerifiedNumber: string | null;
    vatVerifiedAt: string | null;
    registrationNumber: string | null;
    registrationCountry: string | null;
    registrationType: string | null;
    legalForm: string | null;
    internalNote: string | null;
    isActive: boolean;
    createdAt: string;
    updatedAt: string;
    version: number;
    customer: CustomerRole | null;
    vendor: VendorRole | null;
    addresses: RelationAddress[];
    contacts: RelationContact[];
    usage: RelationUsage;
    bankAccounts: RelationBankAccount[];
    bankAccountEvents: RelationBankAccountEvent[];
    warnings: RelationWarning[];
}

// Rolafspraken zoals ze worden opgeslagen: paymentConditionId wint; null voor betaalwijze/valuta = ongewijzigd laten.
export interface SaveRoleTerms {
    paymentConditionId: number | null;
    paymentMethod: string | null;
    mandateReference: string | null;
    mandateSignedOn: string | null;
    deliveryTermKind: string | null;
    incoterm: string | null;
    incotermPlace: string | null;
    deliveryTermText: string | null;
    tradeConditions: string | null;
    tradeConditionsText: string | null;
}

export interface SaveCustomerRole extends SaveRoleTerms {
    currency: string | null;
    creditLimit: number | null;
    isInsured: boolean;
    insurer: string | null;
    insurerText: string | null;
    insuredLimit: number | null;
    insurerReference: string | null;
    insuredUntil: string | null;
}

export interface SaveVendorRole extends SaveRoleTerms { defaultCurrency: string; ggns: VendorGgn[] }

export interface SaveBankAccount {
    id: number | null;
    iban: string | null;
    otherAccountNumber: string | null;
    bic: string | null;
    accountHolder: string;
    currency: string;
    isDefault: boolean;
    isActive: boolean;
}

// POST (requestId) en PUT (expectedVersion): de kaart wordt volledig meegestuurd; null voor een rol = rol niet aanwezig/niet wijzigen.
export interface SaveRelationRequest {
    requestId?: string;
    expectedVersion?: number;
    searchCode: string | null;
    name: string;
    tradeName: string | null;
    preferredLanguage: string | null;
    website: string | null;
    email: string | null;
    phone: string | null;
    vatNumber: string | null;
    registrationNumber: string | null;
    registrationCountry: string | null;
    registrationType: string | null;
    legalForm: string | null;
    internalNote: string | null;
    isActive: boolean;
    acknowledgeSimilarNames: boolean;
    // "Wie": verplicht zodra een bankrekening wordt toegevoegd, haar IBAN/houder/nummer wijzigt of zij (in)actief wordt.
    actorReference: string | null;
    customer: SaveCustomerRole | null;
    vendor: SaveVendorRole | null;
    addresses: RelationAddress[];
    contacts: RelationContact[];
    // Altijd de volledige lijst (elke opgeslagen rekening op id); rekeningen worden nooit verwijderd.
    bankAccounts: SaveBankAccount[];
}

export interface ConfirmBankAccountRequest { expectedVersion: number; actorReference: string }

export interface RelationSettings { globalGapPortalUrl: string }

// GET api/customers/{id}/credit: openstaand bedrag uit legacy-facturen en -orders; nieuwe orders zonder prijs alleen geteld.
export interface CreditExposure {
    customerId: number;
    asOf: string;
    openInvoicesAmount: number;
    openInvoicesCount: number;
    deliveredNotInvoicedAmount: number;
    deliveredNotInvoicedCount: number;
    openOrdersAmount: number;
    openOrdersCount: number;
    totalOutstanding: number;
    unpricedSalesOrdersCount: number;
    unpricedNote: string | null;
    isComplete: boolean;
    creditLimit: number | null;
    overLimitAmount: number;
    isOverLimit: boolean;
    isInsured: boolean;
    insurer: string | null;
    insuredLimit: number | null;
    insuredUntil: string | null;
    insuranceGap: number | null;
    insuranceExpired: boolean;
    insuranceExpiresWithin30Days: boolean;
}

export interface LegacyArticleGgn { productVariantId: number; description: string; ggn: string }

export interface RelationReference {
    languages: string[];
    addressPurposes: string[];
    departmentSuggestions: string[];
    registrationTypeSuggestions: string[];
    legalFormSuggestions: string[];
    currencies: string[];
    vatVerificationStatuses: string[];
    globalGapDatabaseUrl: string;
    globalGapPortalUrl: string;
    paymentMethods: string[];
    paymentConditionBases: string[];
    deliveryTermKinds: string[];
    incoterms: string[];
    tradeConditions: string[];
    creditInsurers: string[];
    legacyArticleGgns: LegacyArticleGgn[];
}

export interface SimilarRelation { id: number; searchCode: string | null; name: string; customerId: number | null; vendorId: number | null }

const isPurpose = (value: unknown): value is AddressPurpose => isString(value) && (ADDRESS_PURPOSES as readonly string[]).includes(value);
const isKind = (value: unknown): value is ChannelKind => isString(value) && (CHANNEL_KINDS as readonly string[]).includes(value);

function isAddress(value: unknown): value is RelationAddress {
    return isObject(value) && isNullableNumber(value.id) && isPurpose(value.purpose) && isNullableString(value.label) && isNullableString(value.line1) && isNullableString(value.line2)
        && isNullableString(value.postalCode) && isNullableString(value.city) && isNullableString(value.region) && isNullableString(value.countryCode)
        && typeof value.isDefault === 'boolean' && typeof value.isActive === 'boolean';
}

function isChannel(value: unknown): value is ContactChannel {
    return isObject(value) && isNullableNumber(value.id) && isKind(value.kind) && isNullableString(value.label) && isString(value.value);
}

function isContact(value: unknown): value is RelationContact {
    return isObject(value) && isNullableNumber(value.id) && isString(value.name) && isNullableString(value.department) && isNullableString(value.jobTitle)
        && typeof value.isPrimary === 'boolean' && typeof value.isActive === 'boolean' && isNullableString(value.note) && isArrayOf(value.channels, isChannel);
}

function isGgn(value: unknown): value is VendorGgn {
    return isObject(value) && isNullableNumber(value.id) && isString(value.ggn) && isNullableString(value.producerName) && typeof value.isActive === 'boolean'
        && isNullableString(value.checkedOn) && isNullableString(value.checkedBy) && isNullableString(value.certificateValidUntil) && typeof value.hasGgnLabel === 'boolean';
}

function isRoleTerms(value: Record<string, unknown>): boolean {
    return isNullableNumber(value.paymentConditionId) && (value.paymentCondition === null || isPaymentConditionSummary(value.paymentCondition)) && isNumber(value.paymentTermsDays)
        && isNullableString(value.paymentMethod) && isNullableString(value.mandateReference) && isNullableString(value.mandateSignedOn) && isNullableString(value.deliveryTermKind)
        && isNullableString(value.incoterm) && isNullableString(value.incotermPlace) && isNullableString(value.deliveryTermText) && isNullableString(value.tradeConditions)
        && isNullableString(value.tradeConditionsText);
}

function isCustomerRole(value: unknown): value is CustomerRole {
    return isObject(value) && isNumber(value.customerId) && isRoleTerms(value) && isNullableString(value.currency) && isNullableNumber(value.creditLimit) && typeof value.isInsured === 'boolean'
        && isNullableString(value.insurer) && isNullableString(value.insurerText) && isNullableNumber(value.insuredLimit) && isNullableString(value.insurerReference) && isNullableString(value.insuredUntil);
}

function isVendorRole(value: unknown): value is VendorRole {
    return isObject(value) && isNumber(value.vendorId) && isRoleTerms(value) && isNullableString(value.defaultCurrency) && isArrayOf(value.ggns, isGgn);
}

function isBankAccount(value: unknown): value is RelationBankAccount {
    return isObject(value) && isNullableNumber(value.id) && isNullableString(value.iban) && isNullableString(value.otherAccountNumber) && isNullableString(value.bic)
        && isString(value.accountHolder) && isString(value.currency) && typeof value.isDefault === 'boolean' && typeof value.isActive === 'boolean'
        && (value.confirmationStatus === 'Unconfirmed' || value.confirmationStatus === 'Confirmed') && isNullableString(value.confirmedAt) && isNullableString(value.confirmedBy);
}

function isBankAccountEvent(value: unknown): value is RelationBankAccountEvent {
    return isObject(value) && isNumber(value.id) && isNumber(value.bankAccountId) && isString(value.kind) && isString(value.actorReference) && isString(value.occurredAt)
        && isNullableString(value.oldIban) && isNullableString(value.newIban) && isNullableString(value.oldAccountHolder) && isNullableString(value.newAccountHolder)
        && isNullableString(value.oldOtherAccountNumber) && isNullableString(value.newOtherAccountNumber);
}

function isWarning(value: unknown): value is RelationWarning {
    return isObject(value) && isString(value.code) && isNullableString(value.field) && isString(value.message) && isNullableString(value.suggestion);
}

function isUsage(value: unknown): value is RelationUsage {
    return isObject(value) && isNumber(value.salesOrders) && isNumber(value.legacyOrders) && isNumber(value.directTradeCommitments) && isNumber(value.purchaseInvoices)
        && isNumber(value.supplyCommitments) && isArrayOf(value.defaultsInUse, isString);
}

export function isRelation(value: unknown): value is Relation {
    return isObject(value) && isNumber(value.id) && isNullableString(value.requestId) && isNullableString(value.searchCode) && isString(value.name) && isNullableString(value.tradeName)
        && isNullableString(value.preferredLanguage) && isNullableString(value.website) && isNullableString(value.email) && isNullableString(value.phone) && isNullableString(value.vatNumber)
        && isString(value.vatVerificationStatus) && isNullableString(value.vatVerifiedNumber) && isNullableString(value.vatVerifiedAt)
        && isNullableString(value.registrationNumber) && isNullableString(value.registrationCountry) && isNullableString(value.registrationType) && isNullableString(value.legalForm)
        && isNullableString(value.internalNote) && typeof value.isActive === 'boolean' && isString(value.createdAt) && isString(value.updatedAt) && isNumber(value.version)
        && (value.customer === null || isCustomerRole(value.customer)) && (value.vendor === null || isVendorRole(value.vendor))
        && isArrayOf(value.addresses, isAddress) && isArrayOf(value.contacts, isContact) && isUsage(value.usage)
        && isArrayOf(value.bankAccounts, isBankAccount) && isArrayOf(value.bankAccountEvents, isBankAccountEvent) && isArrayOf(value.warnings, isWarning);
}

function isListItem(value: unknown): value is RelationListItem {
    return isObject(value) && isNumber(value.id) && isNullableString(value.searchCode) && isString(value.name) && isNullableString(value.tradeName) && isNullableNumber(value.customerId)
        && isNullableNumber(value.vendorId) && typeof value.isActive === 'boolean' && isNullableString(value.city) && isNullableString(value.countryCode) && isNullableString(value.email)
        && isNullableString(value.phone) && isNullableString(value.primaryContactName) && isNullableString(value.vatNumber) && isNumber(value.version);
}

export function isRelationList(value: unknown): value is RelationList {
    return isObject(value) && isArrayOf(value.items, isListItem) && isNumber(value.total) && isNumber(value.page) && isNumber(value.pageSize);
}

export function isRelationReference(value: unknown): value is RelationReference {
    return isObject(value) && isArrayOf(value.languages, isString) && isArrayOf(value.addressPurposes, isString) && isArrayOf(value.departmentSuggestions, isString)
        && isArrayOf(value.registrationTypeSuggestions, isString) && isArrayOf(value.legalFormSuggestions, isString) && isArrayOf(value.currencies, isString)
        && isArrayOf(value.vatVerificationStatuses, isString) && isString(value.globalGapDatabaseUrl) && isString(value.globalGapPortalUrl)
        && isArrayOf(value.paymentMethods, isString) && isArrayOf(value.paymentConditionBases, isString) && isArrayOf(value.deliveryTermKinds, isString)
        && isArrayOf(value.incoterms, isString) && isArrayOf(value.tradeConditions, isString) && isArrayOf(value.creditInsurers, isString)
        && isArrayOf(value.legacyArticleGgns, (item): item is LegacyArticleGgn => isObject(item) && isNumber(item.productVariantId) && isString(item.description) && isString(item.ggn));
}

export function isSimilarRelationList(value: unknown): value is SimilarRelation[] {
    return isArrayOf(value, (item): item is SimilarRelation => isObject(item) && isNumber(item.id) && isNullableString(item.searchCode) && isString(item.name) && isNullableNumber(item.customerId) && isNullableNumber(item.vendorId));
}

export function isRelationSettings(value: unknown): value is RelationSettings {
    return isObject(value) && isString(value.globalGapPortalUrl);
}

export function isCreditExposure(value: unknown): value is CreditExposure {
    return isObject(value) && isNumber(value.customerId) && isString(value.asOf) && isNumber(value.openInvoicesAmount) && isNumber(value.openInvoicesCount)
        && isNumber(value.deliveredNotInvoicedAmount) && isNumber(value.deliveredNotInvoicedCount) && isNumber(value.openOrdersAmount) && isNumber(value.openOrdersCount)
        && isNumber(value.totalOutstanding) && isNumber(value.unpricedSalesOrdersCount) && isNullableString(value.unpricedNote) && typeof value.isComplete === 'boolean'
        && isNullableNumber(value.creditLimit) && isNumber(value.overLimitAmount) && typeof value.isOverLimit === 'boolean' && typeof value.isInsured === 'boolean'
        && isNullableString(value.insurer) && isNullableNumber(value.insuredLimit) && isNullableString(value.insuredUntil) && isNullableNumber(value.insuranceGap)
        && typeof value.insuranceExpired === 'boolean' && typeof value.insuranceExpiresWithin30Days === 'boolean';
}
