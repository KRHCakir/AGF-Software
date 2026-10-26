'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { formatDecimalInput, parsePositiveDecimal } from '../../lib/decimal';
import { QuantityInputMode, palletTotal } from '../../lib/palletInput';
import { unitLabelKey } from '../../lib/units';
import type { PackageUnit } from '../../types/availability';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';

// Hoeveelheid per regel: gewone invoer in verpakkingen, of per pallet ("9 pallets × 120 box = 1.080 box").
// Het totaal in verpakkingen blijft de bron; de verhouding wordt meegestuurd zodat de regel herkenbaar blijft.
// Alleen voor pallets met dezelfde inhoud; gemengde pallets blijven via afzonderlijke inhoudsregels werken.
export type QuantityField = 'quantity' | 'mode' | 'palletCount' | 'packagesPerPallet';

export default function QuantityInput({ prefix, index, line, locked, errors, quantityField = 'quantity', defaultPackagesPerPallet, onChange }: {
    prefix: string;
    index: number;
    line: { quantity: string; unit: PackageUnit | ''; mode: QuantityInputMode; palletCount: string; packagesPerPallet: string };
    locked: boolean;
    errors: (key: string) => string[] | undefined;
    // Naam van het totaalveld in de API-foutsleutels (orders: quantity, aanvoer: expectedQuantity).
    quantityField?: 'quantity' | 'expectedQuantity';
    defaultPackagesPerPallet: number | null;
    onChange: (field: QuantityField, value: string) => void;
}) {
    const { t, locale } = useTranslation();
    // Vertaald eenheidslabel (Doos, Colli, …); zonder gekozen eenheid het woord "eenheid".
    const unit = line.unit === '' ? t('supply_unit').toLowerCase() : t(unitLabelKey(line.unit)).toLowerCase();
    const count = parsePositiveDecimal(line.palletCount);
    const perPallet = parsePositiveDecimal(line.packagesPerPallet);
    const totalKey = `${prefix}.${quantityField}`;

    const setMode = (mode: QuantityInputMode) => {
        if (locked) return;
        onChange('mode', mode);
        // Standaard van de variant als invoerhulp; blijft aanpasbaar en wordt per regel bewaard.
        if (mode === 'pallets' && line.packagesPerPallet === '' && defaultPackagesPerPallet !== null) {
            onChange('packagesPerPallet', formatDecimalInput(defaultPackagesPerPallet));
        }
    };

    return (
        <div className="space-y-1">
            <div className="flex gap-3 text-xs text-gray-800" role="radiogroup" aria-label={`${t('quantity_mode')} ${index + 1}`}>
                {(['packages', 'pallets'] as const).map((mode) => (
                    <label key={mode} className="flex items-center gap-1">
                        <input type="radio" name={`${prefix}.mode`} value={mode} checked={line.mode === mode} disabled={locked} onChange={() => setMode(mode)} />
                        {t(mode === 'packages' ? 'quantity_mode_packages' : 'quantity_mode_pallets')}
                    </label>
                ))}
            </div>
            {line.mode === 'packages' ? (
                <>
                    <label htmlFor={`${totalKey}-input`} className="sr-only">{t('supply_quantity')} {index + 1}</label>
                    <input {...fieldProps(totalKey, errors(totalKey))} inputMode="decimal" value={line.quantity} disabled={locked}
                        onChange={(event) => onChange('quantity', event.target.value)} className={inputClass} />
                    <FieldError id={totalKey} messages={errors(totalKey)} />
                </>
            ) : (
                <>
                    <div className="flex gap-2">
                        <label className="flex flex-1 flex-col text-xs text-gray-700">
                            {t('pallet_count')}
                            <input {...fieldProps(`${prefix}.palletCount`, errors(`${prefix}.palletCount`))} inputMode="decimal" value={line.palletCount} disabled={locked}
                                onChange={(event) => onChange('palletCount', event.target.value)} className={inputClass} />
                        </label>
                        <label className="flex flex-1 flex-col text-xs text-gray-700">
                            {t('packages_per_pallet')}
                            <input {...fieldProps(`${prefix}.packagesPerPallet`, errors(`${prefix}.packagesPerPallet`))} inputMode="decimal" value={line.packagesPerPallet} disabled={locked}
                                onChange={(event) => onChange('packagesPerPallet', event.target.value)} className={inputClass} />
                        </label>
                    </div>
                    <p className="text-xs font-medium text-gray-900" data-testid={`${prefix}.pallet-calculation`}>
                        {count.ok && perPallet.ok
                            ? t('pallet_calculation', { pallets: formatQuantity(count.value, locale), perPallet: formatQuantity(perPallet.value, locale), unit, total: formatQuantity(palletTotal(count.value, perPallet.value), locale) })
                            : t('pallet_calculation_pending')}
                    </p>
                    <p className="text-xs text-gray-500">{t('pallet_input_hint')}</p>
                    <FieldError id={`${prefix}.palletCount`} messages={errors(`${prefix}.palletCount`)} />
                    <FieldError id={`${prefix}.packagesPerPallet`} messages={errors(`${prefix}.packagesPerPallet`)} />
                    <FieldError id={totalKey} messages={errors(totalKey)} />
                </>
            )}
        </div>
    );
}
