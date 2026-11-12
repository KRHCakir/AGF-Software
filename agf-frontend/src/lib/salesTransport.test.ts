import assert from 'node:assert/strict';
import { before, describe, test } from 'node:test';
import type { SalesOrderTransport } from '../types/salesOrderTransport';
import { OTHER_ADDRESS, draftFrom, normalizeTime, requestFrom } from './salesTransport.ts';

const transport = (over: Partial<SalesOrderTransport> = {}): SalesOrderTransport => ({
    salesOrderId: 12, exists: true, mode: 'Carrier', carrierRelationId: 7, carrierName: 'Transport Zeeland B.V.', carrierAddress: null, loadingPlace: null,
    defaultLoadingPlace: 'Mijn AGF Bedrijf', unloadingAddressId: 3, unloadingPlace: null, defaultUnloadingPlace: 'Groothandel Noord, Havenweg 4', unloadingAddresses: [],
    loadAt: '2026-11-13T05:30:00Z', unloadDate: '2026-11-14T00:00:00Z', temperatureC: 2.5, loadingReference: 'LR-55', unloadingReference: null,
    defaultUnloadingReference: 'PO-778', remarks: null, palletKind: null, defaultPalletKind: 'Europallet', version: 4, hasTransportOrder: false, ...over,
});

describe('sales order transport draft', () => {
    before(() => { process.env.TZ = 'Europe/Amsterdam'; });

    test('from the API: local load time, the chosen address, decimal comma', () => {
        const draft = draftFrom(transport());
        assert.deepEqual([draft.loadDate, draft.loadTime, draft.unloadDate, draft.unloadingChoice, draft.temperature], ['2026-11-13', '06:30', '2026-11-14', '3', '2,5']);
        assert.equal(draftFrom(transport({ unloadingPlace: 'Elders 9, Venlo' })).unloadingChoice, OTHER_ADDRESS);
        assert.equal(draftFrom(null).mode, '');
    });

    test('to the API: empty fields stay empty (the server fills in the default), own transport has no carrier', () => {
        const { request, problems } = requestFrom({ ...draftFrom(transport()), loadingReference: '  ', loadTime: '7.15', temperature: '-1,5' }, 4);
        assert.deepEqual(problems, []);
        assert.equal(request!.loadAt, '2026-11-13T06:15:00.000Z');
        assert.deepEqual([request!.loadingReference, request!.unloadingAddressId, request!.unloadingPlace, request!.temperatureC, request!.expectedVersion], [null, 3, null, -1.5, 4]);
        // De laaddatum leeggemaakt: expliciet wissen; nooit ingevuld: niets wissen (de server laat de laadtijd van de order staan).
        assert.deepEqual([request!.clearLoadAt, requestFrom({ ...draftFrom(transport()), loadDate: '', loadTime: '' }, 4, true).request!.clearLoadAt,
            requestFrom({ ...draftFrom(transport()), loadDate: '', loadTime: '' }, 4, false).request!.clearLoadAt], [false, true, false]);
        const own = requestFrom({ ...draftFrom(transport()), mode: 'Own', unloadingChoice: OTHER_ADDRESS, unloadingPlace: ' Elders 9 ' }, 4).request!;
        assert.deepEqual([own.carrierRelationId, own.unloadingAddressId, own.unloadingPlace], [null, null, 'Elders 9']);
    });

    test('problems: no choice, no carrier, a date without a time, a temperature out of range', () => {
        assert.deepEqual(requestFrom(draftFrom(null), null).problems, ['mode']);
        assert.deepEqual(requestFrom({ ...draftFrom(transport()), carrierId: null, loadTime: '', temperature: '31' }, 4).problems, ['carrier', 'loadTime', 'temperature']);
        assert.deepEqual(requestFrom({ ...draftFrom(transport()), temperature: '2,25' }, 4).problems, ['temperature']);
        assert.deepEqual([normalizeTime('630'), normalizeTime('6:30'), normalizeTime('24:00'), normalizeTime('abc')], ['06:30', '06:30', null, null]);
    });

    test('problem: a different unloading address chosen but left empty', () => {
        assert.deepEqual(requestFrom({ ...draftFrom(transport()), unloadingChoice: 'other', unloadingPlace: '  ' }, 4).problems, ['unloadingPlace']);
        assert.deepEqual(requestFrom({ ...draftFrom(transport()), unloadingChoice: 'other', unloadingPlace: 'Kade 5' }, 4).problems, []);
    });
});
