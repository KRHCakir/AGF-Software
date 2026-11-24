import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted } from '../../test/reactHarness.ts';
import PageTitle, { pageTitleClass } from './PageTitle';

// Paginatitel in een kader: één gedeelde kop voor alle schermen uit het menu, met de bestaande kleuren, een titel die minstens 4,5 op 1
// contrast haalt en de data-tour-attributen die de rondleiding gebruikt.

const SCREENS = [
    'src/app/(admin)/dashboard/page.tsx',
    'src/app/(admin)/purchase-orders/page.tsx',
    'src/app/(admin)/purchase-orders/edit/page.tsx',
    'src/app/(admin)/inspection/page.tsx',
    'src/app/(admin)/purchase-invoices/page.tsx',
    'src/app/(admin)/purchase-invoices/new/page.tsx',
    'src/app/(admin)/availability/page.tsx',
    'src/app/(admin)/partijen/page.tsx',
    'src/app/(admin)/sales-orders/page.tsx',
    'src/app/(admin)/mail-drafts/page.tsx',
    'src/app/(admin)/relations/page.tsx',
    'src/app/(admin)/articles/page.tsx',
    'src/app/(admin)/certificates/page.tsx',
    'src/app/(admin)/settings/page.tsx',
    'src/app/(admin)/help/page.tsx',
    'src/components/supply/SupplyWorkspace.tsx',
    'src/components/supply/DirectTradeArchive.tsx',
];

function luminance(hex: string): number {
    const [r, g, b] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

let mounted: Mounted;

describe('PageTitle', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('a heading in a frame that keeps its data-tour attribute and only adds margin classes', async () => {
        await mounted.render(<PageTitle data-tour="page-title" className="mb-4">Inkooporders</PageTitle>);
        const h1 = mounted.container.querySelector('h1')!;
        assert.equal(h1.textContent, 'Inkooporders');
        assert.equal(h1.getAttribute('data-tour'), 'page-title');
        for (const part of ['border', 'border-gray-200', 'border-l-4', 'border-l-blue-700', 'bg-gray-50', 'rounded-md', 'px-4', 'w-fit', 'max-w-full', 'break-words', 'mb-4']) {
            assert.ok(h1.classList.contains(part), part);
        }
        assert.ok(!h1.classList.contains('mb-1'), 'a given margin replaces the default one');
        assert.ok(!/#[0-9a-f]{3,6}|\[/.test(pageTitleClass), 'no fixed colour codes or arbitrary values');
    });

    test('without a margin of its own the frame keeps a small gap above the subtitle', async () => {
        await mounted.render(<PageTitle>Relaties</PageTitle>);
        assert.ok(mounted.container.querySelector('h1')!.classList.contains('mb-1'));
    });

    test('the title reaches at least 4.5:1 on the frame background', () => {
        const css = readFileSync(join(process.cwd(), 'src/app/globals.css'), 'utf8');
        const background = css.match(/--color-gray-50:\s*([^;]+);/)![1].trim();
        const [light, dark] = [luminance(background), luminance('#111827')].sort((x, y) => y - x); // Tailwind gray-900
        assert.ok((light + 0.05) / (dark + 0.05) >= 4.5);
    });

    test('every screen from the menu draws its title with PageTitle', () => {
        for (const file of SCREENS) {
            const source = readFileSync(join(process.cwd(), file), 'utf8');
            assert.ok(source.includes('<PageTitle'), `${file} uses PageTitle`);
            assert.equal(source.match(/<h1[\s>]/), null, `${file} has no own <h1>`);
        }
    });
});
