import assert from 'node:assert/strict';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, test } from 'node:test';

// Fixes 5b, beter leesbaar: de gedempte tekstkleuren uit globals.css halen minstens 4,5 op 1 (WCAG AA) op elke lichte achtergrond
// waarop ze staan, tabellen en velden zijn minstens 14 px en labels minstens 13 px, en de schermen gebruiken geen bleke tekstkleuren
// of vaste mini-lettergroottes meer.

const css = readFileSync(join(process.cwd(), 'src/app/globals.css'), 'utf8');
const token = (name: string) => {
    const match = css.match(new RegExp(`--${name}:\\s*([^;]+);`));
    assert.ok(match, `--${name} in globals.css`);
    return match[1].trim();
};

function luminance(hex: string): number {
    const [r, g, b] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}
export function contrast(a: string, b: string): number {
    const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x);
    return (light + 0.05) / (dark + 0.05);
}

// Lichte achtergronden van rijen, kaarten en meldingen (Tailwind 50/100; grijs uit globals.css).
const backgrounds: Record<string, string> = {
    white: '#ffffff', 'gray-50': token('color-gray-50'), 'gray-100': token('color-gray-100'),
    'amber-50': '#fffbeb', 'blue-50': '#eff6ff', 'red-50': '#fef2f2', 'emerald-50': '#ecfdf5', 'green-50': '#f0fdf4', 'violet-50': '#f5f3ff',
};

function sourceFiles(dir: string): string[] {
    return readdirSync(dir).flatMap((name) => {
        const path = join(dir, name);
        if (statSync(path).isDirectory()) return name === 'node_modules' ? [] : sourceFiles(path);
        return /\.tsx$/.test(name) && !/\.test\.tsx$/.test(name) ? [path] : [];
    });
}

describe('readability', () => {
    test('muted text greys reach at least 4.5:1 on every light background', () => {
        for (const grey of ['gray-400', 'gray-500', 'gray-600']) {
            const colour = token(`color-${grey}`);
            for (const [name, background] of Object.entries(backgrounds)) {
                const ratio = contrast(colour, background);
                assert.ok(ratio >= 4.5, `${grey} ${colour} on ${name}: ${ratio.toFixed(2)}`);
            }
        }
        assert.ok(contrast('#646b78', '#ffffff') > contrast('#6b7280', '#ffffff'), 'darker than the Tailwind default');
    });

    test('tables and fields at least 14 px, labels at least 13 px', () => {
        const px = (value: string) => parseFloat(value) * (value.endsWith('rem') ? 16 : 1);
        assert.ok(px(token('text-sm')) >= 14 && px(token('text-sm')) <= 15, token('text-sm'));
        assert.ok(px(token('text-xs')) >= 13, token('text-xs'));
    });

    test('no screen uses pale text colours or a fixed tiny font size', () => {
        const banned = /(?<![\w:-])(text-gray-300|text-yellow-(500|600|700)|text-green-(400|500|600)|text-red-(400|500)|text-blue-400|text-\[(9|10|11|12)px\])(?![\w-])/;
        for (const file of sourceFiles(join(process.cwd(), 'src'))) {
            const match = readFileSync(file, 'utf8').match(banned);
            assert.equal(match, null, `${file}: ${match?.[0]}`);
        }
    });
});
