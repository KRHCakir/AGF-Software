import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted } from '../../test/reactHarness.ts';
import OrderNumberText from './OrderNumberText';

// Het nummer van een inkooporder in beeld: het ordernummer van de leverancier als hoofdtekst met het eigen IO-nummer klein en grijs als
// intern nummer, of zonder leveranciersnummer alleen het IO-nummer; een lang nummer afgekapt in de lijst, afgebroken in het detail.

let mounted: Mounted;
const main = (scope: Element) => scope.querySelector('[data-testid="order-number-main"]');
const internal = (scope: Element) => scope.querySelector('[data-testid="order-number-internal"]');

describe('OrderNumberText', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('with a supplier number it is the main text and the IO number is the grey internal number; without, the IO number alone', async () => {
        await mounted.render(<>
            <p id="with"><OrderNumberText orderNumber="IO-2026-0007" supplierReference="45-21" stacked /></p>
            <p id="without"><OrderNumberText orderNumber="IO-2026-0008" supplierReference={null} stacked /></p>
            <p id="long"><OrderNumberText orderNumber="IO-2026-0009" supplierReference="BEVESTIGING-2026-000004521-PERU-ZEEVRACHT" stacked truncate /></p>
        </>);
        const withNumber = mounted.container.querySelector('#with')!;
        assert.equal(main(withNumber)!.textContent, '45-21');
        assert.equal(internal(withNumber)!.textContent, 'po_internal_number IO-2026-0007');
        assert.ok(internal(withNumber)!.className.includes('text-gray-600') && internal(withNumber)!.className.includes('text-xs'));
        const without = mounted.container.querySelector('#without')!;
        assert.equal(main(without)!.textContent, 'IO-2026-0008');
        assert.equal(internal(without), null, 'no "internal" label without a supplier number');
        // Lijst: een lang nummer afgekapt met … (volledig in de title); in het detail breekt het af in plaats van over te lopen.
        const long = main(mounted.container.querySelector('#long')!)!;
        assert.ok(long.className.includes('truncate'));
        assert.equal(long.getAttribute('title'), 'BEVESTIGING-2026-000004521-PERU-ZEEVRACHT');
        assert.ok(main(withNumber)!.className.includes('[overflow-wrap:anywhere]'));
    });
});
