'use client';

import { KeyboardEvent } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { focusNextField } from '../../lib/focusNext';
import { unitLabelKey } from '../../lib/units';
import { PACKAGE_UNITS, PackageUnit } from '../../types/availability';
import { fieldProps, inputClass } from '../supply/FieldError';

// De eenheid van een orderregel, met het toetsenbord te kiezen: Tab komt erop, pijl omlaag en omhoog kiezen direct de volgende of
// vorige eenheid (in Chrome op de Mac openen de pijlen anders alleen het menu, zonder te kiezen) en Enter gaat naar het volgende veld
// zonder het formulier te versturen. Spatie opent de lijst van de browser zoals altijd; Tab gaat gewoon door.
export default function UnitSelect({ fieldKey, index, value, onChange, errors, disabled = false, emptyLabel, dataTour, className = '!mt-0 !w-auto shrink-0' }: {
    // Sleutel van het veld in de foutmeldingen (lines[0].unit); het element krijgt `${fieldKey}-input`.
    fieldKey: string;
    index: number;
    value: PackageUnit | '';
    onChange: (unit: PackageUnit | '') => void;
    errors: string[] | undefined;
    disabled?: boolean;
    // Tekst van de lege keuze ("Kies…" of "–").
    emptyLabel?: string;
    dataTour?: string;
    // Opmaak voor de plek: standaard naast het aantal; in een eigen kolom bijvoorbeeld 'mt-1 w-full'.
    className?: string;
}) {
    const { t } = useTranslation();
    const values: (PackageUnit | '')[] = ['', ...PACKAGE_UNITS];

    const onKeyDown = (event: KeyboardEvent<HTMLSelectElement>) => {
        if (event.altKey || event.metaKey || event.ctrlKey) return;
        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            const current = values.indexOf(value);
            const next = event.key === 'ArrowDown' ? Math.min(current + 1, values.length - 1) : Math.max(current - 1, 1);
            if (values[next] !== value) onChange(values[next]);
        } else if (event.key === 'Enter') {
            event.preventDefault();
            focusNextField(event.currentTarget);
        }
    };

    return (
        <select {...fieldProps(fieldKey, errors)} aria-label={`${t('supply_unit')} ${index + 1}`} value={value} disabled={disabled}
            data-tour={dataTour} data-tour-key={dataTour ? index : undefined} onKeyDown={onKeyDown}
            onChange={(event) => onChange(event.target.value as PackageUnit | '')} className={`${inputClass} ${className}`}>
            <option value="">{emptyLabel ?? t('supply_choose')}</option>
            {PACKAGE_UNITS.map((unit) => <option key={unit} value={unit}>{t(unitLabelKey(unit))}</option>)}
        </select>
    );
}
