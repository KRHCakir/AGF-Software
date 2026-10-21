'use client';

import { FormEvent, useEffect, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslation } from '../../i18n/LanguageContext';
import { TOUR_STEPS, findSalesOrders, isAllowedDemoHref, orderHref } from '../../lib/demoAssistant';
import { listSalesOrders } from '../../services/salesOrderService';
import { SalesOrder } from '../../types/salesOrder';

// Demo-assistent: vaste rondleiding en orderzoeken in de bestaande orderlijst. Alleen navigeren, zoeken en uitleggen;
// geen AI, geen schrijfacties, en navigatie uitsluitend naar de vaste routes uit lib/demoAssistant.

const MAX_SHOWN = 10;

type SearchState =
    | { kind: 'idle' }
    | { kind: 'loading' }
    | { kind: 'error' }
    | { kind: 'none'; query: string }
    | { kind: 'opened'; order: SalesOrder }
    | { kind: 'choose'; orders: SalesOrder[] };

export default function DemoAssistant() {
    const { t } = useTranslation();
    const router = useRouter();
    const [open, setOpen] = useState(false);
    const [tab, setTab] = useState<'tour' | 'search'>('tour');
    const [stepIndex, setStepIndex] = useState<number | null>(null);
    const [query, setQuery] = useState('');
    const [search, setSearch] = useState<SearchState>({ kind: 'idle' });
    const request = useRef<AbortController | null>(null);

    useEffect(() => () => request.current?.abort(), []);

    const go = (href: string) => {
        if (!isAllowedDemoHref(href)) return;
        router.push(href);
    };

    const showStep = (index: number) => {
        setStepIndex(index);
        go(TOUR_STEPS[index].href);
    };

    const runSearch = async (event: FormEvent) => {
        event.preventDefault();
        request.current?.abort();
        const term = query;
        if (term.trim() === '') { setSearch({ kind: 'idle' }); return; }
        const controller = new AbortController();
        request.current = controller;
        setSearch({ kind: 'loading' });
        try {
            const orders = await listSalesOrders({}, controller.signal);
            if (controller.signal.aborted) return;
            const found = findSalesOrders(orders, term).orders ?? [];
            if (found.length === 0) {
                setSearch({ kind: 'none', query: term.trim() });
            } else if (found.length === 1) {
                setSearch({ kind: 'opened', order: found[0] });
                go(orderHref(found[0].id));
            } else {
                setSearch({ kind: 'choose', orders: found });
            }
        } catch {
            if (!controller.signal.aborted) setSearch({ kind: 'error' });
        }
    };

    const orderLabel = (order: SalesOrder) => `#${order.id} · ${order.customerName ?? '—'}${order.customerReference ? ` · ${order.customerReference}` : ''}`;

    if (!open) {
        return (
            <button type="button" onClick={() => setOpen(true)} className="fixed bottom-4 right-4 z-30 rounded-full bg-blue-700 px-4 py-2 text-sm font-medium text-white shadow-lg hover:bg-blue-800">
                {t('demo_assistant_open')}
            </button>
        );
    }

    const step = stepIndex === null ? null : TOUR_STEPS[stepIndex];
    const tabClass = (active: boolean) => `flex-1 rounded px-2 py-1 text-sm ${active ? 'bg-white font-medium text-blue-800 shadow-sm' : 'text-gray-600 hover:text-gray-900'}`;
    const buttonClass = 'rounded border border-gray-300 bg-white px-3 py-1 text-sm text-gray-700 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-40';

    return (
        <section aria-label={t('demo_assistant_title')} className="fixed bottom-4 right-4 z-30 w-[22rem] max-w-[calc(100vw-2rem)] rounded-lg border border-gray-300 bg-white shadow-xl">
            <header className="flex items-start justify-between gap-2 border-b border-gray-200 px-4 py-2">
                <div>
                    <h2 className="text-sm font-semibold text-gray-900">{t('demo_assistant_title')}</h2>
                    <p className="text-xs text-gray-500">{t('demo_no_ai_label')}</p>
                </div>
                <button type="button" onClick={() => setOpen(false)} aria-label={t('demo_close')} className="text-gray-400 hover:text-gray-700">✕</button>
            </header>

            <div className="mx-4 mt-3 flex gap-1 rounded bg-gray-100 p-1" role="tablist">
                <button type="button" role="tab" aria-selected={tab === 'tour'} onClick={() => setTab('tour')} className={tabClass(tab === 'tour')}>{t('demo_tab_tour')}</button>
                <button type="button" role="tab" aria-selected={tab === 'search'} onClick={() => setTab('search')} className={tabClass(tab === 'search')}>{t('demo_tab_search')}</button>
            </div>

            <div className="max-h-[60vh] overflow-y-auto px-4 py-3 text-sm text-gray-700">
                {tab === 'tour' && (step === null || stepIndex === null ? (
                    <div className="space-y-3">
                        <p>{t('demo_tour_intro')}</p>
                        <button type="button" onClick={() => showStep(0)} className="rounded bg-blue-700 px-3 py-1 text-sm font-medium text-white hover:bg-blue-800">{t('demo_tour_start')}</button>
                    </div>
                ) : (
                    <div className="space-y-3">
                        <p className="text-xs text-gray-500">{t('demo_step_counter', { current: String(stepIndex + 1), total: String(TOUR_STEPS.length) })}</p>
                        <h3 className="font-semibold text-gray-900">{t(step.titleKey)}</h3>
                        <p>{t(step.bodyKey)}</p>
                        <div className="flex gap-2">
                            <button type="button" onClick={() => showStep(stepIndex - 1)} disabled={stepIndex === 0} className={buttonClass}>{t('demo_previous')}</button>
                            <button type="button" onClick={() => showStep(stepIndex + 1)} disabled={stepIndex === TOUR_STEPS.length - 1} className={buttonClass}>{t('demo_next')}</button>
                            <button type="button" onClick={() => setStepIndex(null)} className={`${buttonClass} ml-auto`}>{t('demo_stop')}</button>
                        </div>
                    </div>
                ))}

                {tab === 'search' && (
                    <div className="space-y-3">
                        <form onSubmit={runSearch} className="flex gap-2">
                            <input
                                type="search"
                                value={query}
                                onChange={(event) => setQuery(event.target.value)}
                                placeholder={t('demo_search_placeholder')}
                                aria-label={t('demo_search_label')}
                                className="min-w-0 flex-1 rounded border border-gray-300 px-2 py-1 text-sm"
                            />
                            <button type="submit" disabled={search.kind === 'loading'} className={buttonClass}>{t('demo_search_button')}</button>
                        </form>
                        <p className="text-xs text-gray-500">{t('demo_search_hint')}</p>
                        <div aria-live="polite">
                            {search.kind === 'loading' && <p>{t('demo_search_loading')}</p>}
                            {search.kind === 'error' && <p className="text-red-700">{t('demo_search_error')}</p>}
                            {search.kind === 'none' && <p>{t('demo_search_none', { query: search.query })}</p>}
                            {search.kind === 'opened' && <p>{t('demo_search_opened', { order: orderLabel(search.order) })}</p>}
                            {search.kind === 'choose' && (
                                <div className="space-y-2">
                                    <p>{t('demo_search_choose', { count: String(search.orders.length) })}</p>
                                    <ul className="space-y-1">
                                        {search.orders.slice(0, MAX_SHOWN).map((order) => (
                                            <li key={order.id}>
                                                <button type="button" onClick={() => go(orderHref(order.id))} className="w-full rounded border border-gray-200 px-2 py-1 text-left hover:bg-blue-50">
                                                    {orderLabel(order)}
                                                </button>
                                            </li>
                                        ))}
                                    </ul>
                                    {search.orders.length > MAX_SHOWN && <p className="text-xs text-gray-500">{t('demo_search_more', { count: String(search.orders.length - MAX_SHOWN) })}</p>}
                                </div>
                            )}
                        </div>
                    </div>
                )}
            </div>
        </section>
    );
}
