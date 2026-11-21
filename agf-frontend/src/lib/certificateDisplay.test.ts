import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { attentionAction, certificateLink, relationAttention, shortTypeName } from './certificateDisplay.ts';

describe('certificate display', () => {
    test('a supplier is asked to check for a new certificate, a customer to request it, the own company to renew', () => {
        assert.deepEqual(attentionAction({ relationId: 4, relationName: 'Teler Uno', isCustomer: false, isVendor: true }), { key: 'cert_action_vendor', params: { name: 'Teler Uno' } });
        assert.deepEqual(attentionAction({ relationId: 5, relationName: 'Klant Groot', isCustomer: true, isVendor: false }), { key: 'cert_action_request', params: { name: 'Klant Groot' } });
        assert.deepEqual(attentionAction({ relationId: 6, relationName: 'Beide', isCustomer: true, isVendor: true }).key, 'cert_action_vendor');
        assert.deepEqual(attentionAction({ relationId: null, relationName: null, isCustomer: false, isVendor: false }).key, 'cert_action_own');
        assert.equal(certificateLink(4), '/relations?open=4&section=certificates');
        assert.equal(certificateLink(null), '/settings#certificaten');
    });

    test('short names and the badge of a relation: expired before expiring soon', () => {
        assert.equal(shortTypeName('GlobalGapIfa', 'GLOBALG.A.P. IFA (GGN)'), 'GGN');
        assert.equal(shortTypeName('Custom', 'Demeter'), 'Demeter');
        const summary = (status: 'Valid' | 'ExpiringSoon' | 'Expired') => ({ typeCode: 'Skal', typeName: 'SKAL', status, productsPending: false });
        assert.equal(relationAttention([summary('Valid'), summary('ExpiringSoon')]), 'ExpiringSoon');
        assert.equal(relationAttention([summary('ExpiringSoon'), summary('Expired')]), 'Expired');
        assert.equal(relationAttention([summary('Valid')]), null);
        assert.equal(relationAttention(undefined), null);
    });
});
