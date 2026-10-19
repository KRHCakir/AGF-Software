import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { blankToNull, buildCustomerPayload, emptyCustomerDraft, isSimilarCustomerName, normalizeCustomerName, similarCustomers, validateCustomerDraft } from './customerRules.ts';

describe('customer rules', () => {
    test('blank optional values become null and everything is trimmed; the name is required', () => {
        const built = buildCustomerPayload({ name: '  Groentehal Jansen ', contactPerson: '   ', email: ' piet@jansen.example ', phone: '', shippingAddress: ' Marktweg 1 ' }, 'r-1', false);
        assert.ok(built.ok);
        assert.deepEqual(built.value, { requestId: 'r-1', name: 'Groentehal Jansen', contactPerson: null, email: 'piet@jansen.example', phone: null, shippingAddress: 'Marktweg 1', acknowledgeSimilarNames: false });
        assert.equal(blankToNull(' \t'), null);
        const missing = buildCustomerPayload(emptyCustomerDraft(), 'r-2', true);
        assert.ok(!missing.ok);
        assert.deepEqual(missing.errors, [{ field: 'name', code: 'required' }]);
    });

    test('length limits match the backend and the e-mail check is light', () => {
        const draft = { ...emptyCustomerDraft(), name: 'a'.repeat(201), contactPerson: 'b'.repeat(101), email: 'geen adres', phone: '1'.repeat(41), shippingAddress: 'd'.repeat(301) };
        assert.deepEqual(validateCustomerDraft(draft).map((e) => `${e.field}:${e.code}`).sort(), ['contactPerson:too_long', 'email:invalid_email', 'name:too_long', 'phone:too_long', 'shippingAddress:too_long']);
        assert.deepEqual(validateCustomerDraft({ ...emptyCustomerDraft(), name: 'Ok', email: 'a@b' }), []);
        assert.deepEqual(validateCustomerDraft({ ...emptyCustomerDraft(), name: 'Ok', email: 'a@@b' }), [{ field: 'email', code: 'invalid_email' }]);
    });

    test('name normalization folds case, accents, punctuation and whitespace like the server', () => {
        assert.equal(normalizeCustomerName('  Gröente-Hal  Jánsen B.V. '), 'groente hal jansen b v');
        assert.equal(normalizeCustomerName(''), '');
        assert.equal(isSimilarCustomerName('Groentehal Jansen', 'GROENTEHAL  JANSEN'), true);
        assert.equal(isSimilarCustomerName('Jansen', 'Groentehal Jansen'), true);
        assert.equal(isSimilarCustomerName('AB', 'Groentehal AB'), false, 'two characters never count as contained');
        assert.equal(isSimilarCustomerName('Fruitpaleis', 'Groentehal Jansen'), false);
        const options = [{ id: 1, name: 'Groentehal Jansen' }, { id: 2, name: 'DEMO Klant B (donderdag-demo)' }];
        assert.deepEqual(similarCustomers('demo klant b', options).map((o) => o.id), [2]);
        assert.deepEqual(similarCustomers('', options), []);
    });
});
