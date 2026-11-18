'use client';

import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatKg, setWeightDecimals } from '../../lib/weightDisplay';
import { ApiError, toApiError } from '../../services/apiClient';
import { getWeightDecimals, updateWeightDecimals } from '../../services/weightDisplayService';
import ErrorNotice from '../supply/ErrorNotice';

// Bedrijfsinstelling: gewichten tonen met 0 of 1 decimaal (standaard 0, hele kilo's). Alleen de weergave op schermen en nieuwe
// documenten; wat is opgeslagen verandert nooit. Het gewicht per colli bij het artikel houdt altijd zijn decimalen.
export default function WeightDisplaySetting() {
    const { t, locale } = useTranslation();
    const [decimals, setDecimals] = useState<number | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [saving, setSaving] = useState(false);
    const [saved, setSaved] = useState(false);

    useEffect(() => {
        const controller = new AbortController();
        getWeightDecimals(controller.signal).then(setDecimals)
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setError(apiError); });
        return () => controller.abort();
    }, []);

    const choose = async (next: number) => {
        if (saving || next === decimals) return;
        setSaving(true);
        setError(null);
        setSaved(false);
        try {
            const stored = await updateWeightDecimals(next);
            setWeightDecimals(stored);
            setDecimals(stored);
            setSaved(true);
        } catch (caught) {
            setError(toApiError(caught));
        } finally {
            setSaving(false);
        }
    };

    return (
        <section id="gewichten" className="space-y-2 rounded-md border border-gray-300 bg-white p-3" data-testid="weight-display-setting" aria-labelledby="weight-display-title">
            <h2 id="weight-display-title" className="text-lg font-semibold text-gray-900">{t('settings_weight_title')}</h2>
            <p className="text-sm text-gray-600">{t('settings_weight_hint')}</p>
            {error && <ErrorNotice error={error} />}
            <fieldset disabled={decimals === null || saving} className="flex flex-wrap gap-x-6 gap-y-1">
                <legend className="sr-only">{t('settings_weight_title')}</legend>
                {[0, 1].map((option) => (
                    <label key={option} className="flex items-center gap-2 text-sm text-gray-900">
                        <input type="radio" name="weight-decimals" value={option} checked={decimals === option} onChange={() => void choose(option)} data-testid={`weight-decimals-${option}`} />
                        {t(option === 0 ? 'settings_weight_whole' : 'settings_weight_one')}
                    </label>
                ))}
            </fieldset>
            {decimals !== null && <p className="text-xs text-gray-600" data-testid="weight-display-example">{t('settings_weight_example', { shown: formatKg(2272.5, locale) })}</p>}
            {saved && <p role="status" className="rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-900">{t('settings_weight_saved')}</p>}
        </section>
    );
}
