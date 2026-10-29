import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { findOpenTourOrder, findTourArticle, findTourVendor } from './prechecks.ts';
import type { PurchaseOrderListItem } from '../../types/purchaseOrder.ts';
import type { ProductOption, VendorOption } from '../../types/referenceData.ts';

// Voorcontroles: dezelfde zoekregels als de schermen, zodat Tab daar precies deze leverancier en dit artikel kiest.

const vendor = (id: number, name: string, isActive = true): VendorOption => ({ id, name, searchCode: null, vatNumber: null, isActive });
const variant = (id: number, name: string, extra: Partial<ProductOption['variants'][number]> = {}) => ({
    id, name, origin: 'Peru', packaging: null, sizeCategory: null, shortcutCode: null, defaultPackagesPerPallet: null, packagingUnit: null,
    defaultNetKgPerPackage: null, defaultGrossKgPerPackage: null, ...extra,
}) as ProductOption['variants'][number];
const item = (id: number, status: string, supplierReference: string | null) => ({ id, orderNumber: `IO-2026-${String(id).padStart(4, '0')}`, status, supplierReference }) as PurchaseOrderListItem;

describe('tour prechecks', () => {
    test('the supplier search must find exactly one active supplier, otherwise it names the problem', () => {
        assert.deepEqual(findTourVendor([vendor(1, "Nature's Pride"), vendor(2, 'Example Supplier')], 'Nature'), vendor(1, "Nature's Pride"));
        assert.deepEqual(findTourVendor([vendor(1, "Nature's Pride", false)], 'Nature'), { key: 'tour_missing_vendor', params: { search: 'Nature' } });
        assert.deepEqual(findTourVendor([vendor(1, "Nature's Pride"), vendor(3, 'Nature Fresh')], 'Nature'), { key: 'tour_missing_vendor_unique', params: { search: 'Nature' } });
    });

    test('Avocado Hass needs a shortcut code, packaging unit and kg per package', () => {
        const products = [
            { id: 1, name: 'Avocado', variants: [variant(2, 'Green', { shortcutCode: 'AG' }), variant(1, 'Hass'), variant(10, 'Hass', { shortcutCode: 'AvoHassPE12', packagingUnit: 'colli', defaultNetKgPerPackage: 4 })] },
        ] as ProductOption[];
        assert.deepEqual(findTourArticle(products), { variantId: 10, code: 'AvoHassPE12', unit: 'colli', kgPerPackage: 4 });
        assert.deepEqual(findTourArticle([{ id: 1, name: 'Avocado', variants: [variant(1, 'Hass')] }] as ProductOption[]), { key: 'tour_missing_article', params: { article: 'Avocado Hass' } });
    });

    test('chapter 2 takes the newest DEMO-TOUR order with nothing received', () => {
        const items = [item(40, 'Ordered', 'DEMO-TOUR-001'), item(45, 'PartiallyReceived', 'DEMO-TOUR-002'), item(44, 'Ordered', 'DEMO-TOUR-003'), item(50, 'Ordered', 'NP-1')];
        assert.equal(findOpenTourOrder(items)?.id, 44);
        assert.equal(findOpenTourOrder([item(50, 'Ordered', null)]), null);
    });
});
