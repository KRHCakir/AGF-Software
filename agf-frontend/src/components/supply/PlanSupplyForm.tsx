'use client';

import Link from 'next/link';
import { useMemo, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { availabilityHref, singleVariantId } from '../../lib/availabilityLinks';
import { formatDateTime, formatQuantity } from '../../lib/availability';
import { unitLabelKey } from '../../lib/units';
import { type LineWeightDraft, lineKgRows, resolveLineWeight, weightPayload } from '../../lib/lineWeight';
import { QuantityInputMode, resolvePalletInput } from '../../lib/palletInput';
import { isDateOnly, todayInAmsterdam } from '../../lib/calendarDate';
import { CHANNEL_LABEL_KEYS, MAX_SUPPLIER_REFERENCE_LENGTH, ORDER_CHANNELS, OrderChannel } from '../../lib/purchaseOrderRules';
import { newRequestId } from '../../lib/requestId';
import { ApiError, toApiError } from '../../services/apiClient';
import { SaveResult, createSupplyCommitment } from '../../services/supplyService';
import { PACKAGE_UNITS, PackageUnit, SupplyRoute } from '../../types/availability';
import { CustomerOption, ProductOption, VendorOption } from '../../types/referenceData';
import { CreateSupplyCommitmentRequest, SupplyCommitment } from '../../types/supply';
import { UnitBadge } from '../availability/QuantityCell';
import KgTotals from '../shared/KgTotals';
import { LineKgFields } from '../shared/LineKgInput';
import ProductVariantPicker from '../shared/ProductVariantPicker';
import { lineKgLabel } from '../shared/lineKgLabel';
import QuantityInput from '../shared/QuantityInput';
import { palletBreakdownLabel } from '../shared/palletBreakdownLabel';
import ErrorNotice from './ErrorNotice';
import FieldError, { fieldProps, inputClass, lineTableClass } from './FieldError';
import { FieldErrors, addError, decimalErrorKey, outcomeUnknown, palletErrorKey } from './messages';
import SuccessPanel, { primaryButtonClass, secondaryButtonClass } from './SuccessPanel';

export interface ReferenceData {
    vendors: VendorOption[];
    customers: CustomerOption[];
    products: ProductOption[];
}

interface LineDraft extends LineWeightDraft {
    key: string;
    productVariantId: string;
    lotNumber: string;
    quantity: string;
    unit: PackageUnit | '';
    // Invoer per pallet: 'pallets' rekent de hoeveelheid uit palletCount × packagesPerPallet.
    mode: QuantityInputMode;
    palletCount: string;
    packagesPerPallet: string;
    loosePackages?: string;
}

interface HeaderDraft {
    vendorId: string;
    customerId: string;
    origin: string;
    destination: string;
    expectedDate: string;
    // Inkooporder: referentie van de leverancier, besteldatum (standaard vandaag) en kanaal (optioneel).
    supplierReference: string;
    orderedOn: string;
    orderChannel: OrderChannel | '';
}

// Bevroren verzending: exact deze payload en requestId worden bij "opnieuw proberen" verstuurd.
interface PendingSubmission {
    requestId: string;
    payload: CreateSupplyCommitmentRequest;
}

const MAX_LINES = 200;
const emptyLine = (): LineDraft => ({ key: newRequestId(), productVariantId: '', lotNumber: '', quantity: '', unit: '', mode: 'packages', palletCount: '', packagesPerPallet: '' });
const emptyHeader = (): HeaderDraft => ({ vendorId: '', customerId: '', origin: '', destination: '', expectedDate: '', supplierReference: '', orderedOn: todayInAmsterdam(), orderChannel: '' });

// Aanvoer plannen voor Warehouse of DirectTrade via POST /api/supply-commitments (idempotent via requestId).
export default function PlanSupplyForm({ route, reference, onReceive }: {
    route: SupplyRoute;
    reference: ReferenceData;
    onReceive: (commitment: SupplyCommitment) => void;
}) {
    const { t, locale } = useTranslation();
    const isDirect = route === 'DirectTrade';
    const [header, setHeader] = useState<HeaderDraft>(emptyHeader);
    const [lines, setLines] = useState<LineDraft[]>(() => [emptyLine()]);
    const [pending, setPending] = useState<PendingSubmission | null>(null);
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [errors, setErrors] = useState<FieldErrors>({});
    const [result, setResult] = useState<SaveResult<SupplyCommitment> | null>(null);
    const inFlight = useRef(false);

    // Na een onbekende uitkomst of na succes is het formulier vergrendeld (geen dubbele aanvoer door een gewijzigde retry).
    const locked = submitting || result !== null || (error !== null && outcomeUnknown(error));

    // Standaard verpakkingen per pallet per variant: invoerhulp bij "Pallet"-invoer.
    const variantDefaults = useMemo(() => new Map(reference.products.flatMap((product) => product.variants.map((variant) => [variant.id, variant.defaultPackagesPerPallet] as const))), [reference.products]);
    // Netto en bruto kg per verpakking van het artikel: berekende kg in de kg-velden van de regel.
    const variants = useMemo(() => new Map(reference.products.flatMap((product) => product.variants.map((variant) => [variant.id, variant] as const))), [reference.products]);

    const edited = () => {
        // Na een definitieve weigering (of niets verstuurd) hoort een gewijzigde invoer bij een nieuw verzoek.
        setPending(null);
        setError(null);
        setErrors({});
    };

    const updateHeader = (field: keyof HeaderDraft, value: string) => {
        if (locked) return;
        setHeader((current) => ({ ...current, [field]: value }));
        edited();
    };

    const updateLine = (key: string, field: keyof Omit<LineDraft, 'key'>, value: string) => {
        if (locked) return;
        setLines((current) => current.map((line) => (line.key === key ? { ...line, [field]: value } : line)));
        edited();
    };

    // Kg-velden van een regel (tekst en "ingevuld"-vlag tegelijk).
    const updateLineWeight = (key: string, patch: LineWeightDraft) => {
        if (locked) return;
        setLines((current) => current.map((line) => (line.key === key ? { ...line, ...patch } : line)));
        edited();
    };

    // Per regel het aantal, de berekende kg en de kg die zou worden verstuurd; ook voor het totaal onder de regels.
    const { rows: lineKg, totals } = lineKgRows(lines, (variantId) => variants.get(variantId));

    const addLine = () => {
        if (locked || lines.length >= MAX_LINES) return;
        setLines((current) => [...current, emptyLine()]);
        edited();
    };

    const removeLine = (key: string) => {
        if (locked || lines.length <= 1) return;
        setLines((current) => current.filter((line) => line.key !== key));
        edited();
    };

    const validate = (): { payload: Omit<CreateSupplyCommitmentRequest, 'requestId'> | null; errors: FieldErrors } => {
        const found: FieldErrors = {};
        const vendorId = Number(header.vendorId);
        if (!header.vendorId) addError(found, 'vendorId', t('supply_error_required'));

        const customerId = Number(header.customerId);
        if (isDirect && !header.customerId) addError(found, 'customerId', t('supply_error_required_customer'));

        const origin = header.origin.trim();
        if (origin === '') addError(found, 'origin', t('supply_error_required'));
        else if (origin.length > 200) addError(found, 'origin', t('supply_error_too_long', { max: '200' }));

        const destination = header.destination.trim();
        if (isDirect && destination === '') addError(found, 'destination', t('supply_error_required_destination'));
        else if (destination.length > 200) addError(found, 'destination', t('supply_error_too_long', { max: '200' }));

        let expectedAt = '';
        if (!/^\d{4}-\d{2}-\d{2}$/.test(header.expectedDate)) {
            addError(found, 'expectedAt', t('supply_error_required_date'));
        } else {
            const local = new Date(`${header.expectedDate}T00:00:00`);
            if (Number.isNaN(local.getTime())) addError(found, 'expectedAt', t('supply_error_required_date'));
            else expectedAt = local.toISOString();
        }

        const supplierReference = header.supplierReference.trim();
        if (supplierReference.length > MAX_SUPPLIER_REFERENCE_LENGTH) addError(found, 'supplierReference', t('supply_error_too_long', { max: String(MAX_SUPPLIER_REFERENCE_LENGTH) }));
        if (header.orderedOn !== '' && !isDateOnly(header.orderedOn)) addError(found, 'orderedOn', t('supply_error_required_date'));

        if (lines.length === 0) addError(found, 'lines', t('supply_error_lines_required'));
        const seen = new Set<string>();
        const payloadLines = lines.map((line, index) => {
            const prefix = `lines[${index}]`;
            if (!line.productVariantId) addError(found, `${prefix}.productVariantId`, t('supply_error_required'));
            // Palletinvoer: het totaal volgt uit pallets × verpakkingen per pallet; de server controleert dezelfde rekensom.
            const amounts = resolvePalletInput(line);
            if (!amounts.ok) for (const error of amounts.errors) addError(found, `${prefix}.${error.field === 'quantity' ? 'expectedQuantity' : error.field}`, t(palletErrorKey(error.field, error.code)));
            if (line.unit === '') addError(found, `${prefix}.expectedUnit`, t('supply_error_required_unit'));
            // Kg optioneel: berekend uit het artikel of ingevuld; bruto nooit onder netto (zelfde regels als LineWeightRules).
            const weight = resolveLineWeight(line, amounts.ok ? amounts.value.quantity : null, variants.get(Number(line.productVariantId)));
            if (!weight.ok) for (const error of weight.errors) addError(found, `${prefix}.${error.field === 'net' ? 'expectedNetWeightKg' : 'expectedGrossWeightKg'}`, error.code === 'gross_below_net' ? t('line_kg_gross_below_net') : t(decimalErrorKey(error.code)));
            const lot = line.lotNumber.trim();
            if (lot.length > 100) addError(found, `${prefix}.lotNumber`, t('supply_error_too_long', { max: '100' }));
            if (lot !== '' && line.productVariantId) {
                const duplicateKey = `${line.productVariantId}|${lot}`;
                if (seen.has(duplicateKey)) addError(found, prefix, t('supply_error_duplicate_line'));
                seen.add(duplicateKey);
            }
            return {
                productVariantId: Number(line.productVariantId),
                expectedQuantity: amounts.ok ? amounts.value.quantity : 0,
                expectedUnit: (line.unit === '' ? 'box' : line.unit) as PackageUnit,
                expectedNetWeightKg: null,
                lotNumber: lot === '' ? null : lot,
                palletCount: amounts.ok ? amounts.value.palletCount : null,
                packagesPerPallet: amounts.ok ? amounts.value.packagesPerPallet : null,
                ...(amounts.ok && amounts.value.loosePackages !== null ? { loosePackages: amounts.value.loosePackages } : {}),
                // Bruto kg en bron alleen als er kg is: een regel zonder kg blijft dezelfde opdracht als vóór deze velden.
                ...(weight.ok ? weightPayload(weight.value, 'expectedNetWeightKg', 'expectedGrossWeightKg') : {}),
            };
        });

        if (Object.keys(found).length > 0) {
            return { payload: null, errors: found };
        }
        return {
            payload: {
                vendorId,
                customerId: isDirect ? customerId : null,
                route,
                origin,
                destination: destination === '' ? null : destination,
                expectedAt,
                ...(supplierReference === '' ? {} : { supplierReference }),
                ...(header.orderedOn === '' ? {} : { orderedOn: header.orderedOn }),
                ...(header.orderChannel === '' ? {} : { orderChannel: header.orderChannel }),
                lines: payloadLines,
            },
            errors: {},
        };
    };

    const submit = async () => {
        if (inFlight.current || result !== null) return;
        let submission = pending;
        if (submission === null) {
            const { payload, errors: found } = validate();
            setErrors(found);
            if (payload === null) return;
            const requestId = newRequestId();
            submission = { requestId, payload: { requestId, ...payload } };
            setPending(submission);
        }

        inFlight.current = true;
        setSubmitting(true);
        setError(null);
        try {
            setResult(await createSupplyCommitment(submission.payload));
        } catch (caught) {
            const apiError = toApiError(caught);
            setError(apiError);
            setErrors(apiError.fieldErrors);
        } finally {
            inFlight.current = false;
            setSubmitting(false);
        }
    };

    const startOver = () => {
        setHeader(emptyHeader());
        setLines([emptyLine()]);
        setPending(null);
        setError(null);
        setErrors({});
        setResult(null);
    };

    if (result !== null) {
        const commitment = result.value;
        const href = availabilityHref({
            route,
            focus: 'expected',
            productVariantId: singleVariantId(commitment.lines.map((line) => line.productVariantId)),
            customerId: commitment.customerId ?? undefined,
        });
        return (
            <SuccessPanel
                title={isDirect ? t('supply_plan_success_direct', { id: String(commitment.id) }) : t('supply_plan_success_warehouse', { id: String(commitment.id) })}
                note={result.replayed ? t('supply_saved_before') : undefined}
                actions={(
                    <>
                        <Link href={href} className={primaryButtonClass}>{t('supply_view_availability')}</Link>
                        <Link href={`/purchase-orders?id=${commitment.id}`} className={secondaryButtonClass}>{t('po_to_order')}</Link>
                        {!isDirect && (
                            <button type="button" className={secondaryButtonClass} onClick={() => onReceive(commitment)}>
                                {t('supply_receive_for_this')}
                            </button>
                        )}
                        <button type="button" className={secondaryButtonClass} onClick={startOver}>{t('supply_new_plan')}</button>
                    </>
                )}
            >
                {commitment.orderNumber && (
                    <p className="mb-2">
                        <span className="block text-xs font-medium uppercase tracking-wide text-green-900">{t('po_own_order_number')}</span>
                        <span className="block font-mono text-3xl font-bold text-gray-900" data-testid="supply-order-number">{commitment.orderNumber}</span>
                    </p>
                )}
                <p>
                    {commitment.supplierReference && <>{t('po_col_supplier_reference')} <span className="font-mono">{commitment.supplierReference}</span> · </>}
                    {commitment.vendorName ?? '–'} · {commitment.origin}
                    {commitment.customerName && <> · 🔒 {commitment.customerName}</>}
                    {commitment.destination && <> → {commitment.destination}</>}
                    {commitment.expectedAt && <> · {formatDateTime(commitment.expectedAt, locale)}</>}
                </p>
                <ul className="mt-2 list-disc pl-5">
                    {commitment.lines.map((line) => (
                        <li key={line.id}>
                            {line.productName} · {line.productVariantName}: {formatQuantity(line.expectedQuantity, locale)} <UnitBadge unit={line.expectedUnit} />
                            {palletBreakdownLabel(line, line.expectedUnit, t, locale) !== null && <> ({palletBreakdownLabel(line, line.expectedUnit, t, locale)})</>}
                            {line.lotNumber && <> · {t('supply_lot')} {line.lotNumber}</>}
                            {lineKgLabel({ netWeightKg: line.expectedNetWeightKg, grossWeightKg: line.expectedGrossWeightKg, weightSource: line.weightSource }, t, locale) !== null
                                && <> · {lineKgLabel({ netWeightKg: line.expectedNetWeightKg, grossWeightKg: line.expectedGrossWeightKg, weightSource: line.weightSource }, t, locale, true)}</>}
                        </li>
                    ))}
                </ul>
            </SuccessPanel>
        );
    }

    const e = (key: string) => errors[key];

    return (
        <form
            noValidate
            aria-busy={submitting}
            onSubmit={(event) => { event.preventDefault(); void submit(); }}
            className={`space-y-4 rounded-md border bg-white p-4 ${isDirect ? 'border-2 border-violet-300' : 'border-gray-300'}`}
        >
            {isDirect && (
                <p className="flex items-center gap-2 rounded border border-violet-300 bg-violet-50 px-3 py-2 text-sm font-medium text-violet-900">
                    <span aria-hidden="true">🔒</span>{t('supply_direct_banner')}
                </p>
            )}

            {error && (
                <ErrorNotice error={error} actions={outcomeUnknown(error) ? (
                    <>
                        <button type="submit" className={primaryButtonClass} disabled={submitting}>{t('supply_retry_same')}</button>
                        <button type="button" className={secondaryButtonClass} onClick={startOver}>{t('supply_discard_and_restart')}</button>
                    </>
                ) : undefined} />
            )}

            <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                <div>
                    <label htmlFor="supplierReference-input" className="text-xs font-medium text-gray-700">{t('po_supplier_reference')}</label>
                    <input {...fieldProps('supplierReference', e('supplierReference'))} value={header.supplierReference} disabled={locked} maxLength={MAX_SUPPLIER_REFERENCE_LENGTH}
                        onChange={(event) => updateHeader('supplierReference', event.target.value)} className={inputClass} />
                    <FieldError id="supplierReference" messages={e('supplierReference')} />
                </div>
                <div>
                    <label htmlFor="orderedOn-input" className="text-xs font-medium text-gray-700">{t('po_ordered_on')}</label>
                    <input {...fieldProps('orderedOn', e('orderedOn'))} type="date" value={header.orderedOn} disabled={locked}
                        onChange={(event) => updateHeader('orderedOn', event.target.value)} className={inputClass} />
                    <FieldError id="orderedOn" messages={e('orderedOn')} />
                </div>
                <fieldset className="col-span-2">
                    <legend className="text-xs font-medium text-gray-700">{t('po_channel')}</legend>
                    <div className="mt-1 flex flex-wrap gap-1">
                        {ORDER_CHANNELS.map((channel) => (
                            <button key={channel} type="button" aria-pressed={header.orderChannel === channel} disabled={locked}
                                onClick={() => updateHeader('orderChannel', header.orderChannel === channel ? '' : channel)}
                                className={`rounded border px-2 py-1 text-xs font-medium ${header.orderChannel === channel ? 'border-blue-600 bg-blue-50 text-blue-900' : 'border-gray-300 bg-white text-gray-700 hover:bg-gray-100'}`}>
                                {t(CHANNEL_LABEL_KEYS[channel])}
                            </button>
                        ))}
                    </div>
                </fieldset>
            </div>

            <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                <div>
                    <label htmlFor="vendorId-input" className="text-xs font-medium text-gray-700">{t('supply_vendor')} *</label>
                    <select {...fieldProps('vendorId', e('vendorId'))} value={header.vendorId} disabled={locked}
                        onChange={(event) => updateHeader('vendorId', event.target.value)} className={inputClass}>
                        <option value="">{t('supply_choose')}</option>
                        {reference.vendors.map((vendor) => <option key={vendor.id} value={vendor.id}>{vendor.name}</option>)}
                    </select>
                    <FieldError id="vendorId" messages={e('vendorId')} />
                </div>
                <div>
                    <label htmlFor="origin-input" className="text-xs font-medium text-gray-700">{t('supply_origin')} *</label>
                    <input {...fieldProps('origin', e('origin'))} value={header.origin} disabled={locked} maxLength={200}
                        onChange={(event) => updateHeader('origin', event.target.value)} className={inputClass} />
                    <FieldError id="origin" messages={e('origin')} />
                </div>
                <div>
                    <label htmlFor="expectedAt-input" className="text-xs font-medium text-gray-700">{t('supply_expected_date')} *</label>
                    <input {...fieldProps('expectedAt', e('expectedAt'))} type="date" value={header.expectedDate} disabled={locked}
                        onChange={(event) => updateHeader('expectedDate', event.target.value)} className={inputClass} />
                    <FieldError id="expectedAt" messages={e('expectedAt')} />
                </div>
            </div>

            {isDirect && (
                <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                    <div>
                        <label htmlFor="customerId-input" className="text-xs font-medium text-violet-900">{t('supply_customer')} *</label>
                        <select {...fieldProps('customerId', e('customerId'))} value={header.customerId} disabled={locked}
                            onChange={(event) => updateHeader('customerId', event.target.value)} className={inputClass}>
                            <option value="">{t('supply_choose')}</option>
                            {reference.customers.map((customer) => <option key={customer.id} value={customer.id}>{customer.name}</option>)}
                        </select>
                        <FieldError id="customerId" messages={e('customerId')} />
                    </div>
                    <div className="col-span-2">
                        <label htmlFor="destination-input" className="text-xs font-medium text-violet-900">{t('supply_destination')} *</label>
                        <input {...fieldProps('destination', e('destination'))} value={header.destination} disabled={locked} maxLength={200}
                            placeholder={t('supply_destination_placeholder')}
                            onChange={(event) => updateHeader('destination', event.target.value)} className={inputClass} />
                        <FieldError id="destination" messages={e('destination')} />
                    </div>
                </div>
            )}

            <fieldset className="space-y-2">
                <legend className="text-sm font-semibold text-gray-900">{t('supply_lines')}</legend>
                <FieldError id="lines" messages={e('lines')} />
                <div className="overflow-x-auto rounded border border-gray-200">
                    <table className={lineTableClass}>
                        <thead className="bg-gray-50 text-left text-xs font-semibold uppercase tracking-wide text-gray-600">
                            <tr>
                                <th scope="col" className="px-2 py-1.5">{t('supply_variant')} *</th>
                                <th scope="col" className="px-2 py-1.5">{t('supply_lot_optional')}</th>
                                <th scope="col" className="px-2 py-1.5">{t('supply_quantity')} *</th>
                                <th scope="col" className="px-2 py-1.5">{t('supply_unit')} *</th>
                                <th scope="col" className="px-2 py-1.5"><span className="sr-only">{t('supply_remove_line')}</span></th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-100">
                            {lines.map((line, index) => {
                                const prefix = `lines[${index}]`;
                                const unitText = line.unit === '' ? t('supply_unit').toLowerCase() : t(unitLabelKey(line.unit)).toLowerCase();
                                return (
                                    <tr key={line.key} className="align-top">
                                        <td className="w-80 px-2 py-1.5">
                                            <label htmlFor={`${prefix}.productVariantId-input`} className="sr-only">{t('supply_variant')} {index + 1}</label>
                                            <ProductVariantPicker id={`${prefix}.productVariantId`} ariaLabel={`${t('supply_variant')} ${index + 1}`} products={reference.products}
                                                value={line.productVariantId} disabled={locked} invalid={(e(`${prefix}.productVariantId`) ?? []).length > 0}
                                                describedBy={(e(`${prefix}.productVariantId`) ?? []).length > 0 ? `${prefix}.productVariantId` : undefined}
                                                onChange={(variantId) => updateLine(line.key, 'productVariantId', variantId)} />
                                            <FieldError id={`${prefix}.productVariantId`} messages={e(`${prefix}.productVariantId`)} />
                                            <FieldError id={prefix} messages={e(prefix)} />
                                        </td>
                                        <td className="px-2 py-1.5">
                                            <label htmlFor={`${prefix}.lotNumber-input`} className="sr-only">{t('supply_lot_optional')} {index + 1}</label>
                                            <input {...fieldProps(`${prefix}.lotNumber`, e(`${prefix}.lotNumber`))} value={line.lotNumber} disabled={locked} maxLength={100}
                                                onChange={(event) => updateLine(line.key, 'lotNumber', event.target.value)} className={inputClass} />
                                            <FieldError id={`${prefix}.lotNumber`} messages={e(`${prefix}.lotNumber`)} />
                                        </td>
                                        <td className="w-72 px-2 py-1.5">
                                            <QuantityInput prefix={prefix} index={index} line={line} locked={locked} errors={e} quantityField="expectedQuantity"
                                                defaultPackagesPerPallet={variantDefaults.get(Number(line.productVariantId)) ?? null}
                                                onChange={(field, value) => updateLine(line.key, field, value)} />
                                            <LineKgFields prefix={prefix} index={index} line={line} row={lineKg[index]} netKey="expectedNetWeightKg" grossKey="expectedGrossWeightKg" unit={unitText}
                                                locked={locked} errors={e} onChange={(patch) => updateLineWeight(line.key, patch)} />
                                        </td>
                                        <td className="w-32 px-2 py-1.5">
                                            <label htmlFor={`${prefix}.expectedUnit-input`} className="sr-only">{t('supply_unit')} {index + 1}</label>
                                            <select {...fieldProps(`${prefix}.expectedUnit`, e(`${prefix}.expectedUnit`))} value={line.unit} disabled={locked}
                                                onChange={(event) => updateLine(line.key, 'unit', event.target.value)} className={inputClass}>
                                                <option value="">{t('supply_choose')}</option>
                                                {PACKAGE_UNITS.map((unit) => <option key={unit} value={unit}>{t(unitLabelKey(unit))}</option>)}
                                            </select>
                                            <FieldError id={`${prefix}.expectedUnit`} messages={e(`${prefix}.expectedUnit`)} />
                                        </td>
                                        <td className="px-2 py-1.5">
                                            <button type="button" onClick={() => removeLine(line.key)} disabled={locked || lines.length <= 1}
                                                className={secondaryButtonClass} aria-label={t('supply_remove_line_n', { n: String(index + 1) })}>✕</button>
                                        </td>
                                    </tr>
                                );
                            })}
                        </tbody>
                    </table>
                </div>
                <KgTotals totals={totals} lineCount={lines.length} testId="supply-kg-totals" />
                <button type="button" onClick={addLine} disabled={locked || lines.length >= MAX_LINES} className={secondaryButtonClass}>
                    + {t('supply_add_line')}
                </button>
            </fieldset>

            <div className="flex items-center gap-3 border-t border-gray-200 pt-3">
                <button type="submit" className={primaryButtonClass} disabled={locked}>
                    {submitting ? t('supply_saving') : isDirect ? t('supply_save_plan_direct') : t('supply_save_plan_warehouse')}
                </button>
            </div>
        </form>
    );
}
