/* eslint-disable @typescript-eslint/no-explicit-any */
'use client';

import { useState, useEffect } from 'react';
import { ProductService, Product } from '../services/productService';
import { InvoiceService } from '../services/invoiceService';
import { OrderService, CreateOrderDto } from '../services/orderService';
import { useTranslation } from '../i18n/LanguageContext';

interface AddOrderModalProps {
    isOpen: boolean;
    onClose: () => void;
    onSuccess: () => void;
}

export default function AddOrderModal({ isOpen, onClose, onSuccess }: AddOrderModalProps) {
    const { t } = useTranslation();
    const [products, setProducts] = useState<Product[]>([]);
    const [customers, setCustomers] = useState<any[]>([]);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);

    const [customerIdInput, setCustomerIdInput] = useState<string>('');

    // Product identification fields
    const [productInput, setProductInput] = useState<string>('');
    const [variantInput, setVariantInput] = useState<string>('');
    const [originInput, setOriginInput] = useState<string>('');
    const [sizeInput, setSizeInput] = useState<string>('');

    const [matchedProduct, setMatchedProduct] = useState<any>(null);
    const [quantity, setQuantity] = useState<string>('1');
    const [priceInput, setPriceInput] = useState<string>('');

    useEffect(() => {
        if (isOpen) {
            Promise.all([
                ProductService.getAll(),
                InvoiceService.getCustomers()
            ]).then(([productsData, customersData]) => {
                setProducts(productsData);
                setCustomers(customersData);
            }).catch(console.error).finally(() => setLoading(false));

            // Clean state on reopen
            setProductInput('');
            setVariantInput('');
            setOriginInput('');
            setSizeInput('');
            setPriceInput('');
            setQuantity('1');
        }
    }, [isOpen]);

    // Real-time matching logic
    useEffect(() => {
        if (!isOpen) return;

        const pLower = productInput.trim().toLowerCase();
        const vLower = variantInput.trim().toLowerCase();
        const oLower = originInput.trim().toLowerCase();
        const sLower = sizeInput.trim().toLowerCase();

        if (!pLower) {
            setMatchedProduct(null);
            return;
        }

        let foundMatch = null;

        // 1. Is the Product Input explicitly a shortcut code?
        for (const p of products) {
            for (const v of p.variants) {
                if (v.shortcutCode && v.shortcutCode.toLowerCase() === pLower) {
                    foundMatch = { ...v, parentName: p.name };
                    break;
                }
            }
            if (foundMatch) break;
        }

        // If it's a shortcut match, immediately explode it into the fields
        if (foundMatch && !vLower && !oLower && !sLower) {
            setProductInput(foundMatch.parentName);
            setVariantInput(foundMatch.name);
            setOriginInput(foundMatch.origin || '');
            setSizeInput(foundMatch.sizeCategory || '');
            setMatchedProduct(foundMatch);
            return;
        }

        // 2. Otherwise try to match exactly against the explicit text
        // (If they provided any of the fields, we match exactly. If a field was left blank by user, we assume it matches anything)
        foundMatch = null;
        for (const p of products) {
            if (p.name.toLowerCase() === pLower) {
                for (const v of p.variants) {
                    const variantMatches = !vLower || v.name.toLowerCase() === vLower;
                    const originMatches = !oLower || (v.origin || '').toLowerCase() === oLower;
                    const sizeMatches = !sLower || (v.sizeCategory || '').toLowerCase() === sLower;

                    if (variantMatches && originMatches && sizeMatches) {
                        foundMatch = { ...v, parentName: p.name };
                        break;
                    }
                }
            }
            if (foundMatch) break;
        }

        setMatchedProduct(foundMatch);

    }, [productInput, variantInput, originInput, sizeInput, products, isOpen]);

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();

        // 1. Zoek of de klant al bestaat in onze dropdown
        const customerMatch = customers.find(c => c.name.toLowerCase() === customerIdInput.toLowerCase().trim());

        // Als hij bestaat, pakken we het ID. Zo niet, pakken we de nieuwe ingevulde naam!
        const finalCustomerId = customerMatch ? customerMatch.id : null;
        const finalCustomerName = !customerMatch ? customerIdInput.trim() : undefined;

        if (!finalCustomerId && !finalCustomerName) {
            alert('Typ een klantnaam in aub.');
            return;
        }

        let finalProductVariantId = matchedProduct?.id;
        setSaving(true);

        // 2. Volautomatisch toevoegen als het product NIET in de database staat (ZONDER auto-shortcut)
        if (!finalProductVariantId) {
            try {
                const newVariant = await ProductService.quickAddShortcut({
                    productName: productInput || 'Onbekend Product',
                    variantName: variantInput,
                    origin: originInput,
                    sizeCategory: sizeInput,
                    shortcutCode: '' // <--- We laten deze nu bewust he-le-maal leeg!
                });

                finalProductVariantId = newVariant.variantId;
            } catch (error) {
                console.error("Fout bij auto-aanmaken product:", error);
                alert(t('add_product_error'));
                setSaving(false);
                return;
            }
        }

        // 3. Order daadwerkelijk opslaan
        try {
            const dto: CreateOrderDto = {
                customerId: finalCustomerId,     // Stuurt het ID als hij hem kent
                customerName: finalCustomerName, // Stuurt de tekst als het een nieuwe klant is
                lines: [
                    {
                        productVariantId: finalProductVariantId,
                        quantity: Number(quantity),
                        unitPrice: Number(priceInput)
                    }
                ]

            };

            await OrderService.create(dto);
            onSuccess();
        } catch (error) {
            console.error("Fout bij aanmaken order:", error);
            alert(t('order_create_error'));
        } finally {
            setSaving(false);
        }
    };

    if (!isOpen) return null;

    return (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
            <div className="bg-white rounded-xl shadow-2xl max-w-lg w-full max-h-[90vh] overflow-y-auto">
                <div className="px-6 py-4 border-b flex justify-between items-center bg-gray-50 rounded-t-xl">
                    <h2 className="text-xl font-bold text-gray-800">{t('new_order_title')}</h2>
                    <button onClick={onClose} className="text-gray-400 hover:text-gray-600">
                        <span className="text-2xl">&times;</span>
                    </button>
                </div>

                <form onSubmit={handleSubmit} className="p-6 space-y-6">
                    {loading ? (
                        <div className="text-center text-gray-500 py-4">Loading application data...</div>
                    ) : (
                        <>
                            <div className="space-y-4">
                                <div>
                                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('customer')}</label>
                                    <input
                                        type="text"
                                        list="customers-list"
                                        className="w-full border-gray-300 rounded-md shadow-sm focus:ring-blue-500 focus:border-blue-500 p-2 border font-bold"
                                        value={customerIdInput}
                                        onChange={(e) => setCustomerIdInput(e.target.value)}
                                        placeholder={t('type_customer_name')}
                                        required
                                    />
                                    <datalist id="customers-list">
                                        {customers.map(c => (
                                            <option key={c.id} value={c.name} />
                                        ))}
                                    </datalist>
                                </div>

                                <div className="grid grid-cols-2 gap-4 border p-4 bg-gray-50 rounded-lg shadow-inner">
                                    {/* Let op: col-span-2 in plaats van 4, zodat de titel mooi over de 2 kolommen spreidt */}
                                    <div className="col-span-2 pb-2 border-b mb-2">
                                        <h3 className="text-sm font-bold text-gray-700">
                                            {t('item_identification')} {matchedProduct && <span className="float-right text-green-600 bg-green-100 px-2 py-0.5 rounded text-xs">✓ {t('valid_match')} (ID: {matchedProduct.id})</span>}
                                        </h3>
                                    </div>

                                    <div className="col-span-1">
                                        <label className="block text-xs font-bold text-gray-700 mb-1">{t('product_or_shortcut')}</label>
                                        <input
                                            type="text"
                                            className={`w-full border-gray-300 rounded-md shadow-sm focus:ring-blue-500 focus:border-blue-500 p-2 border font-bold ${matchedProduct ? 'bg-blue-50 border-blue-200' : ''}`}
                                            placeholder={t('eg_avocado')}
                                            value={productInput}
                                            onChange={(e) => setProductInput(e.target.value)}
                                            required
                                        />
                                    </div>

                                    <div className="col-span-1">
                                        <label className="block text-xs font-bold text-gray-700 mb-1">{t('variant')}</label>
                                        <input
                                            type="text"
                                            className={`w-full border-gray-300 rounded-md shadow-sm focus:ring-blue-500 focus:border-blue-500 p-2 border font-bold ${matchedProduct ? 'bg-blue-50 border-blue-200' : ''}`}
                                            placeholder={t('eg_hass')}
                                            value={variantInput}
                                            onChange={(e) => setVariantInput(e.target.value)}
                                        />
                                    </div>

                                    <div className="col-span-1">
                                        <label className="block text-xs font-bold text-gray-700 mb-1">{t('origin')}</label>
                                        <input
                                            type="text"
                                            className={`w-full border-gray-300 rounded-md shadow-sm focus:ring-blue-500 focus:border-blue-500 p-2 border font-bold ${matchedProduct ? 'bg-blue-50 border-blue-200' : ''}`}
                                            placeholder={t('eg_peru')}
                                            value={originInput}
                                            onChange={(e) => setOriginInput(e.target.value)}
                                        />
                                    </div>

                                    <div className="col-span-1">
                                        <label className="block text-xs font-bold text-gray-700 mb-1">{t('size')}</label>
                                        <input
                                            type="text"
                                            className={`w-full border-gray-300 rounded-md shadow-sm focus:ring-blue-500 focus:border-blue-500 p-2 border font-bold ${matchedProduct ? 'bg-blue-50 border-blue-200' : ''}`}
                                            placeholder={t('eg_22')}
                                            value={sizeInput}
                                            onChange={(e) => setSizeInput(e.target.value)}
                                        />
                                    </div>
                                </div>

                                <div className="grid grid-cols-2 gap-4">
                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">{t('price')}</label>
                                        <input
                                            type="number"
                                            step="0.01"
                                            min="0"
                                            className="w-full border-gray-300 rounded-md shadow-sm focus:ring-blue-500 focus:border-blue-500 p-2 border font-bold"
                                            value={priceInput}
                                            onChange={(e) => setPriceInput(e.target.value)}
                                            placeholder={t('enter_price')}
                                            required
                                        />
                                    </div>

                                    <div>
                                        <label className="block text-sm font-medium text-gray-700 mb-1">{t('quantity')}</label>
                                        <input
                                            type="number"
                                            min="1"
                                            className="w-full border-gray-300 rounded-md shadow-sm focus:ring-blue-500 focus:border-blue-500 p-2 border font-bold"
                                            value={quantity}
                                            onChange={(e) => setQuantity(e.target.value)}
                                            required
                                        />
                                    </div>
                                </div>
                            </div>
                        </>
                    )}

                    <div className="pt-4 border-t flex justify-end space-x-3">
                        <button
                            type="button"
                            onClick={onClose}
                            className="px-4 py-2 border border-gray-300 text-gray-700 rounded-md hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-1"
                            disabled={saving}
                        >
                            {t('cancel')}
                        </button>
                        <button
                            type="submit"
                            className="px-6 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 font-medium focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-1 flex items-center shadow-sm disabled:opacity-50"
                            disabled={loading || saving}
                        >
                            {saving ? t('creating') : t('create_order')}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    );
}
