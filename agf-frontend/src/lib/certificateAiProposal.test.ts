import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { approvalRows, draftFromProposal, fieldMark, linkedProductIds } from './certificateAiProposal.ts';
import { rowsFromCertificates } from './certificateRules.ts';
import type { AiCertificateProposal } from '../types/certificateAi';
import type { Certificate } from '../types/certificate';

// Certificaten deel 2: van het AI-voorstel naar een regel en terug naar de lijst die bij Goedkeuren wordt opgeslagen.
const proposal = (over: Partial<AiCertificateProposal> = {}): AiCertificateProposal => ({
    typeCode: 'GlobalGapIfa', customTypeName: null, documentTypeName: 'GLOBALG.A.P. IFA', number: '4063061591012', holderName: 'Kwekerij Verde Demo S.L.', holderCountry: 'ES',
    certificationBody: 'Demo Certificering B.V.', validFrom: '2026-03-01', validUntil: '2027-02-28', status: 'Valid', daysLeft: 99,
    products: [
        { text: 'Lettuce', page: 1, productId: 10, productName: 'Sla', match: 'Translated' },
        { text: 'Arugula', page: 2, productId: 11, productName: 'Rucola', match: 'Translated' },
        { text: 'Mizuna', page: 2, productId: null, productName: null, match: 'None' },
    ],
    sites: [], holderMismatch: null, sameNumberCertificateId: null, sameNumberTypeName: null, ...over,
});
const stored: Certificate = {
    id: 7, typeCode: 'GlobalGapIfa', customTypeName: null, typeName: 'GGN', number: '4063061591012', certificationBody: 'Oud', validFrom: '2025-03-01', validUntil: '2026-02-28',
    products: [{ id: 10, name: 'Sla' }], productsPending: false, producerName: 'Verde', hasGgnLabel: true, checkedOn: '2025-03-02', checkedBy: 'Jan', isActive: true,
    status: 'Expired', daysLeft: -200, files: [{ id: 1, version: 1, fileName: 'oud.pdf', mediaType: 'application/pdf', sizeBytes: 1, sha256: 'x', createdAt: '2025-03-02T10:00:00Z', createdBy: 'Jan' }],
};

describe('certificate AI proposal', () => {
    test('the draft takes the read values; the holder becomes the producer; unlinked products stay visible', () => {
        const draft = draftFromProposal(proposal());
        assert.deepEqual([draft.row.typeCode, draft.row.number, draft.row.validUntil, draft.row.producerName, draft.row.id], ['GlobalGapIfa', '4063061591012', '2027-02-28', 'Kwekerij Verde Demo S.L.', null]);
        assert.deepEqual(draft.links.map((l) => [l.text, l.page, l.productId]), [['Lettuce', 1, 10], ['Arugula', 2, 11], ['Mizuna', 2, null]]);
        assert.deepEqual(linkedProductIds(draft.links), [10, 11]);
        assert.equal(draftFromProposal(proposal({ typeCode: 'Custom', customTypeName: 'Demeter' })).row.customTypeName, 'Demeter');
    });

    test('a new certificate goes at the end; the same number updates the stored one as a new version (id, files and check stay)', () => {
        const rows = rowsFromCertificates([stored]);
        const fresh = approvalRows(rows, draftFromProposal(proposal({ number: '4049928415684' })));
        assert.equal(fresh.index, 1);
        assert.deepEqual([fresh.rows.length, fresh.rows[1].id, fresh.rows[1].number], [2, null, '4049928415684']);

        const renewed = approvalRows(rows, draftFromProposal(proposal({ sameNumberCertificateId: 7 })));
        assert.equal(renewed.index, 0);
        assert.equal(renewed.rows.length, 1);
        const row = renewed.rows[0];
        assert.deepEqual([row.id, row.validUntil, row.certificationBody, row.productIds, row.checkedOn, row.files.length], [7, '2027-02-28', 'Demo Certificering B.V.', [10, 11], '2025-03-02', 1]);
        // De gebruiker koos toch een tweede certificaat.
        assert.equal(approvalRows(rows, { ...draftFromProposal(proposal({ sameNumberCertificateId: 7 })), targetId: null }).index, 1);
    });

    test('doubt and errors are yellow, a missing field says not found', () => {
        const extraction = { problems: [{ field: 'validUntil', code: 'uncertain' }, { field: 'number', code: 'ggnCheckDigit' }, { field: 'holderName', code: 'missing' }] };
        assert.equal(fieldMark(extraction, 'validUntil'), 'doubt');
        assert.equal(fieldMark(extraction, 'number'), 'doubt');
        assert.equal(fieldMark(extraction, 'holderName'), 'missing');
        assert.equal(fieldMark(extraction, 'certificationBody'), null);
    });
});
