import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { defaultRejectedKg } from './purchaseOrderRejection.ts';

// Dezelfde gevallen als PurchaseOrderRejectionTests.DefaultRejectedKg in de backend.

describe('purchaseOrderRejection', () => {
    test('default rejected kg: remainder exact, calculated per package, weighed proportional', () => {
        assert.equal(defaultRejectedKg(48, 221, 884, 'Weighed', null), 192);
        assert.equal(defaultRejectedKg(1, 3, 40, 'Weighed', null), 13.333);
        assert.equal(defaultRejectedKg(5, 20, 80, 'Calculated', 4), 20);
        assert.equal(defaultRejectedKg(20, 20, 80.5, 'Calculated', 4), 80.5);
    });
});
