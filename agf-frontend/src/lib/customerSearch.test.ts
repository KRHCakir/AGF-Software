import assert from 'node:assert/strict';
import { test } from 'node:test';
import { customerRank, describeCustomer, searchCustomers } from './customerSearch.ts';

const customers = [
    { id: 1, name: 'Local Market Stalls', searchCode: 'LMS', city: 'Rotterdam', vatNumber: null, isActive: true },
    { id: 2, name: 'Groothandel Lommerse', searchCode: 'LO', city: 'Barendrecht', vatNumber: 'NL800123456B01', isActive: true },
    { id: 3, name: 'Lommerse Groothandel', searchCode: null, city: 'Venlo', vatNumber: null, isActive: true },
    { id: 4, name: 'Fruithal Zuid', searchCode: 'FZ', city: 'Lochem', vatNumber: null, isActive: true },
    { id: 5, name: 'Lo Oud (inactief)', searchCode: null, city: null, vatNumber: null, isActive: false },
    { id: 6, name: 'Épicerie Lyon', searchCode: null, city: 'Lyon', vatNumber: 'FR 12 345 678 901', isActive: true },
];

test('exact search code first, then names starting with the term, then contains (name, code, city); inactive never', () => {
    assert.deepEqual(searchCustomers(customers, 'Lo').map((c) => c.id), [2, 1, 3, 4]);
    assert.deepEqual(searchCustomers(customers, 'lo').map((c) => c.id), [2, 1, 3, 4]);
    assert.deepEqual(searchCustomers(customers, 'L').map((c) => c.id), [1, 3, 6, 4, 2]);
});

test('accent-insensitive names and VAT numbers without spaces or dots', () => {
    assert.deepEqual(searchCustomers(customers, 'epic').map((c) => c.id), [6]);
    assert.deepEqual(searchCustomers(customers, 'fr12345').map((c) => c.id), [6]);
    assert.deepEqual(searchCustomers(customers, 'nl800').map((c) => c.id), [2]);
    assert.equal(customerRank(customers[0], ''), null);
    assert.deepEqual(searchCustomers(customers, 'xyz'), []);
});

test('description adds the city when known', () => {
    assert.equal(describeCustomer(customers[2]), 'Lommerse Groothandel · Venlo');
    assert.equal(describeCustomer({ id: 9, name: 'Zonder plaats', searchCode: null, isActive: true }), 'Zonder plaats');
});
