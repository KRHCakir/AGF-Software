'use client';

import Link from 'next/link';
import { useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { formatCalendarDate } from '../../lib/calendarDate';
import { deliveryCsv, recallCsv } from '../../lib/partijTrace';
import type { PartijDetail, SalesLotTraceLine, SalesLotTraceSource } from '../../types/partij';
import { secondaryButtonClass } from '../supply/SuccessPanel';

// Feedback 22 november: herleidbaarheid in beide richtingen, af te drukken (ook als PDF via de afdrukdialoog) en als CSV te exporteren
// voor een recall. Inkoop-partij: waar alles naartoe ging en wat er nog ligt; verkoop-partij: uit welke inkoop-partijen het kwam.

const link = 'text-blue-700 underline hover:text-blue-900';

// Afdrukken: alleen dit overzicht, in een eigen venster (geopend in de klik, anders blokkeert de browser het). De inhoud is de door React
// opgebouwde (en dus ge-escapete) HTML van het overzicht.
function printSection(element: HTMLElement | null, title: string): boolean {
    if (element === null) return true;
    const target = window.open('', '_blank');
    if (target === null) return false;
    const doc = target.document;
    doc.title = title;
    const style = doc.createElement('style');
    style.textContent = 'body{font-family:Arial,sans-serif;font-size:12px;margin:16px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #999;padding:3px 6px;text-align:left}'
        + 'button,[data-print-hide]{display:none}a{color:inherit;text-decoration:none}h2,h3{margin:8px 0}';
    doc.head.appendChild(style);
    doc.body.innerHTML = element.outerHTML;
    target.focus();
    target.print();
    return true;
}

function download(csv: string, name: string) {
    const url = URL.createObjectURL(new Blob(['﻿', csv], { type: 'text/csv;charset=utf-8' }));
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = name;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

function Actions({ onPrint, onCsv, blocked }: { onPrint: () => void; onCsv: () => void; blocked: boolean }) {
    const { t } = useTranslation();
    return (
        <span className="ml-auto flex flex-wrap items-center gap-2" data-print-hide>
            <button type="button" className={secondaryButtonClass} onClick={onPrint} data-tour="partij-trace-print">{t('partij_trace_print')}</button>
            <button type="button" className={secondaryButtonClass} onClick={onCsv} data-tour="partij-trace-csv">{t('partij_trace_csv')}</button>
            {blocked && <span role="alert" className="text-xs text-amber-900">{t('so_slip_popup_blocked')}</span>}
        </span>
    );
}

export default function PartijTrace({ partij }: { partij: PartijDetail }) {
    const { t, locale } = useTranslation();
    const section = useRef<HTMLElement>(null);
    const [blocked, setBlocked] = useState(false);
    const qty = (value: number) => formatQuantity(value, locale);
    const date = (iso: string | null) => (iso === null ? '–' : formatCalendarDate(iso, locale));
    const format = { number: qty, date: (iso: string) => formatCalendarDate(iso, locale) };
    const print = () => setBlocked(!printSection(section.current, `${t('partij_trace_title')} ${partij.number}`));

    if (partij.recall) {
        const recall = partij.recall;
        const csv = () => download(recallCsv(partij.number, recall, {
            lot: t('partij_trace_lot'), received: t('partij_trace_received'), shipped: t('partij_trace_shipped'), returned: t('partij_trace_returned'),
            rejected: t('partij_trace_rejected'), onHand: t('partij_trace_on_hand'), difference: t('partij_trace_difference'), salesLot: t('partij_trace_sales_lot'),
            customer: t('partij_trace_customer'), reference: t('partij_trace_reference'), quantity: t('partij_trace_quantity'), date: t('partij_trace_date'),
            directInvoice: t('partij_trace_direct_invoice'),
        }, format), `Recall ${partij.number}.csv`);
        const totals: [string, number, boolean][] = [
            ['partij_trace_received', recall.received, false], ['partij_trace_shipped', recall.shipped, false], ['partij_trace_returned', recall.returned, false],
            ['partij_trace_rejected', recall.rejected, false], ['partij_trace_on_hand', recall.onHand, false], ['partij_trace_difference', recall.difference, recall.difference !== 0],
        ];
        return (
            <section ref={section} className="space-y-2 rounded-md border border-gray-200 bg-white p-3" data-testid="partij-recall" data-tour="partij-recall">
                <div className="flex flex-wrap items-center gap-2">
                    <h3 className="font-semibold text-gray-900">{t('partij_trace_recall_title', { lot: partij.number })}</h3>
                    <Actions onPrint={print} onCsv={csv} blocked={blocked} />
                </div>
                <p className="text-xs text-gray-600">{t('partij_trace_recall_text')}</p>
                <dl className="grid grid-cols-2 gap-2 text-sm sm:grid-cols-3 lg:grid-cols-6">
                    {totals.map(([key, value, warn]) => (
                        <div key={key} className={`rounded border px-2 py-1 ${warn ? 'border-red-300 bg-red-50' : 'border-gray-200'}`} data-testid={key}>
                            <dt className="text-xs text-gray-600">{t(key)}</dt>
                            <dd className={`tabular-nums font-semibold ${warn ? 'text-red-800' : 'text-gray-900'}`}>{qty(value)} {recall.unit ?? ''}</dd>
                        </div>
                    ))}
                </dl>
                {recall.difference !== 0 && <p role="alert" className="text-sm text-red-800">{t('partij_trace_difference_text', { quantity: qty(recall.difference) })}</p>}
                {recall.destinations.length + recall.directInvoices.length === 0
                    ? <p className="text-sm text-gray-700">{t('partij_trace_nothing_shipped')}</p>
                    : (
                        <div className="overflow-x-auto">
                            <table className="min-w-full text-sm">
                                <thead><tr className="text-left text-xs text-gray-600">
                                    <th className="py-1 pr-2">{t('partij_trace_sales_lot')}</th><th className="py-1 pr-2">{t('partij_trace_customer')}</th>
                                    <th className="py-1 pr-2">{t('partij_trace_reference')}</th><th className="py-1 pr-2 text-right">{t('partij_trace_quantity')}</th>
                                    <th className="py-1 pr-2">{t('partij_trace_date')}</th>
                                </tr></thead>
                                <tbody>
                                    {[...recall.destinations.map((d) => ({ d, invoice: false })), ...recall.directInvoices.map((d) => ({ d, invoice: true }))].map(({ d, invoice }, index) => (
                                        <tr key={index} className="border-t border-gray-100" data-testid="partij-recall-row">
                                            <td className="py-1 pr-2 font-mono">{d.salesPartijId !== null ? <Link href={`/partijen?id=${d.salesPartijId}`} className={link}>{d.salesPartijNumber}</Link> : d.salesPartijNumber ?? '–'}</td>
                                            <td className="py-1 pr-2">{d.customerName ?? '–'}</td>
                                            <td className="py-1 pr-2">
                                                {invoice ? `${t('partij_trace_direct_invoice')} ${d.reference ?? ''}`
                                                    : d.salesOrderId !== null ? <Link href={`/sales-orders?orderId=${d.salesOrderId}`} className={link}>{d.reference}</Link> : d.reference}
                                            </td>
                                            <td className="py-1 pr-2 text-right tabular-nums">{qty(d.quantity)} {d.unit ?? ''}</td>
                                            <td className="py-1 pr-2">{date(d.date)}</td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    )}
                {recall.directInvoices.length > 0 && <p className="text-xs text-gray-600">{t('partij_trace_direct_invoice_note')}</p>}
            </section>
        );
    }

    if (partij.delivery) {
        const trace = partij.delivery;
        const csv = () => download(deliveryCsv(partij.number, trace, {
            lot: t('partij_trace_lot'), customer: t('partij_trace_customer'), reference: t('partij_trace_reference'), date: t('partij_trace_delivery_date'),
            product: t('partij_trace_product'), quantity: t('partij_trace_quantity'), purchaseLot: t('partij_trace_purchase_lot'), supplier: t('partij_trace_supplier'),
            origin: t('partij_trace_origin'), status: t('partij_trace_status'), delivered: t('partij_trace_delivered'), planned: t('partij_trace_planned'),
        }, format), `Levering ${partij.number}.csv`);
        return (
            <section ref={section} className="space-y-2 rounded-md border border-gray-200 bg-white p-3" data-testid="partij-delivery" data-tour="partij-delivery">
                <div className="flex flex-wrap items-center gap-2">
                    <h3 className="font-semibold text-gray-900">{t('partij_trace_delivery_title', { lot: partij.number })}</h3>
                    <Actions onPrint={print} onCsv={csv} blocked={blocked} />
                </div>
                <p className="text-sm text-gray-800">
                    {trace.customerName ?? '–'}
                    {trace.reference && <> · {trace.salesOrderId !== null ? <Link href={`/sales-orders?orderId=${trace.salesOrderId}`} className={link}>{trace.reference}</Link> : trace.reference}</>}
                    {trace.sequence !== null && <> · {t('so_delivery_label', { sequence: String(trace.sequence) })}</>}
                    {' · '}{t(trace.shipped ? 'partij_trace_delivered_on' : 'partij_trace_planned_on', { date: date(trace.deliveryDate) })}
                </p>
                <div className="overflow-x-auto">
                    <table className="min-w-full text-sm">
                        <thead><tr className="text-left text-xs text-gray-600">
                            <th className="py-1 pr-2">{t('partij_trace_product')}</th><th className="py-1 pr-2 text-right">{t('partij_trace_quantity')}</th>
                            <th className="py-1 pr-2">{t('partij_trace_purchase_lot')}</th><th className="py-1 pr-2">{t('partij_trace_supplier')}</th>
                            <th className="py-1 pr-2">{t('partij_trace_origin')}</th>
                        </tr></thead>
                        <tbody>
                            {trace.lines.flatMap((line, lineIndex): { line: SalesLotTraceLine; source: SalesLotTraceSource | null; key: string }[] => (line.sources.length === 0
                                ? [{ line, source: null, key: `${lineIndex}` }]
                                : line.sources.map((source, index) => ({ line, source, key: `${lineIndex}-${index}` })))).map(({ line, source, key }) => (
                                <tr key={key} className="border-t border-gray-100" data-testid="partij-delivery-row">
                                    <td className="py-1 pr-2">{line.product ?? '–'}</td>
                                    <td className="py-1 pr-2 text-right tabular-nums">{qty(source?.quantity ?? line.quantity)} {line.unit ?? ''}</td>
                                    <td className="py-1 pr-2 font-mono">
                                        {source === null ? <span className="font-sans text-gray-600">{t('partij_trace_not_reserved')}</span>
                                            : source.partijId !== null ? <Link href={`/partijen?id=${source.partijId}`} className={link}>{source.number}</Link> : source.number ?? '–'}
                                    </td>
                                    <td className="py-1 pr-2">{source?.vendorName ?? '–'}</td>
                                    <td className="py-1 pr-2">{source?.origin ?? '–'}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            </section>
        );
    }
    return null;
}
