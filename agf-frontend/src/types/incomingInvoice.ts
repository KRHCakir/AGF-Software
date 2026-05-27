export interface IncomingInvoice {
    id: number;
    vendorId?: number;
    vendor?: Vendor;
    invoiceNumber?: string;
    invoiceDate?: string; // ISO Date
    dueDate?: string;
    totalAmount: number;
    vatAmount: number;
    status: 'Draft' | 'Processing' | 'NeedsReview' | 'Approved' | 'Rejected' | 'Error';
    reviewConfidence: number;
    originalFilePath?: string;
    detectedIssues?: string;
    createdAt: string;
    invoiceLines?: IncomingInvoiceLine[];
}

export interface IncomingInvoiceLine {
    id: number;
    incomingInvoiceId: number;
    description?: string;
    quantity: number;
    unitPrice: number;
    lineTotal: number;
    vatPercentage: number;
    vatAmount: number;
}

export interface Vendor {
    id: number;
    name: string;
    email?: string;
    vatNumber?: string;
}
