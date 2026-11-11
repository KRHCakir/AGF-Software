'use client';

import Link from 'next/link';
import { useEffect, useRef } from 'react';
import { useTranslation } from '../i18n/LanguageContext';
import { GROUP_THEME, NAV_GROUPS, NavItem, SETTINGS_ITEMS, START_ITEM, activeGroup, activeNav, groupHome } from '../lib/navigation';
import NavIcon from './NavIcon';

const focusRing = 'focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-500';
// Eén vlak voor alles in de balk (Start, de groepen, Instellingen en Help): zelfde hoogte (44 px), letter en vorm. Vanaf 1280 px groter.
export const TOP_TAB = 'flex min-h-11 items-center justify-center gap-1.5 rounded-md px-2.5 text-[15px] font-semibold whitespace-nowrap xl:gap-2 xl:px-3 xl:text-base 2xl:px-4';
const IDLE = 'text-gray-700 hover:bg-gray-100';
const CURRENT_PLAIN = 'bg-slate-700 text-white';

// Het hoofdmenu boven, vanaf 1024 px (feedback 8 en 11 november). Start, dan de vier procesgroepen als grote tabs met een icoon en
// hun kleurstip; de groepen verdelen de vrije breedte onderling (flex-1, met een maximum), zodat er geen gat meer is tot de rechterkant.
// Instellingen en Help staan rechts in dezelfde balk en dezelfde stijl, na een dun streepje (tussen 1024 en 1280 px alleen als icoon). Een groep aanklikken opent haar eerste
// scherm; de gekleurde balk eronder (SubNav) toont de schermen van die groep. Smaller dan 1024 px: de knop Menu (AppMenu).
export function TopNav({ pathname }: { pathname: string }) {
    const { t } = useTranslation();
    const active = activeNav(pathname);
    // compact: tussen 1024 en 1280 px alleen het icoon (de naam blijft voor schermlezers en als tooltip), anders past de balk niet.
    const plain = (item: NavItem, compact = false) => {
        const current = active?.itemId === item.id;
        return (
            <Link key={item.id} href={item.href} data-nav={item.id} aria-current={current ? 'page' : undefined} title={compact ? t(item.labelKey) : undefined}
                className={`${TOP_TAB} ${focusRing} ${current ? CURRENT_PLAIN : IDLE}`}>
                <NavIcon id={item.id} />
                {compact ? <span className="sr-only xl:not-sr-only">{t(item.labelKey)}</span> : t(item.labelKey)}
            </Link>
        );
    };
    return (
        <nav aria-label={t('nav_main')} className="flex min-w-0 flex-1 items-center gap-1" data-tour="app-menu">
            {plain(START_ITEM)}
            <div className="flex min-w-0 flex-1 items-center gap-1">
                {NAV_GROUPS.map((group) => {
                    const current = active?.groupId === group.id;
                    const theme = GROUP_THEME[group.id];
                    return (
                        <Link key={group.id} href={groupHome(group)} data-nav-group={group.id} data-active={current ? 'true' : undefined}
                            aria-current={current ? 'true' : undefined}
                            className={`${TOP_TAB} max-w-56 flex-1 ${focusRing} ${current ? theme.tabActive : IDLE}`}>
                            <span aria-hidden="true" className={`h-2.5 w-2.5 shrink-0 rounded-full ${current ? 'bg-white' : theme.dot}`} />
                            <NavIcon id={group.id} />
                            {t(group.labelKey)}
                        </Link>
                    );
                })}
            </div>
            <span aria-hidden="true" className="mx-1 h-7 w-px shrink-0 bg-gray-300" />
            {SETTINGS_ITEMS.map((item) => plain(item, true))}
        </nav>
    );
}

// De gekleurde balk onder de kop met de schermen van de groep waar je bent (op elke breedte; smal scrolt hij zelf horizontaal).
// Niets op Start, Instellingen, Help en de oude schermen.
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
            <div ref={bar} className="flex items-center gap-1 overflow-x-auto px-2 py-1.5 md:px-4">
                <span className={`mr-2 hidden shrink-0 items-center gap-1.5 text-sm font-bold uppercase tracking-wide sm:flex ${theme.itemIdle}`}>
                    <span aria-hidden="true" className={`h-2.5 w-2.5 rounded-full ${theme.dot}`} />{t(group.labelKey)}
                </span>
                {group.items.map((item) => {
                    const current = active?.itemId === item.id;
                    return (
                        <Link key={item.id} href={item.href} data-nav={item.id} aria-current={current ? 'page' : undefined}
                            className={`flex min-h-9 shrink-0 items-center gap-1.5 rounded-md px-3 text-[15px] whitespace-nowrap ${focusRing} ${current ? `${theme.itemActive} font-semibold` : theme.itemIdle}`}>
                            <NavIcon id={item.id} className="h-4 w-4" />
                            {t(item.labelKey)}
                        </Link>
                    );
                })}
            </div>
        </nav>
    );
}
