'use client';

import { FormEvent, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { parseNonNegative } from '../../lib/purchaseInvoiceRules';
import { ApiError, toApiError } from '../../services/apiClient';
import { updatePurchaseInvoiceSettings } from '../../services/purchaseInvoiceService';
import type { PurchaseInvoiceSettings, PurchaseOptionalField } from '../../types/purchaseInvoice';
import { inputClass } from '../supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

// Bedrijfsinstellingen voor de invoer (achter het tandwiel): welke optionele velden in gebruik zijn en welke voorstellen
// gelden. Eén bedrijf, geen tenantstructuur; financiële basisvelden zijn hier bewust niet uitschakelbaar.
export default function PurchaseInvoiceSettingsPanel({ settings, onSaved, onClose }: {
    settings: PurchaseInvoiceSettings;
    onSaved: (settings: PurchaseInvoiceSettings) => void;
    onClose: () => void;
}) {
    const { t } = useTranslation();
    const [enabled, setEnabled] = useState<Set<PurchaseOptionalField>>(() => new Set(settings.optionalFields));
    const [vat, setVat] = useState(String(settings.defaultVatPercentage));
    const [terms, setTerms] = useState(String(settings.defaultPaymentTermsDays));
    const [currency, setCurrency] = useState(settings.defaultCurrency);
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const submit = async (event: FormEvent) => {
        event.preventDefault();
        if (saving) return;
        const parsedVat = parseNonNegative(vat, 2);
        if (!parsedVat.ok || parsedVat.value > 100) { setError(t('purchase_error_out_of_range')); return; }
        if (!/^\d+$/.test(terms.trim()) || Number(terms) > 365) { setError(t('purchase_error_out_of_range')); return; }
        if (!/^[A-Za-z]{3}$/.test(currency.trim())) { setError(t('purchase_error_invalid')); return; }
        setSaving(true);
        setError(null);
        try {
            const saved = await updatePurchaseInvoiceSettings({
                optionalFields: settings.allOptionalFields.filter((field) => enabled.has(field)),
                defaultVatPercentage: parsedVat.value,
                defaultPaymentTermsDays: Number(terms),
                defaultCurrency: currency.trim().toUpperCase(),
            });
            onSaved(saved);
            onClose();
        } catch (caught) {
            const apiError: ApiError = toApiError(caught);
            setError(apiError.message);
        } finally {
            setSaving(false);
        }
    };

    return (
        <form onSubmit={submit} aria-label={t('purchase_settings_title')} className="space-y-3 rounded-md border border-gray-300 bg-white p-3 text-sm shadow-md">
            <div className="flex items-start justify-between gap-2">
                <div>
                    <h2 className="font-semibold text-gray-900">{t('purchase_settings_title')}</h2>
                    <p className="text-xs text-gray-600">{t('purchase_settings_hint')}</p>
                </div>
                <button type="button" onClick={onClose} aria-label={t('inspection_cancel')} className="text-gray-500 hover:text-gray-800">✕</button>
            </div>
            <fieldset>
                <legend className="text-xs font-medium text-gray-700">{t('purchase_settings_fields')}</legend>
                <div className="mt-1 grid grid-cols-2 gap-x-4 gap-y-1 md:grid-cols-3">
                    {settings.allOptionalFields.map((field) => (
                        <label key={field} className="flex items-center gap-2 text-gray-800">
                            <input type="checkbox" checked={enabled.has(field)} onChange={(event) => setEnabled((current) => { const next = new Set(current); if (event.target.checked) next.add(field); else next.delete(field); return next; })} />
                            {t(`purchase_field_${field}`)}
                        </label>
                    ))}
                </div>
            </fieldset>
            <div className="grid grid-cols-3 gap-3">
                <label className="text-xs font-medium text-gray-700">{t('purchase_settings_default_vat')}
                    <input value={vat} inputMode="decimal" onChange={(event) => setVat(event.target.value)} className={inputClass} />
                </label>
                <label className="text-xs font-medium text-gray-700">{t('purchase_settings_default_terms')}
                    <input value={terms} inputMode="numeric" onChange={(event) => setTerms(event.target.value)} className={inputClass} />
                </label>
                <label className="text-xs font-medium text-gray-700">{t('purchase_settings_default_currency')}
                    <input value={currency} maxLength={3} onChange={(event) => setCurrency(event.target.value.toUpperCase())} className={`${inputClass} font-mono uppercase`} />
                </label>
            </div>
            <p className="text-xs text-gray-500">{t('purchase_settings_scope')}</p>
            {error && <p role="alert" className="text-xs text-red-800">{error}</p>}
            <div className="flex gap-2">
                <button type="submit" disabled={saving} className={primaryButtonClass}>{t('products_weight_save')}</button>
                <button type="button" onClick={onClose} className={secondaryButtonClass}>{t('inspection_cancel')}</button>
            </div>
        </form>
    );
}
