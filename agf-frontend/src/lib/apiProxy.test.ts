import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { MAX_API_REQUEST_BYTES, requestTooLarge } from './apiProxy.ts';

describe('API proxy size limit', () => {
    test('the website stops a request above the upload limit of the API (32 MB); without a length the API decides', () => {
        assert.equal(MAX_API_REQUEST_BYTES, 32 * 1024 * 1024);
        assert.equal(requestTooLarge(String(MAX_API_REQUEST_BYTES)), false);
        assert.equal(requestTooLarge(String(MAX_API_REQUEST_BYTES + 1)), true);
        assert.equal(requestTooLarge('1024'), false);
        assert.equal(requestTooLarge(null), false);
        assert.equal(requestTooLarge('abc'), false);
        assert.equal(requestTooLarge(' 40000000 '), true);
    });
});
