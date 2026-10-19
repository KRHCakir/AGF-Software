import './setupDom.ts';
import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { parseStoredCommands } from '../lib/pendingCommands.ts';

// Gedeelde hulpfuncties voor componenttests met node:test, jsdom en React's eigen act/createRoot.
// Zonder LanguageProvider geeft t() de sleutel terug; tests zoeken dus op i18n-sleutels.

export const STORAGE_KEY = 'agf_sales_pending_commands';

export const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

export function deferred<T>() {
    let resolve!: (value: T) => void;
    let reject!: (error: unknown) => void;
    const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej; });
    return { promise, resolve, reject };
}

export interface Posted { url: string; body: string }

// fetch-stub: POSTs worden vastgelegd en beantwoord door `nextPost`; GETs door `onGet`.
export function stubFetch(options: { onGet: (url: string) => Response | Promise<Response>; nextPost: () => Promise<Response> }): Posted[] {
    const posted: Posted[] = [];
    globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = String(input);
        if (init?.method === 'POST') {
            posted.push({ url, body: String(init.body) });
            return options.nextPost();
        }
        return options.onGet(url);
    }) as typeof fetch;
    return posted;
}

export class Mounted {
    readonly container: HTMLElement;
    readonly root: Root;

    constructor() {
        this.container = window.document.createElement('div');
        window.document.body.appendChild(this.container);
        this.root = createRoot(this.container);
    }

    render(element: React.ReactNode): Promise<void> {
        return act(async () => { this.root.render(element); });
    }

    async unmount(): Promise<void> {
        await act(async () => this.root.unmount());
        this.container.remove();
    }

    buttons(): HTMLButtonElement[] {
        return Array.from(this.container.querySelectorAll('button'));
    }

    button(text: string): HTMLButtonElement | null {
        return this.buttons().find((b) => b.textContent?.trim() === text) ?? null;
    }

    hasText(text: string): boolean {
        return (this.container.textContent ?? '').includes(text);
    }

    form(): HTMLFormElement | null {
        return this.container.querySelector<HTMLFormElement>('form');
    }

    async waitFor<T>(find: () => T | null | undefined | false, what: string): Promise<T> {
        for (let i = 0; i < 50; i++) {
            const found = find();
            if (found) return found;
            await flush();
        }
        throw new Error(`Timed out waiting for ${what}`);
    }
}

export const flush = () => act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)); });

export const click = (element: HTMLElement) => act(async () => { element.dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true })); });

export const submit = (form: HTMLFormElement) => act(async () => { form.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });

export function setValue(input: HTMLInputElement | HTMLTextAreaElement, value: string) {
    const prototype = input instanceof window.HTMLTextAreaElement ? window.HTMLTextAreaElement.prototype : window.HTMLInputElement.prototype;
    const setter = Object.getOwnPropertyDescriptor(prototype, 'value')!.set!;
    setter.call(input, value);
    input.dispatchEvent(new window.Event('input', { bubbles: true }));
}

export const storedCommands = () => parseStoredCommands(window.sessionStorage.getItem(STORAGE_KEY));
