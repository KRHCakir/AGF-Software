'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { DecimalOptions, formatDecimalInput, parseLocaleDecimal } from '../../lib/decimal';
import { decimalErrorKey } from '../supply/messages';

// Kleine controle onder een getalveld: wat het systeem van de invoer maakt ("= 15.000 kg"), of waarom het niet klopt.
// Zo ziet de gebruiker vóór het opslaan dat "15.000" vijftienduizend is en geen vijftien. Leeg veld: niets.
export default function NumberEcho({ value, unit, options = {}, testId }: { value: string; unit?: string; options?: DecimalOptions; testId?: string }) {
    const { t } = useTranslation();
    const parsed = parseLocaleDecimal(value, undefined, options);
    if (!parsed.ok && parsed.error === 'empty') return null;
    return (
        <p className={`mt-0.5 text-xs ${parsed.ok ? 'text-gray-500' : 'text-amber-800'}`} data-testid={testId ?? 'number-echo'} aria-live="polite">
            {parsed.ok ? `= ${formatDecimalInput(parsed.value, options.maxDecimals ?? 3)}${unit ? ` ${unit}` : ''}` : t(decimalErrorKey(parsed.error))}
        </p>
    );
}
