import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act, useState } from 'react';
import { Mounted, click } from '../../test/reactHarness.ts';
import { defaultReceiveLocation } from '../../lib/orderLogistics.ts';
import type { StockLocation } from '../../types/stockLocation';
import ReceiveLocationField from './ReceiveLocationField';

// Ontvangstlocatie (november 2026): Eigen loods als tekst met Afwijkend? (de enige tabstop), de lijst op verzoek met de focus erin, Escape
// terug naar de standaard; een andere locatie of meer dan één eigen loods toont de lijst meteen; vergrendeld alleen tekst.

const location = (id: number, name: string, kind: StockLocation['kind'], isActive = true): StockLocation =>
    ({ id, name, kind, address: null, countryCode: null, isActive, inUse: false, version: 0 });
const onderweg = location(1, 'Onderweg / direct naar klant', 'InTransit');
const hall = location(2, 'Loods 2', 'OwnWarehouse');

let mounted: Mounted;
let changes: string[];
let added: number;

function Harness({ initial, locations, locked = false, defaultFailed = false }: { initial: string; locations: StockLocation[]; locked?: boolean; defaultFailed?: boolean }) {
    const [value, setValue] = useState(initial);
    return (
        <form>
            <input id="vendor" />
            <ReceiveLocationField value={value} onChange={(id) => { changes.push(id); setValue(id); }} locations={locations} locked={locked} errors={() => undefined}
                ownWarehouseAddress={'DEMO-loods\nVeilingweg 5'} onAddLocation={() => { added++; }} defaultFailed={defaultFailed} onRetryLocations={() => undefined} />
            <input id="next" />
        </form>
    );
}

const select = () => mounted.container.querySelector<HTMLSelectElement>('select');
const tabbables = () => Array.from(mounted.container.querySelectorAll<HTMLElement>('input, select, button, a[href]')).map((element) => element.id || element.textContent || element.tagName);

describe('receive location', () => {
    beforeEach(() => { mounted = new Mounted(); changes = []; added = 0; });
    afterEach(async () => { await mounted.unmount(); });

    test('the default is Eigen loods as text with the address and Afwijkend? as the only tab stop of the field', async () => {
        await mounted.render(<Harness initial="" locations={[onderweg]} />);
        assert.equal(select(), null);
        assert.ok(mounted.hasText('stock_location_own'));
        assert.equal(mounted.container.querySelector('[data-testid="receive-location-address"]')!.textContent, 'DEMO-loods\nVeilingweg 5');
        assert.deepEqual(tabbables(), ['vendor', '✎ po_logistics_location_other', 'next']);
        assert.equal(mounted.container.querySelector('[data-tour="supply-receive-location"]')!.tagName, 'BUTTON');
    });

    test('Afwijkend? opens the list with focus, Onderweg is in it without own locations; Escape goes back to the default', async () => {
        await mounted.render(<Harness initial="" locations={[onderweg]} />);
        await click(mounted.container.querySelector<HTMLElement>('[data-tour="supply-receive-location"]')!);
        const list = select()!;
        assert.equal(document.activeElement, list);
        assert.deepEqual(Array.from(list.options).map((option) => option.value), ['', '1', '__add']);
        await act(async () => { list.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })); });
        assert.equal(select(), null);
        assert.deepEqual(changes, ['']);
        assert.equal(document.activeElement, mounted.container.querySelector('[data-tour="supply-receive-location"]'));
    });

    test('an order with another location shows the list with that location; Terug naar standaard returns to Eigen loods', async () => {
        await mounted.render(<Harness initial="1" locations={[onderweg]} />);
        assert.equal(select()!.value, '1');
        assert.ok(mounted.hasText('po_logistics_location_in_transit'));
        await click(mounted.container.querySelector<HTMLElement>('[data-testid="receive-location-back"]')!);
        assert.equal(select(), null);
        assert.deepEqual(changes, ['']);
    });

    test('a locked order shows only text, also for another location', async () => {
        await mounted.render(<Harness initial="1" locations={[onderweg]} locked />);
        assert.equal(select(), null);
        assert.equal(mounted.container.querySelector('[data-tour="supply-receive-location"]'), null);
        assert.equal(mounted.container.querySelector('[data-testid="receive-location-name"]')!.textContent, 'Onderweg / direct naar klant');
    });

    test('with more than one own warehouse the list is open at once, with the default chosen by the form and no way back', async () => {
        const locations = [onderweg, hall];
        assert.equal(defaultReceiveLocation(locations, 2), '2');
        assert.equal(defaultReceiveLocation([onderweg], 2), '');
        assert.equal(defaultReceiveLocation([onderweg, location(2, 'Loods 2', 'OwnWarehouse', false)], 2), '');
        await mounted.render(<Harness initial={defaultReceiveLocation(locations, 2)} locations={locations} />);
        assert.equal(select()!.value, '2');
        assert.equal(mounted.container.querySelector('[data-testid="receive-location-back"]'), null);
    });

    test('with more than one own warehouse a default that could not be loaded is said, never silently Eigen loods', async () => {
        await mounted.render(<Harness initial="" locations={[onderweg, hall]} defaultFailed />);
        assert.ok(mounted.container.querySelector('[data-testid="receive-default-failed"]'));
        assert.ok(mounted.hasText('po_logistics_default_failed'));
    });

    test('+ Locatie toevoegen opens the settings and keeps the chosen location', async () => {
        await mounted.render(<Harness initial="1" locations={[onderweg]} />);
        const list = select()!;
        await act(async () => {
            list.value = '__add';
            list.dispatchEvent(new window.Event('change', { bubbles: true }));
        });
        assert.equal(added, 1);
        assert.deepEqual(changes, []);
        assert.equal(select()!.value, '1');
    });
});
