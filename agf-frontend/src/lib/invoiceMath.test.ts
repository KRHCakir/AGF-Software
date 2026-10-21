import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { lineNet, lineVat, round2, totals } from './invoiceMath.ts';

// Dezelfde testvectoren als AgfAdmin.Tests InvoiceMathTests: scherm en server moeten identiek rekenen.
describe('invoiceMath', () => {
    test('line amounts round half up to two decimals', () => {
        const cases: [number, number, number, number, number, number][] = [
            [80, 12.5, 0, 1000, 21, 210],
            [3, 10, 10, 27, 0, 0],
            [1, 0.335, 0, 0.34, 21, 0.07],
            [7, 1.115, 12.5, 6.83, 9, 0.61],
            [2.5, 3.333, 0, 8.33, 21, 1.75],
        ];
        for (const [q, p, d, net, vat, vatAmount] of cases) {
            assert.equal(lineNet(q, p, d), net, `net ${q}×${p}−${d}%`);
            assert.equal(lineVat(net, vat), vatAmount, `vat ${net}×${vat}%`);
        }
        assert.equal(round2(1.005), 1.01);
        assert.equal(round2(2.675), 2.68);
    });

    test('totals sum the rounded lines without floating-point noise', () => {
        assert.deepEqual(totals([{ net: 0.34, vat: 0.07 }, { net: 0.34, vat: 0.07 }, { net: 0.34, vat: 0.07 }]), { net: 1.02, vat: 0.21, gross: 1.23 });
        assert.deepEqual(totals([{ net: 0.1, vat: 0.2 }, { net: 0.2, vat: 0.1 }]), { net: 0.3, vat: 0.3, gross: 0.6 });
        assert.deepEqual(totals([]), { net: 0, vat: 0, gross: 0 });
    });
});
