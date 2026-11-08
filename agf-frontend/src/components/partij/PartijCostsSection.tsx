'use client';

import Link from 'next/link';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatMoney } from '../../lib/availability';
import { formatCalendarDate } from '../../lib/calendarDate';
import { formatRate } from '../../lib/exchangeRate';
import type { PartijDetail } from '../../types/partij';
import { sectionClass } from '../purchase/sectionClass';

const link = 'font-mono text-blue-800 underline underline-offset-2';

// Kosten op deze partij (P2): per soort (Handling, Zeevracht, ...) de kostenregels met factuur, leverancier en bedrag excl. btw, en het
// totaal van alleen deze partij. Kosten op gekoppelde partijen van de andere soort staan er apart onder en tellen niet mee: elke kostenpost
// hangt aan precies één partij, dus niets telt dubbel. Geen resultaat of winst: dat komt in P3.
export default function PartijCostsSection({ partij }: { partij: PartijDetail }) {
    const { t, locale } = useTranslation();
    const money = (value: number) => formatMoney(value, 'EUR', locale);
    const costs = partij.costs ?? [];
    const kinds = [...new Set(costs.map((cost) => cost.kind))];
    const linked = partij.linkedCosts ?? [];
    return (
        <section className={sectionClass} aria-labelledby="partij-costs" data-tour="partij-costs">
            <h3 id="partij-costs" className="mb-2 text-sm font-semibold text-gray-900">{t('partij_costs')}</h3>
            {costs.length === 0 ? (
                <p className="text-sm text-gray-600">{t('partij_costs_none')} <Link href="/purchase-invoices" className="text-blue-800 underline underline-offset-2">{t('nav_purchase_invoices')}</Link></p>
            ) : (
                <div className="space-y-2 text-sm">
                    {kinds.map((kind) => {
                        const ofKind = costs.filter((cost) => cost.kind === kind);
                        return (
                            <div key={kind} data-tour="partij-cost-kind" data-tour-key={kind}>
                                <p className="flex justify-between font-medium text-gray-900"><span>{kind}</span><span className="font-mono">{money(ofKind.reduce((total, cost) => total + cost.amount, 0))}</span></p>
                                <ul className="divide-y divide-gray-100">
                                    {ofKind.map((cost, index) => (
                                        <li key={`${cost.invoiceId}-${index}`} className="flex flex-wrap items-center justify-between gap-x-2 gap-y-0.5 py-1" data-tour="partij-cost" data-tour-key={cost.invoiceNumber ?? cost.invoiceId}>
                                            <span className="min-w-0 break-words">
                                                <Link href={`/purchase-invoices?id=${cost.invoiceId}`} className={link}>{cost.invoiceNumber ?? `#${cost.invoiceId}`}</Link>
                                                {' · '}{cost.vendorName ?? '–'}{cost.invoiceDate ? ` · ${formatCalendarDate(cost.invoiceDate, locale)}` : ''}
                                                {cost.description && cost.description !== kind && <span className="text-gray-600"> · {cost.description}</span>}
                                            </span>
                                            <span className="text-right font-mono">
                                                {money(cost.amount)}
                                                {cost.currency && cost.amountInCurrency != null && cost.exchangeRate != null && (
                                                    <span className="block text-xs text-gray-600" data-testid="partij-cost-in-currency">
                                                        {t('partij_cost_in_currency', { amount: formatMoney(cost.amountInCurrency, cost.currency, locale), rate: formatRate(cost.exchangeRate, locale) })}
                                                    </span>
                                                )}
                                            </span>
                                        </li>
                                    ))}
                                </ul>
                            </div>
                        );
                    })}
                    <p className="flex justify-between border-t border-gray-300 pt-1 font-semibold text-gray-900" data-testid="partij-cost-total">
                        <span>{t('partij_costs_total')}</span><span className="font-mono">{money(partij.costTotal ?? 0)}</span>
                    </p>
                </div>
            )}
            {linked.length > 0 && (
                <p className="mt-2 rounded border border-dashed border-gray-300 bg-gray-50 px-2 py-1 text-xs text-gray-700" data-testid="partij-linked-costs">
                    {t(partij.kind === 'Sales' ? 'partij_linked_costs_sales' : 'partij_linked_costs_purchase')}{' '}
                    {linked.map((item, index) => (
                        <span key={item.partijId}>{index > 0 && ', '}<Link href={`/partijen?id=${item.partijId}`} className={link}>{item.number}</Link> {money(item.costTotal)}</span>
                    ))}
                </p>
            )}
        </section>
    );
}
