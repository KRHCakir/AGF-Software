import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue, stubFetch } from '../../test/reactHarness.ts';
import PlanSupplyForm, { type ReferenceData } from './PlanSupplyForm';

// Certificaten deel 2: het GGN van het product op de inkoopregel. Eén treffer in de GGN-certificaten van de leverancier wordt voorgevuld,
// meer treffers geven een keuze, geen treffer laat het veld leeg; een eigen nummer (het GGN van de teler bij een handelaar) mag altijd.
const A = '4049928415684';
const B = '8712345678906';
const variant = (id: number, name: string, shortcutCode: string) => ({ id, name, origin: 'Peru', packaging: '4 kg box', sizeCategory: '18', shortcutCode, packagingUnit: 'box' as const, defaultNetKgPerPackage: 4, defaultGrossKgPerPackage: 4.4, defaultPackagesPerPallet: 252 });
const reference: ReferenceData = {
    vendors: [{ id: 3, name: 'Supplier B', searchCode: 'SUPB', vatNumber: null, isActive: true }],
    customers: [],
    products: [
        { id: 10, name: 'Sla', variants: [variant(1, 'Ijsberg', 'sla')] },
        { id: 11, name: 'Rucola', variants: [variant(2, 'Wild', 'ruc')] },
        { id: 12, name: 'Mango', variants: [variant(3, 'Kent', 'man')] },
    ],
};
const proposals = { vendorId: 3, date: '2026-11-04', proposals: [
    { productId: 10, kind: 'One', options: [{ ggn: A, certificateId: 1, validUntil: '2027-02-28', producerName: 'Teler Verde' }] },
    { productId: 11, kind: 'Many', options: [{ ggn: A, certificateId: 1, validUntil: null, producerName: 'Teler Verde' }, { ggn: B, certificateId: 2, validUntil: null, producerName: 'Teler Azul' }] },
    { productId: 12, kind: 'None', options: [] },
] };

let mounted: Mounted;
let posted: { url: string; body: string }[];
let asked: string[];
const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const testId = (id: string) => mounted.container.querySelector<HTMLElement>(`[data-testid="${id}"]`);
const type = (id: string, text: string) => act(async () => { field(id).focus(); setValue(field(id), text); });
const key = (id: string, keyName: string) => act(async () => { field(id).dispatchEvent(new window.KeyboardEvent('keydown', { key: keyName, bubbles: true, cancelable: true })); });
const pick = async (index: number, code: string) => { await type(`lines[${index}].productVariantId-input`, code); await key(`lines[${index}].productVariantId-input`, 'Enter'); };
const submit = () => act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });

describe('PlanSupplyForm: GGN of the product per line', () => {
    beforeEach(() => {
        asked = [];
        posted = stubFetch({
            onGet: (url) => {
                if (url.includes('/purchase-orders/ggn-proposals')) {
                    asked.push(url);
                    const ids = new URL(url, 'http://x').searchParams.get('productIds')!.split(',').map(Number);
                    return json(200, { ...proposals, proposals: proposals.proposals.filter((p) => ids.includes(p.productId)) });
                }
                return json(404, {});
            },
            nextPost: async () => json(500, { message: 'stop' }),
        });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('one match is filled in, several ask to choose, none stays empty; a typed number stays and goes along', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        await type('vendorId-input', 'supb');
        await key('vendorId-input', 'Enter');
        await type('expectedAt-input', '04-11-2026');
        await pick(0, 'sla');
        await type('lines[0].expectedQuantity-input', '10');
        await mounted.waitFor(() => field('lines[0].ggn-input').value === A, 'one match filled in');
        assert.ok(asked[asked.length - 1].includes('vendorId=3') && asked[asked.length - 1].includes('date=2026-11-04'), asked.join(' '));
        assert.equal(testId('lines[0].ggn-hint')!.textContent, 'po_line_ggn_hint_proposed');

        await key('lines[0].ggn-input', 'Tab');
        await pick(1, 'ruc');
        await type('lines[1].expectedQuantity-input', '5');
        await mounted.waitFor(() => testId('lines[1].ggn-choice') !== null, 'several matches: a choice');
        assert.equal(field('lines[1].ggn-input').value, '');
        assert.equal(testId('lines[1].ggn-hint')!.textContent, 'po_line_ggn_hint_choose');
        await act(async () => { const select = testId('lines[1].ggn-choice') as unknown as HTMLSelectElement; select.value = B; select.dispatchEvent(new window.Event('change', { bubbles: true })); });
        assert.equal(field('lines[1].ggn-input').value, B);

        await key('lines[1].ggn-input', 'Tab');
        await pick(2, 'man');
        await type('lines[2].expectedQuantity-input', '3');
        await mounted.waitFor(() => testId('lines[2].ggn-hint')?.textContent === 'po_line_ggn_hint_none', 'no match');
        assert.equal(field('lines[2].ggn-input').value, '');
        // Het GGN van de teler van de factuur, niet in het certificaat van de leverancier: mag, met een toelichting en zonder blokkade.
        await type('lines[2].ggn-input', '4063 0610 00005');
        assert.equal(testId('lines[2].ggn-hint')!.textContent, 'po_line_ggn_hint_own');
        // Het eerste nummer leegmaken blijft leeg (de gebruiker koos dat), ook als het voorstel opnieuw komt.
        await type('lines[0].ggn-input', '');
        await act(async () => { await new Promise((resolve) => setTimeout(resolve, 20)); });
        assert.equal(field('lines[0].ggn-input').value, '');

        await submit();
        await mounted.waitFor(() => posted.length > 0, 'POST');
        assert.deepEqual(JSON.parse(posted[0].body).lines.map((l: { ggn: string | null }) => l.ggn), [null, B, '4063061000005']);
    });

    test('not 13 digits is a message at the field and nothing is sent; a wrong check digit only warns', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        await type('vendorId-input', 'supb');
        await key('vendorId-input', 'Enter');
        await type('expectedAt-input', '04-11-2026');
        await pick(0, 'man');
        await type('lines[0].expectedQuantity-input', '3');
        await type('lines[0].ggn-input', '4049928415685');
        assert.ok(testId('lines[0].ggn-check-digit'), 'check digit warning');
        await type('lines[0].ggn-input', '40499284');
        await submit();
        assert.ok(mounted.hasText('supply_error_ggn'));
        assert.equal(posted.length, 0);
    });
});
