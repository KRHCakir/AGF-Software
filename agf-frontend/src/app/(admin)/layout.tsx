'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useEffect, useRef, useState } from 'react';
import '../globals.css';
import { LanguageProvider, useTranslation } from '../../i18n/LanguageContext';
import AppMenu from '../../components/AppMenu';
import { SubNav, TopNav, TopNavExtras } from '../../components/TopNav';
import DemoAssistant from '../../components/demo/DemoAssistant';
import { CurrentUserProvider, SignedInAs } from '../../components/shared/CurrentUserContext';
import { showDemoAssistant, showLegacyDemoChrome } from '../../lib/featureFlags';

function AdminLayoutContent({
    children,
}: {
    children: React.ReactNode;
}) {
    const { t, locale, setLocale } = useTranslation();
    const pathname = usePathname();
    const [menuOpen, setMenuOpen] = useState(false);
    const menuButton = useRef<HTMLButtonElement>(null);
    // Escape sluit het mobiele menu, ook als de focus nog op de menuknop staat; de focus gaat terug naar de knop.
    useEffect(() => {
        if (!menuOpen) return;
        const onKey = (event: KeyboardEvent) => {
            if (event.key !== 'Escape') return;
            setMenuOpen(false);
            menuButton.current?.focus();
        };
        window.addEventListener('keydown', onKey);
        return () => window.removeEventListener('keydown', onKey);
    }, [menuOpen]);

    return (
        <div className="flex flex-col min-h-dvh">
            {/* KOP: logo (naar Start), het menu als tabs per procesgroep (vanaf 1024 px; smaller de knop Menu), Instellingen, Help, Oud,
                taal en gebruiker. Daaronder de gekleurde balk met de schermen van de groep waar je bent (SubNav). Geen zijbalk: de pagina
                heeft de volle breedte (feedback 8 november). */}
            <header className="bg-white border-b border-gray-300 h-12 flex items-center px-4 shadow-sm z-20">
                <button ref={menuButton} type="button" className="mr-3 rounded border border-gray-300 px-2 py-1 text-sm font-medium text-gray-800 lg:hidden focus:outline-none focus:ring-2 focus:ring-blue-500"
                    aria-expanded={menuOpen} aria-controls="app-menu-mobile" onClick={() => setMenuOpen((open) => !open)}>
                    {menuOpen ? '✕' : '☰'} {t('nav_menu')}
                </button>
                <Link href="/dashboard" className="font-bold text-xl text-blue-800 mr-4 lg:mr-6 tracking-tight shrink-0">AGF<span className="text-gray-600 font-normal">ERP</span></Link>
                <div className="hidden min-w-0 lg:flex"><TopNav pathname={pathname} /></div>

                <div className="ml-auto flex shrink-0 items-center space-x-4 pl-2 text-gray-500">
                    <div className="hidden lg:block"><TopNavExtras pathname={pathname} /></div>
                    <div className="flex items-center space-x-1 border border-gray-200 rounded p-1">
                        <button
                            type="button"
                            title="English"
                            aria-pressed={locale === 'en'}
                            onClick={() => setLocale('en')}
                            className={`w-6 h-6 flex items-center justify-center rounded text-xs transition-colors ${locale === 'en' ? 'bg-blue-100' : 'hover:bg-gray-100 opacity-50 grayscale hover:grayscale-0'}`}
                        >
                            🇬🇧
                        </button>
                        <button
                            type="button"
                            title="Nederlands"
                            aria-pressed={locale === 'nl'}
                            onClick={() => setLocale('nl')}
                            className={`w-6 h-6 flex items-center justify-center rounded text-xs transition-colors ${locale === 'nl' ? 'bg-blue-100' : 'hover:bg-gray-100 opacity-50 grayscale hover:grayscale-0'}`}
                        >
                            🇳🇱
                        </button>
                    </div>
                    <SignedInAs className="hidden sm:block lg:hidden xl:block" />
                </div>
            </header>

            {/* Altijd in de DOM (hidden als dicht), zodat aria-controls van de menuknop naar een bestaand element wijst. */}
            <div id="app-menu-mobile" hidden={!menuOpen} className="border-b border-gray-300 bg-gray-100 p-2 lg:hidden">
                <AppMenu pathname={pathname} idPrefix="menu-mobile" onNavigate={() => setMenuOpen(false)} />
            </div>

            <SubNav pathname={pathname} />

            <div className="flex flex-1 overflow-hidden">
                {/* MAIN CONTENT AREA: de volle breedte. */}
                <main className="min-w-0 flex-1 overflow-auto bg-gray-50 p-2 md:p-4 relative">
                    {showLegacyDemoChrome && (
                        <div className="mb-2 flex flex-wrap gap-2 text-xs">
                            <div className="rounded border border-gray-300 bg-white p-2 shadow-sm">
                                <div className="mb-1 font-bold text-blue-600">{t('incoming_shipment')}</div>
                                <p>{t('container_arriving')}</p>
                            </div>
                            <div className="rounded border border-gray-300 bg-white p-2 shadow-sm">
                                <div className="mb-1 font-bold text-red-600">{t('stock_warning')}</div>
                                <p>{t('avocados_expiring')}</p>
                            </div>
                        </div>
                    )}
                    {children}
                </main>
            </div>
            {showDemoAssistant && <DemoAssistant />}
        </div>
    );
}

export default function AdminLayout({ children }: { children: React.ReactNode }) {
    return (
        <LanguageProvider>
            <CurrentUserProvider>
                <AdminLayoutContent>{children}</AdminLayoutContent>
            </CurrentUserProvider>
        </LanguageProvider>
    );
}
