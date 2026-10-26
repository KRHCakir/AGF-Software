/* eslint-disable */
'use client';

import { useState, useRef, use, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { salesService, ParseOrderResponse, ParsedOrderItem } from '@/services/salesService';
import { ProductService, Product } from '@/services/productService';
import { InvoiceService } from '@/services/invoiceService';
import { InventoryService, Shipment } from '@/services/inventoryService';
import { CustomerService } from '@/services/customerService';
import { formatMoney } from '@/lib/availability';
import Select from 'react-select';
import CreatableSelect from 'react-select/creatable';

type FastSaleVariant = {
    id: number;
    name: string;
    price: number;
    shortcutCode?: string;
};

export default function FastSalesPage({ params }: { params: Promise<{ sellerId: string }> }) {
    const { sellerId } = use(params);
    const router = useRouter();

    const [inputText, setInputText] = useState('');
    const [isAnalyzing, setIsAnalyzing] = useState(false);
    const [cartItems, setCartItems] = useState<ParsedOrderItem[]>([]);
    const [customerInput, setCustomerInput] = useState(''); // Free text or lookup
    const [isSubmitting, setIsSubmitting] = useState(false);
    const [successMessage, setSuccessMessage] = useState<string | null>(null);
    const [allProducts, setAllProducts] = useState<Product[]>([]);
    const [allCustomers, setAllCustomers] = useState<any[]>([]);
    const [allShipments, setAllShipments] = useState<Shipment[]>([]);
    const [customerStanding, setCustomerStanding] = useState<any>(null);

    useEffect(() => {
        Promise.all([
            ProductService.getAll(),
            InvoiceService.getCustomers(),
            InventoryService.getShipments()
        ]).then(([productsData, customersData, shipmentsData]) => {
            setAllProducts(productsData);
            setAllCustomers(customersData);
            setAllShipments(shipmentsData);
        }).catch(console.error);
    }, []);

    // Bekende bug (apart vervolg-PR): shortcutCode wordt hieronder niet gemapt, dus shortcut-matching vindt nooit iets.
    const allVariants: FastSaleVariant[] = allProducts.flatMap(p =>
        p.variants.map(v => ({
            id: v.id,
            name: `${p.name} ${v.name}`,
            price: v.defaultPrice
        }))
    );

    useEffect(() => {
        const checkStanding = async () => {
            if (!customerInput) {
                setCustomerStanding(null);
                return;
            }
            const match = allCustomers.find(c => c.name === customerInput);
            if (match) {
                try {
                    const standing = await CustomerService.getStanding(match.id);
                    setCustomerStanding(standing);
                } catch (e) {
                    console.error("Failed to fetch customer standing", e);
                }
            } else {
                setCustomerStanding(null);
            }
        };
        checkStanding();
    }, [customerInput, allCustomers]);

    const handleCommandBarEnter = async (e: React.KeyboardEvent<HTMLInputElement>) => {
        if (e.key === 'Enter') {
            e.preventDefault();
            if (!inputText.trim()) return;

            setIsAnalyzing(true);
            try {
                // Zero-latency local parse
                const text = inputText.trim();
                const parts = text.split(/\s+/);
                let matchedVariant: any = null;
                let qty = 1;
                let extPrice: number | null = null;
                let isMatched = false;

                // Case 1: [qty] [shortcut] [price?]
                if (parts.length >= 2 && !isNaN(Number(parts[0]))) {
                    const potentialShortcut = parts[1].toLowerCase();
                    const variant = allVariants.find(v => v.shortcutCode && v.shortcutCode.toLowerCase() === potentialShortcut);
                    if (variant) {
                        qty = Number(parts[0]);
                        matchedVariant = variant;
                        if (parts.length >= 3 && !isNaN(Number(parts[2]))) {
                            extPrice = Number(parts[2]);
                        }
                        isMatched = true;
                    }
                } 
                // Case 2: [shortcut] [qty] [price?]
                else if (parts.length >= 2 && !isNaN(Number(parts[1]))) {
                    const potentialShortcut = parts[0].toLowerCase();
                    const variant = allVariants.find(v => v.shortcutCode && v.shortcutCode.toLowerCase() === potentialShortcut);
                    if (variant) {
                        qty = Number(parts[1]);
                        matchedVariant = variant;
                        if (parts.length >= 3 && !isNaN(Number(parts[2]))) {
                            extPrice = Number(parts[2]);
                        }
                        isMatched = true;
                    }
                }

                if (isMatched) {
                    const newItem: ParsedOrderItem = {
                        originalTextSegment: text,
                        productVariantId: matchedVariant.id,
                        productName: matchedVariant.name,
                        quantity: qty,
                        unit: 'Units',
                        estimatedPrice: extPrice !== null ? extPrice : matchedVariant.price,
                        confidence: 1.0,
                        currentStock: 0 // Re-fetched remotely asynchronously later if needed
                    };
                    setCartItems(prev => [...prev, newItem]);
                    setInputText('');
                } else {
                    // Fallback to Async NLP
                    const result = await salesService.parseOrder(inputText);
                    setCartItems(prev => [...prev, ...result.items]);
                    setInputText('');
                }
            } catch (error) {
                console.error(error);
                alert('Failed to analyze text');
            } finally {
                setIsAnalyzing(false);
            }
        }
    };

    const handleConfirm = async () => {
        if (cartItems.length === 0) return;

        setIsSubmitting(true);
        try {
            const itemsToConfirm = cartItems
                .filter(i => i.productVariantId !== null)
                .map(i => ({
                    productVariantId: i.productVariantId!,
                    quantity: i.quantity,
                    agreedPrice: i.estimatedPrice || undefined, // Could be editable in UI
                    targetShipmentId: (i as any).targetShipmentId || undefined
                }));

            if (itemsToConfirm.length === 0) {
                alert("No valid product variants found to order.");
                setIsSubmitting(false);
                return;
            }

            if (!customerInput.trim()) {
                alert("Please enter a customer.");
                setIsSubmitting(false);
                return;
            }

            const matchedCustomer = allCustomers.find(c => c.name.toLowerCase().trim() === customerInput.toLowerCase().trim());
            const finalCustomerId = matchedCustomer ? matchedCustomer.id : null;
            const finalCustomName = matchedCustomer ? null : customerInput.trim();

            const orderIds = await salesService.confirmOrder(finalCustomerId, finalCustomName, itemsToConfirm);

            setSuccessMessage(`Orders Created: ${orderIds.join(', ')}`);
            setCartItems([]);
            setInputText('');
            // Optional: Redirect or Reset
        } catch (error) {
            console.error(error);
            alert('Failed to submit order');
        } finally {
            setIsSubmitting(false);
        }
    };

    const handleAddRow = () => {
        const newItem: ParsedOrderItem = {
            originalTextSegment: 'Manual Entry',
            productVariantId: null,
            productName: null,
            quantity: 1,
            unit: 'Units',
            estimatedPrice: null,
            confidence: 1.0,
            currentStock: 0
        };
        setCartItems(prev => [...prev, newItem]);
    };

    const handleVariantChange = (index: number, variantId: string) => {
        const newItems = [...cartItems];
        const selectedId = Number(variantId);
        
        let newVariant = null;
        for (const p of allProducts) {
            const match = p.variants.find(v => v.id === selectedId);
            if (match) {
                newVariant = { name: `${p.name} ${match.name}`, price: match.defaultPrice, id: match.id, shortcutCode: match.shortcutCode };
                break;
            }
        }

        if (newVariant) {
            newItems[index] = {
                ...newItems[index],
                productVariantId: selectedId,
                productName: newVariant.name,
                estimatedPrice: newVariant.price,
                currentStock: 0 
            };
            setCartItems(newItems);
        }
    };

    const handleQuantityChange = (index: number, newQty: number) => {
        if (newQty < 1) return;
        const newItems = [...cartItems];
        newItems[index].quantity = newQty;
        setCartItems(newItems);
    };

    const handlePriceChange = (index: number, newPrice: number | null) => {
        if (newPrice !== null && newPrice < 0) return;
        const newItems = [...cartItems];
        newItems[index].estimatedPrice = newPrice;
        setCartItems(newItems);
    };

    const handleTargetShipmentChange = (index: number, shipmentId: string) => {
        const newItems = [...cartItems];
        (newItems[index] as any).targetShipmentId = shipmentId === "" ? undefined : parseInt(shipmentId);
        setCartItems(newItems);
    };

    const handleRemoveItem = (index: number) => {
        setCartItems(prev => prev.filter((_, i) => i !== index));
    };

    return (
        <div className="min-h-screen bg-gray-50 flex flex-col p-4 pb-24">
            {/* Header */}
            <div className="flex justify-between items-center mb-6">
                <div className="flex items-center space-x-4">
                    <Link href="/sales" className="text-gray-500 hover:text-gray-800">
                        ← Back
                    </Link>
                    <h1 className="text-2xl font-bold">Fast Sales Entry</h1>
                    <span className="bg-blue-100 text-blue-800 text-xs font-semibold px-2.5 py-0.5 rounded">Seller ID: {sellerId}</span>
                </div>
                <div className="flex items-center space-x-2 relative z-50">
                    <span className="text-sm text-gray-600 font-medium whitespace-nowrap">Customer: </span>
                    <CreatableSelect
                        instanceId="customer-select"
                        isClearable
                        options={allCustomers.map(c => ({ value: c.name, label: c.name }))}
                        onChange={(newValue: any) => setCustomerInput(newValue ? newValue.value : '')}
                        value={customerInput ? { value: customerInput, label: customerInput } : null}
                        className="w-64 text-sm font-bold text-gray-900 shadow-sm"
                        placeholder="Search or Create..."
                        styles={{
                            control: (base) => ({ ...base, borderColor: '#d1d5db', '&:hover': { borderColor: '#3b82f6' } })
                        }}
                    />
                </div>
            </div>

            {customerStanding && customerStanding.isHighRisk && (
                <div className="bg-red-600 text-white p-4 rounded-lg shadow-md mb-6 animate-pulse flex items-center shadow-red-500/50">
                    <span className="text-3xl mr-4">⚠️</span>
                    <div>
                        <h3 className="font-black text-xl tracking-wide uppercase">Credit Limit Warning</h3>
                        <p className="font-bold text-red-100">
                            This customer has {customerStanding.overdueInvoicesCount} OVERDUE invoices! Do not issue credit without manager approval.
                        </p>
                    </div>
                </div>
            )}

            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 flex-grow">
                {/* INPUT SECTION */}
                <div className="flex flex-col space-y-4">
                    <div className="bg-gradient-to-r from-blue-600 to-indigo-700 p-6 rounded-lg shadow-lg flex-grow flex flex-col text-white">
                        <label className="block text-sm font-bold mb-2">
                            ⚡ High-Speed Command Bar
                        </label>
                        <p className="text-xs text-blue-100 mb-4 tracking-wide font-medium">
                            Syntaxes: <code className="bg-black/20 px-1 py-0.5 rounded">[Qty] [Shortcut] [Price]</code> or <code className="bg-black/20 px-1 py-0.5 rounded">[Shortcut] [Qty]</code>
                        </p>
                        
                        <input
                            type="text"
                            autoFocus
                            className="flex-grow w-full p-4 border-none rounded-lg shadow-inner focus:ring-4 focus:ring-blue-300 text-2xl font-mono text-gray-900 placeholder-gray-400"
                            placeholder="Type fast & hit Enter..."
                            value={inputText}
                            onChange={(e) => setInputText(e.target.value)}
                            onKeyDown={handleCommandBarEnter}
                            disabled={isAnalyzing}
                        />

                        {isAnalyzing && (
                            <div className="mt-4 flex items-center justify-center animate-pulse">
                                <span className="font-bold text-sm tracking-widest text-blue-200">ANALYZING...</span>
                            </div>
                        )}
                    </div>
                </div>

                {/* OUTPUT/PREVIEW SECTION */}
                <div className="bg-white p-4 rounded-lg shadow-sm flex flex-col">
                    <h2 className="text-lg font-bold text-gray-800 mb-4 border-b pb-2">Order Preview</h2>

                    {successMessage && (
                        <div className="bg-green-100 text-green-800 p-4 rounded-lg mb-4 text-center font-medium animate-pulse">
                            ✅ {successMessage}
                        </div>
                    )}

                    {!successMessage && cartItems.length === 0 && (
                        <div className="flex-grow flex items-center justify-center text-gray-400 flex-col">
                            <span className="text-4xl mb-2">🛒</span>
                            <p>Draft items will appear here</p>
                        </div>
                    )}

                    {cartItems.length > 0 && (
                        <>
                            <div className="flex-grow overflow-y-auto space-y-2 mb-4">
                                {cartItems.map((item, idx) => (
                                    <div key={idx} className={`p-3 rounded-lg border flex flex-col space-y-2 ${item.productVariantId ? 'border-green-200 bg-green-50' : 'border-blue-200 bg-blue-50'}`}>
                                        <div className="flex justify-between items-center">
                                            <div className="flex items-center space-x-3 w-2/3">
                                                <input 
                                                    type="number" 
                                                    min="1"
                                                    value={item.quantity}
                                                    onChange={(e) => handleQuantityChange(idx, parseInt(e.target.value) || 1)}
                                                    className={`w-14 h-10 rounded text-center font-bold outline-none border focus:ring-2 focus:ring-blue-500 ${item.productVariantId ? 'bg-white border-green-300' : 'bg-white border-blue-300'}`}
                                                />
                                                <div className="flex-1 w-full relative">
                                                    {item.productVariantId ? (
                                                        <div className="font-bold text-gray-800 flex items-center space-x-2">
                                                            <span>{item.productName || item.originalTextSegment}</span>
                                                            <button 
                                                                onClick={() => {
                                                                    const newItems = [...cartItems];
                                                                    newItems[idx].productVariantId = null;
                                                                    newItems[idx].productName = null;
                                                                    setCartItems(newItems);
                                                                }}
                                                                className="text-xs text-red-500 hover:text-red-700"
                                                                title="Clear Product"
                                                            >
                                                                ✖
                                                            </button>
                                                        </div>
                                                    ) : (
                                                        <div className="w-full">
                                                            <input 
                                                                type="text"
                                                                placeholder="Type shortcut or variant..."
                                                                className="w-full text-sm font-bold text-gray-900 border rounded p-2 focus:ring-blue-500 border-blue-300 shadow-sm"
                                                                value={item.originalTextSegment === 'Manual Entry' ? '' : item.originalTextSegment}
                                                                onChange={(e) => {
                                                                    const val = e.target.value;
                                                                    const newItems = [...cartItems];
                                                                    newItems[idx].originalTextSegment = val;
                                                                    
                                                                    const lowerVal = val.toLowerCase().trim();
                                                                    const match = allVariants.find(v => v.shortcutCode && v.shortcutCode.toLowerCase() === lowerVal);
                                                                    if (match) {
                                                                        newItems[idx].productVariantId = match.id;
                                                                        newItems[idx].productName = match.name;
                                                                        newItems[idx].estimatedPrice = null; // Forces user to type price
                                                                        newItems[idx].currentStock = 0;
                                                                        newItems[idx].originalTextSegment = val; 
                                                                    }
                                                                    setCartItems(newItems);
                                                                }}
                                                            />
                                                        </div>
                                                    )}
                                                    <div className="text-xs text-gray-500 mt-1">
                                                        {item.originalTextSegment === 'Manual Entry' ? 'Type alias to resolve natively' : `Raw: "${item.originalTextSegment}"`} {item.productVariantId && `• Confidence: ${(item.confidence * 100).toFixed(0)}%`}
                                                    </div>
                                                    
                                                    {item.productVariantId && (
                                                        <div className="mt-1">
                                                            <button 
                                                                onClick={async () => {
                                                                    const code = window.prompt("Save a fast-typing shortcut for this item (e.g. 'avo22'). It will be remembered forever:");
                                                                    if (code) {
                                                                        try {
                                                                            await ProductService.updateShortcut(item.productVariantId!, code);
                                                                            alert(`✅ Shortcut '${code}' saved successfully! You can now use it in the Command Bar.`);
                                                                            // Reload products in background to cache the new shortcut
                                                                            ProductService.getAll().then(setAllProducts);
                                                                        } catch(e: any) {
                                                                            alert("Error saving shortcut: " + e.message);
                                                                        }
                                                                    }
                                                                }}
                                                                className="text-xs font-bold text-purple-600 bg-purple-50 hover:bg-purple-100 border border-purple-200 px-2 py-0.5 rounded transition-colors"
                                                            >
                                                                💾 Save as Shortcut
                                                            </button>
                                                        </div>
                                                    )}

                                                    {item.productVariantId && (
                                                        <div className="mt-2 flex items-center space-x-2">
                                                            <span className="text-xs text-gray-400">⚙ Target Match (Optional):</span>
                                                            <select 
                                                                className="text-xs border border-gray-200 rounded text-gray-600 bg-gray-50 w-32 focus:ring-blue-500 p-0.5"
                                                                value={(item as any).targetShipmentId || ""}
                                                                onChange={(e) => handleTargetShipmentChange(idx, e.target.value)}
                                                            >
                                                                <option value="">Auto-FIFO</option>
                                                                {allShipments.map(s => (
                                                                    <option key={s.id} value={s.id}>{s.containerCode}</option>
                                                                ))}
                                                            </select>
                                                        </div>
                                                    )}
                                                </div>
                                            </div>
                                            <div className="flex items-center space-x-4 text-right">
                                                <div className="flex flex-col items-end">
                                                    <div className="flex flex-col items-end bg-yellow-50 p-2 rounded-lg border border-yellow-200 shadow-inner group transition-colors focus-within:ring-2 focus-within:ring-yellow-400">
                                                        <span className="text-xs font-bold text-yellow-800 mb-1 uppercase tracking-wider">Quick Edit Price</span>
                                                        <div className="flex items-center space-x-1">
                                                            <span className="text-yellow-600 font-bold text-lg">€</span>
                                                            <input 
                                                                type="number" 
                                                                step="0.01"
                                                                min="0"
                                                                value={item.estimatedPrice ?? ''}
                                                                onChange={(e) => {
                                                                    const val = e.target.value;
                                                                    handlePriceChange(idx, val === '' ? null : parseFloat(val));
                                                                }}
                                                                className="w-24 text-right font-black text-xl text-gray-900 bg-transparent border-none focus:outline-none placeholder-gray-300 p-0"
                                                                placeholder="0.00"
                                                            />
                                                        </div>
                                                    </div>
                                                    <div className="text-sm font-bold text-gray-500 mt-2">Total: € {formatMoney((item.estimatedPrice || 0) * item.quantity, null, 'nl')}</div>
                                                </div>
                                                <button 
                                                    onClick={() => handleRemoveItem(idx)}
                                                    className="w-8 h-8 rounded-full bg-red-100 hover:bg-red-200 text-red-600 flex items-center justify-center transition-colors"
                                                    title="Remove Item"
                                                >
                                                    🗑️
                                                </button>
                                            </div>
                                        </div>
                                        
                                        {item.productVariantId && typeof item.currentStock === 'number' && item.quantity > item.currentStock && (
                                            <div className="bg-yellow-100 border border-yellow-300 text-yellow-800 text-xs p-2 rounded flex items-center">
                                                <span className="mr-2">⚠️</span> 
                                                <span>Warning: Backordering. Inventory will drop to {item.currentStock - item.quantity} {item.unit || 'units'}. Current stock: {item.currentStock}</span>
                                            </div>
                                        )}
                                    </div>
                                ))}
                            </div>

                            <div className="border-t pt-4">
                                <div className="flex justify-between items-center mb-4 text-lg font-bold">
                                    <span>Total Estimated</span>
                                    <span>
                                        € {formatMoney(cartItems.reduce((sum, item) => sum + ((item.estimatedPrice || 0) * item.quantity), 0), null, 'nl')}
                                    </span>
                                </div>
                                <button
                                    onClick={handleConfirm}
                                    disabled={isSubmitting || cartItems.length === 0 || !customerInput.trim()}
                                    className="w-full bg-green-600 hover:bg-green-700 text-white font-bold py-4 rounded-lg text-lg shadow-lg hover:shadow-xl transition-all disabled:opacity-50"
                                >
                                    {isSubmitting ? 'Submitting...' : 'Confirm & Create Orders 🚀'}
                                </button>
                            </div>
                        </>
                    )}
                </div>
            </div>
        </div>
    );
}
