import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act, useState } from 'react';
import { Mounted } from '../../test/reactHarness.ts';
import VendorPicker from '../../components/purchase/VendorPicker';
import { TourRunner } from './runner.ts';
import type { TourChapter, TourState, TourStep } from './types.ts';

// De stapmotor tegen een echt (jsdom-)document met echte React-componenten: typen, pick met Tab, wachten, een mislukte
// controle die netjes stopt, pauze/hervat/stop, opruimen bij afbreken, en Doe zelf dat op de gebruiker wacht.

const FAST = { charMs: 1, actionMs: 1, readMs: 1, cursorMs: 0, pollMs: 2, timeoutMs: 400 };

let mounted: Mounted;
let runner: TourRunner;
let states: TourState[];
let path: string;
let storedActor: string | null;
let refs: number;

const latest = () => states[states.length - 1];
const step = (id: string, actions: TourStep['actions'], extra: Partial<TourStep> = {}): TourStep => ({
    id, route: '/supply', titleKey: `t_${id}`, whatKey: `w_${id}`, whyKey: `y_${id}`, actions, ...extra,
});
const chapter = (steps: TourStep[], extra: Partial<TourChapter> = {}): TourChapter => ({ id: 'test', titleKey: 'c', summaryKey: 's', writes: true, steps, ...extra });

function makeRunner(allowWrites = true, timing = FAST) {
    return new TourRunner({
        document: window.document,
        navigate: (href) => { path = href; },
        currentPath: () => path,
        isAllowedHref: (href) => ['/supply', '/purchase-orders'].includes(href.split('?')[0]),
        allowWrites,
        actor: { get: () => storedActor, set: (value) => { storedActor = value; } },
        nextRef: () => `DEMO-TOUR-${String(++refs).padStart(3, '0')}`,
        today: () => '2026-10-29',
        onChange: (state) => { states.push(state); },
        timing: () => timing,
    });
}

async function until(condition: () => boolean, what: string) {
    for (let i = 0; i < 400; i++) {
        if (condition()) return;
        await act(async () => { await new Promise((resolve) => setTimeout(resolve, 2)); });
    }
    throw new Error(`Timed out waiting for ${what}: ${JSON.stringify(latest())}`);
}

// Gecontroleerd veld zoals in de app: elke letter gaat via onChange door React.
function Field({ log }: { log: string[] }) {
    const [value, setValue] = useState('');
    return <input data-tour="supply-reference" value={value} onChange={(event) => { log.push(event.target.value); setValue(event.target.value); }} />;
}

const vendors = [
    { id: 1, name: "Nature's Pride", searchCode: 'NP', vatNumber: null, isActive: true },
    { id: 2, name: 'Example Supplier', searchCode: null, vatNumber: null, isActive: true },
];

function Vendor({ onPicked }: { onPicked: (id: string) => void }) {
    const [value, setValue] = useState('');
    return <VendorPicker id="vendorId" ariaLabel="vendor" vendors={vendors} value={value}
        onChange={(vendor) => { const next = vendor ? String(vendor.id) : ''; setValue(next); onPicked(next); }} />;
}

// Het zoekveld zelf krijgt de vaste rondleidingnaam, zoals de schermen dat doen.
const tagVendorInput = () => mounted.container.querySelector('#vendorId-input')!.setAttribute('data-tour', 'supply-vendor');

describe('TourRunner', () => {
    beforeEach(() => {
        mounted = new Mounted();
        states = [];
        path = '/supply';
        storedActor = 'Jan';
        refs = 0;
        runner = makeRunner();
    });
    afterEach(async () => {
        runner.dispose();
        await mounted.unmount();
    });

    test('types letter by letter through React, with the DEMO-TOUR reference and the Demo-assistent actor while running', async () => {
        const log: string[] = [];
        await mounted.render(<Field log={log} />);
        const actorsSeen: (string | null)[] = [];
        await act(async () => {
            void runner.start(chapter([step('ref', [{ kind: 'type', target: 'supply-reference', text: '{{tourRef}}' }, { kind: 'wait', ms: 1 }])]), { mode: 'watch', speed: 'fast' });
        });
        await until(() => { actorsSeen.push(storedActor); return latest().status === 'done'; }, 'done');
        const input = mounted.container.querySelector('input')!;
        assert.equal(input.value, 'DEMO-TOUR-001');
        assert.deepEqual(log.slice(0, 3), ['D', 'DE', 'DEM']);
        assert.equal(log.length, 'DEMO-TOUR-001'.length);
        assert.ok(actorsSeen.includes('Demo-assistent'));
        assert.equal(storedActor, 'Jan');
    });

    test('an action with an empty `when` is skipped, so a field the screen does not ask for is never needed', async () => {
        const log: string[] = [];
        await mounted.render(<Field log={log} />);
        await act(async () => {
            void runner.start(chapter([step('skip', [
                { kind: 'type', target: 'receive-class:1', text: '{{tradeClass}}', when: '{{tradeClass}}' },
                { kind: 'type', target: 'supply-reference', text: 'X', when: '{{tourRef}}' },
            ])], { precheck: async () => ({ missing: [], vars: { tradeClass: '' } }) }), { mode: 'watch', speed: 'fast' });
        });
        await until(() => latest().status === 'done', 'done');
        assert.equal(mounted.container.querySelector('input')!.value, 'X');
    });

    test('pick: types in the vendor search and takes the suggestion with Tab, like a user', async () => {
        let picked = '';
        await mounted.render(<Vendor onPicked={(id) => { picked = id; }} />);
        tagVendorInput();
        await act(async () => {
            void runner.start(chapter([step('vendor', [{ kind: 'pick', target: 'supply-vendor', text: 'Nature', expect: "Nature's Pride" }])]), { mode: 'watch', speed: 'fast' });
        });
        await until(() => latest().status === 'done', 'done');
        assert.equal(picked, '1');
        const input = mounted.container.querySelector<HTMLInputElement>('[data-tour="supply-vendor"]')!;
        assert.ok(input.readOnly);
        assert.ok(input.value.includes("Nature's Pride"));
    });

    test('pick that finds no clear suggestion stops with a clear message instead of continuing', async () => {
        await mounted.render(<Vendor onPicked={() => {}} />);
        tagVendorInput();
        await act(async () => {
            void runner.start(chapter([step('vendor', [{ kind: 'pick', target: 'supply-vendor', text: 'e', expect: "Nature's Pride" }]), step('never', [{ kind: 'wait', ms: 1 }])]), { mode: 'watch', speed: 'fast' });
        });
        await until(() => latest().status === 'failed', 'failed');
        assert.equal(latest().error?.key, 'tour_error_pick');
        assert.equal(latest().stepIndex, 0);
    });

    test('wait for an element that appears later, then assert its text; a failing assert stops and later steps never run', async () => {
        await mounted.render(<p />);
        await act(async () => {
            void runner.start(chapter([
                step('wait', [{ kind: 'wait', target: 'supply-order-number' }, { kind: 'remember', target: 'supply-order-number', name: 'orderNumber', pattern: /(IO-\d{4}-\d{4})/ }]),
                step('check', [{ kind: 'assert', target: 'supply-order-number', text: 'IO-1999' }]),
                step('never', [{ kind: 'type', target: 'supply-order-number', text: 'x' }]),
            ]), { mode: 'watch', speed: 'fast' });
        });
        await act(async () => {
            const number = window.document.createElement('strong');
            number.setAttribute('data-tour', 'supply-order-number');
            number.textContent = 'Inkooporder IO-2026-0057';
            mounted.container.appendChild(number);
        });
        await until(() => latest().status === 'failed', 'failed');
        assert.equal(latest().vars.orderNumber, 'IO-2026-0057');
        assert.equal(latest().stepIndex, 1);
        assert.deepEqual([latest().error?.key, latest().error?.params?.expected], ['tour_error_assert', 'IO-1999']);
        assert.equal(storedActor, 'Jan');
    });

    test('pause stops typing mid-word, resume continues, stop restores the actor and clears the highlight', async () => {
        const log: string[] = [];
        await mounted.render(<Field log={log} />);
        runner.dispose();
        runner = makeRunner(true, { ...FAST, charMs: 15 });
        await act(async () => {
            void runner.start(chapter([step('ref', [{ kind: 'type', target: 'supply-reference', text: 'ABCDEFGHIJKLMNOP' }])]), { mode: 'watch', speed: 'fast' });
        });
        await until(() => log.length >= 2, 'typing started');
        await act(async () => runner.pause());
        assert.equal(latest().status, 'paused');
        const frozen = log.length;
        await act(async () => { await new Promise((resolve) => setTimeout(resolve, 80)); });
        assert.ok(log.length <= frozen + 1, `kept typing while paused: ${frozen} -> ${log.length}`);
        await act(async () => runner.resume());
        await until(() => log.length > frozen + 2, 'typing resumed');
        await act(async () => runner.stop());
        const stoppedAt = log.length;
        await act(async () => { await new Promise((resolve) => setTimeout(resolve, 60)); });
        assert.equal(log.length, stoppedAt);
        assert.deepEqual([latest().status, latest().target, storedActor], ['stopped', null, 'Jan']);
    });

    test('Doe zelf: marks the button and waits for the user; "Doe dit stuk voor mij" types for them', async () => {
        const log: string[] = [];
        let clicks = 0;
        await mounted.render(<><Field log={log} /><button type="button" data-tour="supply-save" onClick={() => { clicks++; }}>Opslaan</button></>);
        await act(async () => {
            void runner.start(chapter([
                step('click', [{ kind: 'click', target: 'supply-save' }]),
                step('type', [{ kind: 'type', target: 'supply-reference', text: 'NP-1' }]),
            ]), { mode: 'self', speed: 'fast' });
        });
        await until(() => latest().status === 'waiting', 'waiting for the user');
        assert.ok(latest().target !== null);
        await act(async () => { await new Promise((resolve) => setTimeout(resolve, 30)); });
        assert.deepEqual([latest().status, latest().stepIndex, clicks], ['waiting', 0, 0]);
        await act(async () => { mounted.container.querySelector<HTMLButtonElement>('[data-tour="supply-save"]')!.click(); });
        await until(() => latest().stepIndex === 1 && latest().status === 'waiting', 'next step waits');
        assert.equal(clicks, 1);
        await act(async () => runner.doThisForMe());
        await until(() => latest().status === 'done', 'done');
        assert.equal(mounted.container.querySelector('input')!.value, 'NP-1');
    });

    test('skip and back move between steps; navigation only to allowed routes', async () => {
        await mounted.render(<p />);
        await act(async () => {
            void runner.start(chapter([
                step('a', [{ kind: 'wait', target: 'never-there' }]),
                step('b', [{ kind: 'navigate', href: 'https://example.com' }], { route: '/purchase-orders' }),
            ]), { mode: 'watch', speed: 'fast' });
        });
        await until(() => latest().status === 'running' && latest().stepIndex === 0, 'first step');
        await act(async () => runner.next());
        await until(() => latest().status === 'failed', 'failed on external url');
        assert.equal(path, '/purchase-orders');
        assert.deepEqual([latest().stepIndex, latest().error?.key, latest().error?.params?.href], [1, 'tour_error_route', 'https://example.com']);
        await act(async () => runner.back());
        await until(() => latest().status === 'running' && latest().stepIndex === 0, 'back to first step');
        await act(async () => runner.stop());
    });

    test('precheck: missing prerequisites are named and nothing starts; writing chapters need the tour flag', async () => {
        await mounted.render(<p />);
        const missing = chapter([step('a', [{ kind: 'wait', ms: 1 }])], { precheck: async () => ({ missing: [{ key: 'tour_missing_vendor', params: { search: 'Nature' } }] }) });
        await act(async () => { await runner.start(missing, { mode: 'watch', speed: 'fast' }); });
        assert.equal(latest().status, 'failed');
        assert.deepEqual(latest().error?.missing, [{ key: 'tour_missing_vendor', params: { search: 'Nature' } }]);
        assert.equal(storedActor, 'Jan');

        runner.dispose();
        runner = makeRunner(false);
        await act(async () => { await runner.start(chapter([step('a', [{ kind: 'wait', ms: 1 }])]), { mode: 'watch', speed: 'fast' }); });
        assert.equal(latest().error?.key, 'tour_error_writes_disabled');
        await act(async () => { void runner.start(chapter([step('a', [{ kind: 'wait', ms: 1 }])], { writes: false }), { mode: 'watch', speed: 'fast' }); });
        await until(() => latest().status === 'done', 'read-only chapter runs without the flag');
    });
});
