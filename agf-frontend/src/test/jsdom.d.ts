// Minimale typen voor jsdom (devDependency, alleen voor de componenttests); bewust geen extra @types-pakket.
declare module 'jsdom' {
    export class JSDOM {
        constructor(html?: string, options?: { url?: string; pretendToBeVisual?: boolean });
        readonly window: Window & typeof globalThis;
    }
}
