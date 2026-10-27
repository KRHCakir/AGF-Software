'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { formatDecimalInput } from '../../lib/decimal';
import { QuantityInputMode, resolvePalletInput } from '../../lib/palletInput';
import type { PackageKgDefaults } from '../../lib/quantityKg';
import { unitLabelKey } from '../../lib/units';
import type { PackageUnit } from '../../types/availability';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import QuantityKgLine from './QuantityKgLine';

// Hoeveelheid per regel: één veld "Aantal" met de eenheid erachter en live de kg volgens het artikel; "per pallet" is een hulp
// ("3 pallets × 120 colli + 15 losse colli = 375 colli"). Het totaal in verpakkingen blijft de bron; de verhouding wordt meegestuurd zodat de regel herkenbaar blijft.
// Alleen voor pallets met dezelfde inhoud; gemengde pallets blijven via afzonderlijke inhoudsregels werken.
export type QuantityField = 'quantity' | 'mode' | 'palletCount' | 'packagesPerPallet' | 'loosePackages';

export default function QuantityInput({ prefix, index, line, locked, errors, quantityField = 'quantity', defaultPackagesPerPallet, kgDefaults, onChange }: {
    prefix: string;
    index: number;
    line: { quantity: string; unit: PackageUnit | ''; mode: QuantityInputMode; palletCount: string; packagesPerPallet: string; loosePackages?: string };
    locked: boolean;
    errors: (key: string) => string[] | undefined;
    // Naam van het totaalveld in de API-foutsleutels (orders: quantity, aanvoer: expectedQuantity).
    quantityField?: 'quantity' | 'expectedQuantity';
    defaultPackagesPerPallet: number | null;
    // Netto en bruto kg per verpakking van het artikel: alleen voor de rekenregel.
    kgDefaults?: PackageKgDefaults;
    onChange: (field: QuantityField, value: string) => void;
}) {
    const { t, locale } = useTranslation();
    // Vertaald eenheidslabel (Doos, Colli, …); zonder gekozen eenheid het woord "eenheid".
    const unit = line.unit === '' ? t('supply_unit').toLowerCase() : t(unitLabelKey(line.unit)).toLowerCase();
    const resolved = resolvePalletInput(line);
    const total = resolved.ok ? resolved.value : null;
    const totalKey = `${prefix}.${quantityField}`;

    const setMode = (mode: QuantityInputMode) => {
        if (locked) return;
        onChange('mode', mode);
        // Standaard van de variant als invoerhulp; blijft aanpasbaar en wordt per regel bewaard.
        if (mode === 'pallets' && line.packagesPerPallet === '' && defaultPackagesPerPallet !== null) {
            onChange('packagesPerPallet', formatDecimalInput(defaultPackagesPerPallet));
        }
        // Aantal intussen anders getypt: een oude verdeling zou dat aantal stil vervangen, dus pallets en losse colli opnieuw invullen.
        const earlier = resolvePalletInput({ ...line, mode: 'pallets' });
        if (mode === 'pallets' && total !== null && earlier.ok && earlier.value.quantity !== total.quantity) {
            onChange('palletCount', '');
            onChange('loosePackages', '');
        }
        // Terug naar het gewone aantal: het berekende totaal blijft staan.
        if (mode === 'packages' && total !== null) onChange('quantity', formatDecimalInput(total.quantity));
    };
    const link = (mode: QuantityInputMode, label: string) => (
        <button type="button" disabled={locked} onClick={() => setMode(mode)} data-testid={`${prefix}.${mode === 'pallets' ? 'per-pallet' : 'plain-quantity'}`}
            className="whitespace-nowrap text-xs text-blue-700 underline decoration-dotted disabled:text-gray-500">{t(label)}</button>
    );
    const field = (name: 'palletCount' | 'packagesPerPallet' | 'loosePackages', label: string) => (
        <label className="flex flex-1 flex-col text-xs text-gray-700">
            {label}
            <input {...fieldProps(`${prefix}.${name}`, errors(`${prefix}.${name}`))} inputMode="numeric" value={line[name] ?? ''} disabled={locked}
                onChange={(event) => onChange(name, event.target.value)} className={`${inputClass} text-right`} />
        </label>
    );

    return (
        <div className="min-w-0 space-y-1">
            {line.mode === 'packages' ? (
                <>
                    <label htmlFor={`${totalKey}-input`} className="sr-only">{t('supply_quantity')} {index + 1}</label>
                    <div className="flex items-center gap-2">
                        <input {...fieldProps(totalKey, errors(totalKey))} inputMode="decimal" value={line.quantity} disabled={locked}
                            onChange={(event) => onChange('quantity', event.target.value)} className={`${inputClass} !mt-0 min-w-[7rem] flex-1 text-right`} />
                        <span className="text-sm text-gray-700" data-testid={`${prefix}.unit-suffix`}>{unit}</span>
                        {link('pallets', 'quantity_per_pallet')}
                    </div>
                    <QuantityKgLine value={line.quantity} unit={unit} defaults={kgDefaults} testId={totalKey} />
                    <FieldError id={totalKey} messages={errors(totalKey)} />
                </>
            ) : (
                <>
                    <div className="flex gap-2">
                        {field('palletCount', t('pallet_count'))}
                        {field('packagesPerPallet', t('packages_per_pallet', { unit }))}
                        {field('loosePackages', t('loose_packages', { unit }))}
                    </div>
                    <p className="text-xs font-medium text-gray-900" data-testid={`${prefix}.pallet-calculation`}>
                        {total !== null
                            ? t(total.loosePackages ? 'pallet_calculation_loose' : 'pallet_calculation', { pallets: formatQuantity(total.palletCount!, locale), perPallet: formatQuantity(total.packagesPerPallet!, locale), loose: formatQuantity(total.loosePackages ?? 0, locale), unit, total: formatQuantity(total.quantity, locale) })
                            : t('pallet_calculation_pending')}
                    </p>
                    <QuantityKgLine value="" quantity={total?.quantity ?? null} unit={unit} defaults={kgDefaults} testId={totalKey} />
                    {link('packages', 'quantity_plain')}
                    <FieldError id={`${prefix}.palletCount`} messages={errors(`${prefix}.palletCount`)} />
                    <FieldError id={`${prefix}.packagesPerPallet`} messages={errors(`${prefix}.packagesPerPallet`)} />
                    <FieldError id={`${prefix}.loosePackages`} messages={errors(`${prefix}.loosePackages`)} />
                    <FieldError id={totalKey} messages={errors(totalKey)} />
                </>
            )}
        </div>
    );
}
