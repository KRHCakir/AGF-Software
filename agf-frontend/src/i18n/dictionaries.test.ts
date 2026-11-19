import assert from 'node:assert/strict';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import en from './en.json';
import nl from './nl.json';

// Beide woordenboeken hebben dezelfde sleutels, geen lege teksten, en elke sleutel die de schermen via t('…') gebruiken bestaat.
// Zo kan een scherm nooit meer een ruwe sleutel tonen (zoals de Producten-pagina deed).

function sourceFiles(dir: string): string[] {
    return readdirSync(dir).flatMap((name) => {
        const path = join(dir, name);
        if (statSync(path).isDirectory()) return name === 'node_modules' ? [] : sourceFiles(path);
        return /\.(ts|tsx)$/.test(name) && !/\.test\.(ts|tsx)$/.test(name) ? [path] : [];
    });
}

describe('i18n dictionaries', () => {
    const nlKeys = new Set(Object.keys(nl));
    const enKeys = new Set(Object.keys(en));

    test('nl and en contain exactly the same keys and no empty texts', () => {
        assert.deepEqual([...nlKeys].filter((k) => !enKeys.has(k)), []);
        assert.deepEqual([...enKeys].filter((k) => !nlKeys.has(k)), []);
        for (const [key, value] of [...Object.entries(nl), ...Object.entries(en)]) assert.ok(value.trim() !== '', `empty text for ${key}`);
    });

    test('every key used through t(\'…\') exists in both dictionaries', () => {
        const used = new Set<string>();
        for (const file of sourceFiles(join(process.cwd(), 'src'))) {
            for (const match of readFileSync(file, 'utf8').matchAll(/\bt\('([a-zA-Z0-9_]+)'/g)) used.add(match[1]);
        }
        assert.ok(used.size > 200, `expected many keys, found ${used.size}`);
        assert.deepEqual([...used].filter((k) => !nlKeys.has(k)).sort(), [], 'missing in nl');
        assert.deepEqual([...used].filter((k) => !enKeys.has(k)).sort(), [], 'missing in en');
    });

    // Fixes 5b: Seizoen is weg uit formulieren, lijsten, filters, documenten en de rondleiding (de kolom blijft in de database).
    test('Season appears nowhere on screen: no text in either dictionary and no season field in any screen', () => {
        for (const [key, value] of [...Object.entries(nl), ...Object.entries(en)]) assert.ok(!/season|seizoen/i.test(`${key} ${value}`), `season text: ${key}`);
        for (const file of sourceFiles(join(process.cwd(), 'src'))) {
            if (!/\.tsx$/.test(file) && !/[\\/]tour[\\/]/.test(file)) continue;
            assert.ok(!/\bseason\b|\.season\b|'season'/i.test(readFileSync(file, 'utf8')), `season in ${file}`);
        }
    });

    test('customer-facing subtitles do not mention the API', () => {
        for (const dict of [nl, en]) {
            for (const key of ['sales_subtitle', 'supply_subtitle', 'availability_subtitle', 'inspection_subtitle']) {
                assert.ok(!/\bAPI\b/.test(dict[key as keyof typeof dict]), `${key} mentions the API`);
            }
        }
    });
});
