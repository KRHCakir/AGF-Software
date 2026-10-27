'use client';

import { useMemo, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { formatCalendarDate, isoToDateInput } from '../../lib/calendarDate';
import { formatDecimalInput } from '../../lib/decimal';
import { CREATE_CUSTOMER_KEY, CREATE_ORDER_KEY, CreateCustomerCommand, CreateOrderCommand, withAttempt } from '../../lib/pendingCommands';
import { newRequestId } from '../../lib/requestId';
import { unitLabelKey } from '../../lib/units';
import { MAX_ORDER_LINES, OrderDraft, OrderLineDraft, buildOrderPayload } from '../../lib/salesOrderRules';
import { ApiError, toApiError } from '../../services/apiClient';
import { createSalesOrder } from '../../services/salesOrderService';
import { SaveResult } from '../../services/supplyService';
import { PACKAGE_UNITS } from '../../types/availability';
import { CreatedCustomer, CustomerOption, ProductOption } from '../../types/referenceData';
import { SalesOrder } from '../../types/salesOrder';
import { UnitBadge } from '../availability/QuantityCell';
import ProductVariantPicker from '../shared/ProductVariantPicker';
import QuantityInput from '../shared/QuantityInput';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { fieldProps, inputClass, lineTableClass } from '../supply/FieldError';
import { FieldErrors, outcomeUnknown } from '../supply/messages';
import SuccessPanel, { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import NewCustomerPanel from './NewCustomerPanel';
import { PendingNotice } from './PendingCommandsBanner';
import { usePendingCommand, usePendingCommands } from './PendingCommandsContext';
import { ruleErrorsToFieldErrors } from './ruleMessages';

const emptyLine = (): OrderLineDraft => ({ key: newRequestId(), productVariantId: '', quantity: '', unit: '', mode: 'packages', palletCount: '', packagesPerPallet: '' });
const emptyDraft = (): OrderDraft => ({ customerId: '', customerReference: '', internalNote: '', requestedDeliveryDate: '', lines: [emptyLine()] });

// Een onafgerond commando wordt als (vergrendeld) concept getoond; verstuurd wordt altijd de bevroren payload zelf.
const draftFromCommand = (command: CreateOrderCommand): OrderDraft => ({
    customerId: String(command.payload.customerId),
    customerReference: command.payload.customerReference ?? '',
    internalNote: command.payload.internalNote ?? '',
    requestedDeliveryDate: isoToDateInput(command.payload.requestedDeliveryDate) ?? '',
    lines: command.payload.lines.map((line) => ({
        key: newRequestId(), productVariantId: String(line.productVariantId), quantity: formatDecimalInput(line.quantity), unit: line.unit,
        mode: line.palletCount !== null ? 'pallets' : 'packages', palletCount: line.palletCount === null ? '' : formatDecimalInput(line.palletCount), packagesPerPallet: line.packagesPerPallet === null ? '' : formatDecimalInput(line.packagesPerPallet),
        loosePackages: line.loosePackages === undefined ? '' : formatDecimalInput(line.loosePackages),
    })),
});

// Nieuwe verkooporder via POST /api/sales-orders. Bestaande klant, leverdatum en expliciete eenheid per regel;
// geen prijzen, btw of facturen. De payload en requestId worden bij versturen bevroren en boven dit formulier bewaard:
// bij een onbekende uitkomst is alleen exact dezelfde herhaling mogelijk, ook na weg- en terugnavigeren.
export default function CreateSalesOrderForm({ actor, customers: knownCustomers, products, onSaved, onOpen, onCancel, onCustomerCreated }: {
    actor: string;
    customers: CustomerOption[];
    products: ProductOption[];
    onSaved: (order: SalesOrder) => void;
    onOpen: (orderId: number) => void;
    onCancel: () => void;
    // Na een nieuwe klant: de pagina ververst de klantenlijst op de achtergrond zonder dit formulier te hermonteren.
    onCustomerCreated?: (customer: CreatedCustomer) => void;
}) {
    const { t, locale } = useTranslation();
    const [pending, setPending, clearPending] = usePendingCommand<CreateOrderCommand>(CREATE_ORDER_KEY);
    const { commands } = usePendingCommands();
    // Nieuwe klant: paneel naast de klantkeuze. Een onafgeronde klantopdracht opent het paneel altijd (ook na sluiten,
    // navigeren of verversen) en kan niet door een tweede worden vervangen. Zojuist aangemaakte klanten staan direct in de keuze.
    const pendingCustomer = (commands[CREATE_CUSTOMER_KEY] as CreateCustomerCommand | undefined) ?? null;
    const [customerPanelOpen, setCustomerPanelOpen] = useState(false);
    const [createdCustomers, setCreatedCustomers] = useState<CustomerOption[]>([]);
    const [customerNotice, setCustomerNotice] = useState<string | null>(null);
    const showCustomerPanel = customerPanelOpen || pendingCustomer !== null;
    const customers = useMemo(() => {
        const extra = createdCustomers.filter((created) => !knownCustomers.some((known) => known.id === created.id));
        return extra.length === 0 ? knownCustomers : [...knownCustomers, ...extra].sort((a, b) => a.name.localeCompare(b.name));
    }, [knownCustomers, createdCustomers]);
    const [draft, setDraft] = useState<OrderDraft>(() => (pending === null ? emptyDraft() : draftFromCommand(pending)));
    const [customerFilter, setCustomerFilter] = useState('');
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [errors, setErrors] = useState<FieldErrors>({});
    const [result, setResult] = useState<SaveResult<SalesOrder> | null>(null);
    const inFlight = useRef(false);

    // Vergrendeld zolang er een onafgerond commando is: nooit een gewijzigde retry, nooit een tweede order ernaast.
    const locked = submitting || result !== null || pending !== null;

    const filteredCustomers = useMemo(() => {
        const term = customerFilter.trim().toLowerCase();
        // Inactieve relaties zijn niet kiesbaar voor een nieuwe order (de server weigert ze ook); een al gekozen klant blijft staan.
        const selectable = customers.filter((customer) => customer.isActive || String(customer.id) === draft.customerId);
        return term === '' ? selectable : selectable.filter((customer) => customer.name.toLowerCase().includes(term) || (customer.searchCode !== null && customer.searchCode.toLowerCase().includes(term)));
    }, [customers, customerFilter, draft.customerId]);

    // Standaard verpakkingen per pallet per variant: invoerhulp bij "Pallet"-invoer.
    const variantDefaults = useMemo(() => new Map(products.flatMap((product) => product.variants.map((variant) => [variant.id, variant.defaultPackagesPerPallet] as const))), [products]);
    // Netto en bruto kg per verpakking: alleen voor de rekenregel onder het aantal.
    const variants = useMemo(() => new Map(products.flatMap((product) => product.variants.map((variant) => [variant.id, variant] as const))), [products]);
    // Verpakkingseenheid uit de artikelstamgegevens: vult alleen een nog lege eenheid voor; een gekozen eenheid blijft staan.
    const variantUnits = useMemo(() => new Map(products.flatMap((product) => product.variants.map((variant) => [variant.id, variant.packagingUnit] as const))), [products]);

    const edited = () => {
        setError(null);
        setErrors({});
    };

    const updateHeader = (field: 'customerId' | 'customerReference' | 'internalNote' | 'requestedDeliveryDate', value: string) => {
        if (locked) return;
        setDraft((current) => ({ ...current, [field]: value }));
        edited();
    };

    const updateLine = (key: string, field: 'productVariantId' | 'quantity' | 'unit' | 'mode' | 'palletCount' | 'packagesPerPallet' | 'loosePackages', value: string) => {
        if (locked) return;
        setDraft((current) => ({ ...current, lines: current.lines.map((line) => (line.key === key ? { ...line, [field]: value } : line)) }));
        edited();
    };

    const addLine = () => {
        if (locked || draft.lines.length >= MAX_ORDER_LINES) return;
        setDraft((current) => ({ ...current, lines: [...current.lines, emptyLine()] }));
        edited();
    };

    const removeLine = (key: string) => {
        if (locked || draft.lines.length <= 1) return;
        setDraft((current) => ({ ...current, lines: current.lines.filter((line) => line.key !== key) }));
        edited();
    };

    const submit = async () => {
        if (inFlight.current || result !== null) return;
        let command = pending;
        if (command === null) {
            const built = buildOrderPayload(draft, actor);
            if (!built.ok) {
                setErrors(ruleErrorsToFieldErrors(built.errors, t));
                return;
            }
            setErrors({});
            const requestId = newRequestId();
            command = {
                kind: 'create-order',
                key: CREATE_ORDER_KEY,
                requestId,
                payload: { requestId, ...built.value },
                display: { customerName: customers.find((customer) => customer.id === built.value.customerId)?.name ?? String(built.value.customerId) },
                attempts: 0,
                lastError: null,
                frozenAt: new Date().toISOString(),
            };
            setPending(command);
        }

        inFlight.current = true;
        setSubmitting(true);
        setError(null);
        try {
            const saved = await createSalesOrder(command.payload);
            clearPending();
            setResult(saved);
            onSaved(saved.value);
        } catch (caught) {
            const apiError = toApiError(caught);
            if (outcomeUnknown(apiError)) {
                // Uitkomst onbekend: het commando blijft bewaard; alleen exact dezelfde herhaling is mogelijk.
                setPending(withAttempt(command, apiError.message) as CreateOrderCommand);
            } else {
                // Definitief antwoord van de server (400/404/409): er is niets opgeslagen; de invoer mag weer wijzigen.
                clearPending();
            }
            setError(apiError);
            setErrors(apiError.fieldErrors);
        } finally {
            inFlight.current = false;
            setSubmitting(false);
        }
    };

    const onCustomerSaved = (customer: CreatedCustomer, replayed: boolean) => {
        setCreatedCustomers((current) => (current.some((c) => c.id === customer.id) ? current : [...current, { id: customer.id, name: customer.name, searchCode: null, isActive: true }]));
        // Alleen de klantkeuze verandert; regels, datum en referentie van het concept blijven staan.
        setDraft((current) => ({ ...current, customerId: String(customer.id) }));
        setCustomerFilter('');
        setCustomerPanelOpen(false);
        setCustomerNotice(`${t('sales_customer_created', { name: customer.name })}${replayed ? ` ${t('supply_saved_before')}` : ''}`);
        edited();
        onCustomerCreated?.(customer);
    };

    const chooseExisting = (customerId: number) => {
        setDraft((current) => ({ ...current, customerId: String(customerId) }));
        setCustomerFilter('');
        setCustomerPanelOpen(false);
        setCustomerNotice(null);
        edited();
    };

    const startOver = () => {
        setDraft(emptyDraft());
        setError(null);
        setErrors({});
        setResult(null);
    };

    const e = (key: string) => errors[key];

    const euro = (value: number) => new Intl.NumberFormat(locale === 'nl' ? 'nl-NL' : 'en-GB', { style: 'currency', currency: 'EUR' }).format(value);

    if (result !== null) {
        const order = result.value;
        return (
            <SuccessPanel
                title={t('sales_create_success', { id: String(order.id), customer: order.customerName ?? '' })}
                note={result.replayed ? t('supply_saved_before') : undefined}
                actions={(
                    <>
                        <button type="button" className={primaryButtonClass} onClick={() => onOpen(order.id)}>{t('sales_reserve_for_this_order')}</button>
                        <button type="button" className={secondaryButtonClass} onClick={startOver}>{t('sales_another_order')}</button>
                    </>
                )}
            >
                <p>
                    {t('sales_delivery_on', { date: formatCalendarDate(order.requestedDeliveryDate, locale) })}
                    {order.customerReference && <> · {t('sales_reference_short')} {order.customerReference}</>}
                </p>
                {/* Kredietlimiet overschreden: waarschuwing met limiet, openstaand en verschil; de order is gewoon vastgelegd. */}
                {order.creditWarning && (
                    <div role="alert" className="mt-2 rounded border border-amber-400 bg-amber-50 px-3 py-2 text-amber-950" data-testid="sales-credit-warning">
                        <p className="font-medium">⚠ {t('sales_credit_warning_title')}</p>
                        <p className="mt-1">{t('sales_credit_warning_amounts', { limit: euro(order.creditWarning.creditLimit), outstanding: euro(order.creditWarning.outstanding), difference: euro(order.creditWarning.difference) })}</p>
                        {!order.creditWarning.isComplete && order.creditWarning.unpricedSalesOrdersCount > 0 && (
                            <p className="mt-1 text-xs">{t(order.creditWarning.unpricedSalesOrdersCount === 1 ? 'relations_credit_unpriced_one' : 'relations_credit_unpriced', { count: String(order.creditWarning.unpricedSalesOrdersCount) })}</p>
                        )}
                        <p className="mt-1 text-xs">{t('sales_credit_warning_hint')}</p>
                    </div>
                )}
                <ul className="mt-2 list-disc pl-5">
                    {order.lines.map((line) => (
                        <li key={line.id}>{line.productName} · {line.productVariantName}: {formatQuantity(line.quantity, locale)} <UnitBadge unit={line.unit} /></li>
                    ))}
                </ul>
            </SuccessPanel>
        );
    }

    return (
        <form noValidate aria-busy={submitting} onSubmit={(event) => { event.preventDefault(); void submit(); }}
            className="space-y-4 rounded-md border border-gray-300 bg-white p-4">
            <div className="flex items-start justify-between gap-4">
                <div>
                    <h2 className="text-lg font-semibold text-gray-900">{t('sales_new_order')}</h2>
                    <p className="text-xs text-gray-600">{t('sales_new_order_hint')}</p>
                </div>
                <button type="button" className={secondaryButtonClass} onClick={onCancel} disabled={submitting}>{t('sales_close_form')}</button>
            </div>

            {pending !== null && (
                <PendingNotice command={pending} submitting={submitting}>
                    <button type="submit" className={primaryButtonClass}>{t('supply_retry_same')}</button>
                </PendingNotice>
            )}
            {error && pending === null && <ErrorNotice error={error} />}
            <FieldError id="actorReference" messages={e('actorReference')} />

            <div className="grid grid-cols-4 gap-3">
                <div className="col-span-2">
                    <div className="flex items-center justify-between gap-2">
                        <label htmlFor="customerId-input" className="text-xs font-medium text-gray-700">{t('sales_customer')} *</label>
                        <button type="button" className={`${secondaryButtonClass} px-2 py-1 text-xs`} disabled={locked && pendingCustomer === null}
                            onClick={() => { setCustomerNotice(null); setCustomerPanelOpen(true); }} aria-expanded={showCustomerPanel}
                            title={locked && pendingCustomer === null ? t('sales_customer_blocked_by_order') : undefined}>
                            + {t('sales_new_customer')}
                        </button>
                    </div>
                    <input type="search" value={customerFilter} onChange={(event) => setCustomerFilter(event.target.value)} disabled={locked}
                        placeholder={t('sales_customer_filter_placeholder')} aria-label={t('sales_customer_filter')} className={inputClass} />
                    <select {...fieldProps('customerId', e('customerId'))} value={draft.customerId} disabled={locked}
                        onChange={(event) => updateHeader('customerId', event.target.value)} className={inputClass}>
                        <option value="">{t('supply_choose')}</option>
                        {draft.customerId && !filteredCustomers.some((customer) => String(customer.id) === draft.customerId) && (
                            <option value={draft.customerId}>{customers.find((customer) => String(customer.id) === draft.customerId)?.name ?? draft.customerId}</option>
                        )}
                        {filteredCustomers.map((customer) => <option key={customer.id} value={customer.id}>{customer.name}</option>)}
                    </select>
                    <FieldError id="customerId" messages={e('customerId')} />
                    {customerNotice && <p role="status" className="mt-1 rounded border border-green-300 bg-green-50 px-2 py-1 text-xs text-green-900">{customerNotice}</p>}
                    {showCustomerPanel && (
                        <div className="mt-2">
                            <NewCustomerPanel customers={customers} onCreated={onCustomerSaved} onChooseExisting={chooseExisting} onClose={() => setCustomerPanelOpen(false)} />
                        </div>
                    )}
                </div>
                <div>
                    <label htmlFor="requestedDeliveryDate-input" className="text-xs font-medium text-gray-700">{t('sales_delivery_date')} *</label>
                    <input {...fieldProps('requestedDeliveryDate', e('requestedDeliveryDate'))} type="date" value={draft.requestedDeliveryDate} disabled={locked}
                        onChange={(event) => updateHeader('requestedDeliveryDate', event.target.value)} className={inputClass} />
                    <FieldError id="requestedDeliveryDate" messages={e('requestedDeliveryDate')} />
                </div>
                <div>
                    <label htmlFor="customerReference-input" className="text-xs font-medium text-gray-700">{t('sales_customer_reference')}</label>
                    <input {...fieldProps('customerReference', e('customerReference'))} value={draft.customerReference} disabled={locked} maxLength={100}
                        placeholder={t('sales_customer_reference_placeholder')}
                        onChange={(event) => updateHeader('customerReference', event.target.value)} className={inputClass} />
                    <FieldError id="customerReference" messages={e('customerReference')} />
                </div>
                <div className="col-span-4">
                    <label htmlFor="internalNote-input" className="text-xs font-medium text-gray-700">{t('internal_note_label')}</label>
                    <input {...fieldProps('internalNote', e('internalNote'))} value={draft.internalNote} disabled={locked} maxLength={1000} placeholder={t('internal_note_placeholder')}
                        onChange={(event) => updateHeader('internalNote', event.target.value)} className={inputClass} />
                    <span className="block text-xs text-gray-500">{t('internal_note_hint')}</span>
                    <FieldError id="internalNote" messages={e('internalNote')} />
                </div>
            </div>

            <fieldset className="space-y-2">
                <legend className="text-sm font-semibold text-gray-900">{t('sales_lines')}</legend>
                <FieldError id="lines" messages={e('lines')} />
                <div className="overflow-x-auto rounded border border-gray-200">
                    <table className={lineTableClass}>
                        <thead className="bg-gray-50 text-left text-xs font-semibold uppercase tracking-wide text-gray-600">
                            <tr>
                                <th scope="col" className="px-2 py-1.5">{t('supply_variant')} *</th>
                                <th scope="col" className="px-2 py-1.5">{t('supply_quantity')} *</th>
                                <th scope="col" className="px-2 py-1.5">{t('supply_unit')} *</th>
                                <th scope="col" className="px-2 py-1.5"><span className="sr-only">{t('supply_remove_line')}</span></th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-100">
                            {draft.lines.map((line, index) => {
                                const prefix = `lines[${index}]`;
                                return (
                                    <tr key={line.key} className="align-top">
                                        <td className="w-96 px-2 py-1.5">
                                            <label htmlFor={`${prefix}.productVariantId-input`} className="sr-only">{t('supply_variant')} {index + 1}</label>
                                            <ProductVariantPicker id={`${prefix}.productVariantId`} ariaLabel={`${t('supply_variant')} ${index + 1}`} products={products}
                                                value={line.productVariantId} disabled={locked} invalid={(e(`${prefix}.productVariantId`) ?? []).length > 0}
                                                describedBy={(e(`${prefix}.productVariantId`) ?? []).length > 0 ? `${prefix}.productVariantId` : undefined}
                                                onChange={(variantId) => {
                                                    updateLine(line.key, 'productVariantId', variantId);
                                                    const unit = variantUnits.get(Number(variantId));
                                                    if (line.unit === '' && unit) updateLine(line.key, 'unit', unit);
                                                }} />
                                            <FieldError id={`${prefix}.productVariantId`} messages={e(`${prefix}.productVariantId`)} />
                                            <FieldError id={prefix} messages={e(prefix)} />
                                        </td>
                                        <td className="w-72 px-2 py-1.5">
                                            <QuantityInput prefix={prefix} index={index} line={line} locked={locked} errors={e}
                                                defaultPackagesPerPallet={variantDefaults.get(Number(line.productVariantId)) ?? null} kgDefaults={variants.get(Number(line.productVariantId))}
                                                onChange={(field, value) => updateLine(line.key, field, value)} />
                                        </td>
                                        <td className="w-32 px-2 py-1.5">
                                            <label htmlFor={`${prefix}.unit-input`} className="sr-only">{t('supply_unit')} {index + 1}</label>
                                            <select {...fieldProps(`${prefix}.unit`, e(`${prefix}.unit`))} value={line.unit} disabled={locked}
                                                onChange={(event) => updateLine(line.key, 'unit', event.target.value)} className={inputClass}>
                                                <option value="">{t('supply_choose')}</option>
                                                {PACKAGE_UNITS.map((unit) => <option key={unit} value={unit}>{t(unitLabelKey(unit))}</option>)}
                                            </select>
                                            <FieldError id={`${prefix}.unit`} messages={e(`${prefix}.unit`)} />
                                        </td>
                                        <td className="px-2 py-1.5">
                                            <button type="button" onClick={() => removeLine(line.key)} disabled={locked || draft.lines.length <= 1}
                                                className={secondaryButtonClass} aria-label={t('supply_remove_line_n', { n: String(index + 1) })}>✕</button>
                                        </td>
                                    </tr>
                                );
                            })}
                        </tbody>
                    </table>
                </div>
                <button type="button" onClick={addLine} disabled={locked || draft.lines.length >= MAX_ORDER_LINES} className={secondaryButtonClass}>
                    + {t('supply_add_line')}
                </button>
            </fieldset>

            {pending === null && (
                <div className="flex items-center gap-3 border-t border-gray-200 pt-3">
                    <button type="submit" className={primaryButtonClass} disabled={locked}>
                        {submitting ? t('supply_saving') : t('sales_save_order')}
                    </button>
                    <span className="text-xs text-gray-600">{t('sales_save_footer')}</span>
                </div>
            )}
        </form>
    );
}
