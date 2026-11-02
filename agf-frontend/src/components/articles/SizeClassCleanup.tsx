'use client';

import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { normalizeTradeClass } from '../../lib/tradeClass';
import { ApiError, toApiError } from '../../services/apiClient';
import { getSizeClassCleanup, setArticleSizeClass } from '../../services/articleService';
import type { Article, SizeClassCleanupItem } from '../../types/article';
import TradeClassPicker from '../shared/TradeClassPicker';
import ErrorNotice from '../supply/ErrorNotice';
import { inputClass } from '../supply/FieldError';
import { secondaryButtonClass } from '../supply/SuccessPanel';

interface Choice { size: string; tradeClass: string }

// "Maat en klasse opschonen" in Artikelen, naast "Herkomst opschonen": oudere artikelen zonder maat, zonder klasse of met een klasse
// die niet in de vaste lijst staat (de migratie zette alleen eenduidige waarden om). Ze blijven bruikbaar; hier vult de gebruiker maat
// en klasse aan. Er wordt nooit automatisch gekozen: een herkende oude schrijfwijze staat alleen alvast als voorstel klaar.
export default function SizeClassCleanup({ reloadToken, onArticleChanged }: {
    reloadToken: number;
    onArticleChanged: (article: Article) => void;
}) {
    const { t } = useTranslation();
    const [items, setItems] = useState<SizeClassCleanupItem[] | null>(null);
    const [chosen, setChosen] = useState<Record<number, Choice>>({});
    const [error, setError] = useState<ApiError | null>(null);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        const controller = new AbortController();
        getSizeClassCleanup(controller.signal)
            .then((list) => {
                setItems(list);
                setChosen(Object.fromEntries(list.map((item) => [item.id, { size: item.sizeCategory ?? '', tradeClass: normalizeTradeClass(item.tradeClass) ?? '' }])));
            })
            .catch((caught: unknown) => { if (toApiError(caught).kind !== 'aborted') setError(toApiError(caught)); });
        return () => controller.abort();
    }, [reloadToken]);

    const edit = (id: number, patch: Partial<Choice>) => setChosen((current) => ({ ...current, [id]: { ...(current[id] ?? { size: '', tradeClass: '' }), ...patch } }));
    const ready = (id: number) => (chosen[id]?.size.trim() ?? '') !== '' && normalizeTradeClass(chosen[id]?.tradeClass) !== null;

    const save = async (item: SizeClassCleanupItem) => {
        const choice = chosen[item.id];
        if (!choice || !ready(item.id)) return;
        setBusy(true);
        setError(null);
        try {
            onArticleChanged(await setArticleSizeClass(item.id, choice.size.trim(), choice.tradeClass));
            setItems((current) => (current ?? []).filter((candidate) => candidate.id !== item.id));
        } catch (caught) {
            setError(toApiError(caught));
        } finally {
            setBusy(false);
        }
    };

    return (
        <section className="rounded-md border border-gray-200 bg-white p-3" aria-labelledby="size-class-cleanup" data-tour="size-class-cleanup">
            <h2 id="size-class-cleanup" className="text-sm font-semibold text-gray-900">{t('size_class_cleanup_title')}</h2>
            <p className="text-xs text-gray-600">{t('size_class_cleanup_hint')}</p>
            {error && <div className="mt-2"><ErrorNotice error={error} /></div>}
            {items === null && error === null && <p className="mt-2 text-sm text-gray-600">{t('articles_loading')}</p>}
            {items !== null && items.length === 0 && <p className="mt-2 text-sm text-green-800">{t('size_class_cleanup_empty')}</p>}
            <ul className="mt-2 divide-y divide-gray-100 text-sm">
                {(items ?? []).map((item) => (
                    <li key={item.id} className="grid grid-cols-2 gap-2 py-2 md:grid-cols-[minmax(0,1fr)_8rem_10rem_auto] md:items-end" data-testid={`size-class-cleanup-${item.id}`}>
                        <span className="col-span-2 min-w-0 break-words md:col-span-1">
                            <span className="font-medium text-gray-900">{item.description}</span>
                            <span className="block text-xs text-gray-600">
                                {t('size_class_cleanup_now', { size: item.sizeCategory ?? '–', tradeClass: item.tradeClass ?? '–' })}
                            </span>
                        </span>
                        <label className="text-xs font-medium text-gray-700">
                            {t('articles_field_size')} *
                            <input value={chosen[item.id]?.size ?? ''} maxLength={100} disabled={busy} placeholder={t('articles_hint_size')} className={inputClass}
                                onChange={(event) => edit(item.id, { size: event.target.value })} />
                        </label>
                        <span>
                            <label htmlFor={`size-class-cleanup-${item.id}-class-input`} className="text-xs font-medium text-gray-700">{t('articles_field_trade_class')} *</label>
                            <TradeClassPicker id={`size-class-cleanup-${item.id}-class`} value={chosen[item.id]?.tradeClass ?? ''} disabled={busy}
                                onChange={(tradeClass) => edit(item.id, { tradeClass })} />
                        </span>
                        <button type="button" className={`${secondaryButtonClass} col-span-2 justify-center md:col-span-1`} disabled={busy || !ready(item.id)}
                            onClick={() => void save(item)}>{t('size_class_cleanup_save')}</button>
                    </li>
                ))}
            </ul>
        </section>
    );
}
