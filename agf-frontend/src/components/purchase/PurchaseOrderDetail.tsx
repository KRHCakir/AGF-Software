'use client';

import Link from 'next/link';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatKg, formatMoney, formatQuantity, qualityStyle } from '../../lib/availability';
import { formatCalendarDate, formatDateOnly } from '../../lib/calendarDate';
import { purchaseOrderChain } from '../../lib/chain';
import { DECISION_LABEL_KEYS, REASON_LABEL_KEYS, labelKey, lineAmountParts, orderNumbers } from '../../lib/purchaseOrderRules';
import OrderNumberText from './OrderNumberText';
import type { PurchaseOrderDetail as Order, PurchaseOrderHistoryEvent } from '../../types/purchaseOrder';
import { getPurchaseOrder } from '../../services/purchaseOrderService';
import PartijLinkPanel from '../partij/PartijLinkPanel';
import ChainStrip from '../shared/ChainStrip';
import { primaryButtonClass } from '../supply/SuccessPanel';
import { DocumentStatusBadge, InvoiceIndicatorBadge, OrderStatusBadge } from './PurchaseOrderBadges';
import PurchaseOrderDocument, { PurchaseOrderChanges } from './PurchaseOrderDocument';
import DeviationsSection from './DeviationsSection';
import ReceiptsSection from './ReceiptsSection';
import { sectionClass } from './sectionClass';
import { PurchaseLineHead, PurchaseLineTotalsRow, lineCell, purchaseLineBodyClass, purchaseLineRowClass, purchaseLineTableClass, usePurchaseLineLabels } from './PurchaseLineTable';
import { unitLabelKey } from '../../lib/units';
import { formatPallets, palletWordKey } from '../../lib/purchaseLine';
import type { PackageUnit } from '../../types/availability';

const section = sectionClass;
const h3 = 'mb-2 text-sm font-semibold text-gray-900';
const replacementBadge = 'whitespace-nowrap rounded border border-amber-300 bg-amber-50 px-1.5 py-0.5 text-xs font-medium text-amber-900';

// Detail van een inkooporder: kop met eigen nummer, regels (besteld/ontvangen/afgekeurd), ontvangsten met keuring en
// "Afkeuren", facturen, afkeuringen en geschiedenis. `invoices` is de plek voor het koppelen van factuurnummers.
export default function PurchaseOrderDetail({ order, onChanged, invoices }: {
    order: Order;
    onChanged: (order: Order) => void;
    invoices?: React.ReactNode;
}) {
    const { t, locale } = useTranslation();
    // Volgende stap: ontvangen, zolang er van een inkooporder voor het eigen magazijn nog iets openstaat.
    const receivable = order.route === 'Warehouse' && order.status !== 'Cancelled' && order.storedStatus !== 'Cancelled' && order.lines.some((line) => line.receivedQuantity < line.expectedQuantity);
    const label = (keys: Readonly<Record<string, string>>, value: string | null) => { const key = labelKey(keys, value); return key ? t(key) : (value ?? '–'); };
    const qty = (value: number) => formatQuantity(value, locale);
    const labels = usePurchaseLineLabels();
    // Herkenbare naam van een andere regel van deze order: het onderscheidende kenmerk (herkomst, maat of verpakking).
    const lineName = (id: number) => { const other = order.lines.find((line) => line.id === id); return other ? [other.origin, other.size, other.packaging].filter(Boolean).join(' · ') || `#${id}` : `#${id}`; };

    return (
        <article className="space-y-3" aria-labelledby="po-detail-title">
            <ChainStrip steps={purchaseOrderChain(order, 'order')} />
            <header className={section}>
                <div className="flex flex-wrap items-center justify-between gap-2">
                    {/* Hoofdnummer: het ordernummer van de leverancier (volledig, ook als het lang is), het eigen IO-nummer klein en grijs eronder. */}
                    <h2 id="po-detail-title" data-tour="po-detail-number" className="min-w-0 text-2xl font-bold text-gray-900">
                        <OrderNumberText orderNumber={order.orderNumber} supplierReference={order.supplierReference} stacked />
                    </h2>
                    {receivable && (
                        <Link href={`/receipts?order=${order.id}`} data-tour="po-receive" className={`${primaryButtonClass} ml-auto`}>{t('po_receive')}</Link>
                    )}
                    <span className="flex flex-wrap gap-1"><OrderStatusBadge status={order.status} /><DocumentStatusBadge status={order.documentStatus} /><span data-tour="po-invoice-indicator" data-tour-key={order.invoiceIndicator}><InvoiceIndicatorBadge indicator={order.invoiceIndicator} /></span></span>
                </div>
                <p className="mt-1 break-words text-sm text-gray-900">
                    <span className="font-medium">{order.vendorName ?? '–'}</span>
                </p>
                {/* Documentstatus (stap 4a): Aanpassen en Definitief maken, of definitief met Correctie. */}
                <PurchaseOrderDocument order={order} onChanged={onChanged} />
                {order.partijId ? (
                    <p className="text-sm text-gray-900" data-tour="po-partij">
                        {t('partij_field')}{' '}
                        <Link href={`/partijen?id=${order.partijId}`} className="font-mono font-semibold text-blue-800 underline underline-offset-2">{order.partijNumber}</Link>
                        {order.supplierLotReference && <> · {t('partij_supplier_lot')} <span className="font-mono">{order.supplierLotReference}</span></>}
                    </p>
                ) : (
                    <div className="mt-1">
                        {order.supplierLotReference && <p className="text-sm text-gray-900">{t('partij_supplier_lot')} <span className="font-mono">{order.supplierLotReference}</span></p>}
                        {order.storedStatus !== 'Cancelled' && (
                            <PartijLinkPanel orderId={order.id} orderNumber={orderNumbers(order).main} dataTourKey={order.id}
                                onLinked={(partij) => {
                                    // Direct zichtbaar maken; daarna op de achtergrond verversen (mislukt dat, dan blijft de gekoppelde partij staan).
                                    onChanged({ ...order, partijId: partij.id, partijNumber: partij.number });
                                    void getPurchaseOrder(order.id).then(onChanged).catch(() => undefined);
                                }} />
                        )}
                    </div>
                )}
                <p className="text-sm text-gray-700">
                    {t('po_col_ordered_on')} {formatDateOnly(order.orderedOn, locale)}
                    {' · '}{t('po_col_expected')} {order.expectedAt ? formatCalendarDate(order.expectedAt, locale) : '–'}
                    {(order.linesOrigin || order.origin) && <> · <span data-tour="po-origin">{order.linesOrigin || order.origin}</span></>}
                </p>
                {order.rejectedQuantity > 0 && (
                    <p className="mt-1 text-sm font-medium text-red-800">{t('po_rejected_of', { rejected: qty(order.rejectedQuantity), total: qty(order.receivedQuantity) })} · {formatKg(order.rejectedNetWeightKg, locale)}</p>
                )}
                {order.note && <p className="mt-1 text-sm text-gray-700">{order.note}</p>}
            </header>

            <section className={section} aria-labelledby="po-lines">
                <h3 id="po-lines" className={h3}>{t('po_lines')}</h3>
                <table className={purchaseLineTableClass} data-tour="po-lines-table">
                    <PurchaseLineHead />
                    <tbody className={purchaseLineBodyClass}>
                        {order.lines.map((line, index) => (
                            <tr key={line.id} className={purchaseLineRowClass(index)}>
                                <td {...lineCell('product', labels.product, '', true)}>
                                    <span className="font-medium text-gray-900">{line.productName} · {line.productVariantName}</span>
                                    {line.partijNumber && line.partijNumber !== order.partijNumber
                                        ? <span className="text-gray-600"> · {t('partij_field')} <Link href={`/partijen?id=${line.partijId}`} className="font-mono text-blue-800 underline underline-offset-2">{line.partijNumber}</Link></span>
                                        : line.lotNumber && <span className="text-gray-600"> · {line.lotNumber}</span>}
                                    {(line.replacesLineId || (line.replacedByLineIds ?? []).length > 0) && (
                                        <span className="ml-1 inline-flex flex-wrap gap-1 align-middle" data-tour="po-line-replacement" data-tour-key={line.id}>
                                            {line.replacesLineId && <span className={replacementBadge}>{t('po_line_replaces', { line: lineName(line.replacesLineId) })}</span>}
                                            {(line.replacedByLineIds ?? []).map((id) => <span key={id} className={replacementBadge}>{t('po_line_replaced_by', { line: lineName(id) })}</span>)}
                                        </span>
                                    )}
                                    <span className="block font-mono text-xs text-gray-800">
                                        {lineAmountParts(line).map((part, partIndex) => (
                                            <span key={part.key} className={part.key === 'po_line_rejected' ? 'text-red-800' : undefined}>
                                                {partIndex > 0 && ' · '}{t(part.key, { quantity: qty(part.quantity) })}{part.kg !== null && <> · {formatKg(part.kg, locale)}</>}
                                            </span>
                                        ))}
                                    </span>
                                </td>
                                <td {...lineCell('brand', labels.brand)}>{line.brand || '–'}</td>
                                <td {...lineCell('country', labels.country)}>{line.origin || '–'}</td>
                                <td {...lineCell('size', labels.size)}>{line.size || '–'}</td>
                                <td {...lineCell('gross', labels.gross)}>
                                    <span>
                                        {line.expectedGrossWeightKg == null ? '–' : formatKg(line.expectedGrossWeightKg, locale)}
                                        {line.weightSource === 'Entered' && line.expectedGrossWeightKg != null && <span className="block text-xs font-medium text-amber-900">{t('po_line_gross_deviates')}</span>}
                                    </span>
                                </td>
                                <td {...lineCell('net', labels.net)}>{line.expectedNetWeightKg == null ? '–' : formatKg(line.expectedNetWeightKg, locale)}</td>
                                <td {...lineCell('quantity', labels.quantity)}>
                                    <span>
                                        {qty(line.expectedQuantity)} {line.unit && <span className="text-xs text-gray-700">{t(unitLabelKey(line.unit as PackageUnit)).toLowerCase()}</span>}
                                        {line.pallets != null && <span className="block text-xs text-gray-600">{t(palletWordKey(line.pallets), { pallets: formatPallets(line.pallets, locale) })}</span>}
                                    </span>
                                </td>
                                <td {...lineCell('packaging', labels.packaging)}>{line.packaging || '–'}</td>
                                <td {...lineCell('price', labels.price)}>{line.unitPrice == null ? '–' : formatMoney(line.unitPrice, null, locale)}</td>
                                <td {...lineCell('total', labels.total)}>{line.lineTotal == null ? '–' : formatMoney(line.lineTotal, null, locale)}</td>
                            </tr>
                        ))}
                    </tbody>
                    {order.totals && <PurchaseLineTotalsRow totals={{ ...order.totals, netKg: order.totals.netWeightKg, grossKg: order.totals.grossWeightKg }} />}
                </table>
            </section>

            <ReceiptsSection order={order} onChanged={onChanged} />

            {(order.deviations ?? []).length > 0 && <DeviationsSection deviations={order.deviations ?? []} />}

            <section className={section} aria-labelledby="po-invoices">
                <h3 id="po-invoices" className={h3}>{t('po_invoices')}</h3>
                {order.invoiceRefs.length === 0 && <p className="text-sm text-gray-600">{t('po_no_invoices')}</p>}
                <ul className="text-sm">
                    {order.invoiceRefs.map((ref) => (
                        <li key={ref.id} data-tour="po-invoice-ref" data-tour-key={ref.invoiceNumber} className="break-words py-0.5">
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
                            {event.inspectedBy
                                ? <span className="text-gray-600"> · {t('inspection_event_by_signed_in', { actor: event.inspectedBy, user: event.actorReference ?? t('inspection_actor_not_given') })}</span>
                                : event.actorReference && <span className="text-gray-600"> · {event.actorReference}</span>}
                        </li>
                    ))}
                </ol>
                <PurchaseOrderChanges order={order} />
            </section>
        </article>
    );
}

function historyText(event: PurchaseOrderHistoryEvent, t: (key: string, values?: Record<string, string>) => string,
    label: (keys: Readonly<Record<string, string>>, value: string | null) => string, qty: (value: number) => string): string {
    switch (event.type) {
        case 'Ordered': return t('po_history_ordered');
        case 'Received': return t('po_history_received', { quantity: qty(event.quantity ?? 0), pallet: event.reference ?? '–' });
        case 'QualityChanged': {
            const quality = (status: string | null) => { const style = status === null ? null : qualityStyle(status); return style ? t(style.key) : (status ?? '–'); };
            return t('po_history_quality', { pallet: event.reference ?? '–', from: quality(event.fromStatus), to: quality(event.toStatus) });
        }
        case 'InvoiceRecorded': return t('po_history_invoice', { number: event.reference ?? '–' });
        case 'CreditNoteRecorded': return t('po_history_credit_note', { number: event.reference ?? '–' });
        case 'Rejected': return t('po_history_rejected', { quantity: qty(event.quantity ?? 0), reason: label(REASON_LABEL_KEYS, event.reason), decision: label(DECISION_LABEL_KEYS, event.decision) });
        case 'DeviationRecorded': return t('po_history_deviation', { quantity: qty(event.quantity ?? 0), pallet: event.reference ?? '–' });
        case 'Edited': return t('po_history_edited', { actor: event.actorReference ?? '–', count: String(event.quantity ?? 0) });
        case 'Finalized': return t(event.reference === 'Invoice' ? 'po_history_finalized_invoice' : 'po_history_finalized', { actor: event.actorReference ?? '–' });
        case 'Corrected': return t('po_history_corrected', { actor: event.actorReference ?? '–', reason: event.reason ?? '–' });
        default: return event.type;
    }
}
