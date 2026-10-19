// Testvervanging voor next/navigation: een in-geheugen URL met useRouter/usePathname/useSearchParams die herrenderen
// bij navigatie. Moet vóór de geteste pagina worden geïmporteerd, zodat de pagina deze module uit de require-cache krijgt.
import { useSyncExternalStore } from 'react';

let current = new URL('http://localhost/');
const listeners = new Set<() => void>();
const subscribe = (listener: () => void) => { listeners.add(listener); return () => { listeners.delete(listener); }; };

export function navigate(href: string): void {
    current = new URL(href, 'http://localhost/');
    for (const listener of listeners) listener();
}

export function currentUrl(): string { return current.pathname + current.search; }

const stub = {
    useRouter: () => ({ replace: navigate, push: navigate, back: () => {}, forward: () => {}, refresh: () => {}, prefetch: () => Promise.resolve() }),
    usePathname: () => useSyncExternalStore(subscribe, () => current.pathname, () => current.pathname),
    useSearchParams: () => {
        const search = useSyncExternalStore(subscribe, () => current.search, () => current.search);
        return new URLSearchParams(search);
    },
    redirect: () => { throw new Error('redirect not supported in tests'); },
    notFound: () => { throw new Error('notFound not supported in tests'); },
};

const id = require.resolve('next/navigation');
require.cache[id] = { id, filename: id, loaded: true, exports: stub, children: [], paths: [], path: id, parent: null, isPreloading: false, require } as unknown as NodeJS.Module;
