'use client';

import Link from 'next/link';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from '../i18n/LanguageContext';
import { GROUP_THEME, LEGACY_GROUP, NAV_GROUPS, NavItem, SETTINGS_ITEMS, START_ITEM, activeGroup, activeNav, groupHome } from '../lib/navigation';

const focusRing = 'focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-500';

// Het hoofdmenu boven (feedback 8 november): de procesgroepen als tabs in de kop, zodat het scherm de volle breedte heeft voor brede
// lijsten. Een groep aanklikken opent haar eerste scherm; de gekleurde balk eronder (SubNav) toont de schermen van die groep. Elke groep
// heeft een eigen kleur (zie GROUP_THEME). Instellingen, Help en de oude schermen ("Oud", een uitklaplijst) staan rechts. Alleen vanaf
// 1024 px; smaller is er de knop Menu met hetzelfde menu onder elkaar (AppMenu).
export function TopNav({ pathname }: { pathname: string }) {
    const { t } = useTranslation();
    const active = activeNav(pathname);
    const tab = 'flex items-center gap-1.5 rounded px-2.5 py-1.5 text-sm font-medium whitespace-nowrap';
    return (
        <nav aria-label={t('nav_main')} className="flex min-w-0 items-center gap-1" data-tour="app-menu">
            <Link href={START_ITEM.href} data-nav={START_ITEM.id} aria-current={active?.itemId === START_ITEM.id ? 'page' : undefined}
                className={`${tab} ${focusRing} ${active?.itemId === START_ITEM.id ? 'bg-slate-700 text-white' : 'text-gray-700 hover:bg-gray-100'}`}>
                {t(START_ITEM.labelKey)}
            </Link>
            {NAV_GROUPS.map((group) => {
                const current = active?.groupId === group.id;
                const theme = GROUP_THEME[group.id];
                return (
                    <Link key={group.id} href={groupHome(group)} data-nav-group={group.id} data-active={current ? 'true' : undefined}
                        className={`${tab} ${focusRing} ${current ? theme.tabActive : 'text-gray-700 hover:bg-gray-100'}`}>
                        <span aria-hidden="true" className={`h-2 w-2 rounded-full ${current ? 'bg-white' : theme.dot}`} />
                        {t(group.labelKey)}
                    </Link>
                );
            })}
        </nav>
    );
}

// Rechts in de kop: Instellingen, Help en "Oud" (de oude schermen, dichtgeklapt; Escape of een klik ernaast sluit de lijst).
export function TopNavExtras({ pathname }: { pathname: string }) {
    const { t } = useTranslation();
    const active = activeNav(pathname);
    const [legacyOpen, setLegacyOpen] = useState(false);
    const box = useRef<HTMLDivElement>(null);
    const button = useRef<HTMLButtonElement>(null);

    useEffect(() => { setLegacyOpen(false); }, [pathname]);
    useEffect(() => {
        if (!legacyOpen) return;
        const onKey = (event: KeyboardEvent) => {
            if (event.key !== 'Escape') return;
            setLegacyOpen(false);
            button.current?.focus();
        };
        const onDown = (event: MouseEvent) => { if (box.current && !box.current.contains(event.target as Node)) setLegacyOpen(false); };
        window.addEventListener('keydown', onKey);
        document.addEventListener('mousedown', onDown);
        return () => { window.removeEventListener('keydown', onKey); document.removeEventListener('mousedown', onDown); };
    }, [legacyOpen]);

    const link = (item: NavItem) => (
        <Link key={item.id} href={item.href} data-nav={item.id} aria-current={active?.itemId === item.id ? 'page' : undefined}
            className={`rounded px-2 py-1.5 text-sm whitespace-nowrap ${focusRing} ${active?.itemId === item.id ? 'bg-gray-200 font-semibold text-gray-900' : 'text-gray-700 hover:bg-gray-100'}`}>
            {t(item.labelKey)}
        </Link>
    );

    return (
        <div className="flex items-center gap-1">
            {SETTINGS_ITEMS.map(link)}
            <div ref={box} className="relative">
                <button ref={button} type="button" aria-expanded={legacyOpen} aria-controls="top-nav-legacy" onClick={() => setLegacyOpen((open) => !open)}
                    className={`rounded px-2 py-1.5 text-sm whitespace-nowrap ${focusRing} ${active?.groupId === 'legacy' ? 'bg-gray-200 font-semibold text-gray-900' : 'text-gray-600 hover:bg-gray-100'}`}>
                    {t(LEGACY_GROUP.labelKey)} <span aria-hidden="true">▾</span>
                </button>
                {/* Altijd in de DOM (hidden als dicht), zodat aria-controls naar een bestaand element wijst. */}
                <div id="top-nav-legacy" hidden={!legacyOpen} className="absolute right-0 top-full z-40 mt-1 w-64 flex-col gap-0.5 rounded border border-gray-300 bg-white p-2 shadow-lg">
                    <p className="px-2 pb-1 text-xs text-gray-600">{t('nav_legacy_hint')}</p>
                    <div className="flex flex-col gap-0.5">{LEGACY_GROUP.items.map(link)}</div>
                </div>
            </div>
        </div>
    );
}

// De gekleurde balk onder de kop met de schermen van de groep waar je bent (op elke breedte; smal scrolt hij zelf horizontaal).
// Niets op Start, Instellingen en Help.
export function SubNav({ pathname }: { pathname: string }) {
    const { t } = useTranslation();
    const group = activeGroup(pathname);
    const active = activeNav(pathname);
    const bar = useRef<HTMLDivElement>(null);
    // Op een smal scherm scrolt de balk zelf: het huidige scherm schuift in beeld (alleen binnen de balk, de pagina blijft staan).
    useEffect(() => {
        const element = bar.current;
        const item = element?.querySelector<HTMLElement>('[aria-current="page"]');
        if (!element || !item) return;
        const left = item.offsetLeft - element.offsetLeft;
        if (left < element.scrollLeft || left + item.offsetWidth > element.scrollLeft + element.clientWidth) element.scrollLeft = Math.max(0, left - 16);
    }, [pathname]);
    if (group === null) return null;
    const theme = GROUP_THEME[group.id];
    return (
        <nav aria-label={t('nav_sub', { group: t(group.labelKey) })} className={`border-b ${theme.bar}`} data-testid="sub-nav" data-nav-group={group.id}>
            <div ref={bar} className="flex items-center gap-1 overflow-x-auto px-2 py-1 md:px-4">
                <span className={`mr-1 hidden shrink-0 items-center gap-1.5 text-xs font-bold uppercase tracking-wide sm:flex ${theme.itemIdle}`}>
                    <span aria-hidden="true" className={`h-2 w-2 rounded-full ${theme.dot}`} />{t(group.labelKey)}
                </span>
                {group.items.map((item) => {
                    const current = active?.itemId === item.id;
                    return (
                        <Link key={item.id} href={item.href} data-nav={item.id} aria-current={current ? 'page' : undefined}
                            className={`shrink-0 rounded px-2.5 py-1 text-sm whitespace-nowrap ${focusRing} ${current ? `${theme.itemActive} font-semibold` : theme.itemIdle}`}>
                            {t(item.labelKey)}
                        </Link>
                    );
                })}
            </div>
        </nav>
    );
}
