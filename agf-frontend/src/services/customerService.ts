import { ApiError, postJson } from './apiClient';
import { CreateCustomerRequest, CreatedCustomer, SimilarCustomer, isCreatedCustomer, isSimilarCustomerList } from '../types/referenceData';

const API_BASE_URL = 'http://localhost:5197/api';

// Bestaande service van de mobiele verkoopflow (app/sales/[sellerId]); ongewijzigd gelaten.
export const CustomerService = {
    async getStanding(id: number): Promise<unknown> {
        const res = await fetch(`${API_BASE_URL}/customers/${id}/standing`);
        if (!res.ok) throw new Error(await res.text());
        return res.json();
    }
};

export type CreateCustomerOutcome =
    | { outcome: 'created' | 'replayed'; customer: CreatedCustomer }
    // Definitief antwoord van de server (409): er is niets opgeslagen; bevestigen is een nieuwe opdracht met een nieuwe requestId.
    | { outcome: 'similar-names'; similar: SimilarCustomer[] };

function optional(value: unknown): string | null {
    return typeof value === 'string' && value.trim() !== '' ? value : null;
}

// Klant aanmaken vanuit Verkoop. 201 = nieuw, 200 = identieke herhaling al verwerkt. Een 409 met code "similar-names"
// is een gewone uitkomst (gelijkende klanten); elke andere fout wordt als ApiError doorgegeven.
export async function createCustomer(request: CreateCustomerRequest, signal?: AbortSignal): Promise<CreateCustomerOutcome> {
    try {
        const { status, json } = await postJson('/customers', request, signal);
        if (!isCreatedCustomer(json)) {
            throw new ApiError('shape', 'Onverwacht antwoord bij het aanmaken van de klant.');
        }
        return {
            outcome: status === 200 ? 'replayed' : 'created',
            customer: { id: json.id, name: json.name, contactPerson: optional(json.contactPerson), email: optional(json.email), phone: optional(json.phone), shippingAddress: optional(json.shippingAddress) },
        };
    } catch (caught) {
        if (caught instanceof ApiError && caught.kind === 'http' && caught.status === 409 && typeof caught.body === 'object' && caught.body !== null) {
            const body = caught.body as Record<string, unknown>;
            if (body.code === 'similar-names' && isSimilarCustomerList(body.similarCustomers)) {
                return { outcome: 'similar-names', similar: body.similarCustomers.map((c) => ({ id: c.id, name: c.name, contactPerson: optional(c.contactPerson), phone: optional(c.phone) })) };
            }
        }
        throw caught;
    }
}
