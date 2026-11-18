import assert from 'node:assert/strict';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, test } from 'node:test';

// Getallen die een gebruiker typt, worden overal met dezelfde parser gelezen (lib/decimal.ts): de punt als decimaalteken, het
// duizendtal, de melding bij een ongeldig getal. Deze test faalt zodra er in agf-frontend weer een eigen vertaling bij komt:
// parseFloat, parseInt of toFixed, Number(...) op de tekst van een veld, of zelf een komma en een punt verwisselen.
const OWN_PARSING: [RegExp, string][] = [
    [/\bparseFloat\(/, 'parseFloat'],
    [/\bparseInt\(/, 'parseInt'],
    [/\.toFixed\(/, 'toFixed'],
    [/\bNumber\([^()]*(?:\([^()]*\)[^()]*)*\.value\b/, 'Number(veld.value)'],
    [/\.replace\(\s*(?:'[.,]'|"[.,]"|\/\\?[.,]\/g?)\s*,\s*(?:'[.,]'|"[.,]")/, 'komma en punt verwisselen'],
    [/\.replace\(\s*(?:'[.,]'|"[.,]"|\/\\?[.,]\/g?)\s*,[^)]*\?\s*'[.,]'\s*:\s*'[.,]'/, 'komma en punt verwisselen'],
];

// Bewuste uitzonderingen, elk met de reden. Een nieuw bestand hoort hier niet bij: gebruik de gedeelde parser.
const ALLOWED: Record<string, string> = {
    // Oude schermen (Oude flows en de pagina voor de telefoon): type="number", de browser leest het getal. Niet meer in ontwikkeling.
    'components/DumpWasteModal.tsx': 'oud scherm',
    'components/ReceiveShipmentModal.tsx': 'oud scherm',
    'components/QuickSellModal.tsx': 'oud scherm',
    'components/ManualInvoiceModal.tsx': 'oud scherm',
    'app/sales/[sellerId]/page.tsx': 'oud scherm (telefoon)',
    'app/(admin)/incoming-invoices/[id]/review/page.tsx': 'oud scherm',
    'app/(admin)/incoming-invoices/page.tsx': 'oud scherm',
    // Hele getallen in een veld dat alleen cijfers toelaat (poort, aantal cijfers van een volgnummer).
    'components/settings/MailSenderSetting.tsx': 'poortnummer, alleen cijfers',
    'components/settings/PartijNumberingSetting.tsx': 'aantal cijfers, alleen hele getallen',
    // Rekenwerk, geen invoer: afronden op centen zoals de server.
    'lib/exchangeRate.ts': 'afronden op centen',
};

function sourceFiles(dir: string): string[] {
    return readdirSync(dir).flatMap((name) => {
        const path = join(dir, name);
        if (statSync(path).isDirectory()) return name === 'test' ? [] : sourceFiles(path);
        return /\.(ts|tsx)$/.test(name) && !/\.test\.(ts|tsx)$/.test(name) ? [path] : [];
    });
}

describe('numbers typed by a user are read by the shared parser only', () => {
    const src = join(process.cwd(), 'src');
    const files = sourceFiles(src);

    test('no own parseFloat, parseInt, toFixed, Number(field.value) or swapping of comma and dot outside lib/decimal.ts', () => {
        assert.ok(files.length > 100, `the source was found (${files.length} files in ${src})`);
        const found = files.flatMap((file) => {
            const name = relative(src, file).split('\\').join('/');
            if (name in ALLOWED) return [];
            return readFileSync(file, 'utf8').split('\n').flatMap((line, index) =>
                OWN_PARSING.filter(([pattern]) => pattern.test(line)).map(([, what]) => `${name}:${index + 1}: ${what}: ${line.trim().slice(0, 120)}`));
        });
        assert.deepEqual(found, []);
    });

    test('every exception still exists and still needs to be one (the list cannot grow stale)', () => {
        for (const name of Object.keys(ALLOWED)) {
            const text = readFileSync(join(src, name), 'utf8');
            assert.ok(OWN_PARSING.some(([pattern]) => text.split('\n').some((line) => pattern.test(line))), `${name} no longer needs its exception (${ALLOWED[name]})`);
        }
    });

    test('the patterns catch what they should and leave ids and whole numbers alone', () => {
        const caught = (line: string) => OWN_PARSING.some(([pattern]) => pattern.test(line));
        for (const line of ["const kg = parseFloat(text);", "onChange={(e) => set(Number(e.target.value))}", "Number(event.target.value) || 0", "price.toFixed(2)",
            "Number(text.replace(',', '.'))", "String(vat).replace('.', ',')", "String(vat).replace('.', locale === 'nl' ? ',' : '.')", "value.replace(/,/g, '.')", "parseInt(quantity)"]) {
            assert.equal(caught(line), true, line);
        }
        for (const line of ["const id = Number(line.productVariantId);", "/^\\d+$/.test(days) ? Number(days) : null", "Number(b.due) - Number(a.due)", "first.replace('.description', '.item')",
            "text.replace(/\\s+/g, ' ')", "raw.replace(/\\/+$/, '')"]) {
            assert.equal(caught(line), false, line);
        }
    });
});
