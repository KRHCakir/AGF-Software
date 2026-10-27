import assert from 'node:assert/strict';
import { test } from 'node:test';
import { palletBreakdownLabel } from './palletBreakdownLabel';

// Eenvoudige vertaler met de Nederlandse teksten van deze twee sleutels.
const texts: Record<string, string> = { pallet_breakdown: '{pallets} pallets × {perPallet}', pallet_breakdown_loose: '{pallets} pallets × {perPallet} + {loose} {unit}', unit_colli: 'Colli' };
const t = (key: string, variables: Record<string, string> = {}) => Object.entries(variables).reduce((text, [name, value]) => text.replace(`{${name}}`, value), texts[key] ?? key);

test('the ratio of a stored line: "3 pallets × 120 + 15 colli"; without loose colli no "+ 0"; old fractions stay readable', () => {
    assert.equal(palletBreakdownLabel({ palletCount: 3, packagesPerPallet: 120, loosePackages: 15 }, 'colli', t, 'nl'), '3 pallets × 120 + 15 colli');
    assert.equal(palletBreakdownLabel({ palletCount: 3, packagesPerPallet: 120, loosePackages: 0 }, 'colli', t, 'nl'), '3 pallets × 120');
    assert.equal(palletBreakdownLabel({ palletCount: 3.5, packagesPerPallet: 120 }, 'colli', t, 'nl'), '3,5 pallets × 120');
    assert.equal(palletBreakdownLabel({ palletCount: null, packagesPerPallet: null }, 'colli', t, 'nl'), null);
});
