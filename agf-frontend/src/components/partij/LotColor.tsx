'use client';

import { useEffect, useId, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { ApiError, toApiError } from '../../services/apiClient';
import { getPartij, setPartijColor } from '../../services/partijService';
import ErrorNotice from '../supply/ErrorNotice';
import { inputClass } from '../supply/FieldError';
import { secondaryButtonClass } from '../supply/SuccessPanel';

// Feedback 22 november: kleur is een kenmerk van de partij, niet van de pallet.

// Klein kenmerk naast een partijnummer (Beschikbaarheid, partijlijst).
export function LotColorBadge({ color }: { color: string | null | undefined }) {
    const { t } = useTranslation();
    if (!color) return null;
    return <span className="ml-1 inline-flex rounded border border-gray-300 bg-white px-1.5 text-xs font-medium text-gray-800" data-testid="lot-color">{t('lot_color_badge', { color })}</span>;
}

// De kleur van een inkoop-partij kiezen uit de lijst van haar product(en); geldt voor de hele partij. Bij de keuring en op het partijdetail.
// Zonder kleurenlijst zegt het veld waar die in te stellen is. Opslaan na een keuze (Enter of de knop), met historie op de server.
// Velden van dezelfde partij (meerdere keuringsregels) houden elkaar bij: een opgeslagen kleur wordt aan de andere velden doorgegeven.
const savedListeners = new Set<(partijId: number, color: string | null) => void>();

type LotColorFieldProps = {
    partijId: number;
    partijNumber?: string | null;
    onSaved?: (color: string | null) => void;
    compact?: boolean;
    // Al bekend (het partijdetail): dan niets ophalen.
    initial?: { color: string | null; allowedColors: string[] };
};

// De toestand hoort bij één partij: wisselt de partij (of de bekende gegevens), dan begint het veld opnieuw.
export function LotColorField(props: LotColorFieldProps) {
    const { partijId, initial } = props;
    const key = `${partijId}:${initial ? `${initial.color ?? ''}|${initial.allowedColors.join(',')}` : ''}`;
    return <LotColorFieldInner key={key} {...props} />;
}

function LotColorFieldInner({ partijId, partijNumber, onSaved, compact = false, initial }: LotColorFieldProps) {
    const { t } = useTranslation();
    const [allowed, setAllowed] = useState<string[] | null>(initial?.allowedColors ?? null);
    const [saved, setSaved] = useState<string | null>(initial?.color ?? null);
    const [value, setValue] = useState(initial?.color ?? '');
    const [busy, setBusy] = useState(false);
    const id = useId();
    const [error, setError] = useState<ApiError | null>(null);

    const known = initial !== undefined;
    useEffect(() => {
        if (known) return;
        const controller = new AbortController();
        getPartij(partijId, controller.signal)
            .then((partij) => { setAllowed(partij.allowedColors ?? []); setSaved(partij.color ?? null); setValue(partij.color ?? ''); })
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setError(apiError); });
        return () => controller.abort();
    }, [partijId, known]);

    useEffect(() => {
        const listener = (changedId: number, color: string | null) => {
            if (changedId !== partijId) return;
            setSaved(color);
            setValue(color ?? '');
        };
        savedListeners.add(listener);
        return () => { savedListeners.delete(listener); };
    }, [partijId]);

    const save = async () => {
        if (busy) return;
        setBusy(true);
        setError(null);
        try {
            const result = await setPartijColor(partijId, value === '' ? null : value);
            setSaved(result.color ?? null);
            setValue(result.color ?? '');
            savedListeners.forEach((listener) => listener(partijId, result.color ?? null));
            onSaved?.(result.color ?? null);
        } catch (caught) {
            setError(toApiError(caught));
        } finally {
            setBusy(false);
        }
    };

    if (allowed === null) return error ? <ErrorNotice error={error} /> : null;
    return (
        <div className={compact ? 'mt-1' : 'space-y-1'} data-testid="lot-color-field">
            <label htmlFor={id} className="text-xs font-medium text-gray-700">
                {partijNumber ? t('lot_color_of', { lot: partijNumber }) : t('lot_color_label')}
            </label>
            {allowed.length === 0 && !saved
                ? <p className="text-xs text-gray-600">{t('lot_color_no_list')}</p>
                : (
                    <div className="flex flex-wrap items-center gap-2">
                        <select id={id} value={value} disabled={busy} onChange={(event) => setValue(event.target.value)} className={`${inputClass} w-auto`} data-tour="lot-color">
                            <option value="">{t('lot_color_none')}</option>
                            {allowed.map((color) => <option key={color} value={color}>{color}</option>)}
                            {saved && !allowed.includes(saved) && <option value={saved}>{saved}</option>}
                        </select>
                        <button type="button" className={secondaryButtonClass} disabled={busy || value === (saved ?? '')} onClick={() => void save()} data-tour="lot-color-save">
                            {busy ? t('supply_saving') : t('lot_color_save')}
                        </button>
                    </div>
                )}
            {allowed.length === 0 && saved && <p className="text-xs text-gray-600">{t('lot_color_no_list')}</p>}
            <span className="block text-xs text-gray-500">{t('lot_color_hint')}</span>
            {error && <ErrorNotice error={error} />}
        </div>
    );
}
