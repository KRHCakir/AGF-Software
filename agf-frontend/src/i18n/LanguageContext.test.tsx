import '../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, describe, test } from 'node:test';
import { Mounted } from '../test/reactHarness.ts';
import { LanguageProvider, initialLocale, useTranslation } from './LanguageContext';

// Taalkeuze: opgeslagen keuze wint; anders de eerste browsertaal die nl of en is; anders Nederlands.

function Probe() {
    const { locale } = useTranslation();
    return <span data-locale={locale}>{locale}</span>;
}

describe('initialLocale', () => {
    test('a stored choice always wins over the browser language', () => {
        assert.equal(initialLocale('en', ['nl-NL']), 'en');
        assert.equal(initialLocale('nl', ['en-US']), 'nl');
    });
    test('without a stored choice the first Dutch or English browser language decides', () => {
        assert.equal(initialLocale(null, ['nl-NL', 'en-US']), 'nl');
        assert.equal(initialLocale(null, ['en-GB', 'nl']), 'en');
        assert.equal(initialLocale(null, ['de-DE', 'en']), 'en');
        assert.equal(initialLocale(null, ['NL']), 'nl');
    });
    test('an unsupported or unknown browser language falls back to Dutch', () => {
        assert.equal(initialLocale(null, ['de-DE', 'fr']), 'nl');
        assert.equal(initialLocale(null, []), 'nl');
        assert.equal(initialLocale('xx', []), 'nl');
    });
});

describe('LanguageProvider on first visit', () => {
    let mounted: Mounted;
    afterEach(async () => { await mounted?.unmount(); });

    const withLanguages = (languages: string[]) => Object.defineProperty(window.navigator, 'languages', { value: languages, configurable: true });

    test('English browser without a stored choice renders English; a stored Dutch choice overrides it', async () => {
        window.localStorage.clear();
        withLanguages(['en-US']);
        mounted = new Mounted();
        await mounted.render(<LanguageProvider><Probe /></LanguageProvider>);
        assert.equal(await mounted.waitFor(() => mounted.container.querySelector('[data-locale]')?.getAttribute('data-locale'), 'locale'), 'en');
        await mounted.unmount();
        window.localStorage.setItem('agf_locale', 'nl');
        mounted = new Mounted();
        await mounted.render(<LanguageProvider><Probe /></LanguageProvider>);
        assert.equal(await mounted.waitFor(() => mounted.container.querySelector('[data-locale]')?.getAttribute('data-locale'), 'locale'), 'nl');
    });

    test('a German browser without a stored choice renders Dutch', async () => {
        window.localStorage.clear();
        withLanguages(['de-DE']);
        mounted = new Mounted();
        await mounted.render(<LanguageProvider><Probe /></LanguageProvider>);
        assert.equal(await mounted.waitFor(() => mounted.container.querySelector('[data-locale]')?.getAttribute('data-locale'), 'locale'), 'nl');
    });
});
