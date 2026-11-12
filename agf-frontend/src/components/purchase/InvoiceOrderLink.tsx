'use client';

import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatMoney, formatQuantity } from '../../lib/availability';
import { type LinkCheck, compareLine, invoiceBasis, orderLabel } from '../../lib/invoiceOrderLink';
import type { LineDraft } from '../../lib/purchaseInvoiceRules';
import { UNIT_LABEL_KEYS } from '../../lib/units';
import type { PackageUnit } from '../../types/availability';
import { toApiError } from '../../services/apiClient';
import { listInvoiceCandidates } from '../../services/purchaseOrderService';
import type { OrderPriceDecision } from '../../types/purchaseInvoice';
import type { InvoiceCandidate } from '../../types/purchaseOrder';
import { inputClass } from '../supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

// Koppelen aan de bon (stap 4b) in Inkoopfactuur invoeren: de orders van de gekozen leverancier met wat er nog open staat, per regel
// wat de bon zegt tegenover de factuur, en bij opslaan eenmaal de vraag wat er met een verschil moet gebeuren.

const signed = (value: number, money: (value: number) => string) => (value > 0 ? `+${money(value)}` : money(value));
// Een eenheid van de bon als woord (box → doos); een onbekende eenheid zoals ze is.
const unitWord = (t: (key: string) => string, unit: string | null) => (unit === null ? '' : unit in UNIT_LABEL_KEYS ? t(UNIT_LABEL_KEYS[unit as PackageUnit]).toLowerCase() : unit);

export function InvoiceOrderPicker({ vendorId, lines, disabled, onAdd }: {
    vendorId: number;
    lines: readonly LineDraft[];
    disabled: boolean;
    onAdd: (order: InvoiceCandidate) => void;
}) {
    const { t, locale } = useTranslation();
    const [query, setQuery] = useState('');
    // De resultaten horen bij de leverancier waarvoor ze zijn opgehaald; bij een andere leverancier worden ze niet getoond.
    const [result, setResult] = useState<{ vendorId: number; orders: InvoiceCandidate[] | null; failed: boolean } | null>(null);
    const current = result !== null && result.vendorId === vendorId ? result : null;
    const orders = current?.orders ?? null;
    const failed = current?.failed ?? false;
    const added = new Set(lines.map((line) => line.orderLine?.lineId).filter((id) => id !== undefined));

    useEffect(() => {
        const controller = new AbortController();
        const timer = setTimeout(() => {
            listInvoiceCandidates(vendorId, query, null, controller.signal)
                .then((found) => setResult({ vendorId, orders: found, failed: false }))
                .catch((caught) => { if (toApiError(caught).kind !== 'aborted') setResult({ vendorId, orders: null, failed: true }); });
        }, query === '' ? 0 : 300);
        return () => { clearTimeout(timer); controller.abort(); };
    }, [vendorId, query]);

    return (
        <section className="space-y-2 rounded-md border border-gray-200 bg-white px-3 py-2" data-testid="invoice-order-picker" data-tour="pinvoice-order-picker">
            <div className="flex flex-wrap items-end gap-2">
                <div className="min-w-0 flex-1">
                    <label htmlFor="invoice-order-search" className="text-sm font-semibold text-gray-900">{t('invoice_link_title')}</label>
                    <input id="invoice-order-search" data-tour="pinvoice-order-search" value={query} disabled={disabled} placeholder={t('invoice_link_search_placeholder')}
                        onChange={(event) => setQuery(event.target.value)} className={inputClass} />
                </div>
            </div>
            {failed && <p role="alert" className="text-sm text-red-800">{t('invoice_link_load_failed')}</p>}
            {orders !== null && orders.length === 0 && <p className="text-sm text-gray-600" data-testid="invoice-order-none">{t(query === '' ? 'invoice_link_none_open' : 'invoice_link_none_found')}</p>}
            {orders !== null && orders.length > 0 && (
                <ul className="divide-y divide-gray-100 text-sm">
                    {orders.map((order) => {
                        const open = order.lines.filter((line) => line.openQuantity > 0);
                        const all = open.length > 0 && open.every((line) => added.has(line.id));
                        return (
                            <li key={order.id} className="flex flex-wrap items-center gap-2 py-1.5" data-testid="invoice-order-candidate" data-order-id={order.id}>
                                <span className="font-semibold text-gray-900">{orderLabel(order)}</span>
                                {order.partijNumber && <span className="text-xs text-gray-600">{t('invoice_link_partij', { number: order.partijNumber })}</span>}
                                <span className="text-gray-700">
                                    {open.length > 0
                                        ? open.map((line) => `${line.productName} ${line.productVariantName}: ${formatQuantity(line.openQuantity, locale)} ${unitWord(t, line.unit)}${line.unitPrice === null ? '' : ` × ${formatMoney(line.unitPrice, null, locale)}`}`).join(' · ')
                                        : t('invoice_link_all_invoiced')}
                                </span>
                                {order.documentStatus === 'Definitive' && <span className="rounded bg-gray-100 px-1.5 py-0.5 text-xs text-gray-700">{t('po_document_definitive')}</span>}
                                <button type="button" className={`${secondaryButtonClass} ml-auto`} disabled={disabled || all} data-tour="pinvoice-order-add" data-tour-key={order.supplierReference ?? order.orderNumber}
                                    onClick={() => onAdd(order)}>
                                    {all ? t('invoice_link_added') : t('invoice_link_add')}
                                </button>
                            </li>
                        );
                    })}
                </ul>
            )}
        </section>
    );
}

// Onder een regel uit de bon: wat de bon zegt, wat al gefactureerd en open is, en het verschil met de factuur in kleur.
export function InvoiceOrderLineInfo({ line, prefix, index, currency }: { line: LineDraft; prefix: string; index: number; currency: string }) {
    const { t, locale } = useTranslation();
    const link = line.orderLine;
    const comparison = compareLine(line);
    if (!link || !comparison) return null;
    const money = (value: number) => formatMoney(value, null, locale);
    const unit = unitWord(t, link.unit);
    return (
        <div className="mt-1 space-y-0.5 text-xs" data-testid={`${prefix}.order-info`} data-tour="pinvoice-order-info" data-tour-key={index}>
            <p className="text-gray-700">
                {t('invoice_link_on_order', { order: orderLabel(link) })}{': '}
                {comparison.quantity !== null && link.orderUnitPrice !== null
                    ? `${formatQuantity(comparison.quantity, locale)} ${unit} × ${money(link.orderUnitPrice)} = ${money(comparison.orderAmount ?? 0)}`
                    : link.orderUnitPrice === null ? t('invoice_link_no_price') : `${money(link.orderUnitPrice)} / ${unit}`}
                {' · '}{t('invoice_link_counts', { invoiced: formatQuantity(link.invoicedQuantity, locale), open: formatQuantity(link.openQuantity, locale), unit })}
            </p>
            {comparison.differs && (
                <p className="font-medium text-red-800" data-testid={`${prefix}.order-difference`} data-tour="pinvoice-order-difference" data-tour-key={index}>
                    {link.orderUnitPrice === null
                        ? t('invoice_link_price_set', { price: money(comparison.invoiceUnitPrice ?? 0), currency })
                        : t('invoice_link_difference', {
                            order: money(link.orderUnitPrice), invoice: money(comparison.invoiceUnitPrice ?? 0),
                            perUnit: signed(comparison.perUnit ?? 0, money), unit, total: signed(comparison.total ?? 0, money),
                        })}
                </p>
            )}
            {comparison.over && (
                <p className="font-medium text-amber-900" data-testid={`${prefix}.order-over`}>
                    {t('invoice_link_over', { basis: formatQuantity(invoiceBasis(link), locale), unit, kind: t(link.receivedQuantity > 0 ? 'invoice_link_basis_received' : 'invoice_link_basis_ordered') })}
                </p>
            )}
            {!comparison.over && comparison.partial && <p className="text-gray-600" data-testid={`${prefix}.order-partial`}>{t('invoice_link_partial')}</p>}
        </div>
    );
}

// Eenmaal per factuur, vóór opslaan: wat er met een verschil moet gebeuren (en de bevestiging bij meer dan besteld of ontvangen).
// Nooit stilletjes aanpassen. "Bon aanpassen" alleen als elke order met een verschil nog open is (een definitieve order: Correctie).
export function InvoicePriceQuestion({ check, currency, onAnswer, onCancel }: {
    check: LinkCheck;
    currency: string;
    onAnswer: (decision: OrderPriceDecision | null) => void;
    onCancel: () => void;
}) {
    const { t, locale } = useTranslation();
    const differs = check.differing.length > 0;
    return (
        <div role="alertdialog" aria-labelledby="invoice-question-title" className="space-y-2 rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-950" data-testid="invoice-price-question" data-tour="pinvoice-price-question">
            <p id="invoice-question-title" className="font-semibold">{t(differs ? 'invoice_question_title' : 'invoice_question_over_title')}</p>
            {differs && <p>{t('invoice_question_text', { lines: String(check.differing.length), total: signed(check.totalDifference, (value) => formatMoney(value, null, locale)), currency })}</p>}
            {check.over.length > 0 && <p data-testid="invoice-question-over">{t('invoice_question_over', { lines: String(check.over.length) })}</p>}
            {differs && !check.adjustable && <p data-testid="invoice-question-not-adjustable">{t('invoice_question_not_adjustable')}</p>}
            <div className="flex flex-wrap gap-2">
                {differs ? (
                    <>
                        {check.adjustable && (
                            <button type="button" className={primaryButtonClass} data-tour="pinvoice-adjust-order" onClick={() => onAnswer('AdjustOrder')}>{t('invoice_question_adjust')}</button>
                        )}
                        <button type="button" className={check.adjustable ? secondaryButtonClass : primaryButtonClass} data-tour="pinvoice-record-difference" onClick={() => onAnswer('RecordDifference')}>{t('invoice_question_record')}</button>
                    </>
                ) : (
                    <button type="button" className={primaryButtonClass} data-tour="pinvoice-accept-over" onClick={() => onAnswer(null)}>{t('invoice_question_save_anyway')}</button>
                )}
                <button type="button" className={secondaryButtonClass} onClick={onCancel}>{t('cancel')}</button>
            </div>
        </div>
    );
}
