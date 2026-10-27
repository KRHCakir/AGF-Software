'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatKg, formatMoney, formatQuantity, qualityStyle } from '../../lib/availability';
import { formatCalendarDate, formatDateOnly } from '../../lib/calendarDate';
import { CHANNEL_LABEL_KEYS, DECISION_LABEL_KEYS, REASON_LABEL_KEYS, labelKey } from '../../lib/purchaseOrderRules';
import type { PurchaseOrderDetail as Order, PurchaseOrderHistoryEvent } from '../../types/purchaseOrder';
import { InvoiceIndicatorBadge, OrderStatusBadge } from './PurchaseOrderBadges';
import ReceiptsSection from './ReceiptsSection';
import { sectionClass } from './sectionClass';

const section = sectionClass;
const h3 = 'mb-2 text-sm font-semibold text-gray-900';

// Detail van een inkooporder: kop met eigen nummer, regels (besteld/ontvangen/afgekeurd), ontvangsten met keuring en
// "Afkeuren", facturen, afkeuringen en geschiedenis. `invoices` is de plek voor het koppelen van factuurnummers.
export default function PurchaseOrderDetail({ order, onChanged, invoices }: {
    order: Order;
    onChanged: (order: Order) => void;
    invoices?: React.ReactNode;
}) {
    const { t, locale } = useTranslation();
    const label = (keys: Readonly<Record<string, string>>, value: string | null) => { const key = labelKey(keys, value); return key ? t(key) : (value ?? '–'); };
    const qty = (value: number) => formatQuantity(value, locale);

    return (
        <article className="space-y-3" aria-labelledby="po-detail-title">
            <header className={section}>
                <div className="flex flex-wrap items-center justify-between gap-2">
                    <h2 id="po-detail-title" className="font-mono text-2xl font-bold text-gray-900">{order.orderNumber}</h2>
                    <span className="flex flex-wrap gap-1"><OrderStatusBadge status={order.status} /><InvoiceIndicatorBadge indicator={order.invoiceIndicator} /></span>
                </div>
                <p className="mt-1 break-words text-sm text-gray-900">
                    <span className="font-medium">{order.vendorName ?? '–'}</span>
                    {order.supplierReference && <> · {t('po_col_supplier_reference')} <span className="font-mono">{order.supplierReference}</span></>}
                </p>
                <p className="text-sm text-gray-700">
                    {t('po_col_ordered_on')} {formatDateOnly(order.orderedOn, locale)}
                    {order.orderChannel && <> · {label(CHANNEL_LABEL_KEYS, order.orderChannel)}</>}
                    {' · '}{t('po_col_expected')} {order.expectedAt ? formatCalendarDate(order.expectedAt, locale) : '–'} · {order.origin}
                </p>
                {order.rejectedQuantity > 0 && (
                    <p className="mt-1 text-sm font-medium text-red-800">{t('po_rejected_of', { rejected: qty(order.rejectedQuantity), total: qty(order.receivedQuantity) })} · {formatKg(order.rejectedNetWeightKg, locale)}</p>
                )}
                {order.note && <p className="mt-1 text-sm text-gray-700">{order.note}</p>}
            </header>

            <section className={section} aria-labelledby="po-lines">
                <h3 id="po-lines" className={h3}>{t('po_lines')}</h3>
                <ul className="divide-y divide-gray-100 text-sm">
                    {order.lines.map((line) => (
                        <li key={line.id} className="py-1.5">
                            <span className="font-medium text-gray-900">{line.productName} · {line.productVariantName}</span>
                            {line.lotNumber && <span className="text-gray-600"> · {line.lotNumber}</span>}
                            <span className="block font-mono text-gray-800">
                                {t('po_line_amounts', { ordered: qty(line.expectedQuantity), received: qty(line.receivedQuantity) })}
                                {line.expectedNetWeightKg !== null && <> · {formatKg(line.expectedNetWeightKg, locale)}</>}
                                {line.rejectedQuantity > 0 && <span className="text-red-800"> · {t('po_line_rejected', { rejected: qty(line.rejectedQuantity) })}</span>}
                            </span>
                        </li>
                    ))}
                </ul>
            </section>

            <ReceiptsSection order={order} onChanged={onChanged} />

            <section className={section} aria-labelledby="po-invoices">
                <h3 id="po-invoices" className={h3}>{t('po_invoices')}</h3>
                {order.invoiceRefs.length === 0 && <p className="text-sm text-gray-600">{t('po_no_invoices')}</p>}
                <ul className="text-sm">
                    {order.invoiceRefs.map((ref) => (
                        <li key={ref.id} className="break-words py-0.5">
                            <span className="font-medium">{ref.kind === 'CreditNote' ? t('po_credit_note') : t('po_invoice')}</span>{' '}
                            <span className="font-mono">{ref.invoiceNumber}</span>
                            {ref.invoiceDate && <> · {formatDateOnly(ref.invoiceDate, locale)}</>}
                            {ref.amountExclVat !== null && <> · {t('po_amount_excl_vat_value', { amount: formatMoney(ref.amountExclVat, null, locale) })}</>}
                        </li>
                    ))}
                </ul>
                {invoices}
            </section>

            {order.rejections.length > 0 && (
                <section className={section} aria-labelledby="po-rejections">
                    <h3 id="po-rejections" className={h3}>{t('po_rejections')}</h3>
                    <ul className="divide-y divide-gray-100 text-sm">
                        {order.rejections.map((rejection) => (
                            <li key={rejection.id} className="py-1.5">
                                <span className="font-medium">{label(REASON_LABEL_KEYS, rejection.reason)}</span> · {label(DECISION_LABEL_KEYS, rejection.decision)}
                                {rejection.explanation && <> · {rejection.explanation}</>}
                                <span className="block font-mono text-gray-800">
                                    {rejection.lines.map((line) => `${qty(line.quantity)} · ${formatKg(line.netWeightKg, locale)} (${line.palletReference ?? '–'})`).join(', ')}
                                </span>
                                <span className="block text-xs text-gray-600">{rejection.actorReference} · {formatDateTime(rejection.rejectedAt, locale)}</span>
                            </li>
                        ))}
                    </ul>
                </section>
            )}

            <section className={section} aria-labelledby="po-history">
                <h3 id="po-history" className={h3}>{t('po_history')}</h3>
                <ol className="space-y-0.5 text-sm">
                    {order.history.map((event, index) => (
                        <li key={index} className="break-words text-gray-800">
                            <span className="text-gray-600">{formatDateTime(event.occurredAt, locale)}</span> · {historyText(event, t, label, qty)}
                            {event.actorReference && <span className="text-gray-600"> · {event.actorReference}</span>}
                        </li>
                    ))}
                </ol>
            </section>
        </article>
    );
}

function historyText(event: PurchaseOrderHistoryEvent, t: (key: string, values?: Record<string, string>) => string,
    label: (keys: Readonly<Record<string, string>>, value: string | null) => string, qty: (value: number) => string): string {
    switch (event.type) {
        case 'Ordered': return t('po_history_ordered') + (event.channel ? ` (${label(CHANNEL_LABEL_KEYS, event.channel)})` : '');
        case 'Received': return t('po_history_received', { quantity: qty(event.quantity ?? 0), pallet: event.reference ?? '–' });
        case 'QualityChanged': {
            const quality = (status: string | null) => { const style = status === null ? null : qualityStyle(status); return style ? t(style.key) : (status ?? '–'); };
            return t('po_history_quality', { pallet: event.reference ?? '–', from: quality(event.fromStatus), to: quality(event.toStatus) });
        }
        case 'InvoiceRecorded': return t('po_history_invoice', { number: event.reference ?? '–' });
        case 'CreditNoteRecorded': return t('po_history_credit_note', { number: event.reference ?? '–' });
        case 'Rejected': return t('po_history_rejected', { quantity: qty(event.quantity ?? 0), reason: label(REASON_LABEL_KEYS, event.reason), decision: label(DECISION_LABEL_KEYS, event.decision) });
        default: return event.type;
    }
}
