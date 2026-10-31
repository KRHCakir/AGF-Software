import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, before, beforeEach, describe, test } from 'node:test';
import { todayInAmsterdam } from '../../lib/calendarDate.ts';
import { Mounted, json, setSelectValue, setValue, stubFetch } from '../../test/reactHarness.ts';
import PlanSupplyForm, { type ReferenceData } from './PlanSupplyForm';

// "Verwacht op" bij Aanvoer plannen met een vaste klok rond middernacht in Europe/Amsterdam: vandaag geeft nooit een fout, gisteren en
// morgen zijn geldig, een niet-bestaande datum niet. De controle gebruikt wat de browser in het datumveld heeft vastgelegd, ook als die
// waarde (nog) niet via een input-event in de state staat. node:test draait elk bestand in een eigen proces.

const reference: ReferenceData = {
    vendors: [{ id: 3, name: 'Supplier B', searchCode: 'SUPB', vatNumber: null, isActive: true }],
    customers: [],
    products: [{ id: 1, name: 'Avocado', variants: [{ id: 1, name: 'Hass', origin: 'Peru', packaging: null, sizeCategory: null, shortcutCode: null, defaultPackagesPerPallet: null, packagingUnit: 'colli', defaultNetKgPerPackage: null, defaultGrossKgPerPackage: null }] }],
};

// 31 oktober 2026 23:30 UTC = zondag 1 november 00:30 in Amsterdam (wintertijd): vandaag is daar al 1 november.
const NOW = Date.UTC(2026, 9, 31, 23, 30);
const RealDate = Date;
class FixedDate extends RealDate {
    constructor(...args: ConstructorParameters<typeof Date> | []) {
        if (args.length === 0) super(NOW);
        else super(...(args as ConstructorParameters<typeof Date>));
    }
    static now() { return NOW; }
}

let mounted: Mounted;
let posted: { url: string; body: string }[];

const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const type = (id: string, text: string) => act(async () => { field(id).focus(); setValue(field(id), text); });
const enter = (id: string) => act(async () => { field(id).dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })); });
const submit = () => act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });

async function fillAllButDate() {
    await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
    await type('vendorId-input', 'supb');
    await enter('vendorId-input');
    await type('lines[0].productVariantId-input', 'hass');
    await enter('lines[0].productVariantId-input');
    await act(async () => { setSelectValue(mounted.container.querySelector<HTMLSelectElement>('[id="lines[0].expectedUnit-input"]')!, 'colli'); });
    await type('lines[0].expectedQuantity-input', '10');
}

describe('PlanSupplyForm: expected date with a fixed clock around midnight', () => {
    before(() => {
        process.env.TZ = 'Europe/Amsterdam';
        assert.equal(new RealDate(2026, 0, 15).getTimezoneOffset(), -60, 'test process must run in Europe/Amsterdam');
    });
    beforeEach(() => {
        globalThis.Date = FixedDate as DateConstructor;
        posted = stubFetch({ onGet: () => json(404, { message: 'unexpected GET' }), nextPost: async () => json(500, { message: 'stop' }) });
        mounted = new Mounted();
    });
    afterEach(async () => {
        await mounted.unmount();
        globalThis.Date = RealDate;
    });

    test('today (00:30 in Amsterdam, still yesterday in UTC) is never an error and is sent as local midnight', async () => {
        assert.equal(todayInAmsterdam(), '2026-11-01');
        await fillAllButDate();
        await type('expectedAt-input', '2026-11-01');
        await submit();
        assert.equal(mounted.hasText('supply_error_required_date'), false);
        await mounted.waitFor(() => posted.length > 0, 'POST');
        assert.equal(JSON.parse(posted[0].body).expectedAt, '2026-10-31T23:00:00.000Z');
    });

    for (const [name, date, iso] of [['yesterday', '2026-10-31', '2026-10-30T23:00:00.000Z'], ['tomorrow', '2026-11-02', '2026-11-01T23:00:00.000Z']] as const) {
        test(`${name} is allowed (a supply can also be recorded afterwards)`, async () => {
            await fillAllButDate();
            await type('expectedAt-input', date);
            await submit();
            await mounted.waitFor(() => posted.length > 0, 'POST');
            assert.equal(JSON.parse(posted[0].body).expectedAt, iso);
        });
    }

    test('an empty or impossible date is refused before sending, with the plain message', async () => {
        await fillAllButDate();
        await submit();
        assert.equal(posted.length, 0);
        assert.ok(mounted.hasText('supply_error_required_date'));
        await type('expectedAt-input', '2026-02-31');
        await submit();
        assert.equal(posted.length, 0);
        assert.ok(mounted.hasText('supply_error_required_date'));
    });

    test('a cleared field never sends the previous date, even when the state still has it', async () => {
        await fillAllButDate();
        await type('expectedAt-input', '2026-11-01');
        // Zoals een browser die het leegmaken (of een half getypte datum) nog niet als input-event doorgaf.
        await act(async () => { field('expectedAt-input').value = ''; });
        await submit();
        assert.equal(posted.length, 0);
        assert.ok(mounted.hasText('supply_error_required_date'));
    });

    test('what the browser holds in the date field counts, also when no input event reached the form yet', async () => {
        await fillAllButDate();
        // Zoals een browser die de laatst getypte cijfers pas later doorgeeft: de waarde staat in het veld, de state is nog leeg.
        await act(async () => { field('expectedAt-input').value = '2026-11-01'; });
        await submit();
        assert.equal(mounted.hasText('supply_error_required_date'), false, 'no "enter a valid date" for a filled-in date');
        await mounted.waitFor(() => posted.length > 0, 'POST');
        assert.equal(JSON.parse(posted[0].body).expectedAt, '2026-10-31T23:00:00.000Z');
    });
});
