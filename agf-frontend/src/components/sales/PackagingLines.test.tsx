import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act, useState } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, setSelectValue, setValue } from '../../test/reactHarness.ts';
import type { PackagingLineDraft } from '../../lib/packaging';
import type { CostArticle } from '../../types/costArticle';
import { PackagingLinesInput, PackagingLinesView } from './PackagingLines';

// Emballage in het orderformulier door de echte componentlevenscyclus: niets komt er vanzelf bij, "Pallet toevoegen" zet het voorstel
// neer (aanpasbaar), een gekozen artikel vult de standaardprijs, en Tab loopt artikel → aantal → prijs zonder de verwijderknop.

const articles: CostArticle[] = [
    { id: 11, name: 'Pallet wegwerp', kind: 'Packaging', isStandard: true, isActive: true, sortOrder: 110, lineCount: 0, defaultUnitPrice: 7.5, vatPercentage: 9 },
    { id: 12, name: 'Euro-pallet', kind: 'Packaging', isStandard: true, isActive: true, sortOrder: 120, lineCount: 0, defaultUnitPrice: null, vatPercentage: 9 },
    { id: 13, name: 'Krat', kind: 'Packaging', isStandard: true, isActive: false, sortOrder: 130, lineCount: 0, defaultUnitPrice: 1.1, vatPercentage: 9 },
];

let mounted: Mounted;
let latest: PackagingLineDraft[];

function Harness({ proposal }: { proposal: number }) {
    const [lines, setLines] = useState<PackagingLineDraft[]>([]);
    return <PackagingLinesInput lines={lines} articles={articles} proposal={proposal} locked={false} errors={() => undefined}
        onChange={(next) => { latest = next; setLines(next); }} />;
}

const rows = () => mounted.container.querySelectorAll('[data-tour="sales-packaging-line"]');
const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;

describe('PackagingLinesInput', () => {
    beforeEach(() => { latest = []; mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('nothing is added by itself; Add pallet puts Pallet wegwerp with the proposal and its price there, focused and adjustable', async () => {
        await mounted.render(<Harness proposal={2} />);
        assert.equal(rows().length, 0, 'no packaging line before a click');
        assert.ok(mounted.hasText('packaging_pallet_proposal'));
        await act(async () => { mounted.button('+ packaging_add_pallet')!.click(); });
        assert.equal(rows().length, 1);
        assert.deepEqual({ ...latest[0], key: '' }, { key: '', costArticleId: '11', quantity: '2', unitPrice: '7,50' });
        assert.equal(document.activeElement?.id, 'packagingLines[0].quantity-input', 'the proposal is focused to adjust');
        await act(async () => { setValue(field('packagingLines[0].quantity-input'), '3'); });
        assert.equal(latest[0].quantity, '3');
        assert.equal(mounted.container.querySelector('[data-testid="packagingLines[0].amount"]')!.textContent, '22.50');
    });

    test('a chosen article fills its standard price; an inactive one is not offered; Tab skips the remove button', async () => {
        await mounted.render(<Harness proposal={1} />);
        await act(async () => { mounted.button('+ packaging_add')!.click(); });
        const select = mounted.container.querySelector<HTMLSelectElement>('[id="packagingLines[0].costArticleId-input"]')!;
        assert.equal(document.activeElement, select);
        assert.deepEqual(Array.from(select.options).map((option) => option.textContent), ['packaging_choose', 'Pallet wegwerp', 'Euro-pallet']);
        await act(async () => { setSelectValue(select, '11'); });
        assert.equal(field('packagingLines[0].unitPrice-input').value, '7,50');
        await act(async () => { setSelectValue(select, '12'); });
        assert.equal(field('packagingLines[0].unitPrice-input').value, '', 'no standard price: empty, the user types it or leaves it');
        const tabbable = Array.from(rows()[0].querySelectorAll<HTMLElement>('select, input, button')).filter((element) => element.tabIndex >= 0).map((element) => element.id);
        assert.deepEqual(tabbable, ['packagingLines[0].costArticleId-input', 'packagingLines[0].quantity-input', 'packagingLines[0].unitPrice-input']);
    });
});

describe('PackagingLinesView', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('saved packaging shows quantity, price, amount and VAT; a line without price says so; nothing to reserve', async () => {
        await mounted.render(<PackagingLinesView lines={[
            { id: 1, costArticleId: 11, name: 'Pallet wegwerp', quantity: 1, unitPrice: 7.5, vatPercentage: 9, lineTotal: 7.5 },
            { id: 2, costArticleId: 12, name: 'Euro-pallet', quantity: 2, unitPrice: null, vatPercentage: 9, lineTotal: null },
        ]} />);
        assert.ok(mounted.hasText('Pallet wegwerp') && mounted.hasText('7.50') && mounted.hasText('packaging_vat'));
        assert.ok(mounted.hasText('packaging_no_price'));
        assert.equal(mounted.container.querySelector('[data-tour^="sales-reserve"], [data-tour="sales-choose-stock"]'), null);
    });
});
