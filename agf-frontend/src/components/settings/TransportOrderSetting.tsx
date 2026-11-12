'use client';

import { FormEvent, useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { ApiError, toApiError } from '../../services/apiClient';
import { getTransportOrderSettings, updateTransportOrderSettings } from '../../services/salesOrderTransportService';
import type { TransportOrderSettings } from '../../types/salesOrderTransport';
import ErrorNotice from '../supply/ErrorNotice';
import { inputClass } from '../supply/FieldError';
import { primaryButtonClass } from '../supply/SuccessPanel';

// Instellingen van de transportopdracht per bedrijf (documenten deel 2): taal van de kopteksten (standaard Engels) en de standaard soort
// pallet ("Europallet"; per order te overschrijven). Geldt voor nieuwe versies.
export default function TransportOrderSetting() {
    const { t } = useTranslation();
    const [value, setValue] = useState<TransportOrderSettings | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [saving, setSaving] = useState(false);
    const [saved, setSaved] = useState(false);

    useEffect(() => {
        const controller = new AbortController();
        getTransportOrderSettings(controller.signal).then(setValue).catch((caught) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setError(apiError); });
        return () => controller.abort();
    }, []);

    const save = async (event: FormEvent) => {
        event.preventDefault();
        if (value === null || saving) return;
        setSaving(true);
        setError(null);
        try {
            setValue(await updateTransportOrderSettings({ ...value, palletKind: value.palletKind.trim() }));
            setSaved(true);
        } catch (caught) {
            setError(toApiError(caught));
        } finally {
            setSaving(false);
        }
    };
    const change = (patch: Partial<TransportOrderSettings>) => { if (value) { setValue({ ...value, ...patch }); setSaved(false); } };

    return (
        <section aria-labelledby="transport-order-settings" className="space-y-2 rounded-md border border-gray-200 bg-white px-4 py-3" data-testid="transport-order-settings">
            <h2 id="transport-order-settings" className="text-lg font-semibold text-gray-900">{t('transport_order_settings_title')}</h2>
            <p className="text-sm text-gray-700">{t('transport_order_settings_text')}</p>
            {error && <ErrorNotice error={error} />}
            {value && (
                <form onSubmit={(event) => void save(event)} className="space-y-2">
                    <fieldset disabled={saving} className="space-y-2">
                        <div className="max-w-xs">
                            <label htmlFor="transport-order-language" className="text-xs font-medium text-gray-700">{t('loading_slip_language')}</label>
                            <select id="transport-order-language" value={value.language} onChange={(event) => change({ language: event.target.value as TransportOrderSettings['language'] })} className={inputClass}>
                                <option value="en">{t('loading_slip_language_en')}</option>
                                <option value="nl">{t('loading_slip_language_nl')}</option>
                            </select>
                        </div>
                        <div className="max-w-md">
                            <label htmlFor="transport-order-pallet-kind" className="text-xs font-medium text-gray-700">{t('transport_order_pallet_kind')}</label>
                            <input id="transport-order-pallet-kind" value={value.palletKind} maxLength={100} onChange={(event) => change({ palletKind: event.target.value })} className={inputClass} />
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
