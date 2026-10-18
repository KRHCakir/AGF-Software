'use client';

import Link from 'next/link';
import { useState } from 'react';
import { usePathname } from 'next/navigation';
import '../globals.css';
import ManualInvoiceModal from '../../components/ManualInvoiceModal';
import QuickSellModal from '../../components/QuickSellModal';
import AddOrderModal from '../../components/AddOrderModal';
import { LanguageProvider, useTranslation } from '../../i18n/LanguageContext';

function AdminLayoutContent({
    children,
}: {
    children: React.ReactNode;
}) {
    const [isInvoiceModalOpen, setIsInvoiceModalOpen] = useState(false);
    const [isSellModalOpen, setIsSellModalOpen] = useState(false);
    const [isOrderModalOpen, setIsOrderModalOpen] = useState(false);

    const { t, locale, setLocale } = useTranslation();

    return (
        <div className="flex flex-col min-h-dvh">
            {/* TOP HEADER */}
            <header className="bg-white border-b border-gray-300 h-12 flex items-center px-4 shadow-sm z-20">
                <div className="font-bold text-xl text-blue-800 mr-8 tracking-tight">AGF<span className="text-gray-600 font-normal">ERP</span></div>

                <nav className="flex space-x-1 h-full items-center">
                    <NavLink href="/dashboard" label={t('home')} />
                    <NavLink href="/orders" label={t('orders')} />
                    <NavLink href="/products" label={t('products')} />
                    <NavLink href="/incoming-invoices" label={t('invoices')} />
                    <NavLink href="/tracking" label={t('tracking')} />
                    <NavLink href="/inventory" label={t('inventory')} />
                    <NavLink href="/availability" label={t('availability')} />
                    <NavLink href="/reports" label={t('reports')} />
                    <NavLink href="/help" label={t('help')} />
                </nav>

                <div className="ml-auto flex items-center space-x-4 text-gray-500">
                    <div className="flex items-center space-x-1 border border-gray-200 rounded p-1">
                        <button 
                            title="English"
                            onClick={() => setLocale('en')} 
                            className={`w-6 h-6 flex items-center justify-center rounded text-xs transition-colors ${locale === 'en' ? 'bg-blue-100' : 'hover:bg-gray-100 opacity-50 grayscale hover:grayscale-0'}`}
                        >
                            🇬🇧
                        </button>
                        <button 
                            title="Nederlands"
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
                {/* LEFT SIDEBAR */}
                <aside className="w-56 bg-gray-100 border-r border-gray-300 flex flex-col p-2 space-y-4">
                    {/* Section 1: Quick Actions */}
                    <div>
                        <h3 className="text-xs font-bold text-gray-500 uppercase mb-2 px-2">{t('quick_actions')}</h3>
                        <div className="flex flex-col space-y-1">
                            <SidebarButton label={t('new_sale_quick')} icon="⚡" onClick={() => setIsSellModalOpen(true)} />
                            <SidebarButton label={t('new_order_std')} icon="📦" onClick={() => setIsOrderModalOpen(true)} />
                            <SidebarButton label={t('add_invoice')} icon="📄" onClick={() => setIsInvoiceModalOpen(true)} />
                            <SidebarButton label={t('track_container')} icon="🚢" />
                        </div>
                    </div>

                    {/* Section 2: Notifications */}
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
                </aside>

                {/* MAIN CONTENT AREA */}
                <main className="flex-1 overflow-auto bg-gray-50 p-4 relative">
                    {children}

                    {/* AI BUBBLE */}
                    <div className="absolute bottom-6 right-6 w-12 h-12 bg-blue-600 rounded-full shadow-lg flex items-center justify-center text-white cursor-pointer hover:bg-blue-700 transition-transform hover:scale-105 z-50">
                        <span className="text-xl">🤖</span>
                    </div>
                </main>
            </div>

            {/* MODALS */}
            {isInvoiceModalOpen && (
                <ManualInvoiceModal
                    isOpen={isInvoiceModalOpen}
                    onClose={() => setIsInvoiceModalOpen(false)}
                    onSuccess={() => {
                        alert(t('invoice_created')); // Improve: Toast or global refresh
                        setIsInvoiceModalOpen(false);
                    }}
                />
            )}

            {isSellModalOpen && (
                <QuickSellModal
                    isOpen={isSellModalOpen}
                    onClose={() => setIsSellModalOpen(false)}
                    onSuccess={() => {
                        alert(t('sale_completed'));
                        setIsSellModalOpen(false);
                    }}
                />
            )}

            {isOrderModalOpen && (
                <AddOrderModal
                    isOpen={isOrderModalOpen}
                    onClose={() => setIsOrderModalOpen(false)}
                    onSuccess={() => {
                        alert(t('order_created_success'));
                        setIsOrderModalOpen(false);
                    }}
                />
            )}
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
        <Link href={href} className={`px-4 h-8 flex items-center rounded-md text-sm font-medium transition-colors ${active ? 'bg-blue-100 text-blue-800' : 'text-gray-600 hover:bg-gray-100 hover:text-gray-900'}`}>
            {label}
        </Link>
    );
}

function SidebarButton({ label, icon, onClick }: { label: string, icon: string, onClick?: () => void }) {
    return (
        <button onClick={onClick} className="flex items-center w-full px-2 py-1.5 text-left text-gray-700 hover:bg-white hover:shadow-sm rounded transition-all">
            <span className="mr-2">{icon}</span>
            {label}
        </button>
    );
}
