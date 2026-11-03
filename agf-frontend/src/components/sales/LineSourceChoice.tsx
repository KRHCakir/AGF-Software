'use client';

import { useTranslation } from '../../i18n/LanguageContext';
import { formatQuantity } from '../../lib/availability';
import { palletEquivalent, proposeSplit } from '../../lib/salesSupply';
import { SupplySource } from '../../types/salesSupply';
import { BalanceState } from './useSalesBalances';

// Per regel van een nieuwe verkooporder: de stand van precies deze variant en eenheid, de keuze Uit voorraad / Uit verwachte aanvoer en
// het voorstel van het systeem. Past het aantal niet in de vrije voorraad, dan kan de regel met één klik gesplitst worden (deel uit voorraad,
// rest uit verwachte aanvoer). Meer dan voorraad + verwacht is een korte waarschuwing, nooit een blokkade. "= x pallets" alleen ter info.
export default function LineSourceChoice({ name, quantity, unitText, state, source, packagesPerPallet, locked, onSource, onSplit }: {
    name: string;
    quantity: number | null;
    unitText: string;
    state: BalanceState | null;
    source: SupplySource | null;
    packagesPerPallet: number | null;
    locked: boolean;
    onSource: (source: SupplySource) => void;
    onSplit: (stock: number, expected: number) => void;
}) {
    const { t, locale } = useTranslation();
    const q = (value: number) => formatQuantity(value, locale);
    const balance = state?.status === 'ready' ? state.balance : null;
    const pallets = quantity === null ? null : palletEquivalent(quantity, packagesPerPallet);
    const freeStock = balance === null ? null : Math.max(0, balance.freeStock);
    const split = quantity !== null && freeStock !== null ? proposeSplit(quantity, freeStock) : null;
    const tooMuchFromStock = source === 'Stock' && split !== null && split.expected > 0;
    // Na deze regel: vrij - aantal; onder nul is het tekort van deze variant (zoals de waarschuwing van de server na opslaan).
    const shortage = balance !== null && quantity !== null ? Math.max(0, quantity - balance.free) : 0;

    return (
        <div className="mt-1 space-y-1 text-xs" data-testid={`${name}.source`}>
            {pallets !== null && <p className="text-gray-600" data-testid={`${name}.pallets`}>{t('sales_pallet_equivalent', { pallets: formatQuantity(pallets, locale) })}</p>}
            {state?.status === 'loading' && <p className="text-gray-500">{t('sales_balance_loading')}</p>}
            {state?.status === 'error' && <p className="text-amber-900">{t('sales_balance_failed')}</p>}
            {balance !== null && (
                <p className="text-gray-800" data-testid={`${name}.balance`}>
                    {t('sales_balance_line', { stock: q(balance.stock), expected: q(balance.expected), free: q(balance.free), unit: unitText })}
                </p>
            )}
            <fieldset className="flex flex-wrap gap-x-3 gap-y-1" disabled={locked}>
                <legend className="sr-only">{t('sales_source_label')}</legend>
                {(['Stock', 'Expected'] as const).map((option) => (
                    <label key={option} className="inline-flex items-center gap-1 text-gray-900">
                        <input type="radio" name={`${name}.supplySource`} value={option} checked={source === option} onChange={() => onSource(option)}
                            data-tour={`sales-line-source-${option}`} />
                        {t(option === 'Stock' ? 'sales_source_stock' : 'sales_source_expected')}
                    </label>
                ))}
            </fieldset>
            {tooMuchFromStock && split !== null && (
                <div role="status" className="rounded border border-amber-300 bg-amber-50 px-2 py-1 text-amber-950" data-testid={`${name}.split-proposal`}>
                    <p>{t('sales_stock_not_enough', { free: q(freeStock ?? 0), unit: unitText })}</p>
                    {split.stock > 0 && !locked && (
                        <button type="button" className="mt-1 rounded border border-amber-400 bg-white px-2 py-0.5 font-medium text-amber-950 hover:bg-amber-100"
                            data-tour="sales-line-split" onClick={() => onSplit(split.stock, split.expected)}>
                            {t('sales_split_line', { stock: q(split.stock), expected: q(split.expected), unit: unitText })}
                        </button>
                    )}
                </div>
            )}
            {shortage > 0 && (
                <p role="status" className="font-medium text-red-800" data-testid={`${name}.shortage`}>
                    {t('sales_shortage_warning', { quantity: q(shortage), unit: unitText })}
                </p>
            )}
        </div>
    );
}
