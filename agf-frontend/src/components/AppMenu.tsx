'use client';

import Link from 'next/link';
import { useTranslation } from '../i18n/LanguageContext';
import { GROUP_THEME, NAV_GROUPS, NavGroup, NavItem, SETTINGS_ITEMS, START_ITEM, activeNav } from '../lib/navigation';
import NavIcon from './NavIcon';

// Het hoofdmenu voor smalle schermen (onder 1024 px klapt de knop "Menu" in de kop het uit; vanaf 1024 px staat het boven, TopNav).
// Dezelfde groepen met hun icoon en kleurstip onder elkaar, elke regel minstens 44 px hoog om met een vinger te raken; Instellingen en
// Help onderaan. De actieve pagina krijgt aria-current en een duidelijke achtergrond, haar groep een gekleurde kop.
export default function AppMenu({ pathname, onNavigate, idPrefix }: {
    pathname: string;
    // Bij het mobiele menu: dichtklappen na het kiezen van een pagina.
    onNavigate?: () => void;
    // Unieke ids per exemplaar (aria-labelledby van de groepen).
    idPrefix: string;
}) {
    const { t } = useTranslation();
    const active = activeNav(pathname);

    const link = (item: NavItem) => {
        const current = active?.itemId === item.id;
        return (
            <Link key={item.id} href={item.href} onClick={onNavigate} aria-current={current ? 'page' : undefined} data-nav={item.id}
                className={`flex min-h-11 items-center gap-3 rounded-md px-3 text-base focus:outline-none focus:ring-2 focus:ring-blue-500 ${current
                    ? 'bg-blue-100 font-semibold text-blue-900' : 'text-gray-800 hover:bg-white hover:shadow-sm'}`}>
                <NavIcon id={item.id} />
                {t(item.labelKey)}
            </Link>
        );
    };

    const group = (entry: NavGroup) => {
        const current = active?.groupId === entry.id;
        return (
            <div key={entry.id} role="group" aria-labelledby={`${idPrefix}-${entry.id}`}>
                <h3 id={`${idPrefix}-${entry.id}`} className={`mb-1 flex items-center gap-2 px-3 text-sm font-bold uppercase tracking-wide ${current ? 'text-blue-800' : 'text-gray-600'}`}>
                    <span aria-hidden="true" className={`h-2.5 w-2.5 rounded-full ${GROUP_THEME[entry.id].dot}`} />
                    <NavIcon id={entry.id} className="h-4 w-4" />
                    {t(entry.labelKey)}
                </h3>
                <div className="flex flex-col gap-0.5">{entry.items.map(link)}</div>
            </div>
        );
    };

    return (
        <nav aria-label={t('nav_main')} className="flex flex-col gap-4" data-tour="app-menu">
            <div className="flex flex-col gap-0.5">{link(START_ITEM)}</div>
            {NAV_GROUPS.map(group)}
            <div className="flex flex-col gap-0.5 border-t border-gray-300 pt-2">{SETTINGS_ITEMS.map(link)}</div>
        </nav>
    );
}
