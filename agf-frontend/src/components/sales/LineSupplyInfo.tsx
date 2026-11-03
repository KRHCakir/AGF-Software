'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { unitLabelKey } from '../../lib/units';
import { coverageParts, freeState } from '../../lib/salesSupply';
import { SalesOrderLine } from '../../types/salesOrder';
import CoverageText from '../availability/CoverageText';

// Onder de kop van een orderregel: de bron (uit voorraad of uit verwachte aanvoer), de stand van precies deze variant en eenheid
// ("Voorraad 40 · Verwacht 20 · Vrij 10 colli") en hoe het nog niet gereserveerde deel gedekt is (oudste verkoop eerst). Alleen bij een
// open order; een oudere server zonder deze velden toont niets extra.
export default function LineSupplyInfo({ line }: { line: SalesOrderLine }) {
    const { t, locale } = useTranslation();
    const balance = line.balance ?? null;
    const parts = coverageParts(line.coverage);
    if (balance === null && line.supplySource === undefined) return null;
    const q = (value: number) => formatQuantity(value, locale);
    const unit = t(unitLabelKey(line.unit)).toLowerCase();
    const state = balance === null ? null : freeState(balance.free);
    return (
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1 border-b border-gray-100 px-3 py-1.5 text-sm" data-testid="line-supply-info" data-tour="sales-line-supply">
            {line.supplySource !== undefined && (
                <span className={`rounded border px-1.5 py-0.5 text-xs font-medium ${line.supplySource === 'Expected' ? 'border-amber-300 bg-amber-50 text-amber-900' : 'border-gray-300 bg-white text-gray-800'}`}
                    data-testid="line-source">
                    {t(line.supplySource === 'Expected' ? 'sales_source_expected' : 'sales_source_stock')}
                </span>
            )}
            {line.awaitingStock && <span className="rounded border border-amber-300 bg-amber-50 px-1.5 py-0.5 text-xs font-medium text-amber-900">{t('sales_awaiting_stock')}</span>}
            {balance !== null && (
                <span className="text-gray-800" data-testid="line-balance">
                    {t('sales_balance_line', { stock: q(balance.stock), expected: q(balance.expected), free: q(balance.free), unit })}
                    {state === 'shortage' && <span className="ml-1 rounded border border-red-300 bg-red-50 px-1.5 py-0.5 text-xs font-semibold uppercase text-red-800">{t('sales_shortage')}</span>}
                </span>
            )}
            {parts.length > 0 && <CoverageText parts={parts} reserved={line.reservedQuantity} testId="line-coverage" />}
        </div>
    );
}
