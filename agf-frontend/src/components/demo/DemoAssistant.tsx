'use client';

import { FormEvent, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslation } from '../../i18n/LanguageContext';
import { TOUR_STEPS, findSalesOrders, isAllowedDemoHref, orderHref } from '../../lib/demoAssistant';
import { showDemoTourAutopilot } from '../../lib/featureFlags';
import { TOUR_CHAPTERS } from '../../lib/tour/chapters';
import type { TourChapter, TourRect, TourSpeed, TourTiming } from '../../lib/tour/types';
import { listSalesOrders } from '../../services/salesOrderService';
import { SalesOrder } from '../../types/salesOrder';
import TourOverlay, { TOUR_PANEL_Z } from './TourOverlay';
import TourPanel from './TourPanel';
import { useTour } from './useTour';

// Demo-assistent: rondleiding en orderzoeken in de bestaande orderlijst; geen AI, navigatie uitsluitend naar de vaste
// routes uit lib/demoAssistant. Zonder NEXT_PUBLIC_DEMO_TOUR blijft het de vaste navigatie-rondleiding (alleen lezen);
// met die vlag wordt de tab Rondleiding de autopilot (lib/tour), die op de demo-omgeving ook schermen invult.

const overlaps = (a: TourRect, b: { top: number; left: number; bottom: number; right: number }) =>
    !(a.left > b.right || a.left + a.width < b.left || a.top > b.bottom || a.top + a.height < b.top);

const MAX_SHOWN = 10;

type SearchState =
    | { kind: 'idle' }
    | { kind: 'loading' }
    | { kind: 'error' }
    | { kind: 'none'; query: string }
    | { kind: 'opened'; order: SalesOrder }
    | { kind: 'choose'; orders: SalesOrder[] };

export default function DemoAssistant({ autopilot = showDemoTourAutopilot, chapters = TOUR_CHAPTERS, tourTiming }: {
    autopilot?: boolean;
    chapters?: readonly TourChapter[];
    // Alleen voor tests: kortere wachttijden.
    tourTiming?: (speed: TourSpeed) => TourTiming;
}) {
    const { t } = useTranslation();
    const router = useRouter();
    const tour = useTour(autopilot, tourTiming);
    const panel = useRef<HTMLElement | null>(null);
    const [dock, setDock] = useState<'bottom' | 'top'>('bottom');
    const [compact, setCompact] = useState(false);
    const [open, setOpen] = useState(false);
    const [tab, setTab] = useState<'tour' | 'search'>('tour');
    const [stepIndex, setStepIndex] = useState<number | null>(null);
    const [query, setQuery] = useState('');
    const [search, setSearch] = useState<SearchState>({ kind: 'idle' });
    const request = useRef<AbortController | null>(null);

    useEffect(() => () => request.current?.abort(), []);

    // Het paneel mag het element waar de rondleiding mee bezig is niet afdekken: dan naar de andere kant, en als het
    // daar ook in de weg zit, klein (alleen de stap en de knoppen).
    // Per element één keer beslissen, zodat het paneel nooit heen en weer springt.
    const target = tour.state.target;
    const decidedFor = useRef<string | null>(null);
    useLayoutEffect(() => {
        const element = panel.current;
        if (!target || !element) {
            decidedFor.current = null;
            setCompact(false);
            return;
        }
        const key = `${Math.round(target.top)}:${Math.round(target.left)}:${Math.round(target.width)}:${Math.round(target.height)}`;
        const rect = element.getBoundingClientRect();
        if (decidedFor.current === key || target.width === 0 || target.height === 0 || rect.width === 0 || !overlaps(target, rect)) return;
        decidedFor.current = key;
        const top = dock === 'bottom' ? 16 : window.innerHeight - 16 - rect.height;
        if (!overlaps(target, { left: rect.left, right: rect.right, top, bottom: top + rect.height })) setDock(dock === 'bottom' ? 'top' : 'bottom');
        else setCompact(true);
    }, [target, dock]);

    const go = (href: string) => {
        if (!isAllowedDemoHref(href)) return;
        router.push(href);
    };

    // Sluiten, naar Rondleiding wisselen of een rondleidingstap tonen breekt een lopende zoekopdracht af; het late antwoord
    // navigeert dan niet meer en toont niets, en de laadstatus gaat terug zodat zoeken daarna weer werkt.
    const cancelSearch = () => {
        request.current?.abort();
        request.current = null;
        setSearch((current) => (current.kind === 'loading' ? { kind: 'idle' } : current));
    };

    const showStep = (index: number) => {
        cancelSearch();
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
            if (controller.signal.aborted || request.current !== controller) return;
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
            if (!controller.signal.aborted && request.current === controller) setSearch({ kind: 'error' });
        }
    };

    const orderLabel = (order: SalesOrder) => `#${order.id} · ${order.customerName ?? '—'}${order.customerReference ? ` · ${order.customerReference}` : ''}`;

    if (!open) {
        return (
            <button type="button" onClick={() => setOpen(true)} className={`fixed bottom-4 right-4 ${TOUR_PANEL_Z} rounded-full bg-blue-700 px-4 py-2 text-sm font-medium text-white shadow-lg hover:bg-blue-800`}>
                {t('demo_assistant_open')}
            </button>
        );
    }

    const step = stepIndex === null ? null : TOUR_STEPS[stepIndex];
    const tabClass = (active: boolean) => `flex-1 rounded px-2 py-1 text-sm ${active ? 'bg-white font-medium text-blue-800 shadow-sm' : 'text-gray-600 hover:text-gray-900'}`;
    const buttonClass = 'rounded border border-gray-300 bg-white px-3 py-1 text-sm text-gray-700 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-40';

    return (
        <section ref={panel} aria-label={t('demo_assistant_title')} data-dock={dock}
            className={`fixed ${dock === 'bottom' ? 'bottom-4' : 'top-4'} right-4 ${TOUR_PANEL_Z} w-[22rem] max-w-[calc(100vw-2rem)] rounded-lg border border-gray-300 bg-white shadow-xl`}>
            {/* Portal naar <body>: de laag dekt de pagina af, het paneel blijft erboven. */}
            {autopilot && <TourOverlay state={tour.state} />}
            <header className="flex items-start justify-between gap-2 border-b border-gray-200 px-4 py-2">
                <div>
                    <h2 className="text-sm font-semibold text-gray-900">{t('demo_assistant_title')}</h2>
                    <p className="text-xs text-gray-500">{t(autopilot ? 'demo_autopilot_label' : 'demo_no_ai_label')}</p>
                </div>
                <button type="button" onClick={() => { cancelSearch(); tour.runner.current?.stop(); setOpen(false); }} aria-label={t('demo_close')} className="text-gray-400 hover:text-gray-700">✕</button>
            </header>

            <div className="mx-4 mt-3 flex gap-1 rounded bg-gray-100 p-1" role="tablist">
                <button type="button" role="tab" aria-selected={tab === 'tour'} onClick={() => { cancelSearch(); setTab('tour'); }} className={tabClass(tab === 'tour')}>{t('demo_tab_tour')}</button>
                <button type="button" role="tab" aria-selected={tab === 'search'} onClick={() => setTab('search')} className={tabClass(tab === 'search')}>{t('demo_tab_search')}</button>
            </div>

            <div className={`${compact ? 'max-h-[30vh]' : 'max-h-[60vh]'} overflow-y-auto px-4 py-3 text-sm text-gray-700`}>
                {tab === 'tour' && autopilot && (
                    <TourPanel chapters={chapters} state={tour.state} settings={tour.settings} onSettings={tour.setSettings} done={tour.done} compact={compact}
                        onStart={(chapter) => { cancelSearch(); tour.start(chapter); }}
                        controls={{
                            pause: () => tour.runner.current?.pause(),
                            resume: () => tour.runner.current?.resume(),
                            back: () => tour.runner.current?.back(),
                            next: () => tour.runner.current?.next(),
                            stop: () => tour.runner.current?.stop(),
                            retry: () => tour.runner.current?.retry(),
                            doThisForMe: () => tour.runner.current?.doThisForMe(),
                        }} />
                )}
                {tab === 'tour' && !autopilot && (step === null || stepIndex === null ? (
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
