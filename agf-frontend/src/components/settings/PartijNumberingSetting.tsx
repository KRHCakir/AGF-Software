'use client';

import { FormEvent, useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { MAX_PARTIJ_FORMAT_LENGTH, MAX_SEQUENCE_DIGITS, MIN_SEQUENCE_DIGITS, PARTIJ_MODES, PARTIJ_PLACEHOLDERS, PARTIJ_RESETS, type PartijKind, type PartijNumberMode, type PartijSequenceReset, isSequenceDigits, partijFormatError } from '../../lib/partijRules';
import { ApiError, toApiError } from '../../services/apiClient';
import { getNextPartijNumber, getPartijNumbering, updatePartijNumbering } from '../../services/partijService';
import type { PartijSettings } from '../../types/partij';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import { primaryButtonClass } from '../supply/SuccessPanel';

const MODE_KEYS: Record<PartijNumberMode, string> = { Auto: 'settings_partij_mode_auto', Own: 'settings_partij_mode_own' };
const RESET_KEYS: Record<PartijSequenceReset, string> = { Never: 'settings_partij_reset_never', Yearly: 'settings_partij_reset_yearly', Monthly: 'settings_partij_reset_monthly' };

// Bedrijfsinstelling: partijnummering. Auto = het systeem stelt het volgende nummer voor (overschrijven mag), Eigen = alleen zelf typen;
// in beide gevallen dezelfde dubbelcontrole. Twee reeksen met elk een eigen formaat, aantal cijfers en teller: inkoop-partijen
// (5{JJ}{MM}{volgnr} = 52611002) en verkoop-partijen ({JJ}{MM}{volgnr}, één per verkooporder). Modus en opnieuw tellen gelden voor
// beide. De voorbeelden ("Volgende partij: 52611002") komen van de server, die het echte nummer pas bij opslaan toekent.
export default function PartijNumberingSetting({ previewDelayMs = 250 }: { previewDelayMs?: number }) {
    const { t } = useTranslation();
    const [value, setValue] = useState<PartijSettings | null>(null);
    const [saved, setSaved] = useState<PartijSettings | null>(null);
    const [loadError, setLoadError] = useState<ApiError | null>(null);
    const [serverError, setServerError] = useState<ApiError | null>(null);
    const [formatErrors, setFormatErrors] = useState<Record<string, string[]>>({});
    const [example, setExample] = useState<string | null>(null);
    const [salesExample, setSalesExample] = useState<string | null>(null);
    const [saving, setSaving] = useState(false);
    const [notice, setNotice] = useState(false);

    useEffect(() => {
        const controller = new AbortController();
        getPartijNumbering(controller.signal)
            .then((settings) => { setValue(settings); setSaved(settings); })
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setLoadError(apiError); });
        return () => controller.abort();
    }, []);

    // Live voorbeeld per reeks: eerst de eigen controle van het formaat, daarna (kort na het typen) het voorstel van de server.
    const localError = value === null ? null : partijFormatError(value.format);
    const salesLocalError = value === null ? null : partijFormatError(value.salesFormat);
    const digitsValid = value !== null && isSequenceDigits(value.sequenceDigits) && isSequenceDigits(value.salesSequenceDigits);
    useEffect(() => {
        const preview = (kind: PartijKind, format: string, digits: number, error: string | null, set: (example: string | null) => void) => {
            if (value === null || value.mode === 'Own' || error !== null || !isSequenceDigits(digits)) { set(null); return () => undefined; }
            const controller = new AbortController();
            const timer = window.setTimeout(() => {
                getNextPartijNumber({ format, reset: value.sequenceReset, digits, kind }, controller.signal)
                    .then((next) => set(next.suggestion))
                    .catch(() => set(null));
            }, previewDelayMs);
            return () => { window.clearTimeout(timer); controller.abort(); };
        };
        const stopPurchase = preview('Purchase', value?.format ?? '', value?.sequenceDigits ?? 0, localError, setExample);
        const stopSales = preview('Sales', value?.salesFormat ?? '', value?.salesSequenceDigits ?? 0, salesLocalError, setSalesExample);
        return () => { stopPurchase(); stopSales(); };
    }, [value, localError, salesLocalError, previewDelayMs]);

    const edit = (patch: Partial<PartijSettings>) => {
        setValue((current) => (current === null ? current : { ...current, ...patch }));
        setFormatErrors({});
        setServerError(null);
        setNotice(false);
    };

    const submit = async (event: FormEvent) => {
        event.preventDefault();
        if (saving || value === null) return;
        if (localError !== null || salesLocalError !== null || !digitsValid) {
            setFormatErrors({
                ...(localError !== null ? { format: [t(`settings_partij_format_error_${localError}`)] } : {}),
                ...(salesLocalError !== null ? { salesFormat: [t(`settings_partij_format_error_${salesLocalError}`)] } : {}),
                ...(!isSequenceDigits(value.sequenceDigits) ? { sequenceDigits: [t('settings_partij_digits_error')] } : {}),
                ...(!isSequenceDigits(value.salesSequenceDigits) ? { salesSequenceDigits: [t('settings_partij_digits_error')] } : {}),
            });
            return;
        }
        setSaving(true);
        try {
            const result = await updatePartijNumbering({ ...value, format: value.format.trim(), salesFormat: value.salesFormat.trim() });
            setValue(result);
            setSaved(result);
            setNotice(true);
        } catch (caught) {
            const apiError = toApiError(caught);
            setServerError(apiError);
            setFormatErrors(apiError.fieldErrors);
        } finally {
            setSaving(false);
        }
    };

    const unchanged = value !== null && saved !== null && value.mode === saved.mode && value.format.trim() === saved.format && value.sequenceReset === saved.sequenceReset
        && value.salesFormat.trim() === saved.salesFormat && value.sequenceDigits === saved.sequenceDigits && value.salesSequenceDigits === saved.salesSequenceDigits;
    const shownError = formatErrors.format ?? (localError !== null ? [t(`settings_partij_format_error_${localError}`)] : undefined);
    const salesShownError = formatErrors.salesFormat ?? (salesLocalError !== null ? [t(`settings_partij_format_error_${salesLocalError}`)] : undefined);
    const digitsField = (id: 'sequenceDigits' | 'salesSequenceDigits') => (
        <div>
            <label htmlFor={`${id}-input`} className="text-xs font-medium text-gray-700">{t('settings_partij_digits')}</label>
            <input {...fieldProps(id, formatErrors[id])} type="number" inputMode="numeric" min={MIN_SEQUENCE_DIGITS} max={MAX_SEQUENCE_DIGITS} value={value?.[id] ?? ''}
                disabled={value === null || saving} onChange={(event) => edit({ [id]: event.target.value === '' ? 0 : Number(event.target.value) })} className={`${inputClass} !w-24`} />
            <FieldError id={id} messages={formatErrors[id]} />
        </div>
    );

    return (
        <form noValidate onSubmit={submit} className="space-y-3 rounded-md border border-gray-300 bg-white p-3" data-testid="partij-numbering-setting" data-tour="settings-partij">
            <div>
                <h2 className="text-lg font-semibold text-gray-900">{t('settings_partij_title')}</h2>
                <p className="text-sm text-gray-600">{t('settings_partij_hint')}</p>
            </div>
            {loadError && <ErrorNotice error={loadError} />}
            {serverError && <ErrorNotice error={serverError} />}
            {notice && <p role="status" className="rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-900">{t('settings_partij_saved')}</p>}
            <fieldset className="space-y-1">
                <legend className="text-xs font-medium text-gray-700">{t('settings_partij_mode')}</legend>
                {PARTIJ_MODES.map((mode) => (
                    <label key={mode} className="flex items-start gap-2 text-sm text-gray-900">
                        <input type="radio" name="partijMode" value={mode} checked={value?.mode === mode} disabled={value === null || saving} className="mt-1"
                            onChange={() => edit({ mode })} />
                        <span>{t(MODE_KEYS[mode])}</span>
                    </label>
                ))}
            </fieldset>
            <h3 className="text-sm font-semibold text-gray-900">{t('settings_partij_purchase_series')}</h3>
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
                <div>
                    <label htmlFor="partijFormat-input" className="text-xs font-medium text-gray-700">{t('settings_partij_format')}</label>
                    <input {...fieldProps('partijFormat', shownError)} value={value?.format ?? ''} maxLength={MAX_PARTIJ_FORMAT_LENGTH} disabled={value === null || saving}
                        onChange={(event) => edit({ format: event.target.value })} className={`${inputClass} font-mono`} aria-describedby="partijFormat-hint" />
                    <span id="partijFormat-hint" className="block text-xs text-gray-500">{t('settings_partij_format_hint', { placeholders: PARTIJ_PLACEHOLDERS.join(' ') })}</span>
                    <FieldError id="partijFormat" messages={shownError} />
                </div>
                {digitsField('sequenceDigits')}
                <div>
                    <label htmlFor="partijReset-input" className="text-xs font-medium text-gray-700">{t('settings_partij_reset')}</label>
                    <select id="partijReset-input" value={value?.sequenceReset ?? 'Yearly'} disabled={value === null || saving} className={inputClass}
                        onChange={(event) => edit({ sequenceReset: event.target.value as PartijSequenceReset })}>
                        {PARTIJ_RESETS.map((reset) => <option key={reset} value={reset}>{t(RESET_KEYS[reset])}</option>)}
                    </select>
                </div>
            </div>
            <p className="text-sm text-gray-800" data-testid="partij-numbering-example" aria-live="polite">
                {t('settings_partij_example')}{' '}
                <span className="font-mono font-semibold">{example ?? '–'}</span>
                {value?.mode === 'Own' && <span className="block text-xs text-gray-600">{t('settings_partij_example_own')}</span>}
            </p>
            <h3 className="text-sm font-semibold text-gray-900">{t('settings_partij_sales_series')}</h3>
            <p className="text-xs text-gray-600">{t('settings_partij_sales_hint')}</p>
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
                <div>
                    <label htmlFor="salesFormat-input" className="text-xs font-medium text-gray-700">{t('settings_partij_format')}</label>
                    <input {...fieldProps('salesFormat', salesShownError)} value={value?.salesFormat ?? ''} maxLength={MAX_PARTIJ_FORMAT_LENGTH} disabled={value === null || saving}
                        data-tour="settings-sales-format" onChange={(event) => edit({ salesFormat: event.target.value })} className={`${inputClass} font-mono`} />
                    <FieldError id="salesFormat" messages={salesShownError} />
                </div>
                {digitsField('salesSequenceDigits')}
            </div>
            <p className="text-sm text-gray-800" data-testid="partij-numbering-sales-example" aria-live="polite">
                {t('settings_partij_sales_example')}{' '}
                <span className="font-mono font-semibold">{salesExample ?? '–'}</span>
            </p>
            <button type="submit" className={primaryButtonClass} disabled={value === null || saving || unchanged}>{saving ? t('supply_saving') : t('settings_partij_save')}</button>
        </form>
    );
}
