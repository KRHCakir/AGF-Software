'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useEffect, useRef, useState } from 'react';
import '../globals.css';
import { LanguageProvider, useTranslation } from '../../i18n/LanguageContext';
import AppMenu from '../../components/AppMenu';
import DemoAssistant from '../../components/demo/DemoAssistant';
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
            {/* KOP: logo (naar Start), menuknop op smalle schermen, taal en gebruiker. Het menu zelf staat links (zie AppMenu). */}
            <header className="bg-white border-b border-gray-300 h-12 flex items-center px-4 shadow-sm z-20">
                <button ref={menuButton} type="button" className="mr-3 rounded border border-gray-300 px-2 py-1 text-sm font-medium text-gray-800 md:hidden focus:outline-none focus:ring-2 focus:ring-blue-500"
                    aria-expanded={menuOpen} aria-controls="app-menu-mobile" onClick={() => setMenuOpen((open) => !open)}>
                    {menuOpen ? '✕' : '☰'} {t('nav_menu')}
                </button>
                <Link href="/dashboard" className="font-bold text-xl text-blue-800 mr-4 md:mr-8 tracking-tight shrink-0">AGF<span className="text-gray-600 font-normal">ERP</span></Link>

                <div className="ml-auto flex shrink-0 items-center space-x-4 pl-2 text-gray-500">
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
                    <span className="hidden sm:inline">{t('user')}: <strong>Admin</strong></span>
                </div>
            </header>

            {menuOpen && (
                <div id="app-menu-mobile" className="border-b border-gray-300 bg-gray-100 p-2 md:hidden">
                    <AppMenu pathname={pathname} idPrefix="menu-mobile" onNavigate={() => setMenuOpen(false)} />
                </div>
            )}

            <div className="flex flex-1 overflow-hidden">
                {/* Het menu per proces; onder 768 px verborgen en via de menuknop bereikbaar. */}
                <aside className="hidden md:flex w-56 shrink-0 bg-gray-100 border-r border-gray-300 flex-col overflow-y-auto p-2">
                    <AppMenu pathname={pathname} idPrefix="menu" />

                    {showLegacyDemoChrome && (
                        <div className="mt-4">
                            <h3 className="text-xs font-bold text-gray-500 uppercase mb-2 px-2">{t('notifications')}</h3>
                            <div className="bg-white border border-gray-300 p-2 rounded text-xs shadow-sm">
                                <div className="font-bold text-blue-600 mb-1">{t('incoming_shipment')}</div>
                                <p>{t('container_arriving')}</p>
                            </div>
                            <div className="bg-white border border-gray-300 p-2 rounded text-xs shadow-sm mt-2">
                                <div className="font-bold text-red-600 mb-1">{t('stock_warning')}</div>
                                <p>{t('avocados_expiring')}</p>
                            </div>
                        </div>
                    )}
                </aside>

                {/* MAIN CONTENT AREA */}
                <main className="min-w-0 flex-1 overflow-auto bg-gray-50 p-2 md:p-4 relative">
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
            <AdminLayoutContent>{children}</AdminLayoutContent>
        </LanguageProvider>
    );
}
