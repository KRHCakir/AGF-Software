'use client';

import Link from 'next/link';
import { FormEvent, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatKg, formatQuantity, logisticsKey } from '../../lib/availability';
import { formatCalendarDate, formatDateOnly } from '../../lib/calendarDate';
import { MAX_PARTIJ_NUMBER_LENGTH, MAX_PARTIJ_REASON_LENGTH } from '../../lib/partijRules';
import { unitLabelKey } from '../../lib/units';
import { ApiError, toApiError } from '../../services/apiClient';
import { correctPartijNumber, deletePartij } from '../../services/partijService';
import { PACKAGE_UNITS, type PackageUnit } from '../../types/availability';
import { type PartijDetail as Detail, isPartijExistsConflict } from '../../types/partij';
import QualityBadge from '../availability/QualityBadge';
import { OrderStatusBadge } from '../purchase/PurchaseOrderBadges';
import { sectionClass } from '../purchase/sectionClass';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import PartijStatusBadge from './PartijStatusBadge';
import { SalesPartijSources, SalesPartijSummary } from './SalesPartijSections';
import PartijCostsSection from './PartijCostsSection';

const h3 = 'mb-2 text-sm font-semibold text-gray-900';
const link = 'font-mono text-blue-800 underline underline-offset-2';

// Partijdetail: de inkooporders (met link), de ontvangsten en pallets met status, de huidige voorraad en de verkooporders die ervan
// reserveren of leveren, en de kosten (P2; resultaat komt in P3). Het nummer corrigeren gaat met een reden (historie); verwijderen kan
// alleen zonder koppelingen.
export default function PartijDetail({ partij, onChanged, onDeleted }: { partij: Detail; onChanged: () => void; onDeleted: () => void }) {
    const { t, locale } = useTranslation();
    const qty = (value: number) => formatQuantity(value, locale);
    const unitText = (unit: string | null) => (unit !== null && (PACKAGE_UNITS as readonly string[]).includes(unit) ? t(unitLabelKey(unit as PackageUnit)) : t('partij_unit_mixed'));
    const pallets = [...new Map(partij.receipts.map((receipt) => [receipt.palletId, receipt])).values()];
    const sales = partij.kind === 'Sales';

    return (
        <article className="space-y-3" aria-labelledby="partij-detail-title" data-tour="partij-detail">
            <header className={sectionClass}>
                <div className="flex flex-wrap items-center justify-between gap-2">
                    <h2 id="partij-detail-title" data-tour="partij-detail-number" className="break-all font-mono text-2xl font-bold text-gray-900">{partij.number}</h2>
                    <span className="flex items-center gap-2">
                        <span className="rounded border border-gray-300 bg-gray-50 px-1.5 py-0.5 text-xs font-medium text-gray-800" data-testid="partij-kind">{t(sales ? 'partij_kind_sales' : 'partij_kind_purchase')}</span>
                        {!sales && <PartijStatusBadge status={partij.status} />}
                    </span>
                </div>
                {sales && <SalesPartijSummary partij={partij} />}
                {!sales && <>
                <p className="mt-1 break-words text-sm text-gray-900">
                    {partij.products.join(', ') || '–'} · {partij.vendors.join(', ') || '–'}
                    {partij.supplierLotReferences.length > 0 && <> · {t('partij_supplier_lot')} <span className="font-mono">{partij.supplierLotReferences.join(', ')}</span></>}
                </p>
                <p className="text-sm text-gray-700">{t('partij_created_by', { date: formatCalendarDate(partij.createdAt, locale), name: partij.createdBy })}</p>
                {partij.note && <p className="mt-1 text-sm text-gray-700">{partij.note}</p>}
                <dl className="mt-2 grid grid-cols-2 gap-2 text-sm sm:grid-cols-3 lg:grid-cols-6" data-tour="partij-totals">
                    {([
                        ['partij_col_purchased', partij.purchasedQuantity],
                        ['partij_col_received', partij.receivedQuantity],
                        ['partij_col_on_hand', partij.onHandQuantity],
                        ['partij_col_in_inspection', partij.inInspectionQuantity],
                        ['partij_col_expected', partij.openExpectedQuantity],
                        ['partij_col_sold', partij.soldQuantity],
                    ] as const).map(([key, value]) => (
                        <div key={key} className="rounded border border-gray-200 bg-gray-50 px-2 py-1">
                            <dt className="text-xs text-gray-600">{t(key)}</dt>
                            <dd className="font-mono font-semibold text-gray-900">{qty(value)} <span className="text-xs font-normal text-gray-600">{unitText(partij.unit)}</span></dd>
                        </div>
                    ))}
                </dl>
                </>}
                {sales && <p className="text-sm text-gray-700">{t('partij_created_by', { date: formatCalendarDate(partij.createdAt, locale), name: partij.createdBy })}</p>}
                <PartijActions partij={partij} onChanged={onChanged} onDeleted={onDeleted} />
            </header>

            {sales && <SalesPartijSources partij={partij} />}

            {!sales && <>
            <section className={sectionClass} aria-labelledby="partij-orders">
                <h3 id="partij-orders" className={h3}>{t('partij_orders')}</h3>
                {partij.orders.length === 0 ? <p className="text-sm text-gray-600">{t('partij_orders_none')}</p> : (
                    <ul className="divide-y divide-gray-100 text-sm">
                        {partij.orders.map((order) => (
                            <li key={order.id} className="flex flex-wrap items-center gap-x-2 gap-y-0.5 py-1.5" data-tour="partij-order" data-tour-key={order.orderNumber}>
                                <Link href={`/purchase-orders?id=${order.id}`} className={`${link} font-semibold`}>{order.orderNumber}</Link>
                                <span>· {order.vendorName ?? '–'}</span>
                                {order.supplierLotReference && <span className="text-gray-700">· {t('partij_supplier_lot')} <span className="font-mono">{order.supplierLotReference}</span></span>}
                                <span className="text-gray-700">· {t('po_col_ordered_on')} {formatDateOnly(order.orderedOn, locale)}</span>
                                <span className="font-mono">· {t('partij_order_amounts', { expected: qty(order.expectedQuantity), received: qty(order.receivedQuantity) })} {unitText(order.unit)}</span>
                                <OrderStatusBadge status={order.status} />
                            </li>
                        ))}
                    </ul>
                )}
            </section>

            <section className={sectionClass} aria-labelledby="partij-receipts">
                <h3 id="partij-receipts" className={h3}>{t('partij_receipts', { pallets: String(pallets.length) })}</h3>
                {partij.receipts.length === 0 ? <p className="text-sm text-gray-600">{t('partij_receipts_none')}</p> : (
                    <ul className="divide-y divide-gray-100 text-sm">
                        {partij.receipts.map((receipt) => (
                            <li key={receipt.palletContentLineId} className="flex flex-wrap items-center gap-x-2 gap-y-0.5 py-1.5" data-tour="partij-receipt" data-tour-key={receipt.palletReference}>
                                <Link href={`/inspection?palletId=${receipt.palletId}`} className={`${link} font-semibold`}>{receipt.palletReference}</Link>
                                <span className="text-xs text-gray-600">({logisticsKey(receipt.palletStatus) ? t(logisticsKey(receipt.palletStatus)!) : receipt.palletStatus})</span>
                                <span>· {receipt.productName} · {receipt.productVariantName}</span>
                                <span className="text-gray-700">· <Link href={`/purchase-orders?id=${receipt.supplyCommitmentId}`} className={link}>{receipt.orderNumber}</Link></span>
                                <span className="font-mono">· {qty(receipt.quantity)} {unitText(receipt.unit)} · {formatKg(receipt.netWeightKg, locale)}</span>
                                <span className="font-mono text-gray-700">· {t('partij_on_hand_now', { quantity: qty(receipt.onHandQuantity) })}</span>
                                <QualityBadge status={receipt.qualityStatus} />
                                {receipt.kind !== 'Receipt' && <span className="text-xs text-gray-600">{t(receipt.kind === 'Correction' ? 'partij_kind_correction' : 'partij_kind_split_off')}</span>}
                                {receipt.lotNumber !== partij.number && <span className="text-xs text-gray-600">{t('partij_recorded_lot', { lot: receipt.lotNumber })}</span>}
                            </li>
                        ))}
                    </ul>
                )}
            </section>

            <section className={sectionClass} aria-labelledby="partij-sales">
                <h3 id="partij-sales" className={h3}>{t('partij_sales')}</h3>
                {partij.salesOrders.length === 0 ? <p className="text-sm text-gray-600">{t('partij_sales_none')}</p> : (
                    <ul className="divide-y divide-gray-100 text-sm">
                        {partij.salesOrders.map((sale) => (
                            <li key={`${sale.salesOrderId}-${sale.unit}`} className="flex flex-wrap items-center gap-x-2 gap-y-0.5 py-1.5" data-tour="partij-sale" data-tour-key={sale.orderNumber ?? sale.salesOrderId}>
                                <Link href={`/sales-orders?orderId=${sale.salesOrderId}`} className={`${link} font-semibold`}>{sale.orderNumber ?? `#${sale.salesOrderId}`}</Link>
                                {sale.salesPartijId !== null && <span>· {t('partij_kind_sales')} <Link href={`/partijen?id=${sale.salesPartijId}`} className={link}>{sale.salesPartijNumber ?? `#${sale.salesPartijId}`}</Link></span>}
                                <span>· {sale.customerName ?? '–'}</span>
                                {sale.reservedQuantity > 0 && <span className="font-mono">· {t('partij_reserved', { quantity: qty(sale.reservedQuantity) })} {unitText(sale.unit)}</span>}
                                {sale.shippedQuantity > 0 && <span className="font-mono">· {t('partij_shipped', { quantity: qty(sale.shippedQuantity) })} {unitText(sale.unit)}</span>}
                            </li>
                        ))}
                    </ul>
                )}
            </section>
            </>}

            <PartijCostsSection partij={partij} />

            <section className={sectionClass} aria-labelledby="partij-history">
                <h3 id="partij-history" className={h3}>{t('partij_history')}</h3>
                <ul className="space-y-1 text-sm">
                    {partij.history.map((event, index) => (
                        <li key={index} className="break-words" data-tour="partij-history-event" data-tour-key={event.type}>
                            <span className="text-gray-600">{formatDateTime(event.occurredAt, locale)}</span>{' · '}
                            {event.type === 'Created' && t('partij_event_created', { number: event.newValue ?? '', name: event.actorReference })}
                            {event.type === 'NumberCorrected' && t('partij_event_corrected', { from: event.oldValue ?? '', to: event.newValue ?? '', name: event.actorReference, reason: event.reason ?? '' })}
                            {event.type === 'NumberChanged' && t('partij_event_changed', { from: event.oldValue ?? '', to: event.newValue ?? '', name: event.actorReference })}
                            {event.type === 'OrderLinked' && (
                                <>
                                    {t('partij_event_linked', { name: event.actorReference })}{' '}
                                    {event.supplyCommitmentId !== null && <Link href={`/purchase-orders?id=${event.supplyCommitmentId}`} className={link}>{partij.orders.find((order) => order.id === event.supplyCommitmentId)?.orderNumber ?? `#${event.supplyCommitmentId}`}</Link>}
                                    {event.reason && <> · {event.reason}</>}
                                </>
                            )}
                        </li>
                    ))}
                </ul>
            </section>
        </article>
    );
}

// Nummer corrigeren (met verplichte reden; de server controleert opnieuw op dubbelen) en verwijderen (alleen zonder koppelingen).
function PartijActions({ partij, onChanged, onDeleted }: { partij: Detail; onChanged: () => void; onDeleted: () => void }) {
    const { t } = useTranslation();
    // Een verkoop-partij waarvan de order nog niets gereserveerd of geleverd heeft: aanpassen zonder reden. Anders altijd met reden.
    const adjustable = partij.kind === 'Sales' && !partij.numberFixed;
    const [correcting, setCorrecting] = useState(false);
    const [number, setNumber] = useState(partij.number);
    const [reason, setReason] = useState('');
    const [errors, setErrors] = useState<Record<string, string[]>>({});
    const [error, setError] = useState<ApiError | null>(null);
    const [busy, setBusy] = useState(false);
    const [confirmDelete, setConfirmDelete] = useState(false);

    const correct = async (event: FormEvent) => {
        event.preventDefault();
        if (busy) return;
        const found: Record<string, string[]> = {};
        if (number.trim() === '') found.number = [t('supply_error_required')];
        if (reason.trim() === '' && !adjustable) found.reason = [t('partij_link_reason_required')];
        setErrors(found);
        if (Object.keys(found).length > 0) return;
        setBusy(true);
        setError(null);
        try {
            await correctPartijNumber(partij.id, { number: number.trim(), reason: reason.trim() === '' ? null : reason.trim() });
            setCorrecting(false);
            setReason('');
            onChanged();
        } catch (caught) {
            const apiError = toApiError(caught);
            if (isPartijExistsConflict(apiError.body)) setErrors({ number: [t('partij_correct_exists', { number: apiError.body.partij.number })] });
            else { setError(apiError); setErrors(apiError.fieldErrors); }
        } finally {
            setBusy(false);
        }
    };

    const remove = async () => {
        if (busy) return;
        setBusy(true);
        setError(null);
        try {
            await deletePartij(partij.id);
            onDeleted();
        } catch (caught) {
            setError(toApiError(caught));
        } finally {
            setBusy(false);
        }
    };

    return (
        <div className="mt-2 space-y-2">
            {error && <ErrorNotice error={error} />}
            <div className="flex flex-wrap gap-2">
                <button type="button" className={secondaryButtonClass} data-tour="partij-correct-open" aria-expanded={correcting} onClick={() => { setCorrecting((open) => !open); setNumber(partij.number); }}>
                    {t(adjustable ? 'partij_adjust' : 'partij_correct')}
                </button>
                {!partij.linked && partij.kind !== 'Sales' && (
                    confirmDelete ? (
                        <>
                            <button type="button" className={`${secondaryButtonClass} border-red-300 text-red-800`} disabled={busy} data-tour="partij-delete-confirm" onClick={() => void remove()}>{t('partij_delete_confirm')}</button>
                            <button type="button" className={secondaryButtonClass} onClick={() => setConfirmDelete(false)}>{t('partij_link_cancel')}</button>
                        </>
                    ) : <button type="button" className={secondaryButtonClass} data-tour="partij-delete" onClick={() => setConfirmDelete(true)}>{t('partij_delete')}</button>
                )}
            </div>
            {partij.linked && <p className="text-xs text-gray-600">{t(partij.kind === 'Sales' ? (adjustable ? 'partij_sales_adjustable_hint' : 'partij_sales_fixed_hint') : 'partij_fixed_hint')}</p>}
            {correcting && (
                <form noValidate onSubmit={correct} className="grid grid-cols-1 gap-2 rounded border border-gray-300 bg-white p-2 sm:grid-cols-3" data-tour="partij-correct-form">
                    <div>
                        <label htmlFor="partijCorrectNumber-input" className="text-xs font-medium text-gray-700">{t('partij_correct_number')} *</label>
                        <input {...fieldProps('partijCorrectNumber', errors.number)} value={number} maxLength={MAX_PARTIJ_NUMBER_LENGTH} onChange={(event) => setNumber(event.target.value)} className={`${inputClass} font-mono`} />
                        <FieldError id="partijCorrectNumber" messages={errors.number} />
                    </div>
                    <div className="sm:col-span-2">
                        <label htmlFor="partijCorrectReason-input" className="text-xs font-medium text-gray-700">{t('partij_link_reason')}{adjustable ? ` (${t('partij_reason_optional')})` : ' *'}</label>
                        <input {...fieldProps('partijCorrectReason', errors.reason)} value={reason} maxLength={MAX_PARTIJ_REASON_LENGTH} onChange={(event) => setReason(event.target.value)} className={inputClass} />
                        <FieldError id="partijCorrectReason" messages={errors.reason} />
                    </div>
                    <div className="sm:col-span-3">
                        <button type="submit" className={primaryButtonClass} disabled={busy} data-tour="partij-correct-save">{busy ? t('supply_saving') : t('partij_correct_save')}</button>
                    </div>
                </form>
            )}
        </div>
    );
}
