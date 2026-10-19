'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useTranslation } from '../../../i18n/LanguageContext';
import { DashboardData, DashboardService } from '../../../services/dashboardService';
import { showLegacyDemoChrome } from '../../../lib/featureFlags';

// Startpagina: ingang naar de werkplekken. De oude kengetallen (deels vaste voorbeeldwaarden) staan standaard uit.
export default function Home() {
  const { t } = useTranslation();
  const workspaces = [
    { href: '/sales-orders', title: t('sales'), description: t('start_sales_desc'), icon: '🧾' },
    { href: '/supply', title: t('supply'), description: t('start_supply_desc'), icon: '📦' },
    { href: '/inspection', title: t('inspection'), description: t('start_inspection_desc'), icon: '🔍' },
    { href: '/availability', title: t('availability'), description: t('start_availability_desc'), icon: '📊' },
  ];
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-800">{t('start_title')}</h1>
        <p className="text-sm text-gray-600">{t('start_subtitle')}</p>
      </div>
      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-4 gap-4">
        {workspaces.map((workspace) => (
          <Link key={workspace.href} href={workspace.href}
            className="block rounded-lg border border-gray-200 bg-white p-4 shadow-sm hover:border-blue-300 hover:bg-blue-50/40 focus:outline-none focus:ring-2 focus:ring-blue-500">
            <div className="text-2xl" aria-hidden="true">{workspace.icon}</div>
            <h2 className="mt-2 text-base font-semibold text-gray-900">{workspace.title}</h2>
            <p className="mt-1 text-sm text-gray-600">{workspace.description}</p>
          </Link>
        ))}
      </div>
      <section aria-labelledby="start-legacy" className="rounded-lg border border-amber-200 bg-amber-50/60 p-4">
        <h2 id="start-legacy" className="text-sm font-semibold text-amber-950">{t('start_legacy_title')}</h2>
        <p className="mt-1 text-sm text-amber-950">{t('start_legacy_desc')}</p>
        <div className="mt-2 flex flex-wrap gap-3 text-sm">
          <Link href="/orders" className="font-medium text-amber-950 underline underline-offset-2">{t('orders_legacy_nav')}</Link>
          <Link href="/inventory" className="font-medium text-amber-950 underline underline-offset-2">{t('inventory_legacy_nav')}</Link>
          <Link href="/incoming-invoices" className="font-medium text-amber-950 underline underline-offset-2">{t('invoices_legacy_nav')}</Link>
          <Link href="/tracking" className="font-medium text-amber-950 underline underline-offset-2">{t('tracking_nav')}</Link>
        </div>
      </section>
      {showLegacyDemoChrome && <LegacyMetrics />}
    </div>
  );
}

// Alleen achter de vlag: de oude kengetallen en de legacy-lijsten, met een expliciete melding over hun herkomst.
function LegacyMetrics() {
  const { t } = useTranslation();
  const [data, setData] = useState<DashboardData | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    DashboardService.getDashboardData()
      .then(setData)
      .catch(() => setFailed(true));
  }, []);

  return (
    <section aria-labelledby="start-legacy-metrics" className="space-y-3">
      <h2 id="start-legacy-metrics" className="text-sm font-semibold text-gray-700">{t('start_legacy_metrics_title')}</h2>
      <p role="note" className="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900">{t('start_legacy_metrics_notice')}</p>
      {failed && <p className="text-sm text-red-700">{t('availability_error_network')}</p>}
      {data && (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          <KPICard title="Total Sales Today" value={`€ ${data.kpis.totalSalesToday.toLocaleString()}`} color="blue" />
          <KPICard title="Unpaid Invoices" value={data.kpis.unpaidInvoices.toString()} color="red" />
          <KPICard title="Shipments Arriving" value={data.kpis.shipmentsArriving.toString()} color="green" />
          <KPICard title="Margin %" value={data.kpis.margin} color="yellow" />
        </div>
      )}
    </section>
  );
}

function KPICard({ title, value, color }: { title: string, value: string, color: string }) {
  const colors = {
    blue: "border-l-4 border-blue-500",
    red: "border-l-4 border-red-500",
    green: "border-l-4 border-green-500",
    yellow: "border-l-4 border-yellow-500",
  };

  return (
    <div className={`bg-white p-4 rounded-lg shadow-sm ${colors[color as keyof typeof colors]}`}>
      <h3 className="text-gray-500 text-xs uppercase font-semibold">{title}</h3>
      <div className="mt-1 text-2xl font-bold text-gray-800">{value}</div>
    </div>
  );
}
