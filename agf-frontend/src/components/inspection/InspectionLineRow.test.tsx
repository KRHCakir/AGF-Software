import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import type { InspectionPallet } from '../../services/inspectionService';
import type { PalletDetailLine } from '../../types/inspection';
import { INSPECTOR_STORAGE_KEY } from './inspectorMemory.ts';
import InspectionLineRow from './InspectionLineRow';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

// "Wie beoordeelt" hoort bij de keuring zelf: in het paneel van de regel, tussen de reden en de bevestigknop, verplicht, voorgevuld
// met de laatste keuze op dit apparaat. De server legt daarnaast de ingelogde gebruiker vast.

const line: PalletDetailLine = {
    id: 7, palletId: 3, supplyCommitmentLineId: 11, supplyCommitmentId: 5, route: 'Warehouse', customerId: null, productVariantId: 9,
    productName: 'Mango', productVariantName: 'Fuerte', lotNumber: 'LOT-7', quantity: 40, unit: 'colli', netWeightKg: 160, weightMethod: 'Calculated',
    netKgPerPackage: 4, ggn: null, dispatchedQuantity: 0, dispatchedNetWeightKg: 0, onHandQuantity: 40, onHandNetWeightKg: 160, depleted: false,
    origin: 'Receipt', originCorrectionLineId: null, qualityStatus: 'Quarantine', qualityReason: null, createdAt: '2026-11-04T08:00:00Z', version: 2, palletVersion: 1,
};
const context = { pallet: { id: 3, contentLines: [line] }, receiptRegisteredAt: null, commitments: {}, events: [] } as unknown as InspectionPallet;

let mounted: Mounted;
let posted: { url: string; body: Record<string, unknown> }[];
let succeeded: number;
let gets: string[];
// Wat de server als eerdere beoordelaars teruggeeft (uniek, meest gebruikt eerst; zie PalletsControllerTests).
let recent: unknown;

const q = <T extends Element>(selector: string) => mounted.container.querySelector<T>(selector);
const click = (element: Element | null) => act(async () => { (element as HTMLElement).click(); });
const approve = () => mounted.buttons().find((b) => b.textContent === 'inspection_action_approve') ?? null;
const confirm = () => q<HTMLButtonElement>('[data-tour="inspection-confirm"]');
const inspector = () => q<HTMLInputElement>('#line-7-inspector');

describe('InspectionLineRow: who inspects is chosen in the inspection itself', () => {
    beforeEach(() => {
        mounted = new Mounted();
        posted = [];
        succeeded = 0;
        gets = [];
        recent = [];
        localStorage.removeItem(INSPECTOR_STORAGE_KEY);
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            if ((init?.method ?? 'GET') === 'GET') {
                gets.push(String(input));
                return json(200, recent);
            }
            posted.push({ url: String(input), body: JSON.parse(String(init?.body)) });
            return json(200, { ...line, qualityStatus: 'Approved', version: 3 });
        }) as typeof fetch;
    });
    afterEach(async () => { await mounted.unmount(); });

    const render = () => mounted.render(<table><tbody><InspectionLineRow context={context} line={line} readOnly={false} onSucceeded={async () => { succeeded += 1; }} onReload={() => {}} /></tbody></table>);

    test('the field sits between the reason and the confirm button, is required, and the error stays next to it with focus', async () => {
        await render();
        await click(approve());
        const reason = q('#line-7-reason')!;
        const field = inspector()!;
        assert.ok(reason.compareDocumentPosition(field) & Node.DOCUMENT_POSITION_FOLLOWING, 'after the reason');
        assert.ok(field.compareDocumentPosition(confirm()!) & Node.DOCUMENT_POSITION_FOLLOWING, 'before the confirm button');
        assert.equal(field.value, '', 'nothing is chosen silently');

        await click(confirm());
        assert.equal(posted.length, 0);
        assert.equal(q('#line-7-inspector-error')?.textContent, 'inspection_error_inspector_required');
        assert.equal(field.getAttribute('aria-invalid'), 'true');
        assert.equal(document.activeElement, field, 'focus jumps to the field with the error');
    });

    test('the inspector is sent with the decision and remembered on this device as the next suggestion', async () => {
        await render();
        await click(approve());
        await act(async () => { setValue(inspector()!, ' Jan '); });
        await click(confirm());
        await mounted.waitFor(() => succeeded === 1, 'saved');
        assert.equal(posted.length, 1);
        assert.match(posted[0].url, /\/pallets\/3\/content-lines\/7\/quality-status$/);
        assert.equal(posted[0].body.inspectedBy, 'Jan');
        assert.equal(posted[0].body.actorReference, undefined, 'the signed-in user is recorded by the server');
        assert.equal(localStorage.getItem(INSPECTOR_STORAGE_KEY), 'Jan');
    });

    test('a remembered inspector is prefilled, visible and can be changed before confirming', async () => {
        localStorage.setItem(INSPECTOR_STORAGE_KEY, 'Steve');
        recent = ['Steve', 'Jan'];
        await render();
        await click(approve());
        assert.equal(inspector()!.value, 'Steve');
        await mounted.waitFor(() => q('#line-7-inspectors option[value="Jan"]'), 'suggestions from earlier inspections');
        await act(async () => { setValue(inspector()!, 'Jan'); });
        await click(confirm());
        await mounted.waitFor(() => posted.length === 1, 'posted');
        assert.equal(posted[0].body.inspectedBy, 'Jan');
    });

    test('the suggestions are the names of earlier inspections, in the order of the server (most used first)', async () => {
        recent = ['Fatima', 'Steve', 'Jan'];
        await render();
        assert.equal(gets.length, 0, 'nothing is fetched before the panel opens');
        await click(approve());
        await mounted.waitFor(() => (mounted.container.querySelectorAll('#line-7-inspectors option').length === 3 ? true : null), 'suggestions');
        assert.ok(gets.some((url) => url.endsWith('/pallets/recent-inspectors')));
        assert.deepEqual(Array.from(mounted.container.querySelectorAll<HTMLOptionElement>('#line-7-inspectors option')).map((option) => option.value), ['Fatima', 'Steve', 'Jan']);
        assert.equal(inspector()!.getAttribute('list'), 'line-7-inspectors');
    });

    test('without earlier inspections (or without an answer) there are no suggestions and typing still works', async () => {
        await render();
        await click(approve());
        await mounted.waitFor(() => (gets.length > 0 ? true : null), 'asked');
        assert.equal(mounted.container.querySelectorAll('#line-7-inspectors option').length, 0);
        recent = { not: 'a list' };
        await click(mounted.buttons().find((b) => b.textContent === 'inspection_cancel') ?? null);
        await click(approve());
        await mounted.waitFor(() => (gets.length > 1 ? true : null), 'asked again');
        assert.equal(mounted.container.querySelectorAll('#line-7-inspectors option').length, 0);
        await act(async () => { setValue(inspector()!, 'Nieuwe naam'); });
        await click(confirm());
        await mounted.waitFor(() => posted.length === 1, 'posted');
        assert.equal(posted[0].body.inspectedBy, 'Nieuwe naam');
    });

    test('no fixed names in the code: the suggestions only come from earlier inspections', () => {
        for (const file of ['src/components/inspection/InspectionLineRow.tsx', 'src/components/inspection/inspectorMemory.ts']) {
            const source = readFileSync(join(process.cwd(), file), 'utf8');
            assert.doesNotMatch(source, /KNOWN_INSPECTORS|'Jan'|'Steve'/, file);
        }
    });
});
