'use client';

import { useMemo, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDate, formatQuantity } from '../../lib/availability';
import { newRequestId } from '../../lib/requestId';
import { MAX_ORDER_LINES, OrderDraft, OrderLineDraft, buildOrderPayload } from '../../lib/salesOrderRules';
import { ApiError, toApiError } from '../../services/apiClient';
import { createSalesOrder } from '../../services/salesOrderService';
import { SaveResult } from '../../services/supplyService';
import { PACKAGE_UNITS } from '../../types/availability';
import { CustomerOption, ProductOption } from '../../types/referenceData';
import { CreateSalesOrderRequest, SalesOrder } from '../../types/salesOrder';
import { UnitBadge } from '../availability/QuantityCell';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import { FieldErrors, outcomeUnknown } from '../supply/messages';
import SuccessPanel, { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { ruleErrorsToFieldErrors } from './ruleMessages';

const emptyLine = (): OrderLineDraft => ({ key: newRequestId(), productVariantId: '', quantity: '', unit: '' });
const emptyDraft = (): OrderDraft => ({ customerId: '', customerReference: '', requestedDeliveryDate: '', lines: [emptyLine()] });

// Nieuwe verkooporder via POST /api/sales-orders. Bestaande klant, leverdatum en expliciete eenheid per regel;
// geen prijzen, btw of facturen. De payload en requestId worden bij versturen bevroren: "opnieuw proberen" stuurt exact hetzelfde.
export default function CreateSalesOrderForm({ actor, customers, products, onCreated, onCancel }: {
    actor: string;
    customers: CustomerOption[];
    products: ProductOption[];
    onCreated: (order: SalesOrder) => void;
    onCancel: () => void;
}) {
    const { t, locale } = useTranslation();
    const [draft, setDraft] = useState<OrderDraft>(emptyDraft);
    const [customerFilter, setCustomerFilter] = useState('');
    const [variantFilter, setVariantFilter] = useState('');
    const [pending, setPending] = useState<CreateSalesOrderRequest | null>(null);
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<ApiError | null>(null);
    const [errors, setErrors] = useState<FieldErrors>({});
    const [result, setResult] = useState<SaveResult<SalesOrder> | null>(null);
    const inFlight = useRef(false);

    // Na een onbekende uitkomst of na succes is het formulier vergrendeld: nooit een gewijzigde retry.
    const locked = submitting || result !== null || (error !== null && outcomeUnknown(error));

    const filteredCustomers = useMemo(() => {
        const term = customerFilter.trim().toLowerCase();
        return term === '' ? customers : customers.filter((customer) => customer.name.toLowerCase().includes(term));
    }, [customers, customerFilter]);

    const filteredProducts = useMemo(() => {
        const term = variantFilter.trim().toLowerCase();
        if (term === '') return products;
        return products
            .map((product) => ({
                ...product,
                variants: product.variants.filter((variant) =>
                    `${product.name} ${variant.name} ${variant.packaging ?? ''} ${variant.origin ?? ''}`.toLowerCase().includes(term)),
            }))
            .filter((product) => product.variants.length > 0);
    }, [products, variantFilter]);

    const variantLabel = useMemo(() => {
        const labels = new Map<number, string>();
        for (const product of products) {
            for (const variant of product.variants) labels.set(variant.id, `${product.name} · ${variant.name}`);
        }
        return labels;
    }, [products]);

    const edited = () => {
        // Gewijzigde invoer hoort bij een nieuw verzoek (nieuwe requestId bij de volgende poging).
        setPending(null);
        setError(null);
        setErrors({});
    };

    const updateHeader = (field: 'customerId' | 'customerReference' | 'requestedDeliveryDate', value: string) => {
        if (locked) return;
        setDraft((current) => ({ ...current, [field]: value }));
        edited();
    };

    const updateLine = (key: string, field: 'productVariantId' | 'quantity' | 'unit', value: string) => {
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
        let request = pending;
        if (request === null) {
            const built = buildOrderPayload(draft, actor);
            if (!built.ok) {
                setErrors(ruleErrorsToFieldErrors(built.errors, t));
                return;
            }
            setErrors({});
            request = { requestId: newRequestId(), ...built.value };
            setPending(request);
        }

        inFlight.current = true;
        setSubmitting(true);
        setError(null);
        try {
            setResult(await createSalesOrder(request));
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
        setDraft(emptyDraft());
        setPending(null);
        setError(null);
        setErrors({});
        setResult(null);
    };

    const e = (key: string) => errors[key];

    if (result !== null) {
        const order = result.value;
        return (
            <SuccessPanel
                title={t('sales_create_success', { id: String(order.id), customer: order.customerName ?? '' })}
                note={result.replayed ? t('supply_saved_before') : undefined}
                actions={(
                    <>
                        <button type="button" className={primaryButtonClass} onClick={() => onCreated(order)}>{t('sales_reserve_for_this_order')}</button>
                        <button type="button" className={secondaryButtonClass} onClick={startOver}>{t('sales_another_order')}</button>
                    </>
                )}
            >
                <p>
                    {t('sales_delivery_on', { date: formatDate(order.requestedDeliveryDate, locale) })}
                    {order.customerReference && <> · {t('sales_reference_short')} {order.customerReference}</>}
                </p>
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
                <button type="button" className={secondaryButtonClass} onClick={onCancel} disabled={submitting}>{t('inspection_cancel')}</button>
            </div>

            {error && (
                <ErrorNotice error={error} actions={outcomeUnknown(error) ? (
                    <>
                        <button type="submit" className={primaryButtonClass} disabled={submitting}>{t('supply_retry_same')}</button>
                        <button type="button" className={secondaryButtonClass} onClick={startOver}>{t('supply_discard_and_restart')}</button>
                    </>
                ) : undefined} />
            )}
            <FieldError id="actorReference" messages={e('actorReference')} />

            <div className="grid grid-cols-4 gap-3">
                <div className="col-span-2">
                    <label htmlFor="customerId-input" className="text-xs font-medium text-gray-700">{t('sales_customer')} *</label>
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
                    <span className="block text-xs text-gray-500">{t('sales_customer_existing_only')}</span>
                    <FieldError id="customerId" messages={e('customerId')} />
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
            </div>

            <fieldset className="space-y-2">
                <legend className="text-sm font-semibold text-gray-900">{t('sales_lines')}</legend>
                <div className="flex items-end gap-3">
                    <label className="text-xs font-medium text-gray-700">
                        {t('supply_variant_filter')}
                        <input type="search" value={variantFilter} onChange={(event) => setVariantFilter(event.target.value)}
                            placeholder={t('supply_variant_filter_placeholder')} className={`${inputClass} w-72`} />
                    </label>
                    <FieldError id="lines" messages={e('lines')} />
                </div>
                <div className="overflow-x-auto rounded border border-gray-200">
                    <table className="min-w-full divide-y divide-gray-200 text-sm">
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
                                const selectedId = Number(line.productVariantId);
                                const selectedVisible = filteredProducts.some((product) => product.variants.some((variant) => variant.id === selectedId));
                                return (
                                    <tr key={line.key} className="align-top">
                                        <td className="w-96 px-2 py-1.5">
                                            <label htmlFor={`${prefix}.productVariantId-input`} className="sr-only">{t('supply_variant')} {index + 1}</label>
                                            <select {...fieldProps(`${prefix}.productVariantId`, e(`${prefix}.productVariantId`))} value={line.productVariantId}
                                                disabled={locked} onChange={(event) => updateLine(line.key, 'productVariantId', event.target.value)} className={inputClass}>
                                                <option value="">{t('supply_choose')}</option>
                                                {!selectedVisible && line.productVariantId && (
                                                    <option value={line.productVariantId}>{variantLabel.get(selectedId) ?? line.productVariantId}</option>
                                                )}
                                                {filteredProducts.map((product) => (
                                                    <optgroup key={product.id} label={product.name}>
                                                        {product.variants.map((variant) => (
                                                            <option key={variant.id} value={variant.id}>
                                                                {[variant.name, variant.packaging, variant.origin].filter(Boolean).join(' · ')}
                                                            </option>
                                                        ))}
                                                    </optgroup>
                                                ))}
                                            </select>
                                            <FieldError id={`${prefix}.productVariantId`} messages={e(`${prefix}.productVariantId`)} />
                                            <FieldError id={prefix} messages={e(prefix)} />
                                        </td>
                                        <td className="w-32 px-2 py-1.5">
                                            <label htmlFor={`${prefix}.quantity-input`} className="sr-only">{t('supply_quantity')} {index + 1}</label>
                                            <input {...fieldProps(`${prefix}.quantity`, e(`${prefix}.quantity`))} inputMode="decimal" value={line.quantity}
                                                disabled={locked} onChange={(event) => updateLine(line.key, 'quantity', event.target.value)} className={inputClass} />
                                            <FieldError id={`${prefix}.quantity`} messages={e(`${prefix}.quantity`)} />
                                        </td>
                                        <td className="w-32 px-2 py-1.5">
                                            <label htmlFor={`${prefix}.unit-input`} className="sr-only">{t('supply_unit')} {index + 1}</label>
                                            <select {...fieldProps(`${prefix}.unit`, e(`${prefix}.unit`))} value={line.unit} disabled={locked}
                                                onChange={(event) => updateLine(line.key, 'unit', event.target.value)} className={inputClass}>
                                                <option value="">{t('supply_choose')}</option>
                                                {PACKAGE_UNITS.map((unit) => <option key={unit} value={unit}>{unit}</option>)}
                                            </select>
                                            <span className="block text-xs text-gray-500">{t('sales_unit_hint')}</span>
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

            <div className="flex items-center gap-3 border-t border-gray-200 pt-3">
                <button type="submit" className={primaryButtonClass} disabled={locked}>
                    {submitting ? t('supply_saving') : t('sales_save_order')}
                </button>
                <span className="text-xs text-gray-600">{t('sales_save_footer')}</span>
            </div>
        </form>
    );
}
