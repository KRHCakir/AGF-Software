'use client';

import type { ReactNode } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatMoney, formatQuantity } from '../../lib/availability';
import { ROW_PLAIN, ROW_STRIPE } from '../../lib/listRows';
import { formatPallets, type PurchaseLineTotals } from '../../lib/purchaseLine';

// De indeling van de inkooporderregel (feedback 11 november), gedeeld door Inkooporder aanmaken en het orderdetail: van links naar
// rechts Product · Merk · Land · Maat · Bruto kg · Netto kg · Aantal · Inhoud · Prijs · Totaal, rijen om en om gekleurd (ook
// afgedrukt), en de totalen onder dezelfde kolommen. Op een telefoon (smaller dan 768 px) is elke regel een blokje met de veldnamen
// ervoor (data-label), zodat niets wegvalt; de totalen staan dan als samenvatting onderaan.
export const PURCHASE_LINE_COLUMNS = ['product', 'brand', 'country', 'size', 'gross', 'net', 'quantity', 'packaging', 'price', 'total'] as const;
export type PurchaseLineColumn = (typeof PURCHASE_LINE_COLUMNS)[number];
const NUMERIC: ReadonlySet<PurchaseLineColumn> = new Set(['gross', 'net', 'quantity', 'price', 'total']);

export const purchaseLineTableClass = 'w-full border-collapse text-sm [print-color-adjust:exact] max-md:block';
export const purchaseLineBodyClass = 'max-md:block max-md:space-y-2';
export const purchaseLineRowClass = (index: number) =>
    `${index % 2 === 1 ? ROW_STRIPE : ROW_PLAIN} align-top max-md:block max-md:rounded max-md:border max-md:border-gray-300 max-md:p-2`;

// Een cel met haar kolomnaam voor de blokweergave; getallen rechts uitgelijnd.
export function lineCell(column: PurchaseLineColumn | 'extra', label: string, extra = '') {
    const numeric = column !== 'extra' && NUMERIC.has(column);
    return {
        'data-label': label,
        'data-column': column,
        className: `px-2 py-1.5 ${numeric ? 'text-right tabular-nums' : ''} max-md:flex max-md:items-start max-md:justify-between max-md:gap-3 max-md:px-0 max-md:py-0.5 max-md:text-right max-md:before:shrink-0 max-md:before:text-left max-md:before:text-xs max-md:before:font-medium max-md:before:text-gray-600 max-md:before:content-[attr(data-label)] ${extra}`,
    };
}

export function usePurchaseLineLabels(): Record<PurchaseLineColumn, string> {
    const { t } = useTranslation();
    return Object.fromEntries(PURCHASE_LINE_COLUMNS.map((column) => [column, t(`po_line_col_${column}`)])) as Record<PurchaseLineColumn, string>;
}

export function PurchaseLineHead({ trailing }: { trailing?: ReactNode }) {
    const labels = usePurchaseLineLabels();
    return (
        <thead className="bg-gray-50 text-left text-xs font-semibold uppercase tracking-wide text-gray-600 max-md:hidden">
            <tr>
                {PURCHASE_LINE_COLUMNS.map((column) => (
                    <th key={column} scope="col" className={`px-2 py-1.5 ${NUMERIC.has(column) ? 'text-right' : ''}`}>{labels[column]}</th>
                ))}
                {trailing}
            </tr>
        </thead>
    );
}

// Totaalrij onder dezelfde kolommen: aantal, bruto, netto en bedrag, met het totaal aantal pallets onder Product. Regels zonder kg,
// prijs of colli per pallet tellen niet mee; dat staat er dan bij, zodat een totaal nooit stilzwijgend onvolledig is.
export function PurchaseLineTotalsRow({ totals, trailingCells = 0 }: { totals: PurchaseLineTotals; trailingCells?: number }) {
    const { t, locale } = useTranslation();
    const labels = usePurchaseLineLabels();
    const missing = (count: number, key: string) => (count > 0 ? <span className="block text-xs font-normal text-gray-600">{t(key, { count: String(count) })}</span> : null);
    const kg = (value: number | null) => (value === null ? '–' : formatKg(value, locale));
    return (
        <tfoot className="border-t-2 border-gray-400 bg-gray-100 font-semibold text-gray-900 max-md:mt-2 max-md:block max-md:rounded max-md:border max-md:p-2" data-tour="supply-line-totals">
            <tr className="max-md:block">
                <td {...lineCell('product', t('po_line_col_pallets'))}>
                    <span className="max-md:hidden">{t('po_line_totals')} · </span>
                    <span data-testid="line-totals-pallets">{totals.pallets === null ? t('po_line_no_pallets') : t('po_line_pallets_total', { pallets: formatPallets(totals.pallets, locale) })}</span>
                    {missing(totals.pallets === null ? 0 : totals.linesWithoutPallets, 'po_line_lines_without_pallets')}
                </td>
                <td className="max-md:hidden" colSpan={3} />
                <td {...lineCell('gross', labels.gross)} data-testid="line-totals-gross">{kg(totals.grossKg)}{missing(totals.grossKg === null ? 0 : totals.linesWithoutGross, 'po_line_lines_without_kg')}</td>
                <td {...lineCell('net', labels.net)} data-testid="line-totals-net">{kg(totals.netKg)}{missing(totals.netKg === null ? 0 : totals.linesWithoutNet, 'po_line_lines_without_kg')}</td>
                <td {...lineCell('quantity', labels.quantity)} data-testid="line-totals-quantity">{formatQuantity(totals.quantity, locale)}</td>
                <td className="max-md:hidden" colSpan={2} />
                <td {...lineCell('total', labels.total)} data-testid="line-totals-amount">
                    {totals.amount === null ? '–' : formatMoney(totals.amount, null, locale)}
                    {missing(totals.amount === null ? 0 : totals.linesWithoutPrice, 'po_line_lines_without_price')}
                </td>
                {trailingCells > 0 && <td className="max-md:hidden" colSpan={trailingCells} />}
            </tr>
        </tfoot>
    );
}
