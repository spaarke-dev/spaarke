/**
 * The firms the "Invoices from" picker offers besides the record's billing firm (ebilling-r1 task 084a, decision 1).
 *
 * Task 084's routes answer for ONE firm and return the firm in use; they do not list choices (notes/084, question e). The picker's data
 * is "the firms that have invoiced the record", read here from `sprk_invoice` through `Xrm.WebApi` AS THE SIGNED-IN USER
 * (DATA-ACCESS-DECISION-CRITERIA: a simple read-only lookup in host context; the same pattern as `RuleReview/firmLookup.ts` and
 * `ApExport/invoices.ts`). The POML forbids `Xrm.WebApi` only for the Procedure, override and rule tables; none of those is read here.
 *
 * Why not a BFF route: `GET /api/finance/core-records/{type}/{id}/firms` lists the ACTIVE firms LINKED to the record (external
 * participation), not the firms that invoiced it, so it is a different set; `/invoices/search` is a semantic search that needs a query.
 *
 * Known limit K2 (notes/084, "Fix round 2" (b)): a `firmId` the caller cannot Read makes the routes answer 503. So the firm ids found on
 * the invoices are read from `sprk_organization` as the caller, and only the rows that come back are offered: a firm the caller cannot
 * Read is never put in the picker. `sprk_vendororg` is field-secured "readable by everyone" (EB-D-200), so the invoice read returns it.
 *
 * Known limit K1: one page of the record's invoices (the platform's page, 5,000 rows, newest first) is read. A record with more invoices
 * than that could miss an older firm; the billing firm is always offered either way.
 *
 * Nothing here writes: the adapter has no write method.
 */

import type { CoreTable } from './launch';

/** The minimal `Xrm.WebApi` read surface (injected; the tests pass a fake). */
export interface WebApiLike {
  retrieveMultipleRecords(entity: string, options: string): Promise<{ entities: Array<Record<string, unknown>> }>;
}

export interface FirmOption {
  /** Lower case, bare. */
  id: string;
  name: string;
}

export interface InvoicedFirms {
  firms: FirmOption[];
  /** False when a read failed: the picker then offers fewer firms than have invoiced the record, and the panel says so. */
  complete: boolean;
}

export interface FirmChoices {
  /** The firms that have invoiced the record and that the caller can Read, by name. Never throws. */
  invoicedBy(table: CoreTable, recordId: string): Promise<InvoicedFirms>;
}

/** The `sprk_invoice` lookup that points at each core record (`sprk_regardingworkassignment` for a work assignment; docs/data-model/sprk_invoice.md). */
export const INVOICE_LOOKUP: Readonly<Record<CoreTable, string>> = {
  sprk_matter: '_sprk_matter_value',
  sprk_project: '_sprk_project_value',
  sprk_workassignment: '_sprk_regardingworkassignment_value',
};

const FIRM_LOOKUP = '_sprk_vendororg_value';
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const CHUNK = 20;

/** The OData options of the invoice read: the firm lookup of the record's invoices, newest first. Exported for the tests. */
export function invoiceFirmQuery(table: CoreTable, recordId: string): string {
  return `?$select=${FIRM_LOOKUP}&$filter=${INVOICE_LOOKUP[table]} eq ${recordId} and ${FIRM_LOOKUP} ne null&$orderby=createdon desc`;
}

/** The OData options of one chunk of the firm-name read. */
export function firmNameQuery(ids: readonly string[]): string {
  return `?$select=sprk_organizationid,sprk_organizationname&$filter=${ids.map(id => `sprk_organizationid eq ${id}`).join(' or ')}`;
}

const byName = (a: FirmOption, b: FirmOption) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }) || a.id.localeCompare(b.id);

export function createFirmChoices(webApi: () => WebApiLike | undefined): FirmChoices {
  return {
    async invoicedBy(table, recordId) {
      const api = webApi();
      if (!api) return { firms: [], complete: false };
      if (!GUID.test(recordId)) return { firms: [], complete: false };

      let firmIds: string[];
      try {
        const result = await api.retrieveMultipleRecords('sprk_invoice', invoiceFirmQuery(table, recordId));
        const seen = new Set<string>();
        for (const row of result.entities) {
          const id = row[FIRM_LOOKUP];
          if (typeof id === 'string' && GUID.test(id)) seen.add(id.toLowerCase());
        }
        firmIds = [...seen];
      } catch {
        return { firms: [], complete: false };
      }
      if (firmIds.length === 0) return { firms: [], complete: true };

      // Only the organizations the caller can Read come back, which is exactly the set the routes will accept (K2).
      const firms: FirmOption[] = [];
      let complete = true;
      for (let i = 0; i < firmIds.length; i += CHUNK) {
        try {
          const result = await api.retrieveMultipleRecords('sprk_organization', firmNameQuery(firmIds.slice(i, i + CHUNK)));
          for (const row of result.entities) {
            const id = row['sprk_organizationid'];
            const name = row['sprk_organizationname'];
            if (typeof id === 'string' && GUID.test(id)) firms.push({ id: id.toLowerCase(), name: typeof name === 'string' && name ? name : id });
          }
        } catch {
          complete = false;
        }
      }
      return { firms: firms.sort(byName), complete };
    },
  };
}
