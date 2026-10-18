'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, formatQuantity } from '../../lib/availability';
import { AvailabilityQuantity, ExpectedUnregistered, PackageUnit } from '../../types/availability';

function Zero() {
    return (
        <span className="text-gray-400">
            <span aria-hidden="true">–</span>
            <span className="sr-only">0</span>
        </span>
    );
}

// Werkelijke hoeveelheid met daaronder het werkelijke netto-gewicht uit palletinhoud.
export function QuantityCell({ value, emphasis = false }: { value: AvailabilityQuantity; emphasis?: boolean }) {
    const { locale } = useTranslation();
    if (value.quantity === 0 && value.netWeightKg === 0) {
        return <Zero />;
    }
    return (
        <div className="leading-tight">
            <div className={emphasis ? 'text-base font-semibold text-gray-900' : 'text-gray-900'}>{formatQuantity(value.quantity, locale)}</div>
            <div className="text-xs text-gray-500">{formatKg(value.netWeightKg, locale)}</div>
        </div>
    );
}

// Verwacht maar nog niet op een pallet: alleen een hoeveelheid, het gewicht is bewust onbekend.
export function ExpectedCell({ value }: { value: ExpectedUnregistered }) {
    const { t, locale } = useTranslation();
    if (value.quantity === 0) {
        return <Zero />;
    }
    return (
        <div className="leading-tight">
            <div className="text-gray-900">{formatQuantity(value.quantity, locale)}</div>
            <div className="text-xs italic text-gray-500">{t('availability_kg_not_known_yet')}</div>
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
