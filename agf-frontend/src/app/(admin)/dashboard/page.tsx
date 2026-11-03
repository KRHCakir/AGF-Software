'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useTranslation } from '../../../i18n/LanguageContext';
import { formatMoney } from '../../../lib/availability';
import { DashboardData, DashboardService } from '../../../services/dashboardService';
import { showLegacyDemoChrome } from '../../../lib/featureFlags';
import { NAV_GROUPS } from '../../../lib/navigation';

// Startpagina: ingang naar de schermen, per procesgroep zoals het menu. De oude kengetallen (deels vaste voorbeeldwaarden) staan standaard uit.
export default function Home() {
  const { t } = useTranslation();
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-800">{t('start_title')}</h1>
        <p className="text-sm text-gray-600">{t('start_subtitle')}</p>
      </div>
      {/* Dezelfde vier procesgroepen als het menu, in de volgorde van de keten: inkopen, voorraad, verkopen; stamgegevens apart. */}
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
        {NAV_GROUPS.map((group) => (
          <section key={group.id} aria-labelledby={`start-${group.id}`} className="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
            <h2 id={`start-${group.id}`} className="text-base font-semibold text-gray-900">{t(group.labelKey)}</h2>
            <ul className="mt-2 space-y-1 text-sm">
              {group.items.map((item) => (
                <li key={item.id}>
                  <Link href={item.href} className="font-medium text-blue-800 underline-offset-2 hover:underline focus:outline-none focus:ring-2 focus:ring-blue-500">{t(item.labelKey)}</Link>
                </li>
              ))}
            </ul>
          </section>
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
  const { t, locale } = useTranslation();
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
          <KPICard title="Total Sales Today" value={`€ ${formatMoney(data.kpis.totalSalesToday, null, locale)}`} color="blue" />
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
