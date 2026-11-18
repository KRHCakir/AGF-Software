import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act, useState } from 'react';
import { LanguageProvider } from '../../i18n/LanguageContext';
import { formatAmountInput, parseLocaleDecimal } from '../../lib/decimal';
import { Mounted, setValue } from '../../test/reactHarness.ts';

// De ene invoerregel voor alle getalvelden, door de echte LanguageProvider (Nederlands): de punt wordt tijdens het typen een komma,
// het totaal rekent direct mee, bij het verlaten komen de vaste decimalen, en een ongeldig getal krijgt de melding met Overnemen.
function Line() {
    const [price, setPrice] = useState('');
    const [quantity, setQuantity] = useState('2');
    const [kg] = useState('2.273');
    const [changes, setChanges] = useState(0);
    const parsed = parseLocaleDecimal(price, undefined, { maxDecimals: 2, allowZero: true });
    const count = parseLocaleDecimal(quantity);
    return (
        <form>
            <input id="price" inputMode="decimal" data-decimals={2} value={price} onChange={(event) => { setPrice(event.target.value); setChanges((n) => n + 1); }} />
            <input id="quantity" inputMode="decimal" value={quantity} onChange={(event) => setQuantity(event.target.value)} />
            <input id="calculated" inputMode="decimal" data-decimals={2} value={kg} onChange={() => setChanges((n) => n + 1)} />
            <input id="name" value="" onChange={() => undefined} />
            <output id="total">{parsed.ok && count.ok ? formatAmountInput(parsed.value * count.value) : 'ongeldig'}</output>
            <output id="changes">{changes}</output>
        </form>
    );
}

describe('number input rule for every decimal field', () => {
    let mounted: Mounted;
    beforeEach(async () => {
        window.localStorage.setItem('agf_locale', 'nl');
        mounted = new Mounted();
        await mounted.render(<LanguageProvider><Line /></LanguageProvider>);
        await act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)); });
    });
    afterEach(async () => { await mounted.unmount(); });

    const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`#${id}`)!;
    const text = (id: string) => mounted.container.querySelector(`#${id}`)!.textContent;
    const type = (id: string, value: string) => act(async () => { setValue(field(id), value); });
    const typeKeys = async (id: string, keys: string) => { for (const key of keys) await type(id, field(id).value + key); };
    const focus = (id: string) => act(async () => { field(id).dispatchEvent(new window.FocusEvent('focusin', { bubbles: true })); });
    const leave = (id: string) => act(async () => { field(id).dispatchEvent(new window.FocusEvent('focusout', { bubbles: true })); });
    const hint = () => window.document.querySelector<HTMLElement>('[data-testid="number-invalid"]');
    const takeOver = () => window.document.querySelector<HTMLButtonElement>('[data-testid="number-take-over"]');

    test('a typed dot becomes a comma while typing and the total follows at once; leaving the field gives the fixed decimals', async () => {
        await focus('price');
        await typeKeys('price', '7.5');
        assert.equal(field('price').value, '7,5', 'the dot became a comma while typing');
        assert.equal(text('total'), '15,00');
        await typeKeys('price', '0');
        assert.equal(field('price').value, '7,50');
        assert.equal(text('total'), '15,00', '7.50 gives the same total as 7,50');
        await leave('price');
        assert.equal(field('price').value, '7,50');

        await focus('price');
        await type('price', '4');
        assert.equal(text('total'), '8,00');
        await leave('price');
        assert.equal(field('price').value, '4,00', '4 becomes 4,00 when leaving the field');
        assert.equal(text('total'), '8,00');
        assert.equal(hint(), null);
    });

    test('a thousands number typed with a dot keeps its dot (15.000), and a pasted number is read the same way', async () => {
        await focus('price');
        await typeKeys('price', '15.0');
        assert.equal(field('price').value, '15,0');
        await typeKeys('price', '00');
        assert.equal(field('price').value, '15.000', 'a dot followed by exactly 3 digits is a thousands number');
        assert.equal(text('total'), '30.000,00');
        await leave('price');
        assert.equal(field('price').value, '15.000,00');

        for (const [pasted, shown, total] of [['7.50', '7,50', '15,00'], ['15.000', '15.000', '30.000,00'], ['15 000,5', '15 000,5', '30.001,00'], ["1'250,25", "1'250,25", '2.500,50']] as const) {
            await focus('price');
            await type('price', pasted);
            assert.equal(field('price').value, shown, pasted);
            assert.equal(text('total'), total, pasted);
            await leave('price');
        }
        assert.equal(field('price').value, '1.250,25', 'after leaving in screen notation');
    });

    test('an invalid number shows "Ongeldig getal. Bedoel je …?" with Overnemen; the total waits until the field is right', async () => {
        await focus('price');
        await type('price', '2.272.5');
        assert.equal(field('price').value, '2.272.5', 'never silently another value');
        assert.equal(text('total'), 'ongeldig');
        assert.ok(hint()!.textContent!.startsWith('Ongeldig getal. Bedoel je 2.272,5?'));
        assert.equal(field('price').getAttribute('data-number-invalid'), 'true');
        await leave('price');
        assert.equal(field('price').value, '2.272.5', 'leaving the field does not repair or hide it');
        assert.ok(hint(), 'the notice stays after leaving the field');

        await act(async () => { takeOver()!.click(); });
        assert.equal(field('price').value, '2.272,5');
        assert.equal(text('total'), '4.545,00');
        assert.equal(hint(), null);
        assert.equal(field('price').hasAttribute('data-number-invalid'), false);

        await type('price', '1.2.3');
        assert.equal(hint()!.textContent, 'Ongeldig getal.', 'no suggestion when there is no likely value');
        assert.equal(takeOver(), null);
        await type('price', '15,000.5');
        assert.ok(hint()!.textContent!.includes('Bedoel je 15.000,5?'), 'dot and comma the wrong way round');
        await act(async () => { field('price').dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', altKey: true, bubbles: true, cancelable: true })); });
        assert.equal(field('price').value, '15.000,5', 'Alt+Enter in the field takes the suggestion');
        assert.equal(hint(), null);
    });

    test('a field that is only visited keeps its text (a calculated value is never turned into an entered one); other fields are left alone', async () => {
        await focus('calculated');
        await leave('calculated');
        assert.equal(field('calculated').value, '2.273');
        assert.equal(text('changes'), '0');
        await focus('quantity');
        await type('quantity', '3');
        await leave('quantity');
        assert.equal(field('quantity').value, '3', 'no fixed decimals without data-decimals');
        await type('name', '7.50');
        assert.equal(field('name').value, '', 'a text field is not a number field');
    });
});
