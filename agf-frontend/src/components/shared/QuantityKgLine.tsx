'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { parsePositiveDecimal } from '../../lib/decimal';
import { type PackageKgDefaults, formatKgOneDecimal, packageKg } from '../../lib/quantityKg';
import NumberEcho from './NumberEcho';

// Onder een aantal: met netto kg per verpakking van het artikel de live rekenregel ("1.560 colli × 10 kg = 15.600,0 kg netto
// · 16.068,0 kg bruto"), anders de gewone controle "= 1.560 colli". Alleen weergave; wat wordt opgeslagen verandert niet.
// Zonder `quantity` wordt het getypte `value` gelezen (gewoon aantalveld); met `quantity` het berekende totaal (per pallet).
export default function QuantityKgLine({ value, quantity: given, unit, defaults, testId }: { value: string; quantity?: number | null; unit: string; defaults: PackageKgDefaults | undefined; testId: string }) {
    const { t, locale } = useTranslation();
    const parsed = parsePositiveDecimal(value);
    const quantity = given !== undefined ? given : parsed.ok ? parsed.value : null;
    const kg = packageKg(quantity, defaults);
    if (kg === null || quantity === null) return <NumberEcho value={value} unit={unit} testId={`${testId}-echo`} />;
    return (
        <p className="mt-0.5 text-xs text-gray-700" data-testid={`${testId}-kg`} aria-live="polite">
            {t('quantity_kg_line', { quantity: formatQuantity(quantity, locale), unit, perPackage: formatQuantity(kg.netKgPerPackage, locale) })}{' '}
            <strong className="font-semibold text-gray-900">{t('quantity_kg_net', { net: formatKgOneDecimal(kg.netKg, locale) })}</strong>
            {kg.grossKg !== null && <> · {t('quantity_kg_gross', { gross: formatKgOneDecimal(kg.grossKg, locale) })}</>}
        </p>
    );
}
