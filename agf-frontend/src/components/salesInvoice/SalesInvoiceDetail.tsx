'use client';

import Link from 'next/link';
import { useState, type ReactNode } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatQuantity } from '../../lib/availability';
import { formatCalendarDate } from '../../lib/calendarDate';
import { formatEuro } from '../../lib/documentCurrency';
import { formatPercent } from '../../lib/orderLineMath';
import { finalizeBlockers, unitLabelKey } from '../../lib/salesInvoice';

import { toApiError } from '../../services/apiClient';
import { discardSalesInvoice, finalizeSalesInvoice, getSalesInvoicePdf } from '../../services/salesInvoiceService';
import type { SalesInvoice, SalesInvoiceLine } from '../../types/salesInvoice';
import { inputClass } from '../supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';
import { SalesInvoiceStatusBadge } from './SalesInvoiceBadges';

const CLEANUP_MS = 60_000;

// De PDF in een nieuw venster (bij de klik geopend, anders blokkeert de browser het) of als bestand.
export async function openInvoicePdf(invoice: Pick<SalesInvoice, 'id' | 'invoiceNumber'>, mode: 'view' | 'download') {
    const target = mode === 'view' ? window.open('', '_blank') : null;
    if (mode === 'view' && target === null) throw new Error('popup-blocked');
    let url: string;
    try {
        url = URL.createObjectURL(await getSalesInvoicePdf(invoice.id, mode === 'download'));
    } catch (caught) {
        target?.close();
        throw caught;
    }
    setTimeout(() => URL.revokeObjectURL(url), CLEANUP_MS);
    if (mode === 'view') {
        target!.location.href = url;
        return;
    }
    const link = window.document.createElement('a');
    link.href = url;
    link.download = invoice.invoiceNumber ? `Factuur ${invoice.invoiceNumber}.pdf` : 'Factuur concept.pdf';
    window.document.body.appendChild(link);
    link.click();
    link.remove();
}

// Een verkoopfactuur: kop met klant, datums en V-nummers, de regels (goederen apart van emballage en diensten), btw per tarief en de
// acties PDF, Definitief maken (met bevestiging; daarna niets meer te wijzigen) en Weggooien (alleen een concept). Children: het
// bewerkblok van een concept (volgende PR's).
export default function SalesInvoiceDetail({ invoice, onChanged, children }: { invoice: SalesInvoice; onChanged: (next: SalesInvoice) => void; children?: ReactNode }) {
    const { t, locale } = useTranslation();
    const [confirm, setConfirm] = useState<'finalize' | 'discard' | null>(null);
    const [reason, setReason] = useState('');
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const date = (value: string | null) => (value ? formatCalendarDate(value, locale) : '–');
    const concept = invoice.status === 'Concept';
    const blockers = finalizeBlockers(invoice);
    const goods = invoice.lines.filter((line) => line.lineKind === 'Product');
    const extras = invoice.lines.filter((line) => line.lineKind !== 'Product');
    const salesLots = [...new Set([...invoice.deliveries.map((d) => d.partijNumber), invoice.salesLotNumber].filter((n): n is string => !!n))];

    const run = async (action: () => Promise<SalesInvoice>) => {
        setBusy(true);
        setError(null);
        try {
            onChanged(await action());
            setConfirm(null);
            setReason('');
        } catch (caught) {
            const apiError = toApiError(caught);
            const fields = Object.values(apiError.fieldErrors).flat();
            setError(fields.length > 0 ? fields.join(' ') : apiError.message);
        } finally {
            setBusy(false);
        }
    };
    const pdf = (mode: 'view' | 'download') => {
        setError(null);
        openInvoicePdf(invoice, mode).catch((caught) => setError(caught instanceof Error && caught.message === 'popup-blocked' ? t('si_popup_blocked') : toApiError(caught).message));
    };
    const unit = (line: SalesInvoiceLine) => { const key = unitLabelKey(line.unit); return key ? t(key) : line.unit; };

    const lineRows = (lines: SalesInvoiceLine[], withGoods: boolean) => lines.map((line, index) => (
        <tr key={line.id} data-testid="si-line" data-line-kind={line.lineKind} className={index % 2 === 1 ? 'bg-gray-50' : 'bg-white'}>
            <td className="px-2 py-1 text-right font-mono">{formatQuantity(line.quantity, locale)}</td>
            <td className="px-2 py-1">{line.description ?? '–'}{line.revenueTypeName && <span className="block text-xs text-gray-600">{line.revenueTypeName}</span>}</td>
            {withGoods && <td className="px-2 py-1">{line.origin ?? ''}</td>}
            {withGoods && <td className="px-2 py-1 font-mono">{line.lotNumber ?? '–'}{line.ggn && <span className="block text-xs text-gray-600">GGN {line.ggn}</span>}</td>}
            {withGoods && <td className="px-2 py-1 text-right font-mono">{line.netWeightKg == null ? '' : formatKg(line.netWeightKg, locale)}</td>}
            <td className="px-2 py-1 text-right font-mono">{line.unitPrice == null ? <span className="text-red-800">{t('si_no_price')}</span> : formatEuro(line.unitPrice, locale)}</td>
            <td className="px-2 py-1">{unit(line)}</td>
            <td className="px-2 py-1 text-right font-mono">{line.discountPercentage === 0 ? '' : `${formatPercent(line.discountPercentage, locale)}%`}</td>
            <td className="px-2 py-1 text-right font-mono">{formatPercent(line.vatPercentage, locale)}%</td>
            <td className="px-2 py-1 text-right font-mono" data-testid="si-line-amount">{line.amount == null ? '–' : formatEuro(line.amount, locale)}</td>
        </tr>
    ));
    const table = (lines: SalesInvoiceLine[], withGoods: boolean, testId: string) => (
        <div className="overflow-x-auto rounded border border-gray-200">
            <table className="min-w-full text-sm" data-testid={testId}>
                <thead className="bg-gray-50 text-left text-xs font-semibold text-gray-700">
                    <tr>
                        <th className="px-2 py-1 text-right">{t('si_line_quantity')}</th>
                        <th className="px-2 py-1">{t('si_line_description')}</th>
                        {withGoods && <th className="px-2 py-1">{t('si_line_origin')}</th>}
                        {withGoods && <th className="px-2 py-1">{t('si_line_lot')}</th>}
                        {withGoods && <th className="px-2 py-1 text-right">{t('si_line_net_kg')}</th>}
                        <th className="px-2 py-1 text-right">{t('si_line_price')}</th>
                        <th className="px-2 py-1">{t('si_line_per')}</th>
                        <th className="px-2 py-1 text-right">{t('si_line_discount')}</th>
                        <th className="px-2 py-1 text-right">{t('si_line_vat')}</th>
                        <th className="px-2 py-1 text-right">{t('si_line_amount')}</th>
                    </tr>
                </thead>
                <tbody>{lineRows(lines, withGoods)}</tbody>
            </table>
        </div>
    );

    return (
        <section className="space-y-3 rounded-md border border-gray-200 bg-white p-3" data-testid="si-detail" data-tour="si-detail" aria-label={invoice.invoiceNumber ?? t('si_concept')}>
            <div className="flex flex-wrap items-start justify-between gap-2">
                <div>
                    <h2 className="text-lg font-semibold text-gray-900" data-testid="si-title">{invoice.invoiceNumber ?? t('si_concept')}</h2>
                    <SalesInvoiceStatusBadge status={invoice.status} stale={invoice.stale} />
                    <p className="text-sm text-gray-700">{invoice.kind === 'Direct' ? t('si_kind_direct') : t('si_kind_delivery')}</p>
                </div>
                <div className="flex flex-wrap gap-2">
                    <button type="button" data-tour="si-pdf" className={secondaryButtonClass} onClick={() => pdf('view')}>{t('si_pdf_view')}</button>
                    <button type="button" className={secondaryButtonClass} onClick={() => pdf('download')}>{t('si_pdf_download')}</button>
                    {concept && <button type="button" data-tour="si-finalize" className={primaryButtonClass} disabled={busy || blockers.length > 0} onClick={() => setConfirm('finalize')}>{t('si_finalize')}</button>}
                    {concept && <button type="button" data-tour="si-discard" className={secondaryButtonClass} disabled={busy} onClick={() => setConfirm('discard')}>{t('si_discard')}</button>}
                </div>
            </div>

            {concept && blockers.length > 0 && (
                <p role="note" data-testid="si-blockers" className="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900">{blockers.map((key) => t(key)).join(' ')}</p>
            )}
            {confirm === 'finalize' && (
                <div role="alertdialog" aria-labelledby="si-confirm-finalize" className="rounded border border-blue-300 bg-blue-50 p-3 text-sm" data-testid="si-confirm-finalize">
                    <p id="si-confirm-finalize" className="font-semibold text-gray-900">{t('si_finalize_confirm_title')}</p>
                    <p className="text-gray-800">{t('si_finalize_confirm_text', { total: invoice.totals.gross == null ? '–' : formatEuro(invoice.totals.gross, locale), customer: invoice.customerName ?? '' })}</p>
                    <div className="mt-2 flex flex-wrap gap-2">
                        <button type="button" data-tour="si-finalize-confirm" className={primaryButtonClass} disabled={busy} onClick={() => run(() => finalizeSalesInvoice(invoice.id, invoice.version))}>{t('si_finalize_confirm')}</button>
                        <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => setConfirm(null)}>{t('si_cancel')}</button>
                    </div>
                </div>
            )}
            {confirm === 'discard' && (
                <div role="alertdialog" aria-labelledby="si-confirm-discard" className="rounded border border-red-300 bg-red-50 p-3 text-sm" data-testid="si-confirm-discard">
                    <p id="si-confirm-discard" className="font-semibold text-gray-900">{t('si_discard_confirm_title')}</p>
                    <label htmlFor="si-discard-reason" className="mt-1 block text-xs font-medium text-gray-700">{t('si_discard_reason')}</label>
                    <input id="si-discard-reason" className={inputClass} value={reason} maxLength={500} onChange={(event) => setReason(event.target.value)} />
                    <div className="mt-2 flex flex-wrap gap-2">
                        <button type="button" className={primaryButtonClass} disabled={busy} onClick={() => run(() => discardSalesInvoice(invoice.id, invoice.version, reason.trim() === '' ? null : reason.trim()))}>{t('si_discard_confirm')}</button>
                        <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => setConfirm(null)}>{t('si_cancel')}</button>
                    </div>
                </div>
            )}
            {error && <p role="alert" className="text-sm text-red-800">{error}</p>}

            <dl className="grid grid-cols-1 gap-x-4 gap-y-1 text-sm sm:grid-cols-2 lg:grid-cols-3">
                <div><dt className="text-xs text-gray-600">{t('si_col_customer')}</dt><dd className="font-medium">{invoice.customerName ?? '–'}</dd>
                    {invoice.customerAddress && <dd className="whitespace-pre-line text-gray-700">{invoice.customerAddress}</dd>}
                    {invoice.customerVatNumber && <dd className="text-gray-700">{t('si_vat_number')} {invoice.customerVatNumber}</dd>}</div>
                <div><dt className="text-xs text-gray-600">{t('si_col_invoice_date')}</dt><dd>{concept ? '–' : date(invoice.invoiceDate)}</dd>
                    <dt className="text-xs text-gray-600">{t('si_col_delivery_date')}</dt><dd>{date(invoice.deliveryDate)}</dd>
                    <dt className="text-xs text-gray-600">{t('si_col_due_date')}</dt><dd>{date(invoice.dueDate)}</dd></div>
                <div><dt className="text-xs text-gray-600">{t('si_reference')}</dt><dd>{invoice.referenceNumber ?? '–'}</dd>
                    <dt className="text-xs text-gray-600">{t('si_col_lots')}</dt><dd className="font-mono" data-testid="si-sales-lots">{salesLots.length === 0 ? '–' : salesLots.join(', ')}</dd>
                    {invoice.deliveries.length > 0 && <dd className="flex flex-wrap gap-x-2">{[...new Map(invoice.deliveries.map((d) => [d.salesOrderId, d])).values()].map((d) => (
                        <Link key={d.salesOrderId} href={`/sales-orders?id=${d.salesOrderId}`} className="text-blue-800 underline underline-offset-2">{d.orderNumber}</Link>))}</dd>}</div>
            </dl>
            {invoice.status === 'Final' && <p className="text-xs text-gray-600">{t('si_finalized_by', { who: invoice.finalizedBy ?? '', when: invoice.finalizedAt ? formatCalendarDate(invoice.finalizedAt, locale) : '' })}</p>}
            {invoice.status === 'Discarded' && <p className="text-xs text-gray-600">{t('si_discarded_by', { who: invoice.discardedBy ?? '', reason: invoice.discardReason ?? '–' })}</p>}

            {children}

            {goods.length > 0 && table(goods, true, 'si-goods')}
            {extras.length > 0 && (<><h3 className="text-sm font-semibold text-gray-900">{t('si_extras')}</h3>{table(extras, false, 'si-extras')}</>)}

            <div className="ml-auto w-full max-w-sm text-sm" data-testid="si-totals" data-tour="si-totals">
                <div className="flex justify-between"><span>{t('si_total_net')}</span><span className="font-mono">{invoice.totals.net == null ? '–' : formatEuro(invoice.totals.net, locale)}</span></div>
                {invoice.totals.vatRates.map((rate) => (
                    <div key={rate.percentage} className="flex justify-between" data-testid="si-vat-rate">
                        <span>{t('si_vat_over', { rate: formatPercent(rate.percentage, locale), base: formatEuro(rate.net, locale) })}</span>
                        <span className="font-mono">{formatEuro(rate.vat, locale)}</span>
                    </div>
                ))}
                <div className="flex justify-between border-t border-gray-300 pt-1 font-semibold"><span>{t('si_total_gross')}</span><span className="font-mono" data-testid="si-gross">{invoice.totals.gross == null ? '–' : formatEuro(invoice.totals.gross, locale)}</span></div>
            </div>
        </section>
    );
}
