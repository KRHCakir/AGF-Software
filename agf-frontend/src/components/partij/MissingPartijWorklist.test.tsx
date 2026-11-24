import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, describe, test } from 'node:test';
import { Mounted, json, stubFetch } from '../../test/reactHarness.ts';
import MissingPartijWorklist from './MissingPartijWorklist';

// Werklijst "Partij ontbreekt": alleen zichtbaar als er voorraad zonder partij is, met links naar de pallet en de inkooporder; er wordt niets
// verzonnen of gekoppeld. Zonder LanguageProvider geeft t() de sleutel terug.

let mounted: Mounted;

describe('MissingPartijWorklist', () => {
    afterEach(async () => { await mounted.unmount(); });

    test('shows stock without a lot with links to the pallet and the purchase order', async () => {
        stubFetch({ nextPost: async () => json(500, {}), onGet: () => json(200, [{ palletContentLineId: 5, palletId: 9, palletReference: 'P-OUD-7', supplyCommitmentId: 46, orderNumber: 'IO-2026-0046',
            vendorName: 'Kwekerij Zonnegloed', product: 'Mango Kent', lotNumber: 'OUD-7', onHandQuantity: 12, unit: 'box', receivedAt: '2026-10-20T08:00:00Z' }]) });
        mounted = new Mounted();
        await mounted.render(<MissingPartijWorklist reloadToken={0} />);
        await mounted.waitFor(() => mounted.container.querySelector('[data-tour="partij-missing"]'), 'worklist');
        assert.ok(mounted.hasText('partij_missing_title'));
        const hrefs = Array.from(mounted.container.querySelectorAll('a')).map((a) => a.getAttribute('href'));
        assert.deepEqual(hrefs, ['/inspection?palletId=9', '/purchase-orders?id=46']);
        assert.ok(mounted.hasText('Mango Kent'));
        assert.equal(mounted.buttons().length, 0, 'the worklist links, it never links a lot itself');
    });

    test('shows nothing when every stock line has a lot, and an error with retry when the list cannot be read', async () => {
        stubFetch({ nextPost: async () => json(500, {}), onGet: () => json(200, []) });
        mounted = new Mounted();
        await mounted.render(<MissingPartijWorklist reloadToken={0} />);
        await new Promise((resolve) => setTimeout(resolve, 20));
        assert.equal(mounted.container.querySelector('[data-tour="partij-missing"]'), null);
        await mounted.unmount();

        stubFetch({ nextPost: async () => json(500, {}), onGet: () => json(500, { message: 'down' }) });
        mounted = new Mounted();
        await mounted.render(<MissingPartijWorklist reloadToken={0} />);
        await mounted.waitFor(() => mounted.container.querySelector('[data-tour="partij-missing"][role="alert"]'), 'load error');
        assert.ok(mounted.hasText('partij_missing_load_failed'));
        assert.equal(mounted.buttons().length, 1, 'retry button');
        assert.ok(mounted.hasText('partij_missing_retry'));
    });
});
