/**
 * invoiceService.ts
 * Service for the Create New Invoice wizard.
 *
 * Creates `sprk_invoice` records in Dataverse via IDataService, mirroring the
 * canonical `WorkAssignmentService.createWorkAssignment` flow EXACTLY per
 * task 030 (visual-host-create-button-r1):
 *   nav-prop discovery -> payload (manifest fields) -> BU defaults cascade ->
 *   applyResolverFields (host regarding) -> createRecord('sprk_invoice') ->
 *   file pipeline with dual-bind (Invoice + host) -> warnings (never throws
 *   for non-fatal side-effects).
 *
 * Field manifest (Phase-0-validated, zero schema gaps):
 *   sprk_invoicenumber, sprk_name (NOT NULL), sprk_description,
 *   sprk_vendororg (-> sprk_organization), sprk_invoicedate (default today).
 *
 * Dependencies are injected via constructor -- no solution-specific imports.
 *
 * @see CreateWorkAssignmentWizard/workAssignmentService.ts — canonical reference
 * @see .claude/adr/ADR-024-polymorphic-resolver-pattern.md
 * @see projects/visual-host-create-button-r1/notes/field-manifests/invoice.md
 */

import type { ICreateInvoiceFormState } from './formTypes';
import type { IDataService } from '../../types/serviceInterfaces';
import type { AssociationResult } from '../AssociateToStep/types';
import { EntityCreationService } from '../../services/EntityCreationService';
import type { IUploadProgress, AuthenticatedFetchFn, IAdditionalBind } from '../../services/EntityCreationService';
import type { IUploadedFile } from '../FileUpload/fileUploadTypes';
import { applyResolverFields, findNavProp } from '../../services/PolymorphicResolverService';
import type { INavPropEntry } from '../../services/PolymorphicResolverService';

// ---------------------------------------------------------------------------
// Result type
// ---------------------------------------------------------------------------

export interface ICreateInvoiceResult {
  status: 'success' | 'partial' | 'error';
  invoiceId?: string;
  invoiceName?: string;
  errorMessage?: string;
  warnings: string[];
}

// ---------------------------------------------------------------------------
// Metadata discovery (same pattern as WorkAssignmentService/EventService)
// ---------------------------------------------------------------------------

const _navPropCache: Record<string, INavPropEntry[]> = {};

async function _discoverNavProps(entityLogicalName: string): Promise<INavPropEntry[]> {
  if (_navPropCache[entityLogicalName]) {
    return _navPropCache[entityLogicalName];
  }

  try {
    const url =
      `/api/data/v9.0/EntityDefinitions(LogicalName='${entityLogicalName}')/ManyToOneRelationships` +
      `?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity`;

    const resp = await fetch(url, { credentials: 'include' });
    if (!resp.ok) {
      console.warn(`[InvoiceService] Nav-prop discovery failed for ${entityLogicalName}:`, resp.status);
      return [];
    }

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const json: any = await resp.json();
    const rels: Array<{
      ReferencingAttribute: string;
      ReferencingEntityNavigationPropertyName: string;
      ReferencedEntity: string;
    }> =
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (json as any).value ?? [];

    const entries: INavPropEntry[] = rels.map(r => ({
      columnName: r.ReferencingAttribute,
      navPropName: r.ReferencingEntityNavigationPropertyName,
      referencedEntity: r.ReferencedEntity,
    }));

    console.info(
      `[InvoiceService] Nav-props for ${entityLogicalName}:`,
      entries.map(e => `${e.columnName} -> ${e.navPropName} (${e.referencedEntity})`)
    );
    _navPropCache[entityLogicalName] = entries;
    return entries;
  } catch (err) {
    console.warn(`[InvoiceService] Nav-prop discovery error for ${entityLogicalName}:`, err);
    return [];
  }
}

/** @internal — for tests, reset the module-level nav-prop cache between cases. */
export function _resetInvoiceServiceNavPropCacheForTests(): void {
  delete _navPropCache['sprk_invoice'];
  delete _navPropCache['sprk_document'];
}

// ---------------------------------------------------------------------------
// Host entity-set / lookup-hint resolution (Matter + Project only — manifest §)
// ---------------------------------------------------------------------------

const _ENTITY_SET_MAP: Record<string, string> = {
  sprk_matter: 'sprk_matters',
  sprk_project: 'sprk_projects',
};

function _resolveEntitySet(entityLogicalName: string): string {
  return _ENTITY_SET_MAP[entityLogicalName] ?? `${entityLogicalName}s`;
}

function _resolveLookupHint(entityLogicalName: string): string {
  return entityLogicalName.startsWith('sprk_') ? entityLogicalName.slice('sprk_'.length) : entityLogicalName;
}

// ---------------------------------------------------------------------------
// Current-user resolution (Xrm-host best-effort — same pattern as
// WorkAssignmentService._getCurrentUserId)
// ---------------------------------------------------------------------------

function _getCurrentUserId(): string {
  const frames: Window[] = [window];
  try {
    if (window.parent !== window) frames.push(window.parent);
  } catch {
    /* cross-origin */
  }
  try {
    if (window.top && window.top !== window) frames.push(window.top);
  } catch {
    /* cross-origin */
  }

  for (const frame of frames) {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (frame as any).Xrm;
      if (xrm?.Utility?.getGlobalContext) {
        const ctx = xrm.Utility.getGlobalContext();
        const userId = ctx?.userSettings?.userId;
        if (typeof userId === 'string' && userId.trim() !== '') {
          return userId.replace(/^\{|\}$/g, '').toLowerCase();
        }
      }
      if (typeof xrm?.Utility?.getUserId === 'function') {
        const userId = xrm.Utility.getUserId();
        if (typeof userId === 'string' && userId.trim() !== '') {
          return userId.replace(/^\{|\}$/g, '').toLowerCase();
        }
      }
    } catch {
      /* cross-origin */
    }
  }
  return '';
}

// ---------------------------------------------------------------------------
// InvoiceService class
// ---------------------------------------------------------------------------

export class InvoiceService {
  private readonly _dataService: IDataService;
  private readonly _entityService: EntityCreationService;
  private readonly _tenantId: string;

  constructor(
    dataService: IDataService,
    authenticatedFetch: AuthenticatedFetchFn,
    bffBaseUrl: string,
    private readonly _containerId?: string,
    /** AAD tenant ID for post-upload RAG indexing routing. Omitted -> indexing skipped. */
    tenantId?: string,
    /** Injection point for tests: override the Xrm.Utility.getUserId() probe. */
    private readonly _getCurrentUserIdOverride?: () => string | null
  ) {
    this._tenantId = tenantId ?? '';
    this._dataService = dataService;
    // EntityCreationService expects IWebApiWithCreate which has createRecord returning { id: string }.
    const webApiAdapter = {
      createRecord: async (entityName: string, data: Record<string, unknown>) => {
        const id = await dataService.createRecord(entityName, data);
        return { id };
      },
      retrieveRecord: (entityName: string, id: string, options?: string) =>
        dataService.retrieveRecord(entityName, id, options),
      retrieveMultipleRecords: (entityName: string, options?: string) =>
        dataService.retrieveMultipleRecords(entityName, options),
      updateRecord: async (entityName: string, id: string, data: Record<string, unknown>) => {
        await dataService.updateRecord(entityName, id, data);
        return { id };
      },
      deleteRecord: async (entityName: string, id: string) => {
        await dataService.deleteRecord(entityName, id);
        return { id };
      },
    };
    this._entityService = new EntityCreationService(webApiAdapter, authenticatedFetch, bffBaseUrl);
  }

  /**
   * Full invoice creation flow:
   *   1. Create sprk_invoice record (manifest fields + BU cascade + resolver fields)
   *   2. Upload files to SPE + create sprk_document records dual-bound to
   *      BOTH the new Invoice and the host record (FR-12 / design §5.8)
   *
   * @param form          Enter Info manifest field values.
   * @param association   The host record this invoice is regarding (from
   *                      AssociateToStep / the Visual Host locked-launch
   *                      seed), or `null` when no association was made.
   * @param uploadedFiles Files from the Add Files step (may be empty).
   * @param onUploadProgress Optional progress callback for the file pipeline.
   *
   * Returns ICreateInvoiceResult -- never throws.
   */
  async createInvoice(
    form: ICreateInvoiceFormState,
    association: AssociationResult | null,
    uploadedFiles: IUploadedFile[],
    onUploadProgress?: (progress: IUploadProgress) => void
  ): Promise<ICreateInvoiceResult> {
    const warnings: string[] = [];

    // -- Step 1: Create Dataverse record -------------------------------------
    let invoiceId: string;

    const navProps = await _discoverNavProps('sprk_invoice');

    const entity: Record<string, unknown> = {
      sprk_name: form.name.trim(),
    };

    if (form.invoiceNumber?.trim()) {
      entity['sprk_invoicenumber'] = form.invoiceNumber.trim();
    }
    if (form.description?.trim()) {
      entity['sprk_description'] = form.description.trim();
    }
    // sprk_invoicedate — defaults to today per spec FR-16 (form.invoiceDate is
    // pre-populated with today's date by CreateInvoiceStep/buildEmptyInvoiceForm,
    // but guard here too so a programmatic caller that omits it still gets a value).
    entity['sprk_invoicedate'] = form.invoiceDate?.trim() ? form.invoiceDate.trim() : todayIsoDateFallback();

    // Store the host-resolved SPE container ID on the invoice record (enables Documents tab).
    // Applied FIRST so it acts as an explicit override during the subsequent BU cascade (INV-5).
    if (this._containerId) {
      entity['sprk_containerid'] = this._containerId;
    }

    // Vendor Org lookup (sprk_vendororg -> sprk_organization)
    if (form.vendorOrgId) {
      const vendorNavProp = findNavProp(navProps, 'sprk_organization', 'vendororg');
      if (vendorNavProp) {
        entity[`${vendorNavProp}@odata.bind`] = `/sprk_organizations(${_cleanGuid(form.vendorOrgId)})`;
      } else {
        console.warn('[InvoiceService] No nav-prop found for sprk_organization (hint: vendororg), skipping binding');
      }
    }

    // FR-WIZ-01..05: Cascade `sprk_containerid` + `sprk_searchindexname` + Phase G
    // `sprk_ai_search_index` lookup from the current user's owning Business Unit.
    // INV-5 guards each scalar field independently.
    try {
      const currentUserId = this._getCurrentUserIdOverride
        ? (this._getCurrentUserIdOverride() ?? '')
        : _getCurrentUserId();
      if (currentUserId) {
        const defaults = await EntityCreationService.resolveUserBuDefaults(this._dataService, currentUserId);
        EntityCreationService.applyUserBuDefaults(entity, defaults);
        if (defaults.searchIndexId) {
          const aiNavProp = findNavProp(navProps, 'sprk_aisearchindex');
          if (aiNavProp) {
            entity[`${aiNavProp}@odata.bind`] = `/sprk_aisearchindexes(${defaults.searchIndexId})`;
          }
        }
      } else {
        console.warn('[InvoiceService] BU cascade skipped: current user ID could not be resolved.');
      }
    } catch (err) {
      // Non-fatal: log and continue. BFF tenant-default chain handles routing if all fields are unset.
      console.warn('[InvoiceService] BU cascade failed (non-fatal):', err);
    }

    // Host regarding (Matter or Project) — ADR-024 Polymorphic Resolver.
    // Writes the entity-specific @odata.bind lookup AND all 5 denormalized
    // resolver fields via the shared `applyResolverFields` primitive.
    if (association?.recordId && association.entityType) {
      const entitySet = _resolveEntitySet(association.entityType);
      const hint = _resolveLookupHint(association.entityType);
      const polyWebApi = {
        retrieveMultipleRecords: (entityLogicalName: string, query: string) =>
          this._dataService.retrieveMultipleRecords(entityLogicalName, query),
      };
      await applyResolverFields(
        polyWebApi,
        entity,
        navProps,
        association.entityType,
        entitySet,
        association.recordId,
        association.recordName,
        hint
      );
    }

    try {
      console.info('[InvoiceService] createRecord payload:', JSON.stringify(entity, null, 2));
      invoiceId = await this._dataService.createRecord('sprk_invoice', entity);
      console.info('[InvoiceService] createRecord success, invoiceId:', invoiceId);
    } catch (err) {
      console.error('[InvoiceService] createRecord error:', err);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const errObj = err as any;
      const message = errObj?.message || (err instanceof Error ? err.message : 'Unknown error');
      return {
        status: 'error',
        errorMessage: `Failed to create invoice record: ${message}`,
        warnings: [],
      };
    }

    // -- Step 2: Upload files to SPE + dual-bind sprk_document ---------------
    if (uploadedFiles.length > 0 && this._containerId) {
      const uploadResult = await this._entityService.uploadFilesToSpe(
        this._containerId,
        uploadedFiles,
        onUploadProgress
      );

      if (!uploadResult.success) {
        warnings.push(
          `File upload failed (${uploadResult.failureCount} of ${uploadedFiles.length}). ` +
            'Files can be added from the invoice record.'
        );
      } else if (uploadResult.uploadedFiles.length > 0) {
        try {
          const docNavProps = await _discoverNavProps('sprk_document');

          // Primary bind: the newly-created Invoice.
          const invoiceNavProp = findNavProp(docNavProps, 'sprk_invoice', 'invoice');
          if (!invoiceNavProp) {
            console.warn(
              '[InvoiceService] No nav-prop found on sprk_document for sprk_invoice — dual-bind incomplete.'
            );
          }

          // Additional bind: the host record (Matter/Project), if an association
          // was made — FR-12 / design.md §5.8 dual-bind (host + child).
          const additionalBinds: IAdditionalBind[] = [];
          if (association?.recordId && association.entityType) {
            const hostEntitySet = _resolveEntitySet(association.entityType);
            const hostHint = _resolveLookupHint(association.entityType);
            const hostNavProp = findNavProp(docNavProps, association.entityType, hostHint);
            if (hostNavProp) {
              additionalBinds.push({
                entitySet: hostEntitySet,
                id: association.recordId,
                navProp: hostNavProp,
              });
            } else {
              console.warn(
                `[InvoiceService] No nav-prop found on sprk_document for host entity "${association.entityType}" — document will bind to the Invoice only.`
              );
            }
          }

          const createResult = await this._entityService.createDocumentRecords(
            'sprk_invoices',
            invoiceId,
            invoiceNavProp ?? 'sprk_Invoice',
            uploadResult.uploadedFiles,
            {
              containerId: this._containerId,
              parentRecordName: form.name.trim(),
              additionalBinds: additionalBinds.length > 0 ? additionalBinds : undefined,
            }
          );
          if (createResult.warnings.length > 0) {
            warnings.push(...createResult.warnings);
          }

          // Trigger RAG indexing per file (canonical sync OBO path). Non-fatal.
          const indexingWarnings = await this._entityService.indexUploadedFiles(
            uploadResult.uploadedFiles,
            this._tenantId,
            {
              entityType: 'sprk_invoice',
              entityId: invoiceId,
              entityName: form.name.trim(),
            },
            createResult.createdDocumentIds
          );
          if (indexingWarnings.length > 0) {
            warnings.push(...indexingWarnings);
          }
        } catch (err) {
          console.warn('[InvoiceService] Document record creation failed:', err);
          warnings.push('Uploaded files saved to storage but document records could not be created.');
        }
      }

      if (uploadResult.failureCount > 0 && uploadResult.successCount > 0) {
        warnings.push(
          `${uploadResult.failureCount} file(s) failed to upload: ` +
            uploadResult.errors.map(e => e.fileName).join(', ')
        );
      }
    } else if (uploadedFiles.length > 0 && !this._containerId) {
      warnings.push('File upload skipped -- no SPE container configured. Files can be added later.');
    }

    return {
      status: warnings.length > 0 ? 'partial' : 'success',
      invoiceId,
      invoiceName: form.name.trim(),
      warnings,
    };
  }
}

// ---------------------------------------------------------------------------
// Small local helpers
// ---------------------------------------------------------------------------

/** Strip braces + lowercase a GUID for @odata.bind URLs (mirrors PolymorphicResolverService). */
function _cleanGuid(id: string): string {
  return id.replace(/[{}]/g, '').toLowerCase();
}

/** Fallback "today" ISO date — used only if a caller passes an empty invoiceDate. */
function todayIsoDateFallback(): string {
  const now = new Date();
  const year = now.getFullYear();
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}
