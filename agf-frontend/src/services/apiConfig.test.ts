import assert from 'node:assert/strict';
import { afterEach, describe, test } from 'node:test';
import { ApiConfigError, DEFAULT_API_BASE_URL, getApiBaseUrl } from './apiConfig.ts';

// De website praat standaard via zichzelf met de API (/api, doorgestuurd door next.config.ts); een eigen pad of een volledig adres
// (zoals de e2e-tests met een eigen API) mag ook. Een ongeldige waarde blijft een zichtbare configuratiefout.
describe('API address', () => {
    const original = process.env.NEXT_PUBLIC_API_URL;
    afterEach(() => {
        if (original === undefined) delete process.env.NEXT_PUBLIC_API_URL;
        else process.env.NEXT_PUBLIC_API_URL = original;
    });

    test('without a setting the website itself is the address: /api', () => {
        delete process.env.NEXT_PUBLIC_API_URL;
        assert.equal(getApiBaseUrl(), '/api');
        assert.equal(DEFAULT_API_BASE_URL, '/api');
        process.env.NEXT_PUBLIC_API_URL = '   ';
        assert.equal(getApiBaseUrl(), '/api');
    });

    test('a path on the website or a full http(s) address is used as given, without a trailing slash', () => {
        process.env.NEXT_PUBLIC_API_URL = '/backend/api/';
        assert.equal(getApiBaseUrl(), '/backend/api');
        process.env.NEXT_PUBLIC_API_URL = 'http://api.test:5198/api/';
        assert.equal(getApiBaseUrl(), 'http://api.test:5198/api');
        process.env.NEXT_PUBLIC_API_URL = 'https://demo.example.nl/api';
        assert.equal(getApiBaseUrl(), 'https://demo.example.nl/api');
    });

    test('an address without protocol, another protocol or nonsense is a configuration error', () => {
        for (const value of ['//api.test/api', 'ftp://api.test/api', 'api.test/api']) {
            process.env.NEXT_PUBLIC_API_URL = value;
            assert.throws(() => getApiBaseUrl(), ApiConfigError, value);
        }
    });
});
