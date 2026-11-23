'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { type LineProgressStatus, activeLines, lineProgress } from '../../lib/orderLineProgress';
import type { PurchaseOrderLine } from '../../types/purchaseOrder';

const STATUS_STYLE: Record<LineProgressStatus, string> = {
    Ordered: 'border-gray-300 bg-white text-gray-800',
    Partial: 'border-amber-300 bg-amber-50 text-amber-900',
    Received: 'border-emerald-300 bg-emerald-50 text-emerald-900',
    Closed: 'border-gray-300 bg-gray-100 text-gray-800',
};

// Per regel besteld, ontvangen, open, partijnummer en status (in de inkooporderlijst uitgeklapt en op het detail). Compact, zodat ook een
// order met 30 regels in één oogopslag leesbaar is; op een smal scherm staat elke regel als één blok.
export default function OrderLineProgress({ lines: allLines }: { lines: PurchaseOrderLine[] }) {
    const lines = activeLines(allLines);
    const { t, locale } = useTranslation();
    const q = (value: number) => formatQuantity(value, locale);
    const number = new Map(lines.map((line, index) => [line.id, index + 1]));
    const cell = 'px-2 py-1 align-top';
    return (
        <div className="max-h-[32rem] overflow-y-auto rounded border border-gray-200" data-testid="order-line-progress" data-tour="order-line-progress">
            <table className="block w-full text-xs md:table md:text-sm">
                <thead className="sticky top-0 hidden bg-gray-50 text-left font-semibold uppercase tracking-wide text-gray-600 md:table-header-group">
                    <tr>
                        <th scope="col" className={cell}>#</th>
                        <th scope="col" className={cell}>{t('supply_variant')}</th>
                        <th scope="col" className={cell}>{t('partij_field')}</th>
                        <th scope="col" className={`${cell} text-right`}>{t('receipt_ordered')}</th>
                        <th scope="col" className={`${cell} text-right`}>{t('receipt_received')}</th>
                        <th scope="col" className={`${cell} text-right`}>{t('receipt_open')}</th>
                        <th scope="col" className={cell}>{t('po_col_status')}</th>
                    </tr>
                </thead>
                <tbody className="block divide-y divide-gray-100 md:table-row-group">
                    {lines.map((line, index) => {
                        const p = lineProgress(line);
                        return (
                            <tr key={line.id} className={`grid grid-cols-[2rem_1fr] gap-x-1 py-1 md:table-row md:py-0 ${index % 2 === 1 ? 'md:bg-gray-50/60' : ''}`} data-testid={`order-line-progress-${line.id}`}>
                                <td className={`${cell} row-span-3 font-mono text-gray-600 md:row-span-1`}>{index + 1}</td>
                                <td className={cell}>
                                    <span className="font-medium text-gray-900">{line.productName} · {line.productVariantName}</span>
                                    {line.origin && <span className="text-gray-600"> · {line.origin}</span>}
                                    {line.splitFromLineId != null && <span className="block text-gray-700">{t('po_line_split_from', { line: String(number.get(line.splitFromLineId) ?? line.splitFromLineId) })}</span>}
                                </td>
                                <td className={`${cell} font-mono`}>
                                    <span className="text-gray-600 md:hidden">{t('partij_field')} </span>{line.partijNumber ?? line.lotNumber ?? '–'}
                                </td>
                                <td className={`${cell} col-start-2 md:text-right`} colSpan={1}>
                                    <span className="md:hidden">{t('receipt_ordered')} </span>{q(p.ordered)}
                                    <span className="md:hidden"> · {t('receipt_received')} {q(p.received)} · {t('receipt_open')} {q(p.open)}</span>
                                </td>
                                <td className={`${cell} hidden text-right md:table-cell`}>{q(p.received)}</td>
                                <td className={`${cell} hidden text-right font-semibold md:table-cell`}>{q(p.open)}</td>
                                <td className={`${cell} col-start-2`}>
                                    <span className={`inline-flex rounded border px-1.5 py-0.5 ${STATUS_STYLE[p.status]}`}>{t(`po_line_status_${p.status.toLowerCase()}`)}</span>
                                    {p.cancelled > 0 && <span className="ml-1 text-gray-700">{t('po_line_cancelled', { quantity: q(p.cancelled) })}</span>}
                                </td>
                            </tr>
                        );
                    })}
                </tbody>
            </table>
        </div>
    );
}
