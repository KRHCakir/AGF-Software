import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import {
    buildCertificatesPayload, certificateStatus, certificateWarnings, certificatesChanged, daysLeft, emptyCertificateRow, hasValidGgnCheckDigit, isBlankRow, rowsFromCertificates, tabAddsRow,
} from './certificateRules.ts';
import type { Certificate } from '../types/certificate';

// Certificaten deel 1: de regels van het blok, gelijk aan de server (CertificateRules en CertificateService).
const TODAY = '2026-11-20';
const ggn = (number: string, productIds: number[] = [1]) => ({ ...emptyCertificateRow('GlobalGapIfa'), number, productIds });

describe('certificate rows', () => {
    test('the status follows the end date: 30 days is still "expires soon", 31 is valid, yesterday is expired', () => {
        assert.equal(certificateStatus('2026-12-21', TODAY, 30), 'Valid');
        assert.equal(certificateStatus('2026-12-20', TODAY, 30), 'ExpiringSoon');
        assert.equal(certificateStatus(TODAY, TODAY, 30), 'ExpiringSoon');
        assert.equal(certificateStatus('2026-11-19', TODAY, 30), 'Expired');
        assert.equal(certificateStatus('', TODAY, 30), 'NoEndDate');
        assert.equal(daysLeft('2026-12-20', TODAY), 30);
        assert.equal(daysLeft('2027-03-30', TODAY), 130, 'across the change to winter time');
    });

    test('a GGN has 13 digits (spaces allowed), other numbers are free; the same number twice is refused', () => {
        const result = buildCertificatesPayload([
            ggn('4049 9284 15684'),
            ggn('404992841568'),
            { ...emptyCertificateRow('GlobalGapCoc'), number: '  CoC-NL-2026/000123456789-ABC ' },
            { ...emptyCertificateRow('Skal'), number: 'NL-BIO-01' },
            { ...emptyCertificateRow('Skal'), number: 'nl bio 01' },
            { ...emptyCertificateRow('Custom'), number: 'D-1' },
            { ...emptyCertificateRow(''), number: 'X' },
            { ...emptyCertificateRow('Brcgs'), number: 'B', validFrom: '2026-12-01', validUntil: '2026-11-01', checkedOn: '2026-11-21' },
            { ...emptyCertificateRow('Brcgs'), number: 'C', validUntil: '31-02-2026' },
            emptyCertificateRow(),
        ], TODAY);
        assert.equal(result.ok, false);
        if (result.ok) return;
        assert.deepEqual(result.errors.map((e) => `${e.field.replace(/certificates\.cert-\d+\./, '')}:${e.code}`), [
            'number:ggn_13_digits', 'number:duplicate', 'customTypeName:custom_type_required', 'typeCode:type_required', 'validUntil:end_before_start', 'checkedOn:check_future', 'validUntil:invalid_date',
        ]);
        const ok = buildCertificatesPayload([ggn('4049 9284 15684', [3, 3, 1]), { ...emptyCertificateRow('GlobalGapCoc'), number: '  CoC-1 ', certificationBody: ' ' }, emptyCertificateRow()], TODAY);
        assert.ok(ok.ok);
        if (!ok.ok) return;
        assert.deepEqual(ok.value.map((c) => [c.number, c.productIds, c.certificationBody]), [['4049928415684', [3, 1], null], ['CoC-1', [], null]]);
    });

    test('warnings that do not block: a GGN without products and a GGN with a wrong check digit', () => {
        assert.equal(hasValidGgnCheckDigit('4049928415684'), true);
        assert.equal(hasValidGgnCheckDigit('4049928415685'), false);
        assert.deepEqual(certificateWarnings(ggn('4049928415684')), []);
        assert.deepEqual(certificateWarnings(ggn('4049928415685', [])), ['products_missing', 'ggn_check_digit']);
        assert.deepEqual(certificateWarnings({ ...ggn('4049928415684', []), id: 7, productsPending: true }), ['products_pending']);
        assert.deepEqual(certificateWarnings({ ...ggn('4049928415684', []), id: 7 }), ['products_missing']);
        assert.deepEqual(certificateWarnings({ ...emptyCertificateRow('Skal'), number: 'X' }), []);
    });

    test('a new row with only a date, product or body filled in is not blank and gets validated', () => {
        for (const patch of [{ validFrom: '2026-01-01' }, { certificationBody: 'Control Union' }, { productIds: [2] }, { producerName: 'X' }, { hasGgnLabel: true }]) {
            assert.equal(buildCertificatesPayload([{ ...emptyCertificateRow(), ...patch }], TODAY).ok, false);
        }
    });

    test('Tab adds a row only after a filled last row; a blank row is no change', () => {
        const filled = { ...emptyCertificateRow('Skal'), number: 'X' };
        const blank = emptyCertificateRow();
        assert.equal(tabAddsRow([filled], filled.key), true);
        assert.equal(tabAddsRow([filled, blank], blank.key), false);
        assert.equal(tabAddsRow([filled, blank], filled.key), false, 'only on the last row');
        assert.equal(isBlankRow(blank), true);
        const certificate: Certificate = {
            id: 4, typeCode: 'Skal', customTypeName: null, typeName: 'SKAL', number: 'NL-BIO-01', certificationBody: null, validFrom: null, validUntil: '2027-01-01',
            products: [{ id: 2, name: 'Mango' }], productsPending: false, producerName: null, hasGgnLabel: false, checkedOn: '2026-11-01', checkedBy: 'Anja', isActive: true,
            status: 'Valid', daysLeft: 42, files: [],
        };
        const rows = rowsFromCertificates([certificate]);
        assert.equal(certificatesChanged([...rows, emptyCertificateRow()], rows), false);
        assert.equal(certificatesChanged([{ ...rows[0], productIds: [2, 5] }], rows), true);
        assert.equal(certificatesChanged([{ ...rows[0], pendingFiles: [new Blob(['%PDF']) as File] }], rows), true, 'a chosen file goes with Save');
    });
});
