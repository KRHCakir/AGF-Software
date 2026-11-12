'use client';

import { FormEvent, useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { ApiError, toApiError } from '../../services/apiClient';
import { getLoadingSlipSettings, updateLoadingSlipSettings } from '../../services/salesOrderDocumentService';
import type { LoadingSlipSettings } from '../../types/salesOrderDocument';
import ErrorNotice from '../supply/ErrorNotice';
import { inputClass } from '../supply/FieldError';
import { primaryButtonClass } from '../supply/SuccessPanel';

// Instellingen van de laadbon per bedrijf (documenten deel 1): taal van de kopteksten (standaard Engels), klantnaam, leverancierskolom en
// de loods (naam en adres van waar geladen wordt; leeg = het bedrijfsadres).
const MAX_WAREHOUSE = 300;

export default function LoadingSlipSetting() {
    const { t } = useTranslation();
    const [value, setValue] = useState<LoadingSlipSettings | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [saving, setSaving] = useState(false);
    const [saved, setSaved] = useState(false);

    useEffect(() => {
        const controller = new AbortController();
        getLoadingSlipSettings(controller.signal).then(setValue).catch((caught) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setError(apiError); });
        return () => controller.abort();
    }, []);

    const save = async (event: FormEvent) => {
        event.preventDefault();
        if (value === null || saving) return;
        setSaving(true);
        setError(null);
        try {
            setValue(await updateLoadingSlipSettings({ ...value, warehouseAddress: value.warehouseAddress?.trim() ? value.warehouseAddress.trim() : null }));
            setSaved(true);
        } catch (caught) {
            setError(toApiError(caught));
        } finally {
            setSaving(false);
        }
    };
    const change = (patch: Partial<LoadingSlipSettings>) => { if (value) { setValue({ ...value, ...patch }); setSaved(false); } };

    return (
        <section aria-labelledby="loading-slip-settings" className="space-y-2 rounded-md border border-gray-200 bg-white px-4 py-3" data-testid="loading-slip-settings">
            <h2 id="loading-slip-settings" className="text-lg font-semibold text-gray-900">{t('loading_slip_settings_title')}</h2>
            <p className="text-sm text-gray-700">{t('loading_slip_settings_text')}</p>
            {error && <ErrorNotice error={error} />}
            {value && (
                <form onSubmit={(event) => void save(event)} className="space-y-2">
                    {/* Tijdens het opslaan niet te wijzigen: het antwoord van de server zou een wijziging van net overschrijven. */}
                    <fieldset disabled={saving} className="space-y-2">
                        <div className="max-w-xs">
                            <label htmlFor="loading-slip-language" className="text-xs font-medium text-gray-700">{t('loading_slip_language')}</label>
                            <select id="loading-slip-language" value={value.language} onChange={(event) => change({ language: event.target.value as LoadingSlipSettings['language'] })} className={inputClass}>
                                <option value="en">{t('loading_slip_language_en')}</option>
                                <option value="nl">{t('loading_slip_language_nl')}</option>
                            </select>
                        </div>
                        <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={value.showCustomer} onChange={(event) => change({ showCustomer: event.target.checked })} /> {t('loading_slip_show_customer')}</label>
                        <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={value.showSupplier} onChange={(event) => change({ showSupplier: event.target.checked })} /> {t('loading_slip_show_supplier')}</label>
                        <div className="max-w-md">
                            <label htmlFor="loading-slip-warehouse" className="text-xs font-medium text-gray-700">{t('loading_slip_warehouse')}</label>
                            <textarea id="loading-slip-warehouse" rows={2} maxLength={MAX_WAREHOUSE} value={value.warehouseAddress ?? ''} placeholder={t('loading_slip_warehouse_placeholder')}
                                onChange={(event) => change({ warehouseAddress: event.target.value })} className={inputClass} aria-describedby="loading-slip-warehouse-hint" />
                            <p id="loading-slip-warehouse-hint" className="text-xs text-gray-600">{t('loading_slip_warehouse_hint')}</p>
                        </div>
                    </fieldset>
                    <div className="flex items-center gap-2">
                        <button type="submit" className={primaryButtonClass} disabled={saving}>{t('loading_slip_save')}</button>
                        {saved && <span role="status" className="text-sm text-green-800">{t('loading_slip_saved')}</span>}
                    </div>
                </form>
            )}
        </section>
    );
}
