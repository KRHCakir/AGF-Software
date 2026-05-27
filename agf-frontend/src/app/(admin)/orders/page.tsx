'use client';

import React, { useState, useEffect, useMemo } from 'react';
import { OrderService, OrderDto } from '../../../services/orderService';
import { useTranslation } from '../../../i18n/LanguageContext';
import AddOrderModal from '../../../components/AddOrderModal';

export default function OrdersPage() {
  const { t } = useTranslation();
  const [orders, setOrders] = useState<OrderDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [isModalOpen, setIsModalOpen] = useState(false);

  const [sortConfig, setSortConfig] = useState<{ key: string, direction: 'asc' | 'desc' }>({
    key: 'createdAt',
    direction: 'desc'
  });

  const loadOrders = async () => {
    setLoading(true);
    try {
      const data = await OrderService.getAll();
      setOrders(data);
    } catch (error) {
      console.error(error);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { loadOrders(); }, []);

  const handleGenerateInvoice = async (id: number) => {
    try {
      await OrderService.generateInvoice(id);
      alert(t('invoice_generated_success'));
      loadOrders();
    } catch (error) {
      alert(t('failed_generate_invoice'));
    }
  };

  const handleDeleteOrder = async (id: number) => {
    if (!window.confirm(t('confirm_delete_order'))) return;
    try {
      await OrderService.deleteOrder(id);
      alert(t('order_cancelled_success'));
      loadOrders();
    } catch (error) {
      alert(t('failed_delete_order'));
    }
  };

  // Vervang de oude sortedOrders door deze gefilterde versie:
  const sortedOrders = useMemo(() => {
    // Filter: laat alleen orders zien die NIET 'Invoiced' of 'Cancelled' zijn
    let activeOrders = orders.filter(o => o.status === 'Pending');

    let sortableItems = [...activeOrders];
    sortableItems.sort((a, b) => {
      let aValue: any = a[sortConfig.key as keyof OrderDto];
      let bValue: any = b[sortConfig.key as keyof OrderDto];

      if (sortConfig.key === 'products') {
        aValue = a.lines[0]?.productName || '';
        bValue = b.lines[0]?.productName || '';
      }

      if (aValue < bValue) return sortConfig.direction === 'asc' ? -1 : 1;
      if (aValue > bValue) return sortConfig.direction === 'asc' ? 1 : -1;
      return 0;
    });
    return sortableItems;
  }, [orders, sortConfig]);

  const requestSort = (key: string) => {
    let direction: 'asc' | 'desc' = 'asc';
    if (sortConfig.key === key && sortConfig.direction === 'asc') {
      direction = 'desc';
    }
    setSortConfig({ key, direction });
  };

  const getSortIndicator = (key: string) => {
    if (sortConfig.key !== key) return <span className="text-gray-300 ml-1">↕</span>;
    return sortConfig.direction === 'asc' ? <span className="text-blue-600 ml-1">↑</span> : <span className="text-blue-600 ml-1">↓</span>;
  };

  return (
    <div className="p-8 max-w-7xl mx-auto space-y-6">
      <div className="flex justify-between items-center">
        <h1 className="text-3xl font-black text-gray-900">{t('orders')}</h1>
        <button
          onClick={() => setIsModalOpen(true)}
          className="bg-blue-600 text-white px-6 py-2 rounded-lg font-bold hover:bg-blue-700 shadow-lg"
        >
          {t('new_order')}
        </button>
      </div>

      <div className="bg-white rounded-xl shadow-sm border border-gray-200 overflow-hidden">
        <table className="w-full text-left border-collapse">
          <thead className="bg-gray-50 text-[11px] uppercase text-gray-500 font-bold border-b">
            <tr>
              {['createdAt', 'id', 'customerName', 'products', 'totalAmount', 'status'].map((key) => (
                <th
                  key={key}
                  className="px-6 py-4 cursor-pointer hover:bg-gray-100 transition-colors"
                  onClick={() => requestSort(key)}
                >
                  <div className="flex items-center">
                    {t(key === 'createdAt' ? 'date' : key === 'customerName' ? 'customer' : key === 'products' ? 'products_table' : key === 'totalAmount' ? 'total_value' : key)}
                    {getSortIndicator(key)}
                  </div>
                </th>
              ))}
              <th className="px-6 py-4 text-gray-400">{t('action')}</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {loading ? (
              <tr><td colSpan={7} className="p-10 text-center text-gray-400">{t('loading_dots')}</td></tr>
            ) : sortedOrders.map(order => (
              <tr key={order.id} className="hover:bg-gray-50/50 transition-colors">
                <td className="px-6 py-4 text-sm text-gray-600">{new Date(order.createdAt).toLocaleDateString()}</td>
                <td className="px-6 py-4 font-mono font-bold text-gray-400">#{order.id}</td>
                <td className="px-6 py-4 font-semibold text-gray-800">{order.customerName}</td>
                <td className="px-6 py-4 text-sm text-gray-600">
                  {order.lines.map(l => `${l.quantity}x ${l.productName}`).join(', ')}
                </td>
                <td className="px-6 py-4 font-bold text-blue-700">
                  €{order.totalAmount.toLocaleString('nl-NL', { minimumFractionDigits: 2 })}
                </td>
                <td className="px-6 py-4">
                  <span className={`px-2 py-0.5 rounded-full text-[10px] font-black uppercase ${order.status === 'Invoiced' ? 'bg-green-100 text-green-700' :
                    order.status === 'Cancelled' ? 'bg-red-100 text-red-700' : 'bg-yellow-100 text-yellow-700'
                    }`}>
                    {t(order.status.toLowerCase())}
                  </span>
                </td>
                <td className="px-6 py-4">
                  <div className="flex gap-2 items-center">
                    {order.status === 'Pending' && (
                      <>
                        {/* Blauwe Generate Invoice knop */}
                        <button
                          onClick={() => handleGenerateInvoice(order.id)}
                          className="bg-blue-600 text-white text-[10px] px-3 py-1.5 rounded font-bold hover:bg-blue-700 uppercase flex items-center gap-1 shadow-sm"
                        >
                          <svg className="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" /></svg>
                          {t('generate_invoice')}
                        </button>

                        {/* Rode Prullenbak */}
                        <button
                          onClick={() => handleDeleteOrder(order.id)}
                          className="bg-red-100 text-red-600 p-1.5 rounded hover:bg-red-200 transition-colors"
                          title={t('cancel_order')}
                        >
                          <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                          </svg>
                        </button>
                      </>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <AddOrderModal
        isOpen={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        onSuccess={() => { setIsModalOpen(false); loadOrders(); }}
      />
    </div>
  );
}