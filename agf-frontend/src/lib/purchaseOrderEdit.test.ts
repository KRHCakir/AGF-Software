import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { setInputLocale } from './decimal.ts';
import { changeFieldKey, documentStatusOf, isDefinitive, isUntouched, lineLimits, orderEditOf, removedInUse, updateLinePayload } from './purchaseOrderEdit.ts';
import { purchaseLinePayloads, type PurchaseArticle } from './purchaseOrderLines.ts';
import type { PurchaseOrderDetail, PurchaseOrderLine } from '../types/purchaseOrder';

// Inkooporder aanpassen: het formulier opent met de opgeslagen waarden (nl-notatie), een niet aangeraakte regel gaat exact terug zoals
// ze was (ook als de artikelstandaard intussen veranderde), en na ontvangst ligt het artikel vast, nooit onder ontvangen.
setInputLocale('nl');
const avocado: PurchaseArticle = { packagingUnit: 'colli', defaultNetKgPerPackage: 4.2, defaultGrossKgPerPackage: 4.6, defaultPackagesPerPallet: 252 };

const line = (over: Partial<PurchaseOrderLine>): PurchaseOrderLine => ({
    id: 7, productVariantId: 1, productName: 'Avocado', productVariantName: 'Hass', lotNumber: null, expectedQuantity: 221, unit: 'colli',
    expectedNetWeightKg: 884, expectedGrossWeightKg: 972.4, weightSource: 'Calculated', unitPrice: 12.5, receivedQuantity: 0, receivedNetWeightKg: 0,
    rejectedQuantity: 0, rejectedNetWeightKg: 0, palletInputCount: null, palletInputPackagesPerPallet: null, palletInputLoose: null, inUse: false, ...over,
});

const order = (lines: PurchaseOrderLine[], over: Partial<PurchaseOrderDetail> = {}): PurchaseOrderDetail => ({
    id: 5, orderNumber: 'IO-2026-0005', vendorId: 3, vendorName: 'Nature\'s Pride', supplierReference: '45-21', orderedOn: '2026-11-10', orderChannel: null, note: 'Bel bij aankomst',
    route: 'Warehouse', customerName: null, origin: '', destination: null, expectedAt: '2026-11-13T23:00:00Z', status: 'Ordered', storedStatus: 'Expected', invoiceIndicator: 'None',
    expectedQuantity: 221, expectedNetWeightKg: 884, receivedQuantity: 0, receivedNetWeightKg: 0, rejectedQuantity: 0, rejectedNetWeightKg: 0, lines, receipts: [], invoiceRefs: [],
    rejections: [], history: [], documentStatus: 'Open', editVersion: 2, editable: true, vendorFixed: false, partijFixed: false, partijId: null, ...over,
});

describe('purchase order edit', () => {
    test('the form opens with the stored values in nl notation, the version and the expected date in Amsterdam', () => {
        const { header, lines, edit } = orderEditOf(order([line({ unitPrice: 12.5, weightSource: 'Entered', expectedGrossWeightKg: 990 })]));
        assert.deepEqual(header, { vendorId: '3', origin: '', expectedDate: '2026-11-14', supplierReference: '45-21', orderedOn: '2026-11-10', supplierLotReference: '', note: 'Bel bij aankomst' });
        assert.deepEqual(lines[0], { key: 'line-7', productVariantId: '1', quantity: '221', unitPrice: '12,50', grossKg: '990', grossKgEntered: true, unit: 'colli', lotNumber: '' });
        assert.equal(edit.version, 2);
        assert.equal(edit.lines.get('line-7')?.lineId, 7);
    });

    test('an untouched line goes back exactly as stored (kg and pallet input), with only the price as typed; a changed line is rebuilt', () => {
        const stored = line({ expectedNetWeightKg: 884, palletInputCount: 1, palletInputPackagesPerPallet: 221 });
        const { lines, edit } = orderEditOf(order([stored]));
        const priced = { ...lines[0], unitPrice: '13' };
        const built = purchaseLinePayloads([priced], () => avocado).lines[0].payload;
        assert.equal(built.expectedNetWeightKg, 928.2, 'the form would recompute net from the new article standard');
        assert.equal(isUntouched(priced, edit.lines.get('line-7')!), true);
        assert.deepEqual(updateLinePayload(priced, built, edit.lines.get('line-7')), {
            id: 7, productVariantId: 1, expectedQuantity: 221, expectedUnit: 'colli', expectedNetWeightKg: 884, expectedGrossWeightKg: 972.4, weightSource: 'Calculated',
            lotNumber: null, palletCount: 1, packagesPerPallet: 221, loosePackages: null, unitPrice: 13,
        });

        const more = { ...lines[0], quantity: '230' };
        const rebuilt = updateLinePayload(more, purchaseLinePayloads([more], () => avocado).lines[0].payload, edit.lines.get('line-7'));
        assert.deepEqual([rebuilt.id, rebuilt.expectedQuantity, rebuilt.expectedNetWeightKg, rebuilt.palletCount, rebuilt.unitPrice], [7, 230, 966, null, 12.5]);
        const fresh = updateLinePayload(more, purchaseLinePayloads([more], () => avocado).lines[0].payload, undefined);
        assert.equal(fresh.id, null);
    });

    test('after a receipt the article is fixed, the quantity never below received and the line stays; others are free', () => {
        const { edit } = orderEditOf(order([line({ inUse: true, receivedQuantity: 40 }), line({ id: 8 })]));
        assert.deepEqual(lineLimits(edit.lines.get('line-7')), { articleFixed: true, minimumQuantity: 40, removable: false });
        assert.deepEqual(lineLimits(edit.lines.get('line-8')), { articleFixed: false, minimumQuantity: null, removable: true });
        assert.deepEqual(lineLimits(undefined), { articleFixed: false, minimumQuantity: null, removable: true });
        assert.deepEqual(removedInUse(edit, new Set(['line-8'])).map((l) => l.lineId), [7]);
        assert.deepEqual(removedInUse(edit, new Set(['line-7'])), []);
    });

    test('document status: an older server without the field is Open; field names of the history have a label', () => {
        assert.equal(documentStatusOf({}), 'Open');
        assert.equal(isDefinitive({ documentStatus: 'Definitive' }), true);
        assert.equal(isDefinitive({ documentStatus: 'Open' }), false);
        assert.equal(changeFieldKey('unitPrice'), 'po_change_price');
        assert.equal(changeFieldKey('somethingNew'), null);
    });
});
