'use client';

import { useState, useEffect } from 'react';
import { ReportsService } from '../../../services/reportsService';
import { ProductService, Product, ProductVariant } from '../../../services/productService';

export default function ReportsPage() {
    const [shipmentIdInput, setShipmentIdInput] = useState('');
    const [invoiceIdInput, setInvoiceIdInput] = useState('');

    const [traceResult, setTraceResult] = useState<any>(null);
    const [traceType, setTraceType] = useState<'forward' | 'backward' | null>(null);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    // Shortcut Manager State
    const [allVariants, setAllVariants] = useState<any[]>([]);
    const [selectedShortcutVariantId, setSelectedShortcutVariantId] = useState<string>('');
    const [shortcutCodeInput, setShortcutCodeInput] = useState<string>('');

    useEffect(() => {
        ProductService.getAll().then(products => {
            const variants = products.flatMap(p => p.variants.map(v => ({
                id: v.id,
                name: `${p.name} ${v.name}`,
                shortcutCode: v.shortcutCode
            })));
            setAllVariants(variants);
        }).catch(console.error);
    }, []);

    const handleSaveShortcut = async () => {
        try {
            await ProductService.updateShortcut(parseInt(selectedShortcutVariantId), shortcutCodeInput.trim() || null);
            alert('Shortcut mapped successfully!');
            // Re-fetch
            const products = await ProductService.getAll();
            const variants = products.flatMap(p => p.variants.map(v => ({
                id: v.id,
                name: `${p.name} ${v.name}`,
                shortcutCode: v.shortcutCode
            })));
            setAllVariants(variants);
            setShortcutCodeInput('');
            setSelectedShortcutVariantId('');
        } catch (e: any) {
            alert("Error: " + e.message);
        }
    };

    const handleForwardTrace = async () => {
        if (!shipmentIdInput) return;
        setLoading(true);
        setError(null);
        try {
            const data = await ReportsService.traceShipment(parseInt(shipmentIdInput));
            setTraceResult(data);
            setTraceType('forward');
        } catch (e: any) {
            setError(e.message || "Failed to trace shipment. Ensure the Batch ID exists.");
        } finally {
            setLoading(false);
        }
    };

    const handleBackwardTrace = async () => {
        if (!invoiceIdInput) return;
        setLoading(true);
        setError(null);
        try {
            const data = await ReportsService.traceInvoice(parseInt(invoiceIdInput));
            setTraceResult(data);
            setTraceType('backward');
        } catch (e: any) {
            setError(e.message || "Failed to trace invoice. Ensure the Invoice ID exists.");
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="p-8 max-w-7xl mx-auto space-y-8">
            <div>
                <h1 className="text-3xl font-black text-gray-900 mb-2">Data Intelligence & Traceability</h1>
                <p className="text-gray-600">Ensure 10-year audit compliance by tracing product origins forward (Recalls) or backwards (Quality Complaints).</p>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-8">
                {/* FORWARD TRACE CARD */}
                <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-200">
                    <div className="flex items-center space-x-3 mb-4 text-blue-600">
                        <span className="text-2xl">⏭️</span>
                        <h2 className="text-xl font-bold text-gray-800">Forward Trace (Batch Recall)</h2>
                    </div>
                    <p className="text-sm text-gray-500 mb-6">Enter a Supplier Shipment ID to see every customer who received product from this batch, and how much physical waste was dumped.</p>
                    <div className="flex space-x-3">
                        <input
                            type="number"
                            className="flex-1 p-3 border rounded-lg focus:ring-blue-500 font-bold"
                            placeholder="Shipment / Batch ID"
                            value={shipmentIdInput}
                            onChange={(e) => setShipmentIdInput(e.target.value)}
                        />
                        <button
                            onClick={handleForwardTrace}
                            disabled={loading || !shipmentIdInput}
                            className="bg-blue-600 hover:bg-blue-700 text-white font-bold py-3 px-6 rounded-lg shadow-md disabled:opacity-50 transition-colors"
                        >
                            Trace Forward
                        </button>
                    </div>
                </div>

                {/* BACKWARD TRACE CARD */}
                <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-200">
                    <div className="flex items-center space-x-3 mb-4 text-emerald-600">
                        <span className="text-2xl">⏮️</span>
                        <h2 className="text-xl font-bold text-gray-800">Backward Trace (Complaint)</h2>
                    </div>
                    <p className="text-sm text-gray-500 mb-6">Enter a Customer Invoice ID to map backward through the warehouse FIFO logic to definitively prove which Supplier Batch was delivered.</p>
                    <div className="flex space-x-3">
                        <input
                            type="number"
                            className="flex-1 p-3 border rounded-lg focus:ring-emerald-500 font-bold"
                            placeholder="Customer Invoice ID"
                            value={invoiceIdInput}
                            onChange={(e) => setInvoiceIdInput(e.target.value)}
                        />
                        <button
                            onClick={handleBackwardTrace}
                            disabled={loading || !invoiceIdInput}
                            className="bg-emerald-600 hover:bg-emerald-700 text-white font-bold py-3 px-6 rounded-lg shadow-md disabled:opacity-50 transition-colors"
                        >
                            Trace Backward
                        </button>
                    </div>
                </div>
            </div>

            {/* SHORTCUT MANAGER CARD
            <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-200">
                <div className="flex items-center space-x-3 mb-4 text-purple-600">
                    <span className="text-2xl">⚡</span>
                    <h2 className="text-xl font-bold text-gray-800">Shortcut Manager</h2>
                </div>
                <p className="text-sm text-gray-500 mb-6">Map shorthand codes to specific product variants for high-speed POS checkouts.</p>
                <div className="flex flex-col md:flex-row space-y-4 md:space-y-0 md:space-x-4">
                    <select
                        className="flex-1 p-3 border rounded-lg focus:ring-purple-500 text-sm font-bold bg-white"
                        value={selectedShortcutVariantId}
                        onChange={e => setSelectedShortcutVariantId(e.target.value)}
                    >
                        <option value="">-- Select Product Variant --</option>
                        {allVariants.map(v => (
                            <option key={v.id} value={v.id}>{v.name} {v.shortcutCode ? `(Alias: ${v.shortcutCode})` : ''}</option>
                        ))}
                    </select>
                    <input 
                        type="text" 
                        className="flex-1 p-3 border rounded-lg focus:ring-purple-500 font-bold font-mono"
                        placeholder="Type shortcut code (e.g. avohassper22)"
                        value={shortcutCodeInput}
                        onChange={(e) => setShortcutCodeInput(e.target.value)}
                    />
                    <button 
                        onClick={handleSaveShortcut}
                        disabled={!selectedShortcutVariantId || !shortcutCodeInput}
                        className="bg-purple-600 hover:bg-purple-700 text-white font-bold py-3 px-6 rounded-lg shadow-md disabled:opacity-50 transition-colors whitespace-nowrap"
                    >
                        Save Shortcut
                    </button>
                </div>
            </div> */}

            {error && (
                <div className="bg-red-50 border-l-4 border-red-500 p-4 text-red-700 font-bold">
                    {error}
                </div>
            )}

            {/* RESULTS PORTAL */}
            {traceResult && traceType === 'forward' && (
                <div className="bg-white rounded-xl shadow border border-gray-200 overflow-hidden mt-8 animate-fade-in">
                    <div className="bg-blue-900 text-white p-6">
                        <h2 className="text-2xl font-black">Shipment Recall Report: #{traceResult.shipmentId}</h2>
                        <div className="mt-4 grid grid-cols-3 gap-4 text-sm font-medium">
                            <div className="bg-blue-800 p-3 rounded">Associated Batches: {traceResult.totalBatches}</div>
                            <div className="bg-blue-800 p-3 rounded text-red-200">Total Wasted Units: {traceResult.totalWastedUnits}</div>
                        </div>
                    </div>

                    <div className="p-6 border-b border-gray-100 bg-gray-50">
                        <h3 className="font-bold text-gray-700 mb-2">Waste Breakdown</h3>
                        {traceResult.wastedReasons.length > 0 ? (
                            <div className="flex space-x-4">
                                {traceResult.wastedReasons.map((w: any, idx: number) => (
                                    <span key={idx} className="bg-red-100 text-red-800 px-3 py-1 rounded-full text-xs font-bold border border-red-200">
                                        {w.reason}: {w.count} units
                                    </span>
                                ))}
                            </div>
                        ) : (
                            <span className="text-gray-400 text-sm">No documented shrinkage for this batch.</span>
                        )}
                    </div>

                    <div className="p-0">
                        <table className="w-full text-left">
                            <thead className="bg-gray-100 text-gray-600 text-sm font-bold border-b">
                                <tr>
                                    <th className="p-4">Exposed Customer</th>
                                    <th className="p-4">Invoice #</th>
                                    <th className="p-4">Units Dispatched</th>
                                    <th className="p-4">Date Out</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-100">
                                {traceResult.customers.length > 0 ? traceResult.customers.map((c: any, i: number) => (
                                    <tr key={i} className="hover:bg-blue-50 transition-colors">
                                        <td className="p-4 font-bold text-gray-900">{c.customerName}</td>
                                        <td className="p-4 text-blue-600 font-mono">{c.invoiceNumber}</td>
                                        <td className="p-4 font-bold text-red-600">{c.quantityAllocated}x</td>
                                        <td className="p-4 text-gray-500 text-sm">{new Date(c.dateSold).toLocaleString()}</td>
                                    </tr>
                                )) : (
                                    <tr><td colSpan={4} className="p-8 text-center text-gray-400 italic">No customers have received stock from this batch yet.</td></tr>
                                )}
                            </tbody>
                        </table>
                    </div>
                </div>
            )}

            {traceResult && traceType === 'backward' && (
                <div className="bg-white rounded-xl shadow border border-gray-200 overflow-hidden mt-8 animate-fade-in">
                    <div className="bg-emerald-900 text-white p-6">
                        <h2 className="text-2xl font-black">Quality Complaint Trace: Invoice {traceResult.invoiceNumber}</h2>
                        <p className="text-emerald-200 mt-2 text-sm">Definitively mappings of physical batch origins dispatched to this client.</p>
                    </div>

                    <div className="p-0">
                        <table className="w-full text-left bg-white">
                            <thead className="bg-gray-100 text-gray-600 text-sm font-bold border-b">
                                <tr>
                                    <th className="p-4">Product DB ID</th>
                                    <th className="p-4">Quantity Supplied</th>
                                    <th className="p-4 font-black">Origin Container</th>
                                    <th className="p-4">Source Country</th>
                                    <th className="p-4 text-xs text-gray-400">System Batch Ref</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-100">
                                {traceResult.sources.length > 0 ? traceResult.sources.map((s: any, i: number) => (
                                    <tr key={i} className="hover:bg-emerald-50 transition-colors">
                                        <td className="p-4 font-bold text-gray-700">{s.productName}</td>
                                        <td className="p-4 font-bold text-emerald-600">{s.batchQuantity}x</td>
                                        <td className="p-4">
                                            <span className="bg-emerald-100 text-emerald-800 px-2 py-1 rounded font-bold border border-emerald-200">
                                                {s.containerCode}
                                            </span>
                                        </td>
                                        <td className="p-4 text-gray-600">{s.origin}</td>
                                        <td className="p-4 font-mono text-xs text-gray-400">#{s.inventoryBatchId}</td>
                                    </tr>
                                )) : (
                                    <tr><td colSpan={5} className="p-8 text-center text-gray-400 italic">No batch tracking blocks allocated. This may be an off-books sale or an invalid invoice.</td></tr>
                                )}
                            </tbody>
                        </table>
                    </div>
                </div>
            )}
        </div>
    );
}
