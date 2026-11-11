import '../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted } from '../test/reactHarness.ts';
import NotFound from './not-found';

// Een bewaarde link naar een verdwenen oud scherm (/invoices, /reports) of een verkeerd adres: een nette melding met een knop naar Start.
describe('not found', () => {
    let mounted: Mounted;
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('says the page does not exist (any more) and offers the way to Start', async () => {
        await mounted.render(<NotFound />);
        assert.ok(mounted.container.querySelector('[data-testid="not-found"] h1'));
        const link = mounted.container.querySelector<HTMLAnchorElement>('a')!;
        assert.equal(link.getAttribute('href'), '/dashboard');
        assert.ok(link.className.includes('min-h-11'));
    });
});
