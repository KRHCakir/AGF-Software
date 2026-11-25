import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, click, json, setValue, stubFetch, type Posted } from '../../test/reactHarness.ts';
import { mailDocumentName } from '../sales/MailDraftsPanel';
import RevenueTypesSetting from './RevenueTypesSetting';

// Verkoopfacturen onder Beheer: omzetsoorten (de standaard kan niet inactief, een nieuwe met grootboekcode) en de naam van een factuur in
// een mailconcept.

let mounted: Mounted;
let posted: Posted[];

describe('revenue types setting', () => {
    beforeEach(() => {
        posted = stubFetch({
            onGet: () => json(200, [
                { id: 1, name: 'Omzet diensten', externalCode: null, isActive: true, isDefault: true, version: 0 },
                { id: 2, name: 'Omzet transport', externalCode: '8010', isActive: true, isDefault: false, version: 0 },
            ]),
            nextPost: async () => json(201, { id: 3, name: 'Omzet emballage', externalCode: '8020', isActive: true, isDefault: false, version: 0 }),
        });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('the default has no Set inactive; a new revenue type is added with its ledger code', async () => {
        await mounted.render(<RevenueTypesSetting />);
        const items = await mounted.waitFor(() => { const found = mounted.container.querySelectorAll('[data-testid="revenue-type"]'); return found.length === 2 ? found : null; }, 'types');
        assert.ok(items[0].textContent!.includes('settings_revenue_types_default'));
        assert.equal(items[0].textContent!.includes('settings_revenue_types_deactivate'), false);
        assert.ok(items[1].textContent!.includes('settings_revenue_types_deactivate'));
        setValue(mounted.container.querySelector<HTMLInputElement>('#revenue-type-name')!, 'Omzet emballage');
        setValue(mounted.container.querySelector<HTMLInputElement>('#revenue-type-code')!, '8020');
        await click([...mounted.buttons()].find((b) => b.textContent === 'settings_revenue_types_add')!);
        await mounted.waitFor(() => (posted.length === 1 ? true : null), 'POST');
        assert.deepEqual(JSON.parse(posted[0].body), { name: 'Omzet emballage', externalCode: '8020', isActive: true, isDefault: false });
    });

    test('a mail draft of an invoice is named after the invoice', () => {
        const t = (key: string, params?: Record<string, string>) => `${key} ${params?.number ?? ''}`.trim();
        assert.equal(mailDocumentName(t, { documentKind: 'Invoice', documentNumber: 'VF-2026-00001', documentVersion: 1 }), 'si_mail_document_name VF-2026-00001');
        assert.equal(mailDocumentName(t, { documentKind: 'LoadingSlip', documentNumber: 'LB-2026-0003', documentVersion: 2 }), 'so_slip_name LB-2026-0003');
    });
});
