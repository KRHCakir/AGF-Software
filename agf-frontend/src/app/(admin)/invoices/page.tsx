'use client';

import LegacyNotice from '../../../components/LegacyNotice';

import React, { useEffect, useState } from "react";
import { useTranslation } from "../../../i18n/LanguageContext";

const API_BASE = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5197";

// Vorm van GET /api/Invoice (InvoiceController.GetInvoices).
interface InvoiceListItem {
  id: number;
  invoiceNumber: string;
  orderId: number | null;
  customerName: string;
  totalAmount: number;
  status: string;
  invoiceDate: string;
  dueDate: string | null;
  createdAt: string;
}

export default function InvoicesPage() {
  const { t } = useTranslation();
  const [invoices, setInvoices] = useState<InvoiceListItem[]>([]);
  const [loading, setLoading] = useState(false);

  const loadInvoices = () => {
    setLoading(true);
    fetch(`${API_BASE}/api/Invoice`)
      .then((r) => r.json())
      .then(setInvoices)
      .catch(console.error)
      .finally(() => setLoading(false));
  };

  useEffect(() => { loadInvoices(); }, []);

  return (
    <div className="p-8 max-w-7xl mx-auto space-y-6">
      <LegacyNotice noticeKey="invoices_legacy_notice" linkHref="/sales-orders" linkKey="orders_legacy_go_to_sales" />
      <div className="flex justify-between items-center">
        <h1 className="text-3xl font-black text-gray-900">{t('invoices_legacy_title')}</h1>
      </div>

      <div className="bg-white rounded-xl shadow-sm border border-gray-200 overflow-hidden">
        <table className="w-full text-left border-collapse">
          <thead className="bg-gray-50 text-[11px] uppercase text-gray-500 font-bold border-b">
            <tr>
              <th className="px-6 py-4">Invoice #</th>
              <th className="px-6 py-4">Customer</th>
              <th className="px-6 py-4">Amount</th>
              <th className="px-6 py-4">Date</th>
              <th className="px-6 py-4">Due</th>
              <th className="px-6 py-4">Status</th>
              <th className="px-6 py-4 text-right">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {loading ? (
              <tr><td colSpan={7} className="p-10 text-center text-gray-400 italic">Loading sales invoices...</td></tr>
            ) : invoices.length === 0 ? (
              <tr><td colSpan={7} className="p-10 text-center text-gray-400 italic">No sales invoices generated yet.</td></tr>
            ) : invoices.map((i) => (
              <tr key={i.id} className="hover:bg-gray-50/50 transition-colors">
                <td className="px-6 py-4 font-mono font-bold text-blue-600">{i.invoiceNumber}</td>
                <td className="px-6 py-4 font-semibold text-gray-800">{i.customerName}</td>
                <td className="px-6 py-4 font-bold text-gray-900">
                  € {i.totalAmount.toLocaleString('nl-NL', { minimumFractionDigits: 2 })}
                </td>
                <td className="px-6 py-4 text-sm text-gray-500">{new Date(i.invoiceDate).toLocaleDateString()}</td>
                <td className="px-6 py-4 text-sm text-gray-500">
                  <span className={new Date(i.dueDate ?? 0) < new Date() ? "text-red-500 font-bold" : ""}>
                    {new Date(i.dueDate ?? 0).toLocaleDateString()}
                  </span>
                </td>
                <td className="px-6 py-4">
                  <span className="px-2 py-0.5 rounded-full text-[10px] font-black uppercase bg-green-100 text-green-700">
                    {i.status}
                  </span>
                </td>
                <td className="px-6 py-4 text-right">
                  <a
                    className="inline-flex items-center gap-1 bg-blue-600 text-white text-[10px] px-3 py-1.5 rounded font-bold hover:bg-blue-700 uppercase shadow-sm transition-all"
                    href={`${API_BASE}/api/Invoice/${i.id}/pdf`}
                    target="_blank"
                    rel="noopener noreferrer"
                  >
                    <svg className="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M7 21h10a2 2 0 002-2V9.414a1 1 0 00-.293-.707l-5.414-5.414A1 1 0 0012.586 3H7a2 2 0 00-2 2v14a2 2 0 002 2z" /></svg>
                    PDF
                  </a>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}