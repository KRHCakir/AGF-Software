'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { AvailabilityWarning } from '../../types/availability';

const WARNING_KEYS: Record<string, string> = {
    OVER_REGISTERED: 'availability_warning_over_registered',
    SOURCE_UNIT_UNKNOWN: 'availability_warning_source_unit_unknown',
    DIRECT_TRADE_IN_WAREHOUSE: 'availability_warning_direct_trade_in_warehouse',
};

export default function WarningBadges({ warnings }: { warnings: AvailabilityWarning[] }) {
    const { t, locale } = useTranslation();
    if (warnings.length === 0) {
        return null;
    }
    return (
        <ul className="space-y-1">
            {warnings.map((warning, index) => (
                <li key={`${warning.code}-${warning.supplyCommitmentLineId ?? ''}-${warning.palletContentLineId ?? ''}-${index}`}
                    className="flex items-start gap-1 rounded border border-amber-300 bg-amber-50 px-2 py-0.5 text-xs text-amber-900">
                    <span aria-hidden="true">⚠</span>
                    <span>
                        {WARNING_KEYS[warning.code]
                            ? t(WARNING_KEYS[warning.code], { quantity: warning.quantity === null ? '' : formatQuantity(warning.quantity, locale) })
                            : warning.code}
                    </span>
                </li>
            ))}
        </ul>
    );
}
