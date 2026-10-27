import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act, useState } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, click, setValue } from '../../test/reactHarness.ts';
import type { CustomerOption } from '../../types/referenceData';
import CustomerPicker from './CustomerPicker';

// Klantkeuze door de echte componentlevenscyclus: suggesties vanaf 1 teken, Tab en Enter nemen de gemarkeerde (standaard de
// bovenste) over en gaan door naar het volgende veld, pijltjes bewegen, Escape sluit, en zonder treffer "Nieuwe klant".

const customers: CustomerOption[] = [
    { id: 1, name: 'Local Market Stalls', searchCode: 'LMS', isActive: true, city: 'Rotterdam', vatNumber: null },
    { id: 2, name: 'Lommerse Groothandel', searchCode: null, isActive: true, city: 'Barendrecht', vatNumber: 'NL800123456B01' },
    { id: 3, name: 'Fruithal Zuid', searchCode: 'FZ', isActive: true, city: 'Lochem', vatNumber: null },
    { id: 4, name: 'Lo Oud', searchCode: null, isActive: false, city: null, vatNumber: null },
];

let mounted: Mounted;
let chosen: (number | null)[];
let newCustomer: string[];

function Harness() {
    const [value, setValueState] = useState('');
    return (
        <form>
            <CustomerPicker id="customerId" customers={customers} value={value}
                onChange={(customer) => { chosen.push(customer?.id ?? null); setValueState(customer === null ? '' : String(customer.id)); }}
                onNewCustomer={(name) => newCustomer.push(name)} />
            <input id="next-field" />
        </form>
    );
}

const input = () => mounted.container.querySelector<HTMLInputElement>('#customerId-input')!;
const options = () => Array.from(mounted.container.querySelectorAll<HTMLLIElement>('li[role="option"]:not([aria-disabled="true"])'));
const activeOption = () => options().find((option) => option.getAttribute('aria-selected') === 'true')?.textContent ?? null;
const key = async (name: string) => {
    const event = new window.KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true });
    await act(async () => { input().dispatchEvent(event); });
    return event.defaultPrevented;
};
const type = async (text: string) => { await act(async () => { input().focus(); setValue(input(), text); }); };

describe('CustomerPicker', () => {
    beforeEach(() => { chosen = []; newCustomer = []; mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('"Lo" shows suggestions at once (name, city; inactive never) and Tab takes the top one without blocking Tab', async () => {
        await mounted.render(<Harness />);
        await type('Lo');
        assert.deepEqual(options().map((o) => o.textContent), ['Local Market Stalls · RotterdamLMS', 'Lommerse Groothandel · Barendrecht', 'Fruithal Zuid · LochemFZ']);
        assert.equal(activeOption(), 'Local Market Stalls · RotterdamLMS', 'top suggestion highlighted');
        assert.equal(await key('Tab'), false, 'Tab still leaves the field');
        assert.deepEqual(chosen, [1]);
        assert.equal(input().value, 'Local Market Stalls · Rotterdam');
        assert.equal(input().readOnly, true);
    });

    test('arrows move the highlight and Enter takes it and moves to the next field; Escape closes', async () => {
        await mounted.render(<Harness />);
        await type('lo');
        await key('ArrowDown');
        await key('ArrowDown');
        assert.equal(activeOption(), 'Fruithal Zuid · LochemFZ');
        await key('ArrowUp');
        assert.equal(await key('Enter'), true, 'Enter never submits the form');
        assert.deepEqual(chosen, [2]);
        assert.equal(window.document.activeElement?.id, 'next-field');

        await click(mounted.container.querySelector<HTMLButtonElement>('button[aria-label="sales_customer_change"]')!);
        await type('nl800');
        assert.deepEqual(options().map((o) => o.textContent), ['Lommerse Groothandel · Barendrecht'], 'VAT number search');
        assert.equal(await key('Escape'), true);
        assert.equal(input().getAttribute('aria-expanded'), 'false');
    });

    test('an exact search code comes first; no match offers "new customer" with the typed name', async () => {
        await mounted.render(<Harness />);
        await type('fz');
        assert.equal(activeOption(), 'Fruithal Zuid · LochemFZ');
        await type('Groentehal Jansen');
        assert.deepEqual(options(), []);
        assert.ok(mounted.hasText('sales_customer_none'));
        const offer = mounted.container.querySelector<HTMLElement>('[data-testid="customer-no-match"]')!;
        assert.ok(offer.textContent?.includes('sales_customer_not_found'));
        await click(offer.querySelector('button')!);
        assert.deepEqual(newCustomer, ['Groentehal Jansen']);
        assert.deepEqual(chosen, []);
    });
});
