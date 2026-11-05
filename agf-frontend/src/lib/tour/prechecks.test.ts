import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { findOpenTourOrder, findOriginVariant, findTourArticle, findTourCustomer, findTourVendor, lotVars, receiptVars, shortDate } from './prechecks.ts';
import type { PurchaseOrderListItem } from '../../types/purchaseOrder.ts';
import type { CustomerOption, ProductOption, VendorOption } from '../../types/referenceData.ts';

// Voorcontroles: de rondleiding werkt met haar eigen voorbeeldgegevens (DEMO-TOUR-…) en dezelfde zoekregels als de schermen, zodat Tab
// daar precies deze leverancier, klant en dit artikel kiest. Is er niets, dan maakt het hoofdstuk ze zelf aan.

const vendor = (id: number, name: string, searchCode: string | null, isActive = true): VendorOption => ({ id, name, searchCode, vatNumber: null, isActive });
const customer = (id: number, name: string, searchCode: string | null, isActive = true) => ({ id, name, searchCode, isActive }) as CustomerOption;
const variant = (id: number, name: string, extra: Partial<ProductOption['variants'][number]> = {}) => ({
    id, name, origin: 'Peru', packaging: null, sizeCategory: null, shortcutCode: null, defaultPackagesPerPallet: null, packagingUnit: null,
    defaultNetKgPerPackage: null, defaultGrossKgPerPackage: null, ...extra,
}) as ProductOption['variants'][number];
const item = (id: number, status: string, supplierReference: string | null) => ({ id, orderNumber: `IO-2026-${String(id).padStart(4, '0')}`, status, supplierReference }) as PurchaseOrderListItem;
const tourHass = (id: number, ref: string, extra: Partial<ProductOption['variants'][number]> = {}) => variant(id, 'Hass', {
    shortcutCode: ref, brand: ref, originCountryCode: 'PE', sizeCategory: '18', tradeClass: 'I', packaging: '4 kg doos', packagingUnit: 'colli', defaultNetKgPerPackage: 4, ...extra,
});

describe('tour prechecks', () => {
    test('blind entry: the tour fills in every attribute of this variant (empty ones are skipped) and the right kg field', () => {
        const pe = variant(10, 'Hass', { originCountryCode: 'PE', sizeCategory: '18', packaging: '4 kg doos', tradeClass: 'I', brand: 'Verita', weightMethod: 'Calculated' });
        assert.deepEqual(receiptVars(pe, 10, 4), {
            originText: 'PE', originName: 'Peru', size: '18', packaging: '4 kg doos', tradeClass: 'I', brand: 'Verita', kgPerPackage: '4', netKg: '',
        });
        assert.equal(receiptVars({ ...pe, brand: null }, 10, 4).brand, '');
        assert.deepEqual([receiptVars({ ...pe, weightMethod: 'Weighed' }, 10, 4).kgPerPackage, receiptVars({ ...pe, weightMethod: 'Weighed' }, 10, 4).netKg], ['', '40']);
    });

    test('the tour supplier is the newest active DEMO-TOUR supplier whose search code finds only her', () => {
        const vendors = [vendor(1, "Nature's Pride", 'NATURE'), vendor(4, 'DEMO-TOUR-101 Kwekerij', 'DEMO-TOUR-101-LEV'), vendor(7, 'DEMO-TOUR-102 Kwekerij', 'DEMO-TOUR-102-LEV'),
            vendor(9, 'DEMO-TOUR-103 Kwekerij', 'DEMO-TOUR-103-LEV', false)];
        assert.deepEqual(findTourVendor(vendors), { vendor: vendors[2], search: 'DEMO-TOUR-102-LEV' });
        assert.equal(findTourVendor([vendor(1, "Nature's Pride", 'NATURE')]), null);
        assert.equal(findTourVendor([]), null);
    });

    test('the tour customer is the newest active DEMO-TOUR customer, found at the top by her search code', () => {
        const customers = [customer(2, 'Lokaal Fruit B.V.', 'LOKAAL'), customer(5, 'DEMO-TOUR-101 Groothandel', 'DEMO-TOUR-101-KLANT'), customer(6, 'DEMO-TOUR-102 Groothandel', 'DEMO-TOUR-102-KLANT', false)];
        assert.deepEqual(findTourCustomer(customers), { customer: customers[1], search: 'DEMO-TOUR-101-KLANT' });
        assert.equal(findTourCustomer([customers[0]]), null);
    });

    test('the tour article is the newest complete Avocado Hass with a DEMO-TOUR brand, a code, a unit and kg per package', () => {
        const products = [{ id: 1, name: 'Avocado', variants: [
            variant(2, 'Hass', { shortcutCode: 'AVOPE18', packagingUnit: 'colli', defaultNetKgPerPackage: 4, sizeCategory: '18', tradeClass: 'I', originCountryCode: 'PE' }),
            tourHass(10, 'DEMO-TOUR-101'), tourHass(12, 'DEMO-TOUR-102', { isArchived: true }), tourHass(11, 'DEMO-TOUR-103', { sizeCategory: null }),
        ] }] as ProductOption[];
        assert.deepEqual(findTourArticle(products), { variantId: 10, code: 'DEMO-TOUR-101', unit: 'colli', kgPerPackage: 4 });
        assert.equal(findTourArticle([{ id: 1, name: 'Avocado', variants: [products[0].variants[0]] }] as ProductOption[]), null);
        assert.equal(findTourArticle([]), null);
    });

    test('a different origin belongs to the variant with the same size, packaging, class and brand from that country', () => {
        const pe = tourHass(10, 'DEMO-TOUR-101');
        const cl = tourHass(13, 'DEMO-TOUR-101', { shortcutCode: null, originCountryCode: 'CL', origin: 'Chili' });
        const otherBrand = tourHass(14, 'DEMO-TOUR-999', { originCountryCode: 'CL' });
        assert.equal(findOriginVariant(pe, [pe, otherBrand, cl], 'CL')?.id, 13);
        assert.equal(findOriginVariant(pe, [pe, otherBrand], 'CL'), null);
    });

    test('the expected date is typed with a two-digit year, as a user does', () => {
        assert.equal(shortDate('2026-11-07'), '07-11-26');
    });

    test('receiving takes the newest DEMO-TOUR order with nothing received', () => {
        const items = [item(40, 'Ordered', 'DEMO-TOUR-001'), item(45, 'PartiallyReceived', 'DEMO-TOUR-002'), item(44, 'Ordered', 'DEMO-TOUR-003'), item(50, 'Ordered', 'NP-1')];
        assert.equal(findOpenTourOrder(items)?.id, 44);
        assert.equal(findOpenTourOrder([item(50, 'Ordered', null)]), null);
    });

    test('a receipt on an order with a lot types nothing and uses the lot number; without a lot the tour types its own reference', () => {
        assert.deepEqual(lotVars({ partijNumber: '2611001' }), { lot: '2611001', typeLot: '', partijNumber: '2611001' });
        assert.deepEqual(lotVars({ partijNumber: null }), { typeLot: 'yes', partijNumber: '' });
    });
});
