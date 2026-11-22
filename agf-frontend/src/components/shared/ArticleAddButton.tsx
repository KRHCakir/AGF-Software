'use client';

import { type KeyboardEvent, useEffect, useId, useMemo, useRef, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { type SearchableProduct, type VariantMatch, describeVariant, searchVariants } from '../../lib/productSearch';
import { inputClass } from '../supply/FieldError';
import { primaryButtonClass } from '../supply/SuccessPanel';

const MAX_LISTED = 50;

// Alle actieve artikelen op omschrijving, voor de lijst zonder zoekterm.
function allVariants(products: readonly SearchableProduct[]): VariantMatch[] {
    return products.flatMap((product) => product.variants.filter((variant) => !variant.isArchived).map((variant) => ({
        variantId: variant.id, productName: product.name, variant, description: describeVariant(product.name, variant), exactShortcut: false,
    }))).sort((a, b) => a.description.localeCompare(b.description));
}

// "Artikel toevoegen" op een order (feedback 22 november): een lijst van bestaande artikelen, geen vrije keuze. Zoeken op snelcode,
// naam, herkomst (ook de landcode) en maat; pijltjes kiezen, Enter of klik zet het artikel direct als regel in de order (de order zet de
// cursor op Aantal). Escape sluit de lijst. "Nieuw artikel" opent het zijpaneel; dat maakt het artikel en zet het op een nieuwe regel.
export default function ArticleAddButton({ products, onPick, onNewArticle, disabled = false, dataTour }: {
    products: readonly SearchableProduct[];
    onPick: (variantId: number) => void;
    onNewArticle?: () => void;
    disabled?: boolean;
    dataTour?: string;
}) {
    const { t } = useTranslation();
    const listId = useId();
    const [open, setOpen] = useState(false);
    const [query, setQuery] = useState('');
    const [highlight, setHighlight] = useState(0);
    const search = useRef<HTMLInputElement | null>(null);
    const button = useRef<HTMLButtonElement | null>(null);

    const matches = useMemo(() => (query.trim() === '' ? allVariants(products) : searchVariants(products, query)), [products, query]);
    const shown = matches.slice(0, MAX_LISTED);
    useEffect(() => { setHighlight(0); }, [query]);
    useEffect(() => { if (open) search.current?.focus(); }, [open]);
    // Pijltjes verplaatsen alleen de selectie; de begrensde lijst scrolt mee zodat de gekozen optie in beeld blijft.
    const activeId = shown[highlight]?.variantId;
    useEffect(() => {
        if (open && activeId !== undefined) document.getElementById(`${listId}-${activeId}`)?.scrollIntoView?.({ block: 'nearest' });
    }, [open, activeId, listId]);

    const close = (focusButton: boolean) => {
        setOpen(false);
        setQuery('');
        if (focusButton) setTimeout(() => button.current?.focus(), 0);
    };
    const pick = (match: VariantMatch) => {
        close(false);
        onPick(match.variantId);
    };

    const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
        if (event.key === 'ArrowDown') { event.preventDefault(); setHighlight((index) => Math.min(index + 1, shown.length - 1)); }
        else if (event.key === 'ArrowUp') { event.preventDefault(); setHighlight((index) => Math.max(index - 1, 0)); }
        else if (event.key === 'Enter') {
            event.preventDefault();
            const match = shown[highlight];
            if (match) pick(match);
        } else if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); close(true); }
    };

    return (
        // Smal scherm (390 px): de lijst is zo breed als de regel waarin de knop staat; breed scherm: 36rem onder de knop.
        <div className="relative w-full sm:w-auto">
            <button ref={button} type="button" className={primaryButtonClass} disabled={disabled} aria-expanded={open} aria-controls={open ? listId : undefined}
                onClick={() => (open ? close(true) : setOpen(true))} data-tour={dataTour}>+ {t('article_add_button')}</button>
            {open && (
                <div className="absolute left-0 right-0 z-30 mt-1 rounded-md border border-gray-300 bg-white p-2 shadow-lg sm:right-auto sm:w-[36rem]" data-testid="article-add-list">
                    <label htmlFor={`${listId}-search`} className="sr-only">{t('article_add_search')}</label>
                    <input ref={search} id={`${listId}-search`} type="search" value={query} onChange={(event) => setQuery(event.target.value)} onKeyDown={onKeyDown}
                        role="combobox" aria-expanded="true" aria-controls={listId} aria-activedescendant={shown[highlight] ? `${listId}-${shown[highlight].variantId}` : undefined}
                        placeholder={t('article_add_search')} className={`${inputClass} mt-0`} autoComplete="off" data-tour="article-add-search" />
                    <ul id={listId} role="listbox" aria-label={t('article_add_button')} className="mt-1 max-h-72 overflow-y-auto text-sm">
                        {shown.map((match, index) => (
                            <li key={match.variantId} id={`${listId}-${match.variantId}`} role="option" aria-selected={index === highlight}
                                className={`cursor-pointer rounded px-2 py-1 ${index === highlight ? 'bg-blue-100 text-blue-950' : 'text-gray-900 hover:bg-gray-100'}`}
                                onMouseDown={(event) => event.preventDefault()} onClick={() => pick(match)} data-tour="article-add-option" data-tour-key={match.variant.shortcutCode ?? String(match.variantId)}>
                                {match.variant.shortcutCode && <span className="mr-2 font-mono text-xs text-gray-700">{match.variant.shortcutCode}</span>}
                                {match.description}
                            </li>
                        ))}
                        {shown.length === 0 && <li className="px-2 py-1 text-gray-600">{t('article_add_none')}</li>}
                    </ul>
                    {matches.length > shown.length && <p className="mt-1 px-2 text-xs text-gray-600">{t('article_add_more', { count: String(matches.length - shown.length) })}</p>}
                    {onNewArticle && (
                        <button type="button" className="mt-2 text-sm font-medium text-blue-800 underline underline-offset-2" onClick={() => { button.current?.focus(); close(false); onNewArticle(); }} data-tour="article-add-new">
                            + {t('article_add_new')}
                        </button>
                    )}
                </div>
            )}
        </div>
    );
}
