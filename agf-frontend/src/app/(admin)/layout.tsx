'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import '../globals.css';
import { LanguageProvider, useTranslation } from '../../i18n/LanguageContext';
import { showLegacyDemoChrome } from '../../lib/featureFlags';

function AdminLayoutContent({
    children,
}: {
    children: React.ReactNode;
}) {
    const { t, locale, setLocale } = useTranslation();

    return (
        <div className="flex flex-col min-h-dvh">
            {/* TOP HEADER */}
            <header className="bg-white border-b border-gray-300 h-12 flex items-center px-4 shadow-sm z-20">
                <div className="font-bold text-xl text-blue-800 mr-8 tracking-tight">AGF<span className="text-gray-600 font-normal">ERP</span></div>

                <nav className="flex space-x-1 h-full items-center">
                    <NavLink href="/dashboard" label={t('home')} />
                    <NavLink href="/sales-orders" label={t('sales')} />
                    <NavLink href="/supply" label={t('supply')} />
                    <NavLink href="/inspection" label={t('inspection')} />
                    <NavLink href="/availability" label={t('availability')} />
                    <NavLink href="/products" label={t('products')} />
                    <NavLink href="/reports" label={t('reports')} />
                    <NavLink href="/help" label={t('help')} />
                </nav>

                <div className="ml-auto flex items-center space-x-4 text-gray-500">
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
                    <span>{t('user')}: <strong>Admin</strong></span>
                </div>
            </header>

            <div className="flex flex-1 overflow-hidden">
                {/* LEFT SIDEBAR: snelkoppelingen naar de nieuwe werkplekken; geen oude modals meer. */}
                <aside className="w-56 bg-gray-100 border-r border-gray-300 flex flex-col p-2 space-y-4">
                    <div>
                        <h3 className="text-xs font-bold text-gray-500 uppercase mb-2 px-2">{t('workspaces')}</h3>
                        <nav className="flex flex-col space-y-1" aria-label={t('workspaces')}>
                            <SidebarLink href="/sales-orders" label={t('sidebar_new_sales_order')} icon="🧾" />
                            <SidebarLink href="/supply" label={t('sidebar_plan_supply')} icon="📅" />
                            <SidebarLink href="/supply#receive" label={t('sidebar_receive_pallet')} icon="📦" />
                            <SidebarLink href="/inspection" label={t('sidebar_inspection')} icon="🔍" />
                            <SidebarLink href="/availability" label={t('sidebar_availability')} icon="📊" />
                        </nav>
                    </div>

                    {/* Oude flows: duidelijk apart gezet; niets wordt gesynchroniseerd. Tracking is een niet-actieve placeholder. */}
                    <div>
                        <h3 className="text-xs font-bold text-gray-500 uppercase mb-2 px-2">{t('legacy_flows')}</h3>
                        <nav className="flex flex-col space-y-1 text-sm" aria-label={t('legacy_flows')}>
                            <SidebarLink href="/orders" label={t('orders_legacy_nav')} icon="🗂️" />
                            <SidebarLink href="/inventory" label={t('inventory_legacy_nav')} icon="🗂️" />
                            <SidebarLink href="/incoming-invoices" label={t('invoices_legacy_nav')} icon="🗂️" />
                            <SidebarLink href="/tracking" label={t('tracking_nav')} icon="⏸️" />
                        </nav>
                    </div>

                    {showLegacyDemoChrome && (
                        <div>
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
                <main className="flex-1 overflow-auto bg-gray-50 p-4 relative">
                    {children}
                </main>
            </div>
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

function NavLink({ href, label }: { href: string; label: string }) {
    const pathname = usePathname();
    const active = pathname === href || pathname.startsWith(href + '/');
    return (
        <Link href={href} aria-current={active ? 'page' : undefined} className={`px-3 h-8 flex items-center rounded-md text-sm font-medium transition-colors ${active ? 'bg-blue-100 text-blue-800' : 'text-gray-600 hover:bg-gray-100 hover:text-gray-900'}`}>
            {label}
        </Link>
    );
}

function SidebarLink({ href, label, icon }: { href: string; label: string; icon: string }) {
    return (
        <Link href={href} className="flex items-center w-full px-2 py-1.5 text-left text-sm text-gray-700 hover:bg-white hover:shadow-sm rounded transition-all">
            <span className="mr-2" aria-hidden="true">{icon}</span>
            {label}
        </Link>
    );
}
