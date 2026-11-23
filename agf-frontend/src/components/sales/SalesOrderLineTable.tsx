'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatMoney, formatQuantity } from '../../lib/availability';
import { formatPercent } from '../../lib/orderLineMath';
import { formatPallets, palletWordKey } from '../../lib/purchaseLine';
import { unitLabelKey } from '../../lib/units';
import { shownKgTotal } from '../../lib/weightDisplay';
import type { SalesOrder } from '../../types/salesOrder';
import { OrderAmountsSummary, PurchaseLineHead, PurchaseLineTotalsRow, lineCell, purchaseLineBodyClass, purchaseLineRowClass, purchaseLineTableClass, usePurchaseLineLabels } from '../purchase/PurchaseLineTable';

// De regels van een verkooporder in de indeling van de inkooporder (feedback 22 november), alleen lezen: artikel, wat uit het artikel komt,
// kg, aantal met pallets, prijs, korting, btw en bedrag, de totalen onder dezelfde kolommen en daaronder de btw per tarief over de hele order
// (ook emballage). Reserveren, de stand en de herkomst per stuk staan zoals altijd per regel eronder (OrderLinePanel).
export default function SalesOrderLineTable({ order }: { order: SalesOrder }) {
    const { t, locale } = useTranslation();
    const labels = usePurchaseLineLabels();
    if (order.lines.length === 0) return null;
    const money = (value: number | null | undefined) => (value == null ? '–' : formatMoney(value, 'EUR', locale));
    const percent = (value: number | null | undefined) => (value == null ? '–' : `${formatPercent(value, locale)}%`);
    const totals = order.totals;
    return (
        <section className="rounded-md border border-gray-300 bg-white p-3" aria-labelledby="sales-lines-table" data-tour="sales-lines-table">
            <h3 id="sales-lines-table" className="mb-2 text-sm font-semibold text-gray-900">{t('sales_lines')}</h3>
            <table className={purchaseLineTableClass} data-testid="sales-order-lines">
                <PurchaseLineHead />
                <tbody className={purchaseLineBodyClass}>
                    {order.lines.map((line, index) => (
                        <tr key={line.id} className={purchaseLineRowClass(index)}>
                            <td {...lineCell('product', labels.product, '', true)}>
                                <span className="font-medium text-gray-900">{line.productName} · {line.productVariantName}</span>
                                <span className="block text-xs text-gray-600">{t(line.supplySource === 'Expected' ? 'sales_source_expected' : 'sales_source_stock')}</span>
                            </td>
                            <td {...lineCell('brand', labels.brand)}>{line.brand || '–'}</td>
                            <td {...lineCell('country', labels.country)}>{line.origin || '–'}</td>
                            <td {...lineCell('size', labels.size)}>{line.size || '–'}</td>
                            <td {...lineCell('gross', labels.gross)}>
                                <span>
                                    {line.grossWeightKg == null ? '–' : formatKg(line.grossWeightKg, locale)}
                                    {line.weightSource === 'Entered' && line.grossWeightKg != null && <span className="block text-xs font-medium text-amber-900">{t('po_line_gross_deviates')}</span>}
                                </span>
                            </td>
                            <td {...lineCell('net', labels.net)}>{line.netWeightKg == null ? '–' : formatKg(line.netWeightKg, locale)}</td>
                            <td {...lineCell('quantity', labels.quantity)}>
                                <span>
                                    {formatQuantity(line.quantity, locale)} <span className="text-xs text-gray-700">{t(unitLabelKey(line.unit)).toLowerCase()}</span>
                                    {line.pallets != null && <span className="block text-xs text-gray-600">{t(palletWordKey(line.pallets), { pallets: formatPallets(line.pallets, locale) })}</span>}
                                </span>
                            </td>
                            <td {...lineCell('packaging', labels.packaging)}>{line.packaging || '–'}</td>
                            <td {...lineCell('price', labels.price)}>{money(line.unitPrice)}</td>
                            <td {...lineCell('discount', labels.discount)}>{percent(line.discountPercentage)}</td>
                            <td {...lineCell('vat', labels.vat)}>{percent(line.vatPercentage)}</td>
                            <td {...lineCell('total', labels.total)}>{money(line.lineTotal)}</td>
                        </tr>
                    ))}
                </tbody>
                {totals && <PurchaseLineTotalsRow totals={{ ...totals, netKg: totals.netWeightKg, grossKg: totals.grossWeightKg,
                    shownNetKg: totals.netWeightKg === null ? null : shownKgTotal(order.lines.map((line) => line.netWeightKg ?? null)),
                    shownGrossKg: totals.grossWeightKg === null ? null : shownKgTotal(order.lines.map((line) => line.grossWeightKg ?? null)) }} currency="EUR" />}
            </table>
            <OrderAmountsSummary amounts={totals?.amounts} currency="EUR" />
        </section>
    );
}
