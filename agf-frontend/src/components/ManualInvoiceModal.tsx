/* eslint-disable @typescript-eslint/no-explicit-any */
'use client';

import { useState, useEffect } from 'react';
import { IncomingInvoiceService } from '../services/incomingInvoiceService';
import { useTranslation } from '../i18n/LanguageContext';

export default function ManualInvoiceModal({ isOpen, onClose, onSuccess }: { isOpen: boolean, onClose: () => void, onSuccess: () => void }) {
    const { t } = useTranslation();
    const [vendors, setVendors] = useState<any[]>([]);
    const [vendorId, setVendorId] = useState(0);
    const [invoiceNumber, setInvoiceNumber] = useState('');
    const [invoiceDate, setInvoiceDate] = useState(new Date().toISOString().split('T')[0]);
    const [lines, setLines] = useState([{ description: '', quantity: 1, unitPrice: 0, vatPercentage: 21, productVariantId: null as number | null }]);
    const [loading, setLoading] = useState(false);

    useEffect(() => {
        if (isOpen) {
            IncomingInvoiceService.getVendors().then(data => {
                setVendors(data);
                if (data.length > 0) setVendorId(data[0].id);
            }).catch(err => console.error("Failed to load vendors", err));
        }
    }, [isOpen]);

    if (!isOpen) return null;

    const addLine = () => {
        setLines([...lines, { description: '', quantity: 1, unitPrice: 0, vatPercentage: 21, productVariantId: null }]);
    };

    const updateLine = (index: number, field: string, value: any) => {
        const newLines = [...lines];
        (newLines[index] as any)[field] = value;
        setLines(newLines);
    };

    const removeLine = (index: number) => {
        setLines(lines.filter((_, i) => i !== index));
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setLoading(true);
        try {
            await IncomingInvoiceService.createManual({
                vendorId,
                invoiceNumber,
                invoiceDate: new Date(invoiceDate).toISOString(),
                lines: lines.map(l => ({
                    ...l,
                    productVariantId: l.productVariantId ?? undefined
                }))
            });
            onSuccess();
            onClose();
        } catch (error) {
            alert(t('failed_manual_invoice') + error);
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
            <div className="bg-white rounded-lg shadow-xl w-full max-w-2xl max-h-[90vh] overflow-y-auto">
                <form onSubmit={handleSubmit} className="p-6">
                    <div className="flex justify-between items-center mb-6">
                        <h2 className="text-xl font-bold text-gray-800">{t('new_incoming_invoice')}</h2>
                        <button type="button" onClick={onClose} className="text-gray-500 hover:text-gray-700">✕</button>
                    </div>

                    <div className="grid grid-cols-2 gap-4 mb-4">
                        <div>
                            <label className="block text-sm font-medium text-gray-700">{t('vendor')}</label>
                            <select value={vendorId} onChange={e => setVendorId(Number(e.target.value))} className="mt-1 block w-full border border-gray-300 rounded-md p-2" required>
                                {vendors.map(v => (
                                    <option key={v.id} value={v.id}>{v.name}</option>
                                ))}
                            </select>
                        </div>
                        <div>
                            <label className="block text-sm font-medium text-gray-700">{t('invoice_number')}</label>
                            <input type="text" value={invoiceNumber} onChange={e => setInvoiceNumber(e.target.value)} className="mt-1 block w-full border border-gray-300 rounded-md p-2" required placeholder="INV-2024-..." />
                        </div>
                        <div>
                            <label className="block text-sm font-medium text-gray-700">{t('invoice_date')}</label>
                            <input type="date" value={invoiceDate} onChange={e => setInvoiceDate(e.target.value)} className="mt-1 block w-full border border-gray-300 rounded-md p-2" required />
                        </div>
                    </div>

                    <h3 className="text-sm font-bold text-gray-700 mb-2">{t('line_items')}</h3>
                    <div className="space-y-3 mb-4">
                        {lines.map((line, index) => (
                            <div key={index} className="flex gap-2 items-start bg-gray-50 p-2 rounded border">
                                <div className="flex-1">
                                    <input type="text" placeholder={t('description_regular')} value={line.description} onChange={e => updateLine(index, 'description', e.target.value)} className="w-full border rounded p-1 text-sm" required />
                                    <div className="flex gap-2 mt-1">
                                        <input type="number" placeholder={t('prod_var_id_opt')} value={line.productVariantId || ''} onChange={e => updateLine(index, 'productVariantId', e.target.value ? Number(e.target.value) : null)} className="w-1/2 border rounded p-1 text-xs" />
                                    </div>
                                </div>
                                <div className="w-16">
                                    <input type="number" placeholder={t('qty')} value={line.quantity} onChange={e => updateLine(index, 'quantity', Number(e.target.value))} className="w-full border rounded p-1 text-sm" required />
                                </div>
                                <div className="w-20">
                                    <input type="number" step="0.01" placeholder={t('price')} value={line.unitPrice} onChange={e => updateLine(index, 'unitPrice', Number(e.target.value))} className="w-full border rounded p-1 text-sm" required />
                                </div>
                                <div className="w-14">
                                    <input type="number" value={line.vatPercentage} onChange={e => updateLine(index, 'vatPercentage', Number(e.target.value))} className="w-full border rounded p-1 text-sm" />
                                </div>
                                <button type="button" onClick={() => removeLine(index)} className="text-red-500 hover:text-red-700 px-1">✕</button>
                            </div>
                        ))}
                        <button type="button" onClick={addLine} className="text-sm text-blue-600 hover:text-blue-800 font-medium">{t('add_item')}</button>
                    </div>

                    <div className="flex justify-end gap-3 pt-4 border-t">
                        <button type="button" onClick={onClose} className="px-4 py-2 text-gray-700 hover:bg-gray-100 rounded-md">{t('cancel')}</button>
                        <button type="submit" disabled={loading} className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 disabled:opacity-50">
                            {loading ? t('saving') : t('create_invoice')}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    );
}
