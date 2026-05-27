/* eslint-disable */
'use client';

import { useEffect, useState, useRef, use } from 'react';
import { useRouter } from 'next/navigation';
import { IncomingInvoice } from '../../../../../types/incomingInvoice';
import { IncomingInvoiceService } from '../../../../../services/incomingInvoiceService';

export default function InvoiceReviewPage({ params }: { params: Promise<{ id: string }> }) {
    // Unwrap params in Next.js 15
    const { id } = use(params);
    const router = useRouter();

    const [invoice, setInvoice] = useState<IncomingInvoice | null>(null);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);

    // Form State
    const [formData, setFormData] = useState<Partial<IncomingInvoice>>({});

    useEffect(() => {
        if (id) loadInvoice(Number(id));
    }, [id]);

    // Keyboard Shortcuts
    useEffect(() => {
        const handleKeyDown = (e: KeyboardEvent) => {
            // Escape to Reject/Flag
            if (e.key === 'Escape') {
                e.preventDefault();
                // handleReject(); // Optional: confirm before rejecting
            }
            // Ctrl+Enter to Approve
            if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
                e.preventDefault();
                handleApprove();
            }
        };

        window.addEventListener('keydown', handleKeyDown);
        return () => window.removeEventListener('keydown', handleKeyDown);
    }, [invoice]);

    async function loadInvoice(id: number) {
        try {
            const data = await IncomingInvoiceService.getById(id);
            setInvoice(data);
            setFormData(data);
        } catch (error) {
            console.error(error);
            alert('Failed to load invoice');
        } finally {
            setLoading(false);
        }
    }

    const handleChange = (field: keyof IncomingInvoice, value: any) => {
        setFormData(prev => ({ ...prev, [field]: value }));
    };

    const handleApprove = async () => {
        if (!invoice) return;
        setSaving(true);
        try {
            // First save any changes
            await IncomingInvoiceService.update(invoice.id, formData as IncomingInvoice);
            // Then approve
            await IncomingInvoiceService.approve(invoice.id);
            router.push('/incoming-invoices');
        } catch (error) {
            console.error(error);
            alert('Failed to approve invoice');
        } finally {
            setSaving(false);
        }
    };

    const handleReject = async () => {
        if (!invoice) return;
        if (!confirm('Are you sure you want to reject this invoice?')) return;
        setSaving(true);
        try {
            await IncomingInvoiceService.reject(invoice.id);
            router.push('/incoming-invoices');
        } catch (error) {
            console.error(error);
            alert('Failed to reject invoice');
        } finally {
            setSaving(false);
        }
    };

    if (loading) return <div className="flex items-center justify-center h-screen">Loading...</div>;
    if (!invoice) return <div className="flex items-center justify-center h-screen">Invoice not found</div>;

    const confidenceColor = invoice.reviewConfidence > 0.8 ? 'text-green-600' : invoice.reviewConfidence > 0.5 ? 'text-yellow-600' : 'text-red-600';

    return (
        <div className="flex h-screen bg-gray-100 overflow-hidden">
            {/* LEFT PANE: Document Viewer */}
            <div className="w-1/2 bg-gray-800 border-r border-gray-300 flex flex-col">
                <div className="p-4 bg-gray-900 text-white flex justify-between items-center">
                    <h2 className="text-sm font-semibold uppercase tracking-wide">Original Document</h2>
                    <span className="text-xs text-gray-400">{invoice.originalFilePath || 'No file attached'}</span>
                </div>
                <div className="flex-1 flex items-center justify-center bg-gray-700 text-gray-400">
                    {/* Placeholder for PDF Viewer */}
                    <div className="text-center">
                        <p className="text-lg mb-2">📄 Document View</p>
                        <p className="text-sm">(PDF Rendering Placeholder)</p>
                    </div>
                </div>
            </div>

            {/* RIGHT PANE: Data Extraction Form */}
            <div className="w-1/2 flex flex-col bg-white">
                {/* Header */}
                <div className="p-6 border-b border-gray-200 flex justify-between items-center bg-white sticky top-0 z-10">
                    <div>
                        <h1 className="text-2xl font-bold text-gray-900">Review Invoice #{invoice.id}</h1>
                        <p className="text-sm text-gray-500 mt-1">
                            Confidence: <span className={`font-bold ${confidenceColor}`}>{(invoice.reviewConfidence * 100).toFixed(0)}%</span>
                        </p>
                    </div>
                    <div className="space-x-3">
                        <button
                            onClick={handleReject}
                            className="px-4 py-2 border border-red-300 text-red-700 rounded hover:bg-red-50 text-sm font-medium focus:ring-2 focus:ring-red-500"
                            title="Press Esc"
                        >
                            Reject (Esc)
                        </button>
                        <button
                            onClick={handleApprove}
                            className="px-6 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 text-sm font-medium shadow-sm focus:ring-2 focus:ring-blue-500 focus:outline-none"
                            title="Press Ctrl+Enter"
                        >
                            Approve & Next (Ctrl+Enter)
                        </button>
                    </div>
                </div>

                {/* Scrollable Form Area */}
                <div className="flex-1 overflow-y-auto p-8">
                    <div className="max-w-2xl mx-auto space-y-6">

                        {/* Messages / Warnings */}
                        {invoice.detectedIssues && (
                            <div className="p-4 bg-yellow-50 border-l-4 border-yellow-400 text-yellow-700">
                                <p className="font-bold">AI Warnings:</p>
                                <p>{invoice.detectedIssues}</p>
                            </div>
                        )}

                        <div className="grid grid-cols-2 gap-6">
                            {/* Vendor Information */}
                            <div className="col-span-2 space-y-2">
                                <label className="block text-sm font-medium text-gray-700">Vendor Name</label>
                                <input
                                    type="text"
                                    value={formData.vendor?.name || ''}
                                    className="block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 p-2 border"
                                    onChange={(e) => {
                                        // Complex update for nested vendor object would be needed properly
                                        // For now, simpler handling or just showing it's readonly from this view for prototype
                                    }}
                                    readOnly // Making vendor read-only for MVP simplification
                                />
                            </div>

                            {/* Invoice Details */}
                            <div className="space-y-2">
                                <label className="block text-sm font-medium text-gray-700">Invoice Number</label>
                                <input
                                    type="text"
                                    autoFocus
                                    value={formData.invoiceNumber || ''}
                                    onChange={(e) => handleChange('invoiceNumber', e.target.value)}
                                    className="block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 p-2 border"
                                />
                            </div>

                            <div className="space-y-2">
                                <label className="block text-sm font-medium text-gray-700">Invoice Date</label>
                                <input
                                    type="date"
                                    value={formData.invoiceDate ? new Date(formData.invoiceDate).toISOString().split('T')[0] : ''}
                                    onChange={(e) => handleChange('invoiceDate', e.target.value)}
                                    className="block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 p-2 border"
                                />
                            </div>

                            <div className="space-y-2">
                                <label className="block text-sm font-medium text-gray-700">Total Amount</label>
                                <div className="relative rounded-md shadow-sm">
                                    <div className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3">
                                        <span className="text-gray-500 sm:text-sm">€</span>
                                    </div>
                                    <input
                                        type="number"
                                        step="0.01"
                                        value={formData.totalAmount || 0}
                                        onChange={(e) => handleChange('totalAmount', parseFloat(e.target.value))}
                                        className="block w-full rounded-md border-gray-300 pl-7 focus:border-blue-500 focus:ring-blue-500 p-2 border font-mono font-bold text-lg"
                                    />
                                </div>
                            </div>

                            <div className="space-y-2">
                                <label className="block text-sm font-medium text-gray-700">VAT Amount</label>
                                <div className="relative rounded-md shadow-sm">
                                    <div className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3">
                                        <span className="text-gray-500 sm:text-sm">€</span>
                                    </div>
                                    <input
                                        type="number"
                                        step="0.01"
                                        value={formData.vatAmount || 0}
                                        onChange={(e) => handleChange('vatAmount', parseFloat(e.target.value))}
                                        className="block w-full rounded-md border-gray-300 pl-7 focus:border-blue-500 focus:ring-blue-500 p-2 border"
                                    />
                                </div>
                            </div>
                        </div>

                        {/* Line Items Placeholder */}
                        <div className="mt-8 border-t pt-6">
                            <h3 className="text-lg font-medium text-gray-900 mb-4">Line Items</h3>
                            <div className="bg-gray-50 p-4 rounded text-center text-gray-500 text-sm">
                                Line item editing is available in detailed view.
                                <br />
                                <span className="text-xs">(Found {invoice.invoiceLines?.length || 0} lines)</span>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
}
