'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useTranslation } from '../i18n/LanguageContext';
import { LEGACY_GROUP, NAV_GROUPS, NavGroup, NavItem, SETTINGS_ITEMS, START_ITEM, activeNav } from '../lib/navigation';

// Het hoofdmenu in vier procesgroepen (zie lib/navigation.ts). Op brede schermen een vaste zijbalk; onder 768 px een knop "Menu" in de
// kop die hetzelfde menu uitklapt. De actieve pagina krijgt aria-current en een duidelijke achtergrond, haar groep een gekleurde kop.
// "Oud" staat onderaan, dichtgeklapt, tenzij je op een van die schermen bent.
export default function AppMenu({ pathname, onNavigate, idPrefix }: {
    pathname: string;
    // Bij het mobiele menu: dichtklappen na het kiezen van een pagina.
    onNavigate?: () => void;
    // Twee menu's in de DOM (zijbalk en mobiel): unieke ids per exemplaar.
    idPrefix: string;
}) {
    const { t } = useTranslation();
    const active = activeNav(pathname);
    const [legacyOpen, setLegacyOpen] = useState(active?.groupId === 'legacy');
    const legacyId = `${idPrefix}-legacy`;
    // Navigeer je (link, terugknop) naar een oud scherm, dan klapt "Oud" open; handmatig open- en dichtklappen blijft verder vrij.
    const inLegacy = active?.groupId === 'legacy';
    useEffect(() => { if (inLegacy) setLegacyOpen(true); }, [inLegacy, pathname]);

    const link = (item: NavItem, small = false) => {
        const current = active?.itemId === item.id;
        return (
            <Link key={item.id} href={item.href} onClick={onNavigate} aria-current={current ? 'page' : undefined} data-nav={item.id}
                className={`block rounded px-2 py-1.5 ${small ? 'text-xs' : 'text-sm'} focus:outline-none focus:ring-2 focus:ring-blue-500 ${current
                    ? 'bg-blue-100 font-semibold text-blue-900' : 'text-gray-700 hover:bg-white hover:shadow-sm'}`}>
                {t(item.labelKey)}
            </Link>
        );
    };

    const group = (entry: NavGroup) => (
        <div key={entry.id} role="group" aria-labelledby={`${idPrefix}-${entry.id}`}>
            <h3 id={`${idPrefix}-${entry.id}`} className={`mb-1 px-2 text-xs font-bold uppercase ${active?.groupId === entry.id ? 'text-blue-800' : 'text-gray-500'}`}>
                {t(entry.labelKey)}
            </h3>
            <div className="flex flex-col gap-0.5">{entry.items.map((item) => link(item))}</div>
        </div>
    );

    return (
        <nav aria-label={t('nav_main')} className="flex flex-col gap-4" data-tour="app-menu">
            <div className="flex flex-col gap-0.5">{link(START_ITEM)}</div>
            {NAV_GROUPS.map(group)}
            <div className="flex flex-col gap-0.5 border-t border-gray-300 pt-2">{SETTINGS_ITEMS.map((item) => link(item))}</div>
            <div className="border-t border-gray-300 pt-2">
                <button type="button" aria-expanded={legacyOpen} aria-controls={legacyOpen ? legacyId : undefined} onClick={() => setLegacyOpen((open) => !open)}
                    className="flex w-full items-center justify-between rounded px-2 py-1 text-xs font-bold uppercase text-gray-500 hover:bg-white focus:outline-none focus:ring-2 focus:ring-blue-500">
                    <span>{t(LEGACY_GROUP.labelKey)}</span>
                    <span aria-hidden="true">{legacyOpen ? '▾' : '▸'}</span>
                </button>
                {legacyOpen && (
                    <div id={legacyId} className="mt-1 flex flex-col gap-0.5">
                        <p className="px-2 text-xs text-gray-600">{t('nav_legacy_hint')}</p>
                        {LEGACY_GROUP.items.map((item) => link(item, true))}
                    </div>
                )}
            </div>
        </nav>
    );
}
