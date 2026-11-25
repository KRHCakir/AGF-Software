'use client';

import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { toApiError, type ApiError } from '../../services/apiClient';
import { getRevenueTypes, saveRevenueType } from '../../services/salesInvoiceService';
import type { RevenueType } from '../../types/salesInvoice';
import ErrorNotice from '../supply/ErrorNotice';
import { inputClass } from '../supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

// Verkoopfactuur 1: de omzetsoorten (Beheer) voor dienst- en vrije regels op een verkoopfactuur, met een code voor de grootboekrekening in
// het boekhoudpakket (later). Precies één standaard; nooit verwijderen, wel inactief zetten (niet de standaard).
export default function RevenueTypesSetting() {
    const { t } = useTranslation();
    const [types, setTypes] = useState<RevenueType[] | null>(null);
    const [name, setName] = useState('');
    const [code, setCode] = useState('');
    const [error, setError] = useState<ApiError | null>(null);
    const [busy, setBusy] = useState(false);
    const [token, setToken] = useState(0);

    useEffect(() => {
        const controller = new AbortController();
        getRevenueTypes(true, controller.signal).then(setTypes)
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setError(apiError); });
        return () => controller.abort();
    }, [token]);

    const run = async (work: () => Promise<unknown>) => {
        setBusy(true);
        setError(null);
        try { await work(); setToken((n) => n + 1); } catch (caught) { setError(toApiError(caught)); } finally { setBusy(false); }
    };
    const update = (type: RevenueType, patch: Partial<Pick<RevenueType, 'isActive' | 'isDefault'>>) => run(() => saveRevenueType(type.id, {
        name: type.name, externalCode: type.externalCode, isActive: patch.isActive ?? type.isActive, isDefault: patch.isDefault ?? type.isDefault, expectedVersion: type.version,
    }));

    return (
        <section id="omzetsoorten" className="space-y-2 rounded-md border border-gray-300 bg-white p-3" data-testid="revenue-types-setting" aria-labelledby="revenue-types-title">
            <h2 id="revenue-types-title" className="text-lg font-semibold text-gray-900">{t('settings_revenue_types_title')}</h2>
            <p className="text-sm text-gray-600">{t('settings_revenue_types_hint')}</p>
            {error && <ErrorNotice error={error} />}
            {types && (
                <ul className="space-y-1 text-sm">
                    {types.map((type) => (
                        <li key={type.id} className="flex flex-wrap items-center gap-2" data-testid="revenue-type">
                            <span className={`font-medium ${type.isActive ? 'text-gray-900' : 'text-gray-600 line-through'}`}>{type.name}</span>
                            {type.externalCode && <span className="font-mono text-xs text-gray-700">{type.externalCode}</span>}
                            {type.isDefault && <span className="rounded border border-blue-300 bg-blue-50 px-1.5 text-xs text-blue-900">{t('settings_revenue_types_default')}</span>}
                            {!type.isDefault && type.isActive && <button type="button" className="text-xs text-blue-800 underline underline-offset-2" disabled={busy} onClick={() => void update(type, { isDefault: true })}>{t('settings_revenue_types_make_default')}</button>}
                            {!type.isDefault && <button type="button" className="text-xs text-blue-800 underline underline-offset-2" disabled={busy} onClick={() => void update(type, { isActive: !type.isActive })}>
                                {t(type.isActive ? 'settings_revenue_types_deactivate' : 'settings_revenue_types_activate')}</button>}
                        </li>
                    ))}
                </ul>
            )}
            <form className="flex flex-wrap items-end gap-2" onSubmit={(event) => {
                event.preventDefault();
                if (name.trim() === '') return;
                void run(async () => { await saveRevenueType(null, { name: name.trim(), externalCode: code.trim() === '' ? null : code.trim(), isActive: true, isDefault: false }); setName(''); setCode(''); });
            }}>
                <div>
                    <label htmlFor="revenue-type-name" className="text-xs font-medium text-gray-700">{t('settings_revenue_types_name')}</label>
                    <input id="revenue-type-name" className={inputClass} value={name} maxLength={100} onChange={(event) => setName(event.target.value)} />
                </div>
                <div>
                    <label htmlFor="revenue-type-code" className="text-xs font-medium text-gray-700">{t('settings_revenue_types_code')}</label>
                    <input id="revenue-type-code" className={`${inputClass} font-mono`} value={code} maxLength={50} onChange={(event) => setCode(event.target.value)} />
                </div>
                <button type="submit" className={name.trim() === '' ? secondaryButtonClass : primaryButtonClass} disabled={busy || name.trim() === ''}>{t('settings_revenue_types_add')}</button>
            </form>
        </section>
    );
}
