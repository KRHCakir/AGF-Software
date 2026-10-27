'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import type { WeightTotals } from '../../lib/lineWeight';
import { formatKgOneDecimal } from '../../lib/quantityKg';
import { unitLabelKey } from '../../lib/units';
import type { PackageUnit } from '../../types/availability';

// Totaalregel onder de orderregels of aanvoerregels: aantallen per eenheid (nooit samengevoegd), netto en bruto kg, en hoeveel
// regels nog geen kg hebben. Alleen weergave.
export default function KgTotals({ totals, lineCount, testId }: { totals: WeightTotals; lineCount: number; testId: string }) {
    const { t, locale } = useTranslation();
    if (totals.quantities.length === 0) return null;
    const withoutKg = Math.max(totals.linesWithoutNet, totals.linesWithoutGross);
    return (
        <p className="flex flex-wrap items-baseline gap-x-3 gap-y-0.5 rounded border border-gray-200 bg-gray-50 px-3 py-1.5 text-sm" data-testid={testId}>
            <span className="font-semibold text-gray-900">{t('line_kg_total')}</span>
            <span className="text-gray-900">{totals.quantities.map((entry) => `${formatQuantity(entry.quantity, locale)} ${t(unitLabelKey(entry.unit as PackageUnit)).toLowerCase()}`).join(' + ')}</span>
            {totals.linesWithoutNet < lineCount && <span className="font-medium text-gray-900">{t('quantity_kg_net', { net: formatKgOneDecimal(totals.netKg, locale) })}</span>}
            {totals.linesWithoutGross < lineCount && <span className="text-gray-800">{t('quantity_kg_gross', { gross: formatKgOneDecimal(totals.grossKg, locale) })}</span>}
            {withoutKg > 0 && <span className="text-xs text-gray-600">{t(withoutKg === 1 ? 'line_kg_lines_without_one' : 'line_kg_lines_without', { count: String(withoutKg) })}</span>}
        </p>
    );
}
