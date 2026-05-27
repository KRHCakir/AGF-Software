/* eslint-disable */
'use client';

import { useEffect, useState } from 'react';
import { IncomingInvoice } from '../../../types/incomingInvoice';
import { IncomingInvoiceService } from '../../../services/incomingInvoiceService';
import { useRouter } from 'next/navigation';

export default function IncomingInvoicesPage() {
    const [invoices, setInvoices] = useState<IncomingInvoice[]>([]);
    const [loading, setLoading] = useState(true);
    const [selectedIndex, setSelectedIndex] = useState(0);
    const router = useRouter();

    useEffect(() => {
        loadInvoices();
    }, []);

    // Keyboard navigation for grid
    useEffect(() => {
        const handleKeyDown = (e: KeyboardEvent) => {
            if (e.key === 'ArrowDown') {
                setSelectedIndex(prev => Math.min(prev + 1, invoices.length - 1));
            } else if (e.key === 'ArrowUp') {
                setSelectedIndex(prev => Math.max(prev - 1, 0));
            } else if (e.key === 'Enter') {
                if (invoices[selectedIndex]) {
                    router.push(`/incoming-invoices/${invoices[selectedIndex].id}/review`);
                }
            } else if (e.key === 'F5') {
                e.preventDefault();
                loadInvoices();
            }
        };
        window.addEventListener('keydown', handleKeyDown);
        return () => window.removeEventListener('keydown', handleKeyDown);
    }, [invoices, selectedIndex, router]);

    async function loadInvoices() {
        setLoading(true);
        try {
            const data = await IncomingInvoiceService.getAll();
            setInvoices(data);
        } catch (error) {
            console.error(error);
        } finally {
            setLoading(false);
        }
    }

    return (
        <div className="flex flex-col h-full">
            {/* Title Bar */}
            <div className="bg-[#000080] text-white px-2 py-1 font-bold flex justify-between items-center mb-1">
                <span>1. Incoming Overview</span>
                <span className="text-xs font-normal">[Insert] New</span>
            </div>

            {/* Grid */}
            <div className="flex-1 bg-white classic-inset overflow-auto">
                <table className="w-full border-collapse text-xs font-mono">
                    <thead className="bg-[#c0c0c0] sticky top-0">
                        <tr>
                            <th className="border border-gray-600 px-1 text-left w-24">Date</th>
                            <th className="border border-gray-600 px-1 text-left w-32">Number</th>
                            <th className="border border-gray-600 px-1 text-left">Vendor</th>
                            <th className="border border-gray-600 px-1 text-right w-24">Amount</th>
                            <th className="border border-gray-600 px-1 text-center w-20">Sts</th>
                            <th className="border border-gray-600 px-1 text-right w-12">Conf</th>
                        </tr>
                    </thead>
                    <tbody>
                        {invoices.map((invoice, index) => (
                            <tr
                                key={invoice.id}
                                className={`cursor-pointer ${index === selectedIndex ? 'bg-[#000080] text-white' : 'hover:bg-gray-100'}`}
                                onClick={() => {
                                    setSelectedIndex(index);
                                    router.push(`/incoming-invoices/${invoice.id}/review`);
                                }}
                            >
                                <td className="border-r border-gray-300 px-1 truncate">
                                    {invoice.invoiceDate ? new Date(invoice.invoiceDate).toLocaleDateString() : ''}
                                </td>
                                <td className="border-r border-gray-300 px-1 truncate">{invoice.invoiceNumber}</td>
                                <td className="border-r border-gray-300 px-1 truncate">{invoice.vendor?.name}</td>
                                <td className="border-r border-gray-300 px-1 text-right">
                                    {invoice.totalAmount.toFixed(2)}
                                </td>
                                <td className="border-r border-gray-300 px-1 text-center truncate">{invoice.status.substring(0, 8)}</td>
                                <td className="border-r border-gray-300 px-1 text-right">{(invoice.reviewConfidence * 100).toFixed(0)}%</td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>

            <div className="mt-1 text-xs text-black">
                {invoices.length} records found.
            </div>
        </div>
    );
}
