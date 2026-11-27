'use client';

import { FormEvent, useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { countryLabel } from '../../lib/countries';
import { ApiError, toApiError } from '../../services/apiClient';
import { getCountries } from '../../services/countryService';
import { getReceiveDefaults, listStockLocations, saveReceiveDefaults, saveStockLocation } from '../../services/stockLocationService';
import type { CountryReference } from '../../types/country';
import { STOCK_LOCATION_KINDS, type StockLocation, type StockLocationKind } from '../../types/stockLocation';
import CountryPicker from '../shared/CountryPicker';
import ErrorNotice from '../supply/ErrorNotice';
import FieldError, { fieldProps, inputClass } from '../supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../supply/SuccessPanel';

interface Draft {
    id: number | null;
    name: string;
    kind: StockLocationKind;
    address: string;
    countryCode: string | null;
    isActive: boolean;
    version: number;
    inUse: boolean;
}

const emptyDraft = (): Draft => ({ id: null, name: '', kind: 'InTransit', address: '', countryCode: null, isActive: true, version: 0, inUse: false });

// Instellingen › Voorraadlocaties (feedback 22 november): voorraad kan ook bij de leverancier, op een cross-dock, in een externe opslag of
// onderweg liggen (direct naar een klant in een ander land). De eigen loods is er altijd (het adres uit de laadbon-instellingen) en staat
// hier niet als regel. Nooit verwijderen: inactief zetten; de soort ligt vast zodra een pallet of inkooporder de locatie gebruikt.
export default function StockLocationsSetting() {
    const { t, locale } = useTranslation();
    const [locations, setLocations] = useState<StockLocation[] | null>(null);
    const [countries, setCountries] = useState<CountryReference | null>(null);
    const [draft, setDraft] = useState<Draft | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [saving, setSaving] = useState(false);
    const [saved, setSaved] = useState<string | null>(null);

    useEffect(() => {
        const controller = new AbortController();
        Promise.all([listStockLocations(controller.signal), getCountries(controller.signal)])
            .then(([list, countryList]) => { setLocations(list); setCountries(countryList); })
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setError(apiError); });
        return () => controller.abort();
    }, []);

    const submit = async (event: FormEvent) => {
        event.preventDefault();
        if (draft === null || saving) return;
        setSaving(true);
        setError(null);
        try {
            const stored = await saveStockLocation(draft.id, {
                name: draft.name.trim(), kind: draft.kind, address: draft.address.trim() === '' ? null : draft.address.trim(), countryCode: draft.countryCode,
                isActive: draft.isActive, ...(draft.id === null ? {} : { expectedVersion: draft.version }),
            });
            setLocations((current) => [...(current ?? []).filter((l) => l.id !== stored.id), stored].sort((a, b) => a.name.localeCompare(b.name)));
            setSaved(stored.name);
            setDraft(null);
        } catch (caught) {
            setError(toApiError(caught));
        } finally {
            setSaving(false);
        }
    };

    const e = (field: string) => error?.fieldErrors[field];
    return (
        <section id="voorraadlocaties" className="space-y-2 rounded-md border border-gray-300 bg-white p-3" data-testid="stock-locations-setting" aria-labelledby="stock-locations-title">
            <h2 id="stock-locations-title" className="text-lg font-semibold text-gray-900">{t('stock_locations_title')}</h2>
            <p className="text-sm text-gray-600">{t('stock_locations_hint')}</p>
            {error && Object.keys(error.fieldErrors).length === 0 && <ErrorNotice error={error} />}
            {saved && <p role="status" className="rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-900">{t('stock_locations_saved', { name: saved })}</p>}
            {locations !== null && (
                <ul className="divide-y divide-gray-100 rounded border border-gray-200 text-sm">
                    <li className="flex flex-wrap items-center gap-x-2 px-2 py-1.5 text-gray-700">
                        <span className="font-medium text-gray-900">{t('stock_location_own')}</span>
                        <span className="text-xs">· {t('stock_location_kind_OwnWarehouse')} · {t('stock_location_own_hint')}</span>
                    </li>
                    {locations.map((location) => (
                        <li key={location.id} className="flex flex-wrap items-start justify-between gap-2 px-2 py-1.5" data-testid={`stock-location-${location.id}`}>
                            <span className="min-w-0">
                                <span className={`font-medium ${location.isActive ? 'text-gray-900' : 'text-gray-600 line-through'}`}>{location.name}</span>
                                <span className="text-xs text-gray-700"> · {t(`stock_location_kind_${location.kind}`)}{location.countryCode && countries && ` · ${countryLabel(countries, location.countryCode, locale)}`}</span>
                                {location.address && <span className="block whitespace-pre-line text-xs text-gray-700">{location.address}</span>}
                            </span>
                            <button type="button" className="text-xs font-medium text-blue-800 underline underline-offset-2" data-tour="stock-location-edit" data-tour-key={location.id}
                                onClick={() => { setSaved(null); setError(null); setDraft({ ...location, address: location.address ?? '' }); }}>{t('stock_location_edit')}</button>
                        </li>
                    ))}
                </ul>
            )}
            {locations !== null && <DefaultReceiveLocation locations={locations} />}
            {draft === null && (
                <button type="button" className={secondaryButtonClass} data-tour="stock-location-new" disabled={locations === null}
                    onClick={() => { setSaved(null); setError(null); setDraft(emptyDraft()); }}>{t('stock_location_new')}</button>
            )}
            {draft !== null && countries !== null && (
                <form onSubmit={(event) => void submit(event)} className="grid gap-2 rounded border border-gray-200 bg-gray-50 p-2 sm:grid-cols-2" data-testid="stock-location-form" noValidate>
                    <label className="text-sm text-gray-800">{t('stock_location_name')}
                        <input {...fieldProps('stock-location-name', e('name'))} autoFocus value={draft.name} maxLength={100} data-tour="stock-location-name" onChange={(event) => setDraft({ ...draft, name: event.target.value })} className={inputClass} />
                        <FieldError id="stock-location-name" messages={e('name')} />
                    </label>
                    <label className="text-sm text-gray-800">{t('stock_location_kind')}
                        <select {...fieldProps('stock-location-kind', e('kind'))} value={draft.kind} disabled={draft.inUse} data-tour="stock-location-kind"
                            onChange={(event) => setDraft({ ...draft, kind: event.target.value as StockLocationKind })} className={inputClass}>
                            {STOCK_LOCATION_KINDS.filter((kind) => kind !== 'OwnWarehouse' || draft.kind === 'OwnWarehouse').map((kind) => <option key={kind} value={kind}>{t(`stock_location_kind_${kind}`)}</option>)}
                        </select>
                        {draft.inUse && <span className="block text-xs text-gray-700">{t('stock_location_kind_fixed')}</span>}
                        <FieldError id="stock-location-kind" messages={e('kind')} />
                    </label>
                    <label className="text-sm text-gray-800 sm:col-span-2">{t('stock_location_address')}
                        <textarea {...fieldProps('stock-location-address', e('address'))} rows={3} value={draft.address} maxLength={500} data-tour="stock-location-address"
                            onChange={(event) => setDraft({ ...draft, address: event.target.value })} className={inputClass} />
                        <FieldError id="stock-location-address" messages={e('address')} />
                    </label>
                    <div className="text-sm text-gray-800">
                        <label htmlFor="stock-location-country-input">{t('stock_location_country')}</label>
                        <CountryPicker id="stock-location-country" countries={countries} value={draft.countryCode} onChange={(code) => setDraft({ ...draft, countryCode: code })} dataTour="stock-location-country" />
                        <FieldError id="stock-location-countryCode" messages={e('countryCode')} />
                    </div>
                    <label className="flex items-center gap-2 self-end text-sm text-gray-800">
                        <input type="checkbox" checked={draft.isActive} onChange={(event) => setDraft({ ...draft, isActive: event.target.checked })} />
                        {t('stock_location_active')}
                    </label>
                    <div className="flex flex-wrap gap-2 sm:col-span-2">
                        <button type="submit" className={primaryButtonClass} disabled={saving} data-tour="stock-location-save">{t('stock_location_save')}</button>
                        <button type="button" className={secondaryButtonClass} onClick={() => { setDraft(null); setError(null); }}>{t('stock_location_cancel')}</button>
                    </div>
                </form>
            )}
        </section>
    );
}

// November 2026: de standaard ontvangstlocatie van een nieuwe inkooporder. Alleen zichtbaar (en nodig) als er naast de ingebouwde eigen loods
// nog een actieve eigen loods is; opslaan meteen bij kiezen.
function DefaultReceiveLocation({ locations }: { locations: readonly StockLocation[] }) {
    const { t } = useTranslation();
    const own = locations.filter((location) => location.isActive && location.kind === 'OwnWarehouse');
    const [value, setValue] = useState<string | null>(null);
    const [status, setStatus] = useState<'saved' | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const visible = own.length > 0;
    useEffect(() => {
        if (!visible) return;
        const controller = new AbortController();
        getReceiveDefaults(controller.signal)
            .then((defaults) => setValue(defaults.defaultReceiveLocationId === null ? '' : String(defaults.defaultReceiveLocationId)))
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setError(apiError); });
        return () => controller.abort();
    }, [visible]);
    if (!visible) return null;
    const choose = async (next: string) => {
        const previous = value;
        setValue(next);
        setStatus(null);
        setError(null);
        try {
            const saved = await saveReceiveDefaults(next === '' ? null : Number(next));
            setValue(saved.defaultReceiveLocationId === null ? '' : String(saved.defaultReceiveLocationId));
            setStatus('saved');
        } catch (caught) {
            setValue(previous);
            setError(toApiError(caught));
        }
    };
    return (
        <div className="rounded border border-gray-200 p-2" data-testid="default-receive-location">
            <label htmlFor="default-receive-location" className="text-xs font-medium text-gray-700">{t('stock_location_default_title')}</label>
            <select id="default-receive-location" className={`${inputClass} max-w-sm`} value={value ?? ''} disabled={value === null} data-tour="stock-location-default"
                onChange={(event) => void choose(event.target.value)}>
                <option value="">{t('stock_location_own')}</option>
                {own.map((location) => <option key={location.id} value={String(location.id)}>{location.name}</option>)}
            </select>
            <span className="block text-xs text-gray-600">{t('stock_location_default_hint')}</span>
            {status === 'saved' && <p role="status" className="text-xs text-green-900">{t('stock_location_default_saved')}</p>}
            {error && <ErrorNotice error={error} />}
        </div>
    );
}
