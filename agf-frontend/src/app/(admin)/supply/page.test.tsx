import '../../../test/setupDom.ts';
import '../../../test/nextNavigationStub.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted } from '../../../test/reactHarness.ts';
import { currentUrl, navigate } from '../../../test/nextNavigationStub.ts';
import SupplyRedirectPage from './page';

// Het oude adres /supply blijft werken: het verwijst door naar het scherm met de nieuwe naam, ook vanuit oude snelkoppelingen
// (#plan, #receive) en bladwijzers.
let mounted: Mounted;

describe('/supply redirects to the new screens', () => {
    beforeEach(() => { navigate('/supply'); mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); window.location.hash = ''; });

    for (const [hash, target] of [['', '/purchase-orders/new'], ['#plan', '/purchase-orders/new'], ['#receive', '/receipts'], ['#direct', '/direct-trade']] as const) {
        test(`/supply${hash} -> ${target}`, async () => {
            window.location.hash = hash;
            await mounted.render(<SupplyRedirectPage />);
            await mounted.waitFor(() => currentUrl() === target, target);
            assert.ok(mounted.hasText('nav_redirecting'));
        });
    }
});
