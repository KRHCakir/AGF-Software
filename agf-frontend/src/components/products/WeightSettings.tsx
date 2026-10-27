'use client';

import { FormEvent, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDecimalInput, parsePositiveDecimal } from '../../lib/decimal';
import { WEIGHT_METHODS, WeightMethod, isWeightMethod } from '../../lib/weight';
import { ApiError, toApiError } from '../../services/apiClient';
import { updatePackagingSettings, updateWeightSettings } from '../../services/weightSettingsService';
import { inputClass } from '../supply/FieldError';
import { decimalErrorKey } from '../supply/messages';

// Gewichtsmethode en standaard netto kg per verpakking van één productvariant. Geldt voor nieuwe ontvangsten;
// bestaande inhoudsregels houden hun eigen methode en kg per verpakking.
export default function WeightSettings({ variantId, weightMethod, defaultNetKgPerPackage, defaultPackagesPerPallet, onSaved }: {
    variantId: number;
    weightMethod: WeightMethod;
    defaultNetKgPerPackage: number | null;
    defaultPackagesPerPallet: number | null;
    onSaved: () => void;
}) {
    const { t } = useTranslation();
    const [method, setMethod] = useState<WeightMethod>(weightMethod);
    const [kg, setKg] = useState(defaultNetKgPerPackage === null ? '' : formatDecimalInput(defaultNetKgPerPackage));
    const [perPallet, setPerPallet] = useState(defaultPackagesPerPallet === null ? '' : formatDecimalInput(defaultPackagesPerPallet));
    const [saving, setSaving] = useState(false);
    const [saved, setSaved] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const submit = async (event: FormEvent) => {
        event.preventDefault();
        if (saving) return;
        let value: number | null = null;
        if (kg.trim() !== '') {
            const parsed = parsePositiveDecimal(kg);
            if (!parsed.ok) { setError(t(decimalErrorKey(parsed.error))); return; }
            value = parsed.value;
        }
        let perPalletValue: number | null = null;
        if (perPallet.trim() !== '') {
            const parsed = parsePositiveDecimal(perPallet);
            if (!parsed.ok) { setError(t(decimalErrorKey(parsed.error))); return; }
            perPalletValue = parsed.value;
        }
        setSaving(true);
        setError(null);
        setSaved(false);
        try {
            await updateWeightSettings(variantId, { weightMethod: method, defaultNetKgPerPackage: value });
            await updatePackagingSettings(variantId, perPalletValue);
            setSaved(true);
            onSaved();
        } catch (caught) {
            const apiError: ApiError = toApiError(caught);
            setError(apiError.message);
        } finally {
            setSaving(false);
        }
    };

    return (
        <form onSubmit={submit} className="flex flex-wrap items-end gap-2 text-xs">
            <label className="flex flex-col gap-0.5 text-gray-700">
                {t('products_weight_method')}
                <select value={method} onChange={(event) => { if (isWeightMethod(event.target.value)) { setMethod(event.target.value); setSaved(false); } }} className={`${inputClass} mt-0 w-56`}>
                    {WEIGHT_METHODS.map((option) => <option key={option} value={option}>{t(option === 'Weighed' ? 'products_weight_method_weighed' : 'products_weight_method_calculated')}</option>)}
                </select>
            </label>
            <label className="flex flex-col gap-0.5 text-gray-700">
                {t('products_weight_default_kg')}
                <input value={kg} inputMode="decimal" onChange={(event) => { setKg(event.target.value); setSaved(false); }} className={`${inputClass} mt-0 w-28`} aria-label={t('products_weight_default_kg')} />
            </label>
            <label className="flex flex-col gap-0.5 text-gray-700">
                {t('products_packages_per_pallet')}
                <input value={perPallet} inputMode="decimal" onChange={(event) => { setPerPallet(event.target.value); setSaved(false); }} className={`${inputClass} mt-0 w-28`} aria-label={t('products_packages_per_pallet')} />
            </label>
            <button type="submit" disabled={saving} className="rounded border border-gray-300 bg-white px-2 py-1 font-medium text-gray-800 hover:bg-gray-100 disabled:opacity-50">
                {t('products_weight_save')}
            </button>
            {saved && <span role="status" className="text-green-800">{t('products_weight_saved')}</span>}
            {error && <span role="alert" className="text-red-800">{error}</span>}
            <span className="basis-full text-gray-500">{t('products_weight_hint')}</span>
        </form>
    );
}
