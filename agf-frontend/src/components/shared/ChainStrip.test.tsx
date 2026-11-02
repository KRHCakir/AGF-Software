import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import type { ChainStep } from '../../lib/chain';
import { Mounted } from '../../test/reactHarness.ts';
import ChainStrip from './ChainStrip';

// De strip zelf: elke stap met naam en status (ook voor schermlezers), de huidige stap gemarkeerd, links waar ze er zijn en geen enkele
// link bij een stap die (nog) niet bestaat.
let mounted: Mounted;
const steps: ChainStep[] = [
    { key: 'order', state: 'done', parts: [{ key: 'chain_order_vendor', params: { vendor: 'NP' } }], links: [{ label: 'IO-2026-0003', href: '/purchase-orders?id=3' }], current: true },
    { key: 'receipt', state: 'partial', parts: [{ key: 'chain_received_of', params: { received: 15, expected: 20, unit: 'colli' } }], links: [{ label: '', key: 'chain_receive_action', href: '/receipts?order=3' }], current: false },
    { key: 'invoice', state: 'unavailable', parts: [{ key: 'chain_not_yet' }], links: [], current: false },
];

describe('ChainStrip', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('every step with its name and state, the current one marked, links only where they exist', async () => {
        await mounted.render(<ChainStrip steps={steps} />);
        const items = Array.from(mounted.container.querySelectorAll<HTMLElement>('[data-chain-step]'));
        assert.deepEqual(items.map((item) => item.dataset.chainStep), ['order', 'receipt', 'invoice']);
        assert.deepEqual(items.map((item) => item.dataset.chainState), ['done', 'partial', 'unavailable']);
        assert.equal(items[0].getAttribute('aria-current'), 'step');
        assert.equal(items[1].getAttribute('aria-current'), null);
        assert.ok(items[0].textContent?.includes('chain_step_order') && items[0].textContent?.includes('chain_state_done'));
        assert.deepEqual(Array.from(items[1].querySelectorAll('a')).map((a) => [a.textContent, a.getAttribute('href')]), [['chain_receive_action', '/receipts?order=3']]);
        assert.equal(items[2].querySelectorAll('a').length, 0, 'no dead links on a step that does not exist yet');
        assert.equal(mounted.container.querySelector('nav')?.getAttribute('aria-label'), 'chain_label');
    });
});
