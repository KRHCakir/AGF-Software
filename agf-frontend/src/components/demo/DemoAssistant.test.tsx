import '../../test/setupDom.ts';
import '../../test/nextNavigationStub.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, click, deferred, flush, json, setValue, stubFetch, submit } from '../../test/reactHarness.ts';
import type { Posted } from '../../test/reactHarness.ts';
import { currentUrl, navigate } from '../../test/nextNavigationStub.ts';
import DemoAssistant from './DemoAssistant';
import type { TourChapter } from '../../lib/tour/types.ts';

// Demo-assistent via de echte component: rondleiding met Volgende/Vorige/Stoppen en orderzoeken dat alleen GET gebruikt.

const order = (id: number, customerName: string, customerReference: string | null) => ({
    id, requestId: `o-${id}`, customerId: id, customerName, status: 'Open', customerReference,
    requestedDeliveryDate: '2026-10-29T22:00:00.000Z', actorReference: 'office-1', createdAt: '2026-10-19T09:00:00.000Z', version: 1,
    cancelledAt: null, cancelReason: null, cancelledBy: null, fulfilled: false, delivered: false, lines: [],
});

let mounted: Mounted;
let posted: Posted[];
let gets: string[];
// Bij een gezette `slow` blijft het zoekantwoord hangen tot de test het vrijgeeft; het negeert het abortsignaal bewust,
// zodat het late antwoord echt binnenkomt en alleen de component het mag negeren.
let slow: ReturnType<typeof deferred<Response>> | null;
const methods: string[] = [];

describe('DemoAssistant', () => {
    beforeEach(() => {
        gets = [];
        slow = null;
        methods.length = 0;
        posted = stubFetch({
            onGet: (url) => {
                gets.push(url);
                if (/\/sales-orders$/.test(url) && slow) { const pending = slow; slow = null; return pending.promise; }
                if (/\/sales-orders$/.test(url)) return json(200, [order(13, 'DEMO Klant B', 'DEMO-A'), order(14, 'DEMO Klant B', 'DEMO-B'), order(15, 'DEMO Klant C', 'DEMO-C')]);
                return json(404, { message: `unexpected GET ${url}` });
            },
            nextPost: async () => json(500, { message: 'no writes expected' }),
        });
        const getOnly = globalThis.fetch;
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => { methods.push(init?.method ?? 'GET'); return getOnly(input, init); }) as typeof fetch;
        navigate('/dashboard');
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    const open = async () => { await click(mounted.button('demo_assistant_open')!); };
    const searchFor = async (text: string) => {
        await click(mounted.button('demo_tab_search')!);
        await act(async () => { setValue(mounted.container.querySelector<HTMLInputElement>('input[type="search"]')!, text); });
        await submit(mounted.form()!);
    };

    test('shows an honest label that no AI is used', async () => {
        await mounted.render(<DemoAssistant />);
        await open();
        assert.ok(mounted.hasText('demo_no_ai_label'));
    });

    test('tour navigates with Next and Previous and stops', async () => {
        await mounted.render(<DemoAssistant />);
        await open();
        await click(mounted.button('demo_tour_start')!);
        assert.equal(currentUrl(), '/supply');
        assert.ok(mounted.hasText('demo_step_supply_body'));
        await click(mounted.button('demo_next')!);
        assert.equal(currentUrl(), '/inspection?search=DEMO');
        await click(mounted.button('demo_next')!);
        await click(mounted.button('demo_next')!);
        assert.equal(currentUrl(), '/sales-orders');
        assert.equal(mounted.button('demo_next')!.disabled, true);
        await click(mounted.button('demo_previous')!);
        assert.equal(currentUrl(), '/availability');
        await click(mounted.button('demo_stop')!);
        assert.equal(mounted.hasText('demo_step_availability_body'), false);
        assert.equal(currentUrl(), '/availability');
    });

    test('a single result opens the order; an unknown number shows a notice and stays put', async () => {
        await mounted.render(<DemoAssistant />);
        await open();
        await searchFor('#9999');
        await mounted.waitFor(() => mounted.hasText('demo_search_none'), 'no-result notice');
        assert.equal(currentUrl(), '/dashboard');
        await searchFor('DEMO-C');
        await mounted.waitFor(() => currentUrl() === '/sales-orders?orderId=15', 'order 15 opened');
    });

    test('several results let the user choose', async () => {
        await mounted.render(<DemoAssistant />);
        await open();
        await searchFor('demo klant b');
        const choice = await mounted.waitFor(() => mounted.buttons().find((b) => b.textContent?.includes('#14')), 'choice list');
        assert.equal(currentUrl(), '/dashboard');
        assert.ok(mounted.buttons().some((b) => b.textContent?.includes('#13')));
        await click(choice);
        assert.equal(currentUrl(), '/sales-orders?orderId=14');
    });

    const lateList = () => json(200, [order(13, 'DEMO Klant B', 'DEMO-A'), order(14, 'DEMO Klant B', 'DEMO-B'), order(15, 'DEMO Klant C', 'DEMO-C')]);

    test('closing the panel during a search ignores the late answer; searching works again afterwards', async () => {
        await mounted.render(<DemoAssistant />);
        await open();
        slow = deferred<Response>();
        const late = slow;
        await searchFor('DEMO-C');
        assert.ok(mounted.hasText('demo_search_loading'));
        await click(mounted.container.querySelector<HTMLButtonElement>('button[aria-label="demo_close"]')!);
        late.resolve(lateList());
        await flush(); await flush();
        assert.equal(currentUrl(), '/dashboard');
        await open();
        await click(mounted.button('demo_tab_search')!);
        assert.equal(mounted.hasText('demo_search_loading'), false);
        assert.equal(mounted.hasText('demo_search_opened'), false);
        assert.equal(mounted.button('demo_search_button')!.disabled, false);
        await searchFor('DEMO-C');
        await mounted.waitFor(() => currentUrl() === '/sales-orders?orderId=15', 'order 15 opened after a new search');
    });

    test('switching to the tour and starting it during a search ignores the late answer', async () => {
        await mounted.render(<DemoAssistant />);
        await open();
        slow = deferred<Response>();
        const late = slow;
        await searchFor('demo klant b');
        await click(mounted.button('demo_tab_tour')!);
        await click(mounted.button('demo_tour_start')!);
        assert.equal(currentUrl(), '/supply');
        late.resolve(lateList());
        await flush(); await flush();
        assert.equal(currentUrl(), '/supply');
        assert.ok(mounted.hasText('demo_step_supply_body'));
        await click(mounted.button('demo_tab_search')!);
        assert.equal(mounted.hasText('demo_search_choose'), false);
        assert.equal(mounted.buttons().some((b) => b.textContent?.includes('#14')), false);
        assert.equal(mounted.hasText('demo_search_loading'), false);
        assert.equal(mounted.button('demo_search_button')!.disabled, false);
    });

    test('never writes: only GET requests on the sales order list', async () => {
        await mounted.render(<DemoAssistant />);
        await open();
        await searchFor('demo');
        await mounted.waitFor(() => mounted.buttons().some((b) => b.textContent?.includes('#15')), 'results');
        assert.deepEqual(posted, []);
        assert.ok(methods.every((m) => m === 'GET'));
        assert.ok(gets.every((url) => /\/sales-orders$/.test(url)));
    });

    test('without NEXT_PUBLIC_DEMO_TOUR the tour stays the navigation tour: no autopilot, no chapters', async () => {
        await mounted.render(<DemoAssistant />);
        await open();
        assert.ok(mounted.button('demo_tour_start'));
        assert.equal(mounted.hasText('demo_tour_mode_watch'), false);
        assert.equal(mounted.hasText('demo_tour_chapters'), false);
        assert.ok(mounted.hasText('demo_no_ai_label'));
    });
});

// Autopilot via de echte component: hoofdstukkenlijst, afspelen met "Nu doen we dit" en "Waarom", invullen op de pagina
// met DEMO-TOUR-referentie en de actor "Demo-assistent", klikblokkade tijdens Kijk mee, en een voorcontrole die zegt wat ontbreekt.
const QUICK = () => ({ charMs: 1, actionMs: 1, readMs: 5, cursorMs: 0, pollMs: 2, timeoutMs: 500 });

const writing: TourChapter = {
    id: 'write', titleKey: 'ch_write', summaryKey: 'ch_write_summary', writes: true, doneKey: 'ch_write_done',
    steps: [
        { id: 'ref', route: '/supply', titleKey: 'st_ref', whatKey: 'what_ref', whyKey: 'why_ref', actions: [{ kind: 'type', target: 'supply-reference', text: '{{tourRef}}' }] },
        { id: 'wait', route: '/supply', titleKey: 'st_wait', whatKey: 'what_wait', whyKey: 'why_wait', actions: [{ kind: 'wait', target: 'tour-gate' }] },
    ],
};
const blocked: TourChapter = {
    id: 'blocked', titleKey: 'ch_blocked', summaryKey: 'ch_blocked_summary', writes: true, steps: writing.steps,
    precheck: async () => ({ missing: [{ key: 'tour_missing_vendor', params: { search: 'Nature' } }] }),
};
const soon: TourChapter = { id: 'soon', titleKey: 'ch_soon', summaryKey: 'ch_soon_summary', writes: true, steps: [], comingSoon: true };

async function until(condition: () => boolean, what: string) {
    for (let i = 0; i < 400; i++) {
        if (condition()) return;
        await act(async () => { await new Promise((resolve) => setTimeout(resolve, 3)); });
    }
    throw new Error(`Timed out waiting for ${what}`);
}

describe('DemoAssistant autopilot', () => {
    beforeEach(() => {
        localStorage.clear();
        localStorage.setItem('agf_actor_reference', 'Jan');
        navigate('/dashboard');
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    const page = (
        <>
            <input data-tour="supply-reference" defaultValue="" />
            <DemoAssistant autopilot chapters={[writing, blocked, soon]} tourTiming={QUICK} />
        </>
    );

    test('chapter list with mode and speed; a writing chapter fills the page as Demo-assistent and shows Now/Why and the controls', async () => {
        await mounted.render(page);
        await click(mounted.button('demo_assistant_open')!);
        assert.ok(mounted.hasText('demo_autopilot_label'));
        for (const key of ['demo_tour_mode_watch', 'demo_tour_mode_self', 'demo_tour_speed_slow', 'demo_tour_speed_normal', 'demo_tour_speed_fast']) assert.ok(mounted.button(key), key);
        assert.ok(mounted.hasText('demo_tour_coming_soon'));
        assert.equal(mounted.container.querySelectorAll('[data-testid="tour-chapter-soon"] button').length, 0);

        await click(mounted.container.querySelector<HTMLButtonElement>('[data-testid="tour-chapter-write"] button')!);
        await until(() => mounted.hasText('what_wait'), 'second step');
        assert.equal(currentUrl(), '/supply');
        assert.equal(mounted.container.querySelector<HTMLInputElement>('[data-tour="supply-reference"]')!.value, 'DEMO-TOUR-001');
        assert.equal(localStorage.getItem('agf_actor_reference'), 'Demo-assistent');
        assert.ok(mounted.hasText('demo_step_counter'));
        assert.ok(mounted.hasText('demo_tour_now') && mounted.hasText('demo_tour_why') && mounted.hasText('why_wait'));
        for (const key of ['demo_tour_pause', 'demo_tour_back', 'demo_tour_next', 'demo_tour_stop']) assert.ok(mounted.button(key), key);
        assert.ok(window.document.querySelector('[data-testid="tour-blocker"]'), 'page clicks are blocked while watching');

        await click(mounted.button('demo_tour_pause')!);
        assert.ok(mounted.hasText('demo_tour_paused'));
        assert.equal(window.document.querySelector('[data-testid="tour-blocker"]'), null);
        await click(mounted.button('demo_tour_resume')!);
        await act(async () => {
            const gate = window.document.createElement('span');
            gate.setAttribute('data-tour', 'tour-gate');
            mounted.container.appendChild(gate);
        });
        await until(() => mounted.hasText('demo_tour_done'), 'done');
        assert.ok(mounted.hasText('ch_write_done'));
        assert.ok(mounted.hasText('demo_tour_chapter_done'));
        assert.equal(localStorage.getItem('agf_actor_reference'), 'Jan');
        assert.equal(posted.length, 0);
    });

    test('a tab closed in the middle of a chapter: the next visit gives the own user name back', async () => {
        localStorage.setItem('agf_actor_reference', 'Demo-assistent');
        localStorage.setItem('agf_demo_tour_saved_actor', JSON.stringify('Jan'));
        await mounted.render(page);
        assert.equal(localStorage.getItem('agf_actor_reference'), 'Jan');
        assert.equal(localStorage.getItem('agf_demo_tour_saved_actor'), null);
    });

    test('a failed precheck names what is missing and offers no retry', async () => {
        await mounted.render(page);
        await click(mounted.button('demo_assistant_open')!);
        await click(mounted.container.querySelector<HTMLButtonElement>('[data-testid="tour-chapter-blocked"] button')!);
        await until(() => mounted.hasText('demo_tour_precheck_title'), 'precheck message');
        assert.ok(mounted.hasText('tour_missing_vendor'));
        assert.equal(mounted.button('demo_tour_retry'), null);
        assert.equal(currentUrl(), '/dashboard');
        await click(mounted.button('demo_tour_to_chapters')!);
        assert.ok(mounted.hasText('demo_tour_chapters'));
    });

    test('Stop and closing the panel end the tour and give the user name back', async () => {
        await mounted.render(page);
        await click(mounted.button('demo_assistant_open')!);
        await click(mounted.container.querySelector<HTMLButtonElement>('[data-testid="tour-chapter-write"] button')!);
        await until(() => mounted.hasText('what_wait'), 'second step');
        await click(mounted.container.querySelector<HTMLButtonElement>('button[aria-label="demo_close"]')!);
        assert.equal(localStorage.getItem('agf_actor_reference'), 'Jan');
        assert.equal(window.document.querySelector('[data-testid="tour-highlight"]'), null);
    });
});
