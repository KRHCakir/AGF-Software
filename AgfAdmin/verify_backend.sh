#!/bin/bash

BASE_URL="http://localhost:5197/api"

echo "1. Testing Manual Incoming Invoice (Purchase)..."
curl -X POST "$BASE_URL/incoming-invoices/manual" \
     -H "Content-Type: application/json" \
     -d '{
           "vendorId": 1, 
           "invoiceNumber": "MAN-BUY-001", 
           "invoiceDate": "2023-10-27T00:00:00Z", 
           "lines": [
             { "description": "Test Item 1", "quantity": 100, "unitPrice": 5.50, "productVariantId": 1 }
           ]
         }'
echo -e "\n"

echo "2. Testing Manual Outgoing Invoice (Sale)..."
curl -X POST "$BASE_URL/invoice/manual" \
     -H "Content-Type: application/json" \
     -d '{
           "customerId": 1, 
           "invoiceDate": "2023-10-28T00:00:00Z", 
           "lines": [
             { "description": "Sold Item 1", "quantity": 10, "unitPrice": 12.00, "productVariantId": 1 }
           ]
         }'
echo -e "\n"
