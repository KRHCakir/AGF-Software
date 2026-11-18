import assert from 'node:assert/strict';
import { afterEach, describe, test } from 'node:test';
import { formatKg as availabilityKg } from './availability.ts';
import { lineWeightTotals } from './lineWeight.ts';
import { formatKg, formatKgPerPackage, formatWeight, roundWeight, setWeightDecimals, shownKgTotal, weightDecimals } from './weightDisplay.ts';

// Gewichten tonen: hele kilo's (standaard) of één decimaal; alleen bij het tonen afgerond, halve kilo's omhoog; het totaal onder
// regels is de som van de getoonde regels; het gewicht per colli houdt zijn decimalen; en nooit een getal als 2.272.5.
describe('showing weights', () => {
    afterEach(() => setWeightDecimals(0));

    test('whole kilos by default, half kilos up (2.272,5 becomes 2.273); one decimal when the company sets it', () => {
        assert.equal(weightDecimals(), 0);
        assert.deepEqual([2272.5, 2272.499, 0.5, 0.499, 15600, 16068, 12.75].map((kg) => formatKg(kg, 'nl')), ['2.273 kg', '2.272 kg', '1 kg', '0 kg', '15.600 kg', '16.068 kg', '13 kg']);
        assert.equal(formatKg(2272.5, 'en'), '2,273 kg');
        assert.equal(availabilityKg(2272.5, 'nl'), '2.273 kg', 'every screen that shows kg goes through the same function');
        setWeightDecimals(1);
        assert.deepEqual([2272.5, 2272.55, 2272.549, 15600].map((kg) => formatKg(kg, 'nl')), ['2.272,5 kg', '2.272,6 kg', '2.272,5 kg', '15.600,0 kg']);
        setWeightDecimals(7);
        assert.equal(weightDecimals(), 0, 'anything but 1 is whole kilos');
    });

    test('a shown weight never has two dots or a dot as the decimal sign, whatever the value', () => {
        for (const decimals of [0, 1]) {
            setWeightDecimals(decimals);
            for (const kg of [2272.5, 0.5, 999.5, 999.95, 1000.4, 1234567.891, 2020, 4.5]) {
                assert.match(formatWeight(kg, 'nl'), /^\d{1,3}(\.\d{3})*(,\d)?$/, `${kg} with ${decimals} decimals`);
                assert.match(formatKgPerPackage(kg, 'nl'), /^\d{1,3}(\.\d{3})*,\d{2,3} kg$/, `${kg} per package`);
            }
        }
    });

    test('the weight per package keeps its decimals (4,00 kg; 1,50 kg; 1,125 kg), also with whole kilos', () => {
        assert.deepEqual([4, 1.5, 1.8, 1.125, 4.5].map((kg) => formatKgPerPackage(kg, 'nl')), ['4,00 kg', '1,50 kg', '1,80 kg', '1,125 kg', '4,50 kg']);
        assert.equal(formatKgPerPackage(1.5, 'en'), '1.50 kg');
    });

    test('a total under lines is the sum of the shown lines, so it adds up; the real values stay for prices and pallets', () => {
        // 10,5 + 10,5 + 0,4: echt 21,4; getoond 11 + 11 + 0 = 22.
        assert.equal(shownKgTotal([10.5, 10.5, 0.4, null, undefined]), 22);
        assert.equal(roundWeight(10.5), 11);
        const totals = lineWeightTotals([
            { quantity: 3, unit: 'colli', netWeightKg: 10.5, grossWeightKg: 11.4 },
            { quantity: 3, unit: 'colli', netWeightKg: 10.5, grossWeightKg: 11.4 },
            { quantity: 1, unit: 'colli', netWeightKg: null, grossWeightKg: null },
        ]);
        assert.deepEqual([totals.netKg, totals.grossKg], [21, 22.8], 'the real sums, not rounded');
        assert.deepEqual([totals.shownNetKg, totals.shownGrossKg], [22, 22], 'shown: 11 + 11 and 11 + 11');
        setWeightDecimals(1);
        assert.equal(shownKgTotal([10.55, 10.55]), 21.2, '10,6 + 10,6');
        assert.deepEqual([lineWeightTotals([{ quantity: 1, unit: 'colli', netWeightKg: 10.5, grossWeightKg: null }]).shownNetKg], [10.5]);
    });
});
