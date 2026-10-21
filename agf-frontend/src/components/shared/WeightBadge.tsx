'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg } from '../../lib/availability';
import { unitLabelKey } from '../../lib/units';
import { WEIGHT_BASIS_LABEL_KEYS, WEIGHT_METHOD_LABEL_KEYS, WeightBasis, WeightMethod } from '../../lib/weight';
import type { PackageUnit } from '../../types/availability';

// Toont bij een kg-getal hoe het tot stand kwam: gewogen, of berekend uit het verpakkingsgewicht (nooit als gewogen).
export function WeightBadge({ method, kgPerPackage, unit }: { method: WeightMethod; kgPerPackage: number | null; unit?: PackageUnit | null }) {
    const { t, locale } = useTranslation();
    const calculated = method === 'Calculated';
    const tone = calculated ? 'border-amber-300 bg-amber-50 text-amber-900' : 'border-gray-300 bg-gray-50 text-gray-700';
    return (
        <span data-weight-method={method} className={`inline-flex max-w-full flex-wrap items-center gap-1 rounded border px-1.5 py-0.5 text-xs ${tone}`}>
            <span>{t(WEIGHT_METHOD_LABEL_KEYS[method])}</span>
            {calculated && kgPerPackage !== null && (
                <span className="font-mono">{t('weight_per_package', { kg: formatKg(kgPerPackage, locale), unit: unit ? t(unitLabelKey(unit)).toLowerCase() : t('availability_col_unit').toLowerCase() })}</span>
            )}
        </span>
    );
}

// Per beschikbaarheidsrij: herkomst van de opgetelde kg (gewogen, berekend of gemengd).
export function WeightBasisBadge({ basis }: { basis: WeightBasis | null }) {
    const { t } = useTranslation();
    if (basis === null || basis === 'weighed') return null;
    return (
        <span data-weight-basis={basis} className="mt-1 inline-flex rounded border border-amber-300 bg-amber-50 px-1.5 py-0.5 text-xs text-amber-900">
            {t(WEIGHT_BASIS_LABEL_KEYS[basis])}
        </span>
    );
}
