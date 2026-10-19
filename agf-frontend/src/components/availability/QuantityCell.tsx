'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatQuantity } from '../../lib/availability';
import { AvailabilityQuantity, ExpectedUnregistered, PackageUnit } from '../../types/availability';

// Een bekende nul is een getal en wordt als "0" getoond (gedempt), nooit als streepje: de API levert hier altijd een
// werkelijk geteld aantal en gewogen gewicht. Onbekende waarden (null) krijgen een eigen, expliciete weergave.
export function QuantityCell({ value, emphasis = false }: { value: AvailabilityQuantity; emphasis?: boolean }) {
    const { locale } = useTranslation();
    const zero = value.quantity === 0 && value.netWeightKg === 0;
    const tone = zero ? 'text-gray-400' : 'text-gray-900';
    return (
        <div className="leading-tight" data-zero={zero ? 'true' : undefined}>
            <div className={emphasis && !zero ? `text-base font-semibold ${tone}` : tone}>{formatQuantity(value.quantity, locale)}</div>
            <div className={zero ? 'text-xs text-gray-400' : 'text-xs text-gray-500'}>{formatKg(value.netWeightKg, locale)}</div>
        </div>
    );
}

// Verwacht maar nog niet op een pallet: alleen een hoeveelheid; het gewicht is onbekend (null) en wordt nooit als 0 getoond.
export function ExpectedCell({ value }: { value: ExpectedUnregistered }) {
    const { t, locale } = useTranslation();
    const zero = value.quantity === 0;
    return (
        <div className="leading-tight" data-zero={zero ? 'true' : undefined}>
            <div className={zero ? 'text-gray-400' : 'text-gray-900'}>{formatQuantity(value.quantity, locale)}</div>
            {!zero && <div className="text-xs italic text-gray-500">{t('availability_kg_not_known_yet')}</div>}
        </div>
    );
}

export function UnitBadge({ unit }: { unit: PackageUnit | null }) {
    const { t } = useTranslation();
    if (unit === null) {
        return (
            <span className="inline-flex rounded border border-amber-300 bg-amber-50 px-2 py-0.5 font-mono text-xs text-amber-900">
                {t('availability_unit_unknown')}
            </span>
        );
    }
    return <span className="inline-flex rounded border border-slate-300 bg-slate-50 px-2 py-0.5 font-mono text-xs text-slate-800">{unit}</span>;
}
