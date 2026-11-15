import assert from 'node:assert/strict';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, test } from 'node:test';

// Online draait de website achter één adres en praat ze via zichzelf met de API (src/services/apiConfig.ts). Een vast adres zoals
// http://localhost:5197 in de code werkt daar niet: deze test faalt zodra er weer een in de broncode komt. Tests en testhulpen
// (src/test) mogen wel een eigen adres gebruiken.
const FIXED_ADDRESS = [/localhost:\d/i, /\/\/localhost\b/i, /127\.0\.0\.1/, /\b5197\b/];

function sourceFiles(dir: string): string[] {
    return readdirSync(dir).flatMap((name) => {
        const path = join(dir, name);
        if (statSync(path).isDirectory()) return name === 'test' ? [] : sourceFiles(path);
        return /\.(ts|tsx|js|jsx|json)$/.test(name) && !/\.test\.(ts|tsx)$/.test(name) ? [path] : [];
    });
}

describe('no fixed API address in the code', () => {
    test('no localhost, 127.0.0.1 or port 5197 in the website source; the API address comes from apiConfig only', () => {
        const src = join(process.cwd(), 'src');
        const files = sourceFiles(src);
        assert.ok(files.length > 100, `the source was found (${files.length} files in ${src})`);
        const found = files.flatMap((file) => readFileSync(file, 'utf8').split('\n').flatMap((line, index) =>
            FIXED_ADDRESS.some((pattern) => pattern.test(line)) ? [`${relative(src, file)}:${index + 1}: ${line.trim()}`] : []));
        assert.deepEqual(found, []);
    });
});
