import '../../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, stubFetch } from '../../../test/reactHarness.ts';
import { requestSupplyMode } from '../../../lib/supplyShortcut';
import SupplyPage from './page';

// De werkplek Aanvoer volgt de zijbalk-snelkoppelingen terwijl zij open staat: "Pallet ontvangen" opent de ontvangstmodus
// voor het eigen magazijn en "Aanvoer plannen" zet expliciet plannen, ook als ontvangen actief was.
let mounted: Mounted;
const checkedMode = () => (mounted.container.querySelector<HTMLInputElement>('input[type=radio][name="supply-mode"]:checked')
    ?? Array.from(mounted.container.querySelectorAll<HTMLInputElement>('input[type=radio]')).find((r) => r.checked && (r.value === 'plan' || r.value === 'receive')))?.value ?? null;

describe('SupplyPage shortcuts while mounted', () => {
    beforeEach(() => {
        window.localStorage.setItem('agf_actor_reference', 'wh-1');
        window.location.hash = '';
        stubFetch({ onGet: () => json(200, []), nextPost: async () => json(500, {}) });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); window.location.hash = ''; });

    test('receive then plan requests switch the mounted page explicitly', async () => {
        await mounted.render(<SupplyPage />);
        await mounted.waitFor(() => mounted.hasText('supply_choose_destination_first'), 'page');
        await mounted.waitFor(() => { requestSupplyMode('receive'); return checkedMode() === 'receive'; }, 'receive mode');
        assert.ok(mounted.hasText('supply_receive_sources_title'), 'receive form visible');
        await mounted.waitFor(() => { requestSupplyMode('plan'); return checkedMode() === 'plan'; }, 'plan mode');
        assert.equal(mounted.hasText('supply_receive_sources_title'), false);
    });

    test('arriving with #receive opens receive mode on mount', async () => {
        window.location.hash = '#receive';
        await mounted.render(<SupplyPage />);
        await mounted.waitFor(() => checkedMode() === 'receive', 'receive mode on arrival');
    });
});
