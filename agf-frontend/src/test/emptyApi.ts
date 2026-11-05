// Wat de API op een verse, lege database teruggeeft (schone start), vastgelegd met scripts/new-clean-db.sh. Landen ingekort tot Nederland en
// Peru: genoeg voor de schermen. Paden zonder eigen antwoord krijgen een lege lijst.
export const EMPTY_API: Readonly<Record<string, unknown>> = {
    '/articles/reference': {"productNames":[],"packagingUnits":["box","colli","tray","piece","dozen"],"weightMethods":["Weighed","Calculated"],"goodsCodeSystems":["HS","CN","TARIC"],"tradeClassSuggestions":["Extra","I","II"]},
    '/articles/origin-cleanup': [],
    '/availability': {"generatedAt":"2026-11-05T08:00:00Z","basis":{"source":"SupplyCommitmentLines + PalletContentLines","reservationsApplied":true,"legacyInventoryIncluded":false,"stockLedgerIncluded":false},"variants":[]},
    '/me': {"userId":"admin","displayName":"Admin","roles":["Admin"],"permissions":["Inkoop","Voorraad","Keuring","Verkoop","Stamgegevens","Beheer","Verkoopcijfers"]},
    '/partijen/next-number': {"mode":"Auto","format":"{JJ}{MM}{volgnr}","sequenceReset":"Yearly","suggestion":"2611001"},
    '/partijen': {"items":[],"total":0,"page":1,"pageSize":50},
    '/purchase-invoices/settings': {"allOptionalFields":["receivedDate","paymentReference","remarks","grossWeightKg","netWeightKg","containerNumber","transporter","driverName","licensePlate","incoterms","customsValue","originCountry","receiptReference"],"optionalFields":["receivedDate","paymentReference","remarks","originCountry","receiptReference"],"defaultVatPercentage":21.00,"defaultPaymentTermsDays":30,"defaultCurrency":"EUR"},
    '/purchase-orders': {"items":[],"total":0,"page":1,"pageSize":50},
    '/relations/reference': {"languages":["nl","en","de","fr","es","it","pl","tr"],"addressPurposes":["Main","Billing","Delivery"],"departmentSuggestions":["verkoop","inkoop","administratie","transport","kwaliteit","directie"],"registrationTypeSuggestions":["KvK","HRB","HRA","SIRET","CRN","CIF","NIP","Other"],"legalFormSuggestions":["BV","NV","VOF","Eenmanszaak","Maatschap","Stichting","Coöperatie","GmbH","Ltd","SARL","SL","Sp. z o.o.","Other"],"currencies":["EUR","USD","GBP","PLN","TRY","MAD","ZAR"],"vatVerificationStatuses":["NotChecked","Valid","Invalid","Unavailable","NotApplicable"],"globalGapDatabaseUrl":"https://database.globalgap.org/","globalGapPortalUrl":"https://prod.osapiens.cloud/portal/webbundle/foodplus/field-service-os/supply-chain-portal?app-route-hash=%252Fcertificates","paymentMethods":["BankTransfer","DirectDebit","Cash","Offset"],"paymentConditionBases":["InvoiceDate","EndOfMonth"],"defaultPaymentTermsDays":30,"deliveryTermKinds":["Incoterm","Franco","AfKwekerij","Other"],"incoterms":["EXW","FCA","CPT","CIP","DAP","DPU","DDP","FAS","FOB","CFR","CIF"],"tradeConditions":["Own","Frugiventa","Cofreurop","Other"],"creditInsurers":["Atradius","AllianzTrade","Coface","Other"],"legacyArticleGgns":[]},
    '/relations': {"items":[],"total":0,"page":1,"pageSize":50},
    '/countries': {"countries":[{"code":"NL","nameNl":"Nederland","nameEn":"Netherlands"},{"code":"PE","nameNl":"Peru","nameEn":"Peru"}],"aliases":[]},
};

// Op het pad zonder query en host; elk ander pad is een lege lijst.
export function emptyApiResponse(url: string): unknown {
    const path = new URL(url, 'http://localhost').pathname.replace(/^\/api/, '');
    return EMPTY_API[path] ?? [];
}
