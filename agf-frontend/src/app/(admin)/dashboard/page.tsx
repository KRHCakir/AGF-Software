/* eslint-disable @typescript-eslint/no-explicit-any */
'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { DashboardService, DashboardData } from '../../../services/dashboardService';

export default function Home() {
  const [data, setData] = useState<DashboardData | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    DashboardService.getDashboardData()
      .then(setData)
      .catch(console.error)
      .finally(() => setLoading(false));
  }, []);

  if (loading) {
    return <div className="p-8 text-gray-500">Loading Dashboard...</div>;
  }

  if (!data) {
    return <div className="p-8 text-red-500">Failed to load Dashboard data. Is the backend running?</div>;
  }

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-bold text-gray-800">Dashboard</h1>

      {/* KPI CARDS */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
        <KPICard title="Total Sales Today" value={`€ ${data.kpis.totalSalesToday.toLocaleString()}`} change="Real-time" color="blue" />
        <KPICard title="Unpaid Invoices" value={data.kpis.unpaidInvoices.toString()} change="Action Needed" color="red" />
        <KPICard title="Shipments Arriving" value={data.kpis.shipmentsArriving.toString()} change="In 24h" color="green" />
        <KPICard title="Margin %" value={data.kpis.margin} change="Estimated" color="yellow" />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Recent Orders */}
        <div className="lg:col-span-2 bg-white border border-gray-200 rounded-lg shadow-sm p-4">
          <div className="flex justify-between items-center mb-4">
            <h2 className="font-bold text-gray-700">Recent Orders</h2>
            <Link href="/orders" className="text-blue-600 text-xs hover:underline">View All</Link>
          </div>
          <table className="w-full text-sm">
            <thead className="text-gray-500 border-b">
              <tr>
                <th className="text-left pb-2">Order #</th>
                <th className="text-left pb-2">Customer</th>
                <th className="text-left pb-2">Product</th>
                <th className="text-right pb-2">Amount</th>
                <th className="text-center pb-2">Status</th>
              </tr>
            </thead>
            <tbody className="divide-y">
              {data.recentOrders.length === 0 && (
                <tr>
                  <td colSpan={5} className="py-4 text-center text-gray-500">No recent orders found.</td>
                </tr>
              )}
              {data.recentOrders.map((order: any) => (
                <tr key={order.id}>
                  <td className="py-2">{order.orderNumber}</td>
                  <td>{order.customerName}</td>
                  <td>{order.productName}</td>
                  <td className="text-right">€ {order.amount.toFixed(2)}</td>
                  <td className="text-center">
                    <span className="bg-blue-100 text-blue-800 px-2 py-0.5 rounded-full text-xs">{order.status}</span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {/* Incoming Container Status */}
        <div className="bg-white border border-gray-200 rounded-lg shadow-sm p-4">
          <div className="flex justify-between items-center mb-4">
            <h2 className="font-bold text-gray-700">In Transit</h2>
            <Link href="/tracking" className="text-blue-600 text-xs hover:underline">Map</Link>
          </div>
          <div className="space-y-3">
            {data.shipments.length === 0 && (
              <div className="text-sm text-gray-500 text-center py-4">No shipments in transit.</div>
            )}
            {data.shipments.map((shipment: any) => (
              <ContainerStatus
                key={shipment.id}
                code={shipment.containerCode}
                origin={shipment.origin}
                eta={new Date(shipment.estimatedTimeOfArrival).toLocaleDateString()}
                status={shipment.status}
              />
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

function KPICard({ title, value, change, color }: { title: string, value: string, change: string, color: string }) {
  const colors = {
    blue: "border-l-4 border-blue-500",
    red: "border-l-4 border-red-500",
    green: "border-l-4 border-green-500",
    yellow: "border-l-4 border-yellow-500",
  };

  return (
    <div className={`bg-white p-4 rounded-lg shadow-sm ${colors[color as keyof typeof colors]}`}>
      <h3 className="text-gray-500 text-xs uppercase font-semibold">{title}</h3>
      <div className="flex justify-between items-end mt-1">
        <span className="text-2xl font-bold text-gray-800">{value}</span>
        <span className={`text-xs px-2 py-0.5 rounded bg-${color}-50 text-${color}-700 font-medium`}>
          {change}
        </span>
      </div>
    </div>
  );
}

function ContainerStatus({ code, origin, eta, status }: { code: string, origin: string, eta: string, status: string }) {
  return (
    <div className="flex items-center justify-between p-2 hover:bg-gray-50 rounded border border-transparent hover:border-gray-200 transition">
      <div>
        <div className="text-sm font-bold text-gray-800">{code}</div>
        <div className="text-xs text-gray-500">From {origin} • ETA: {eta}</div>
      </div>
      <div className="text-xs font-semibold bg-gray-100 px-2 py-1 rounded text-gray-600">
        {status}
      </div>
    </div>
  );
}
