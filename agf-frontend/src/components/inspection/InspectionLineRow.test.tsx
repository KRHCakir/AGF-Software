import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import type { InspectionPallet } from '../../services/inspectionService';
import type { PalletDetailLine } from '../../types/inspection';
import { INSPECTOR_STORAGE_KEY } from './inspectorMemory.ts';
import InspectionLineRow from './InspectionLineRow';

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
        localStorage.removeItem(INSPECTOR_STORAGE_KEY);
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
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
        await render();
        await click(approve());
        assert.equal(inspector()!.value, 'Steve');
        assert.ok(q('#line-7-inspectors option[value="Jan"]'), 'suggestions from the known employees');
        await act(async () => { setValue(inspector()!, 'Jan'); });
        await click(confirm());
        await mounted.waitFor(() => posted.length === 1, 'posted');
        assert.equal(posted[0].body.inspectedBy, 'Jan');
    });
});
