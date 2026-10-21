import '../../test/setupDom.ts';
import '../../test/nextNavigationStub.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, click, json, setValue, stubFetch, submit } from '../../test/reactHarness.ts';
import type { Posted } from '../../test/reactHarness.ts';
import { currentUrl, navigate } from '../../test/nextNavigationStub.ts';
import DemoAssistant from './DemoAssistant';

// Demo-assistent via de echte component: rondleiding met Volgende/Vorige/Stoppen en orderzoeken dat alleen GET gebruikt.

const order = (id: number, customerName: string, customerReference: string | null) => ({
    id, requestId: `o-${id}`, customerId: id, customerName, status: 'Open', customerReference,
    requestedDeliveryDate: '2026-10-29T22:00:00.000Z', actorReference: 'office-1', createdAt: '2026-10-19T09:00:00.000Z', version: 1,
    cancelledAt: null, cancelReason: null, cancelledBy: null, fulfilled: false, delivered: false, lines: [],
});

let mounted: Mounted;
let posted: Posted[];
let gets: string[];
const methods: string[] = [];

describe('DemoAssistant', () => {
    beforeEach(() => {
        gets = [];
        methods.length = 0;
        posted = stubFetch({
            onGet: (url) => {
                gets.push(url);
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
        assert.equal(currentUrl(), '/inspection');
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

    test('never writes: only GET requests on the sales order list', async () => {
        await mounted.render(<DemoAssistant />);
        await open();
        await searchFor('demo');
        await mounted.waitFor(() => mounted.buttons().some((b) => b.textContent?.includes('#15')), 'results');
        assert.deepEqual(posted, []);
        assert.ok(methods.every((m) => m === 'GET'));
        assert.ok(gets.every((url) => /\/sales-orders$/.test(url)));
    });
});
