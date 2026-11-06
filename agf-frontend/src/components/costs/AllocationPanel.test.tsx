import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act, useState } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import type { InvoiceLineAllocation } from '../../types/partijCost';
import AllocationPanel from './AllocationPanel';
import { newestLines } from './PurchaseInvoiceDetail';

// "Aan welke partij hoort dit?" door de echte componentlevenscyclus: €5.000 handling over twee verkoop-partijen (€500 + €1.000, rest €3.500
// algemene kosten als waarschuwing), meer dan het regelbedrag geweigerd naast Opslaan, verdelen naar kg met een partij zonder kg, een
// gelijktijdige wijziging (409) en de Tab-volgorde van zoeken naar bedragen naar opslaan.

const lot = (id: number, number: string, kind: 'Purchase' | 'Sales', over: Record<string, unknown> = {}) => ({
    id, number, kind, salesOrderId: kind === 'Sales' ? id + 100 : null, salesOrderNumber: kind === 'Sales' ? `VO-2026-00${id}` : null, salesOrderStatus: kind === 'Sales' ? 'Open' : null,
    customerName: kind === 'Sales' ? 'Groothandel Jansen' : null, orderedQuantity: kind === 'Sales' ? 500 : 0, sourcePartijen: [], status: 'Open', createdAt: '2026-11-05T09:00:00Z',
    vendors: kind === 'Purchase' ? ['Kwekerij Peru'] : [], products: ['Avocado Hass'], supplierLotReferences: [], unit: 'colli', purchasedQuantity: 0,
    receivedQuantity: kind === 'Purchase' ? 1000 : 0, onHandQuantity: 0, inInspectionQuantity: 0, openExpectedQuantity: 0, soldQuantity: 0, netKg: 2000, ...over,
});
const line = (over: Partial<InvoiceLineAllocation> = {}): InvoiceLineAllocation => ({
    lineId: 41, description: 'Handling', kind: 'Handling', lineTotal: 5000, allocated: 0, general: 5000, version: 0, allocations: [], history: [], ...over,
});

interface Call { method: string; url: string; body: Record<string, unknown> | null }
let mounted: Mounted;
let calls: Call[];
let putAnswer: () => Response;
let saved: unknown[];

const input = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const tour = (name: string, key?: string) => mounted.container.querySelector<HTMLElement>(`[data-tour="${name}"]${key === undefined ? '' : `[data-tour-key="${key}"]`}`);
const click = (element: HTMLElement | null) => act(async () => { element!.click(); });

// Zoals PurchaseInvoiceDetail: na opslaan krijgt het paneel de nieuwe stand van de server als nieuwe regel.
function Harness({ initial }: { initial: InvoiceLineAllocation }) {
    const [current, setCurrent] = useState(initial);
    return <AllocationPanel invoiceId={7} line={current} canAllocate onReload={() => {}} onSaved={(next) => { saved.push(next); setCurrent(next.lines[0]); }} />;
}

describe('AllocationPanel', () => {
    beforeEach(() => {
        calls = [];
        saved = [];
        putAnswer = () => json(200, { invoiceId: 7, currency: 'EUR', canAllocate: true, lines: [line({ allocated: 1500, general: 3500, version: 1 })] });
        globalThis.fetch = (async (inputUrl: RequestInfo | URL, init?: RequestInit) => {
            const call: Call = { method: init?.method ?? 'GET', url: String(inputUrl), body: init?.body ? JSON.parse(String(init.body)) as Record<string, unknown> : null };
            calls.push(call);
            if (call.method === 'PUT') return putAnswer();
            const kind = new URL(call.url).searchParams.get('kind');
            return json(200, { total: 3, page: 1, pageSize: 8, items: kind === 'Sales'
                ? [lot(1, '261140', 'Sales'), lot(2, '261141', 'Sales', { netKg: 0, orderedQuantity: 300 })]
                : [lot(3, '52611002', 'Purchase')] });
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    const render = (over: Partial<InvoiceLineAllocation> = {}) => mounted.render(<Harness initial={line(over)} />);

    test('€500 and €1.000 out of €5.000 on two sales lots: €3.500 general costs as a warning, then saved with the version', async () => {
        await render();
        assert.ok(mounted.hasText('allocation_question'));
        assert.equal(input('line-41-search'), null, 'general costs by default: no lot search');
        await click(tour('allocation-mode', 'Sales'));
        await mounted.waitFor(() => tour('allocation-pick', '261140') !== null, 'sales lots found');
        assert.ok(tour('allocation-pick', '261140')!.textContent!.includes('Groothandel Jansen'), 'each choice says what it is');
        await click(tour('allocation-pick', '261140'));
        await click(tour('allocation-pick', '261141'));
        await act(async () => { setValue(input('line-41-amount-1'), '500'); });
        await act(async () => { setValue(input('line-41-amount-2'), '1.000'); });
        assert.ok(mounted.container.querySelector('[data-testid="line-41-general"]')!.textContent!.includes('allocation_general'));
        await click(tour('allocation-save'));
        await mounted.waitFor(() => saved.length === 1, 'saved');
        // De melding blijft staan nadat de nieuwe stand van de server binnen is (de rondleiding wacht erop).
        await mounted.waitFor(() => tour('allocation-saved') !== null, 'saved notice stays');
        const put = calls.find((call) => call.method === 'PUT')!;
        assert.ok(put.url.endsWith('/purchase-invoices/7/lines/41/allocations'));
        assert.deepEqual(put.body!.allocations, [{ partijId: 1, amount: 500 }, { partijId: 2, amount: 1000 }]);
        assert.equal(put.body!.expectedVersion, 0);
        assert.ok(typeof put.body!.requestId === 'string');
    });

    test('more than the line amount is refused next to the save button and nothing is sent', async () => {
        await render();
        await click(tour('allocation-mode', 'Sales'));
        await mounted.waitFor(() => tour('allocation-pick', '261140') !== null, 'found');
        await click(tour('allocation-pick', '261140'));
        await act(async () => { setValue(input('line-41-amount-1'), '5.000,01'); });
        assert.ok(mounted.container.querySelector('[data-testid="line-41-over"]')!.textContent!.includes('allocation_over'));
        await click(tour('allocation-save'));
        assert.equal(calls.filter((call) => call.method === 'PUT').length, 0);
    });

    test('split by kg: a lot without kg does not take part and says so; split by packages uses the ordered packages', async () => {
        await render();
        await click(tour('allocation-mode', 'Sales'));
        await mounted.waitFor(() => tour('allocation-pick', '261140') !== null, 'found');
        await click(tour('allocation-pick', '261140'));
        await click(tour('allocation-pick', '261141'));
        await click(tour('allocation-spread', 'kg'));
        assert.equal(input('line-41-amount-1').value, '5.000,00');
        assert.equal(input('line-41-amount-2').value, '');
        assert.ok(mounted.hasText('allocation_skipped_kg'));
        await click(tour('allocation-spread', 'colli'));
        assert.deepEqual([input('line-41-amount-1').value, input('line-41-amount-2').value], ['3.125,00', '1.875,00']);
        assert.equal(mounted.hasText('allocation_skipped_kg'), false);
        // Weer naar kg: de overgeslagen partij blokkeert opslaan niet en gaat niet mee.
        await click(tour('allocation-spread', 'kg'));
        assert.equal(mounted.hasText('allocation_amount_invalid'), false);
        await click(tour('allocation-save'));
        await mounted.waitFor(() => saved.length === 1, 'saved without the skipped lot');
        assert.deepEqual(calls.find((call) => call.method === 'PUT')!.body!.allocations, [{ partijId: 1, amount: 5000 }]);
    });

    test('while saving nothing can be edited; a reloaded allocation shows its own kind', async () => {
        let release: (response: Response) => void = () => {};
        putAnswer = () => { throw new Error('not used'); };
        globalThis.fetch = (async (inputUrl: RequestInfo | URL, init?: RequestInit) => {
            if (init?.method === 'PUT') return new Promise<Response>((resolve) => { release = resolve; });
            return json(200, { total: 1, page: 1, pageSize: 8, items: [lot(1, '261140', 'Sales')] });
        }) as typeof fetch;
        let setLine: (line: InvoiceLineAllocation) => void = () => {};
        function Reloadable() {
            const [current, set] = useState(line());
            setLine = set;
            return <AllocationPanel invoiceId={7} line={current} canAllocate onSaved={() => {}} onReload={() => {}} />;
        }
        await mounted.render(<Reloadable />);
        await click(tour('allocation-mode', 'Sales'));
        await mounted.waitFor(() => tour('allocation-pick', '261140') !== null, 'found');
        await click(tour('allocation-pick', '261140'));
        await act(async () => { setValue(input('line-41-amount-1'), '500'); });
        await click(tour('allocation-save'));
        // Uitgeschakeld via de omliggende fieldset (:disabled), zoals de browser het ook doet.
        assert.equal(input('line-41-amount-1').matches(':disabled'), true, 'amounts locked while saving');
        assert.equal(tour('allocation-spread', 'kg')!.matches(':disabled'), true);
        await act(async () => { release(json(200, { invoiceId: 7, currency: 'EUR', canAllocate: true, lines: [line({ version: 1 })] })); });
        // Herladen met partijen van een ander (versie 2): de soort volgt de geladen verdeling.
        await act(async () => { setLine(line({ version: 2, allocations: [{ partijId: 3, number: '52611002', partijKind: 'Purchase', amount: 200 }] })); });
        assert.equal((tour('allocation-mode', 'Purchase') as HTMLInputElement).checked, true);
        assert.equal(input('line-41-amount-3').value, '200,00');
    });

    test('saving another line of the invoice keeps the unsaved draft of this line', async () => {
        let setOther: (line: InvoiceLineAllocation) => void = () => {};
        function TwoLines() {
            const [other, setLine] = useState(line({ lineId: 42 }));
            setOther = setLine;
            return <AllocationPanel invoiceId={7} line={other} canAllocate onSaved={() => {}} onReload={() => {}} />;
        }
        await mounted.render(<TwoLines />);
        await click(tour('allocation-mode', 'Sales'));
        await mounted.waitFor(() => tour('allocation-pick', '261140') !== null, 'found');
        await click(tour('allocation-pick', '261140'));
        await act(async () => { setValue(input('line-42-amount-1'), '750'); });
        // De pagina vervangt na opslaan van een andere regel alle regels: zelfde regel en versie, nieuw object.
        await act(async () => { setOther(line({ lineId: 42 })); });
        assert.equal(input('line-42-amount-1').value, '750', 'draft kept');
        // Een nieuwe versie van deze regel (opnieuw geladen) neemt wel de stand van de server over.
        await act(async () => { setOther(line({ lineId: 42, version: 2 })); });
        assert.equal(input('line-42-amount-1'), null);
    });

    test('one line can take a purchase lot and a sales lot; a conflict asks to reload; Tab goes from search to the amounts to save', async () => {
        putAnswer = () => json(409, { message: 'changed' });
        await render();
        await click(tour('allocation-mode', 'Purchase'));
        await mounted.waitFor(() => tour('allocation-pick', '52611002') !== null, 'purchase lots');
        await click(tour('allocation-pick', '52611002'));
        await click(tour('allocation-mode', 'Sales'));
        await mounted.waitFor(() => tour('allocation-pick', '261140') !== null, 'sales lots');
        await click(tour('allocation-pick', '261140'));
        await act(async () => { setValue(input('line-41-amount-3'), '2000'); });
        await act(async () => { setValue(input('line-41-amount-1'), '500'); });
        // Tab-volgorde: zoeken, gevonden partijen, bedragen in de volgorde van de partijen, verdeelknoppen, opslaan.
        const focusable = Array.from(mounted.container.querySelectorAll<HTMLElement>('input, button')).filter((element) => element.tabIndex >= 0 && !(element as HTMLButtonElement).disabled)
            .map((element) => element.dataset.tour ?? element.getAttribute('aria-label') ?? element.tagName);
        const order = ['allocation-search', 'allocation-amount', 'allocation-spread', 'allocation-save'].map((name) => focusable.indexOf(name));
        assert.deepEqual([...order].sort((a, b) => a - b), order);
        assert.equal(focusable.filter((name) => name === 'allocation-amount').length, 2);
        await click(tour('allocation-save'));
        await mounted.waitFor(() => mounted.hasText('allocation_conflict'), 'conflict');
        assert.deepEqual(calls.find((call) => call.method === 'PUT')!.body!.allocations, [{ partijId: 3, amount: 2000 }, { partijId: 1, amount: 500 }]);
    });

    test('an existing allocation shows its lots, amounts and history; choosing general costs saves an empty allocation', async () => {
        await render({ allocated: 500, general: 4500, version: 3,
            allocations: [{ partijId: 1, number: '261140', partijKind: 'Sales', amount: 500, party: 'Groothandel Jansen · VO-2026-001', products: 'Avocado Hass', quantity: 500, unit: 'colli', netKg: 2000 }],
            history: [{ occurredAt: '2026-11-05T09:00:00Z', type: 'Changed', partijId: 1, number: '261140', oldAmount: 400, newAmount: 500, actorReference: 'Admin' }] });
        assert.equal(input('line-41-amount-1').value, '500,00');
        assert.ok(mounted.hasText('allocation_event_changed'));
        await click(tour('allocation-mode', 'General'));
        await click(tour('allocation-save'));
        await mounted.waitFor(() => saved.length === 1, 'saved');
        assert.deepEqual(calls.find((call) => call.method === 'PUT')!.body!.allocations, []);
        assert.equal(calls.find((call) => call.method === 'PUT')!.body!.expectedVersion, 3);
    });

    test('an older answer never puts back an older state of another line', () => {
        const current = { invoiceId: 7, currency: 'EUR', canAllocate: true, lines: [line({ lineId: 1, version: 3 }), line({ lineId: 2, version: 1 })] };
        const older = { invoiceId: 7, currency: 'EUR', canAllocate: true, lines: [line({ lineId: 1, version: 2 }), line({ lineId: 2, version: 2 })] };
        assert.deepEqual(newestLines(current, older).lines.map((item) => [item.lineId, item.version]), [[1, 3], [2, 2]]);
    });
});
