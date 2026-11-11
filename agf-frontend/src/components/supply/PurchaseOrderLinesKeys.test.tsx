import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act, useState } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted } from '../../test/reactHarness.ts';
import { emptyPurchaseLine, purchaseLineRow, type PurchaseLineDraft } from '../../lib/purchaseOrderLines';
import type { ProductOption } from '../../types/referenceData';
import PurchaseOrderLines from './PurchaseOrderLines';

// Toetsenbord in de regels: Tab loopt alleen door Product, Aantal en Prijs (de knoppen staan buiten de Tab-volgorde, zoals de eigenaar
// wil); Bruto aanpassen en regel verwijderen bereik je vanuit elk veld van de regel met Alt+B en Alt+R, wat onder de tabel staat.
const products: ProductOption[] = [{ id: 1, name: 'Avocado', variants: [{ id: 1, name: 'Hass', origin: 'Peru', packaging: '4 kg box', sizeCategory: '18', shortcutCode: 'a', packagingUnit: 'box', defaultNetKgPerPackage: 4, defaultGrossKgPerPackage: 4.4, defaultPackagesPerPallet: 252 }] }];
const variant = products[0].variants[0];

function Harness() {
    const [lines, setLines] = useState<PurchaseLineDraft[]>([
        { ...emptyPurchaseLine('a'), productVariantId: '1', quantity: '100' },
        { ...emptyPurchaseLine('b'), productVariantId: '1', quantity: '50' },
    ]);
    return <PurchaseOrderLines lines={lines} rows={lines.map((line) => purchaseLineRow(line, variant))} products={products} variantOf={() => variant} locked={false} isDirect={false}
        errors={() => undefined} onChange={(key, patch) => setLines((current) => current.map((line) => (line.key === key ? { ...line, ...patch } : line)))}
        onAdd={() => setLines((current) => [...current, emptyPurchaseLine(`n${current.length}`)])} onRemove={(key) => setLines((current) => current.filter((line) => line.key !== key))}
        productExtra={() => null} />;
}

let mounted: Mounted;
const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`);
const alt = (id: string, code: string) => act(async () => { field(id)!.dispatchEvent(new window.KeyboardEvent('keydown', { key: code.slice(3).toLowerCase(), code, altKey: true, bubbles: true, cancelable: true })); });

describe('purchase order lines: keyboard', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('Tab goes through product, quantity and price only; every button is outside the Tab order and names its shortcut', async () => {
        await mounted.render(<Harness />);
        const row = mounted.container.querySelector('tbody tr')!;
        const tabbable = Array.from(row.querySelectorAll<HTMLElement>('input, button, select')).filter((element) => element.tabIndex >= 0).map((element) => element.id);
        assert.deepEqual(tabbable, ['lines[0].productVariantId-input', 'lines[0].expectedQuantity-input', 'lines[0].unitPrice-input']);
        assert.ok(Array.from(row.querySelectorAll<HTMLButtonElement>('button')).every((button) => button.tabIndex === -1));
        const adjust = row.querySelector<HTMLButtonElement>('[data-tour="supply-line-gross-adjust"]')!;
        const remove = row.querySelector<HTMLButtonElement>('button[aria-label^="supply_remove_line_n"]')!;
        assert.ok(/\(.+B\)$/.test(adjust.title) && /\(.+R\)$/.test(remove.title), `${adjust.title} / ${remove.title}`);
        assert.equal(mounted.container.querySelector('[data-testid="purchase-lines-keys"]')!.textContent, 'po_line_keys_hint');
    });

    test('Alt+A asks for a new article on that line', async () => {
        const opened: string[] = [];
        function WithNewArticle() {
            const lines = [{ ...emptyPurchaseLine('a'), productVariantId: '1', quantity: '100' }, { ...emptyPurchaseLine('b'), productVariantId: '1', quantity: '50' }];
            return <PurchaseOrderLines lines={lines} rows={lines.map((line) => purchaseLineRow(line, variant))} products={products} variantOf={() => variant} locked={false} isDirect={false}
                errors={() => undefined} onChange={() => undefined} onAdd={() => undefined} onRemove={() => undefined} onNewArticle={(key) => opened.push(key)} productExtra={() => null} />;
        }
        await mounted.render(<WithNewArticle />);
        await alt('lines[1].expectedQuantity-input', 'KeyA');
        assert.deepEqual(opened, ['b']);
    });

    test('Alt+B opens Bruto aanpassen on that line, Alt+R removes the line', async () => {
        await mounted.render(<Harness />);
        assert.equal(field('lines[1].expectedGrossWeightKg-input'), null);
        await alt('lines[1].expectedQuantity-input', 'KeyB');
        assert.ok(field('lines[1].expectedGrossWeightKg-input'), 'gross input open on line 2');
        assert.equal(field('lines[0].expectedGrossWeightKg-input'), null, 'line 1 unchanged');
        await alt('lines[0].unitPrice-input', 'KeyR');
        assert.equal(mounted.container.querySelectorAll('tbody tr').length, 1);
        assert.equal(field('lines[0].expectedQuantity-input')!.value, '50');
    });
});
