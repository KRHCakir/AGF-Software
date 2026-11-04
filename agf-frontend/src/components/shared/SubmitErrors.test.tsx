import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act, useState } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted } from '../../test/reactHarness.ts';
import { fieldProps } from '../supply/FieldError';
import SubmitErrors from './SubmitErrors';

// Opslaan ging niet door: de melding staat naast de knop en de focus gaat naar het eerste veld met een fout, nooit naar iets bovenaan.

let mounted: Mounted;
let fail: (errors: Record<string, string[]>) => void;

function Harness({ moveFocus = true }: { moveFocus?: boolean }) {
    const [errors, setErrors] = useState<Record<string, string[]>>({});
    fail = setErrors;
    return (
        <form>
            <input {...fieldProps('first', errors.first)} />
            <input {...fieldProps('second', errors.second)} />
            <button type="submit">save</button>
            <SubmitErrors errors={errors} moveFocus={moveFocus} />
        </form>
    );
}

const alert = () => mounted.container.querySelector<HTMLElement>('[data-testid="submit-errors"]');

describe('SubmitErrors', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('nothing to show while there is no error', async () => {
        await mounted.render(<Harness />);
        assert.equal(alert(), null);
    });

    test('next to the save button with the number of fields, and the first invalid field in the form gets focus', async () => {
        await mounted.render(<Harness />);
        await act(async () => fail({ second: ['x'], first: ['y'] }));
        assert.equal(alert()?.textContent, 'form_errors_next_to_save');
        assert.equal(alert()?.previousElementSibling?.textContent, 'save', 'right after the save button');
        assert.equal(document.activeElement?.id, 'first-input', 'first in visual order, not in the order of the keys');
        await act(async () => fail({ second: ['x'] }));
        assert.equal(alert()?.textContent, 'form_errors_next_to_save_one');
        assert.equal(document.activeElement?.id, 'second-input');
    });

    test('an error without its own field puts the focus on the message itself', async () => {
        await mounted.render(<Harness />);
        await act(async () => fail({ lines: ['x'] }));
        assert.equal(document.activeElement, alert());
    });

    test('forms that move the focus themselves can turn it off', async () => {
        await mounted.render(<Harness moveFocus={false} />);
        await act(async () => fail({ first: ['y'] }));
        assert.ok(alert());
        assert.notEqual(document.activeElement?.id, 'first-input');
    });
});
