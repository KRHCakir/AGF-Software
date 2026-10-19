import { JSDOM } from 'jsdom';

// Headless DOM voor React-componenttests met node:test. Dit bestand moet als eerste worden geïmporteerd,
// vóór react-dom, zodat React een document aantreft. Alleen voor tests; nooit onderdeel van de Next-build.
const dom = new JSDOM('<!doctype html><html><body></body></html>', { url: 'http://localhost/', pretendToBeVisual: true });
const { window } = dom;

const globals = globalThis as Record<string, unknown>;
globals.window = window;
// next/link verwijst naar `self`; in Node bestaat dat niet.
globals.self = window;
globals.document = window.document;
// Node heeft zelf een alleen-lezen navigator; die wordt hier bewust vervangen door die van jsdom.
Object.defineProperty(globalThis, 'navigator', { value: window.navigator, configurable: true, writable: true });
globals.HTMLElement = window.HTMLElement;
globals.HTMLInputElement = window.HTMLInputElement;
globals.HTMLFormElement = window.HTMLFormElement;
globals.Event = window.Event;
globals.Node = window.Node;
globals.sessionStorage = window.sessionStorage;
globals.localStorage = window.localStorage;
globals.getComputedStyle = window.getComputedStyle;
globals.requestAnimationFrame = (callback: FrameRequestCallback) => setTimeout(() => callback(Date.now()), 0) as unknown as number;
globals.cancelAnimationFrame = (id: number) => clearTimeout(id);
globals.IS_REACT_ACT_ENVIRONMENT = true;
process.env.NEXT_PUBLIC_API_URL = 'http://api.test/api';

export { window };
