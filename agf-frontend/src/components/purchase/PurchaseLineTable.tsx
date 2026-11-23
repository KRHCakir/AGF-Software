'use client';

import type { ReactNode } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatMoney, formatQuantity } from '../../lib/availability';
import { ROW_PLAIN, ROW_STRIPE } from '../../lib/listRows';
import { formatPallets, type PurchaseLineTotals } from '../../lib/purchaseLine';
import { formatEuro } from '../../lib/documentCurrency';
import { isEuro, toEuro } from '../../lib/exchangeRate';
import { type OrderAmounts, formatPercent } from '../../lib/orderLineMath';

// De indeling van de inkooporderregel (feedback 11 november), gedeeld door Inkooporder aanmaken en het orderdetail: van links naar
// rechts Product · Merk · Land · Maat · Bruto kg · Netto kg · Aantal · Inhoud · Prijs · Korting · Btw · Bedrag, rijen om en om gekleurd
// (ook afgedrukt), en de totalen onder dezelfde kolommen. Op een telefoon (smaller dan 768 px) is elke regel een blokje met de veldnamen
// ervoor (data-label), zodat niets wegvalt; de totalen staan dan als samenvatting onderaan. Feedback 22 november: dezelfde indeling voor
// de verkooporder, met korting en btw per regel en onder de tabel de btw per tarief (OrderAmountsSummary).
export const PURCHASE_LINE_COLUMNS = ['product', 'brand', 'country', 'size', 'gross', 'net', 'quantity', 'packaging', 'price', 'discount', 'vat', 'total'] as const;
export type PurchaseLineColumn = (typeof PURCHASE_LINE_COLUMNS)[number];
const NUMERIC: ReadonlySet<PurchaseLineColumn> = new Set(['gross', 'net', 'quantity', 'price', 'discount', 'vat', 'total']);

export const purchaseLineTableClass = 'w-full border-collapse text-sm [print-color-adjust:exact] max-md:block';
export const purchaseLineBodyClass = 'max-md:block max-md:space-y-2';
export const purchaseLineRowClass = (index: number) =>
    `${index % 2 === 1 ? ROW_STRIPE : ROW_PLAIN} align-top max-md:block max-md:rounded max-md:border max-md:border-gray-300 max-md:p-2`;

// Een cel met haar kolomnaam voor de blokweergave; getallen rechts uitgelijnd.
// stacked: de cel blijft in de blokweergave een gewoon blok (links, zonder label) i.p.v. label-links/waarde-rechts.
export function lineCell(column: PurchaseLineColumn | 'extra', label: string, extra = '', stacked = false) {
    const numeric = column !== 'extra' && NUMERIC.has(column);
    return {
        'data-label': label,
        'data-column': column,
        className: `px-2 py-1.5 ${numeric ? 'text-right tabular-nums' : ''} max-md:px-0 max-md:py-0.5 ${stacked ? 'max-md:block max-md:text-left max-md:before:hidden' : 'max-md:flex max-md:items-start max-md:justify-between max-md:gap-3 max-md:text-right max-md:before:shrink-0 max-md:before:text-left max-md:before:text-xs max-md:before:font-medium max-md:before:text-gray-600 max-md:before:content-[attr(data-label)]'} ${extra}`,
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
// Valuta-ronde: het bedrag met de valuta van het document, en bij een andere valuta dan euro het bedrag in euro eronder (≈ € …).
export function PurchaseLineTotalsRow({ totals, trailingCells = 0, currency = null, rate = null }: { totals: PurchaseLineTotals; trailingCells?: number; currency?: string | null; rate?: number | null }) {
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
                    {missing(totals.linesWithoutPallets, 'po_line_lines_without_pallets')}
                </td>
                <td className="max-md:hidden" colSpan={3} />
                <td {...lineCell('gross', labels.gross)} data-testid="line-totals-gross">{kg(totals.shownGrossKg === undefined ? totals.grossKg : totals.shownGrossKg)}{missing(totals.linesWithoutGross, 'po_line_lines_without_kg')}</td>
                <td {...lineCell('net', labels.net)} data-testid="line-totals-net">{kg(totals.shownNetKg === undefined ? totals.netKg : totals.shownNetKg)}{missing(totals.linesWithoutNet, 'po_line_lines_without_kg')}</td>
                <td {...lineCell('quantity', labels.quantity)} data-testid="line-totals-quantity">{formatQuantity(totals.quantity, locale)}</td>
                <td className="max-md:hidden" colSpan={4} />
                <td {...lineCell('total', labels.total)} data-testid="line-totals-amount">
                    {totals.amount === null ? '–' : formatMoney(totals.amount, currency, locale)}
                    {totals.amount !== null && currency !== null && !isEuro(currency) && rate !== null && (
                        <span className="block text-xs font-normal text-gray-700" data-testid="line-totals-euro">{t('currency_euro_hint', { amount: formatEuro(toEuro(totals.amount, rate), locale) })}</span>
                    )}
                    {missing(totals.linesWithoutPrice, 'po_line_lines_without_price')}
                </td>
                {trailingCells > 0 && <td className="max-md:hidden" colSpan={trailingCells} />}
            </tr>
        </tfoot>
    );
}

// Onder de regels (feedback 22 november): totaal excl. btw, de btw per tarief (over welk bedrag) en totaal incl. btw. Regels zonder prijs
// of zonder btw-tarief staan erbij, zodat een totaal nooit stilzwijgend onvolledig is. Valuta zoals de order; bij een andere valuta dan
// euro het totaal in euro eronder.
export function OrderAmountsSummary({ amounts, currency = null, rate = null }: { amounts: OrderAmounts | undefined; currency?: string | null; rate?: number | null }) {
    const { t, locale } = useTranslation();
    if (amounts === undefined || amounts.net === null) return null;
    const money = (value: number | null) => (value === null ? '–' : formatMoney(value, currency, locale));
    const percent = (value: number) => formatPercent(value, locale);
    const row = 'flex items-baseline justify-between gap-6';
    return (
        <dl className="ml-auto mt-2 w-full max-w-sm space-y-1 rounded border border-gray-300 bg-gray-50 p-3 text-sm tabular-nums" data-testid="order-amounts" data-tour="order-amounts">
            <div className={row}><dt>{t('order_amounts_net')}</dt><dd data-testid="order-amounts-net">{money(amounts.net)}</dd></div>
            {amounts.vatRates.map((vat) => (
                <div key={vat.percentage} className={`${row} text-gray-700`} data-testid={`order-amounts-vat-${vat.percentage}`}>
                    <dt>{t('order_amounts_vat_rate', { rate: percent(vat.percentage), net: money(vat.net) })}</dt>
                    <dd>{money(vat.vat)}</dd>
                </div>
            ))}
            <div className={`${row} border-t border-gray-300 pt-1 font-semibold`}>
                <dt>{t('order_amounts_gross')}</dt>
                <dd data-testid="order-amounts-gross">
                    {money(amounts.gross)}
                    {currency !== null && !isEuro(currency) && rate !== null && amounts.gross !== null && (
                        <span className="block text-xs font-normal text-gray-700">{t('currency_euro_hint', { amount: formatEuro(toEuro(amounts.gross, rate), locale) })}</span>
                    )}
                </dd>
            </div>
            {amounts.linesWithoutPrice > 0 && <p className="text-xs text-gray-600" data-testid="order-amounts-without-price">{t('po_line_lines_without_price', { count: String(amounts.linesWithoutPrice) })}</p>}
            {amounts.linesWithoutVat > 0 && <p className="text-xs text-amber-900" data-testid="order-amounts-without-vat">{t('order_amounts_lines_without_vat', { count: String(amounts.linesWithoutVat) })}</p>}
        </dl>
    );
}
