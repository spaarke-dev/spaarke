/**
 * TrackingFieldTrio PCF Control
 *
 * Compact three-field editor: Monitor (toggle) + High Priority (toggle) +
 * Access Permission (segmented 3-value picker). Designed to fit inside a 33%
 * form column where the standard Dataverse field controls waste too much
 * horizontal space.
 *
 * v1.0.1 — added showTitle / showVersion PCF properties, alignment fix
 * (explicit 2-row grid), pale color scheme, option-set color binding.
 *
 * v1.0.6 (task 023, FR-14/FR-18) — the rendered component now lives in
 * `@spaarke/ui-components` (entity-agnostic `TrackingFieldTrio`; options
 * injected via props). This `index.ts` is the ONLY place in the tree that
 * knows about `sprk_communication`'s Access Permission choice values —
 * `getAccessPermissionOptions()` supplies the real Dataverse OptionSet
 * metadata (value + label + color) when available, falling back to the
 * hardcoded Standard/Limited/Restricted triple (no color — the shared
 * core's position-based default palette applies) when metadata is
 * unavailable (e.g., harness/test environments), preserving the PRE-LIFT
 * behavior (NFR-04 — zero regression).
 *
 * v1.0.8 (task 040, teams-app-r1) — wires the shared core's governance
 * toolbar (person + email icons). `onOpenGrantModal` / `onOpenEmailMembers`
 * are STUB handlers here (console-logged, no dialog) — task 041 replaces
 * the grant-modal stub, task 042 replaces the email-members stub, with no
 * further changes required to the shared `TrackingFieldTrio` core.
 * `canGrantAccess` defaults to `true` (fail-open) — task 041 wires the real
 * privilege check once the access-grant flow's authorization data source
 * exists; building that check here would be premature (no consumer yet).
 *
 * v1.0.9 (task 041, teams-app-r1) — replaces the grant-modal stub with the
 * real `AccessGrantModal` (`@spaarke/ui-components`). This file is the ONLY
 * place that knows the `sprk_project` entity + its `sprk_assigned*` field
 * names (R1 scope per design.md §5 — `sprk_externalrecordaccess` is
 * `sprk_project`-scoped) — the shared modal itself stays entity-agnostic
 * (ADR-012), receiving candidates/grants/search/classification via callback
 * props backed by `context.webAPI` (host-context Dataverse reads, per
 * `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`). BFF writes (grant /
 * invite-and-grant / revoke) go through `@spaarke/auth`'s `authenticatedFetch`
 * (bootstrapped in `init()` via `authInit.ts`, same pattern as
 * `SemanticSearchControl`). `canGrantAccess` now reflects a real
 * `context.utils.hasEntityPrivilege('sprk_externalrecordaccess', Create,
 * Global)` check (fail-open only if the API is unavailable, e.g. a harness
 * environment) — `AccessGrantModal` itself applies a second, defense-in-depth
 * gate on the same prop.
 *
 * v1.0.10 (task 042, teams-app-r1) — replaces the email-members stub with the
 * canonical `SendEmailDialog` (`EmailComposer` engine, `@spaarke/ui-components`,
 * ADR-045). Clicking the email icon reuses the SAME `fetchCandidates()`
 * membership-contact data source task 041 wired for the grant modal — no
 * separate recipient-derivation rule (per this task's `<constraint
 * source="project">`). Candidates without a populated email are dropped; a
 * record with NO emailable membership contacts shows a small empty-state
 * alert instead of opening the dialog with zero recipients (send flows
 * through the composer's own `sendCommunication()` call — no custom send
 * logic is added here).
 *
 * v1.0.11 (task 043, teams-app-r1) — wires `AccessGrantModal`'s new
 * `accessPermissionState` prop (spec FR-14 Option A sharing gate) to this
 * record's bound `sprk_project` Access Permission field. This file is the
 * ONLY place that maps the raw `ACCESS_PERMISSION_STANDARD` /
 * `_LIMITED` / `_RESTRICTED` OptionSet integers to the shared modal's
 * entity-agnostic `AccessPermissionState` vocabulary — the modal itself
 * never sees the raw Dataverse values (ADR-012). Distinct from, and does
 * not touch, the per-grant `sprk_accesslevel` wiring below.
 *
 * @remarks
 * - Uses React 16 APIs per ADR-022 (ReactDOM.render, not createRoot)
 * - Uses Fluent UI v9 per ADR-021 (via platform libraries)
 */

import { IInputs, IOutputs } from './generated/ManifestTypes';
import * as React from 'react';
import * as ReactDOM from 'react-dom';
import {
  FluentProvider,
  webLightTheme,
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
} from '@fluentui/react-components';
import { authenticatedFetch } from '@spaarke/auth';
// Aliased on import — the PCF control class below MUST be named
// `TrackingFieldTrio` to match `constructor="TrackingFieldTrio"` in
// ControlManifest.Input.xml, so the shared component is imported under a
// distinct local name.
import {
  TrackingFieldTrio as SharedTrackingFieldTrio,
  type ITrackingFieldTrioProps,
  type IAccessPermissionOption,
} from '@spaarke/ui-components/dist/components/TrackingFieldTrio';
import {
  AccessGrantModal,
  type IAccessGrantCandidate,
  type IAccessGrantRecord,
  type IContactSearchResult,
  type IOrganizationPick,
  type ExternalGrantRootType,
  type AccessPermissionState,
} from '@spaarke/ui-components/dist/components/AccessGrantModal';
// Shared side-pane Advanced Lookup (task 071) — adopted as-is per §11: the PCF
// host wires INavigationService.openLookup (→ Xrm.Utility.lookupObjects) and
// passes plain pickContact/pickOrganization callbacks into the Xrm-free modal.
import { createXrmNavigationService } from '@spaarke/ui-components/dist/utils/adapters';
import type { INavigationService } from '@spaarke/ui-components/dist/types/serviceInterfaces';
// Canonical `SendEmailDialog` (task 042, ADR-045) — the `EmailComposer`
// wrapper that owns the Dialog chrome + `sendCommunication()` send flow.
// This is the ONLY email-send surface this control is allowed to open (no
// forked dialog, no ad-hoc fetch to the send endpoint).
import { SendEmailDialog } from '@spaarke/ui-components/dist/components/EmailComposer';
// Shared per-widget error boundary (task 073 UAT #1) — wraps the dialog subtree
// so a render error inside a shared dialog degrades to a small inline card
// instead of blanking the whole PCF (defense-in-depth alongside the
// react/jsx-runtime dedupe in ../webpack.config.js).
import { WidgetErrorBoundary } from '@spaarke/ui-components/dist/components/WidgetErrorBoundary';
import { initializeAuth } from './authInit';
// Dataverse Environment Variable resolution (task 073 UAT fix) — the SAME mechanism
// SemanticSearchControl uses so the grant modal's BFF auth needs NO per-control form config: the MSAL
// client id / BFF app id / BFF base url come from sprk_MsalClientId / sprk_BffApiAppId / sprk_BffApiBaseUrl.
import { getEnvironmentVariable, getApiBaseUrl } from '../shared/utils/environmentVariables';

// sprk_communication Access Permission choice values — MUST match the
// Dataverse OptionSet values. Entity-specific: lives ONLY here (the PCF
// caller), never in the shared `TrackingFieldTrio` core (FR-14).
const ACCESS_PERMISSION_STANDARD = 100000000;
const ACCESS_PERMISSION_LIMITED = 100000001;
const ACCESS_PERMISSION_RESTRICTED = 100000002;

// Fallback segments (no per-option color) used when the bound OptionSet's
// field metadata isn't available (e.g., harness/test environments). The
// shared core's position-based default palette (green/yellow/red, by index)
// then supplies the same colors the pre-lift hardcoded fallback did.
const FALLBACK_ACCESS_PERMISSION_OPTIONS: IAccessPermissionOption[] = [
  { value: ACCESS_PERMISSION_STANDARD, label: 'Standard' },
  { value: ACCESS_PERMISSION_LIMITED, label: 'Limited' },
  { value: ACCESS_PERMISSION_RESTRICTED, label: 'Restricted' },
];

// v1.0.12 (task 071, external-access-r2) — the access-grant surface is now
// POLYMORPHIC across the three external-grant roots (Project / Matter / Work
// Assignment), the UI companion to tasks 028 (read) + 070 (write). The host
// entity is derived from the bound record at runtime (`context.page.entityTypeName`)
// instead of the R1 `sprk_project` hardcode, and the modal's contact picker is
// the SHARED side-pane Advanced Lookup (INavigationService.openLookup) rather
// than the inline Combobox. This file remains the ONLY place that knows the
// concrete Dataverse entity names + their `sprk_assigned*` role fields — the
// shared `AccessGrantModal` stays entity-agnostic (ADR-012 / FR-14).
const EXTERNAL_ACCESS_ENTITY = 'sprk_externalrecordaccess';

// The polymorphic external-grant root config, keyed by the host form's entity
// (task 070/071). `recordType` is the semantic root type the modal sends as
// `{recordType, recordId}`; `rootValueField` is the `sprk_externalrecordaccess`
// lookup filter for reading the record's active grants (fixes the R1
// `_sprk_projectid_value` bug — the verified lookup name is `_sprk_project_value`,
// per task 070's live $metadata verification). Matter + Work Assignment carry
// the SAME `sprk_assigned*` role fields as Project (verified), so a single
// shared CANDIDATE_ROLE_FIELDS set below serves all three.
interface IGrantRootConfig {
  recordType: ExternalGrantRootType;
  rootValueField: string;
}
const GRANT_ROOT_BY_ENTITY: Readonly<Record<string, IGrantRootConfig>> = {
  sprk_project: { recordType: 'project', rootValueField: '_sprk_project_value' },
  sprk_matter: { recordType: 'matter', rootValueField: '_sprk_matter_value' },
  sprk_workassignment: { recordType: 'workassignment', rootValueField: '_sprk_workassignment_value' },
};
// Back-compat default when the host entity is unknown/unavailable (e.g. a
// harness environment where `context.page` isn't populated) — preserves the
// R1 project behavior.
const DEFAULT_GRANT_ROOT: IGrantRootConfig = GRANT_ROOT_BY_ENTITY['sprk_project'];

// The access-conferring role-field set shared by all three grant roots
// (Project / Matter / Work Assignment carry the SAME `sprk_assigned*` lookups —
// verified in notes/polymorphic-grant-authoring-enhancement.md; task 021's
// convention-based discovery, mirrored client-side). A newly-added `sprk_assigned*` field
// does NOT auto-qualify here the way it does in the server-side metadata
// discovery (task 021/022) — this client-side list is a UI convenience for
// the candidate section, not the security enforcement path (enforcement is
// entirely server-side per design.md §5). If the role-field set changes,
// update this list; nothing security-load-bearing depends on it being
// exhaustive.
const CANDIDATE_ROLE_FIELDS: readonly { attr: string; role: string }[] = [
  { attr: 'sprk_assignedattorney1', role: 'Assigned Attorney 1' },
  { attr: 'sprk_assignedattorney2', role: 'Assigned Attorney 2' },
  { attr: 'sprk_assignedparalegal1', role: 'Assigned Paralegal 1' },
  { attr: 'sprk_assignedparalegal2', role: 'Assigned Paralegal 2' },
  { attr: 'sprk_assignedtoexternal', role: 'Assigned To (External)' },
  { attr: 'sprk_assignedtointernal', role: 'Assigned To (Internal)' },
];

/** Resolves the Dataverse org URL for the MSAL redirect URI, mirroring the
 * `Xrm.Utility.getGlobalContext().getClientUrl()` pattern used by every other
 * Spaarke PCF's `authInit.ts` (e.g. `RelatedDocumentCount`,
 * `CommunicationActions`). Returns `''` when `Xrm` isn't available (harness). */
function getClientUrl(): string {
  const xrm = (
    window as unknown as { Xrm?: { Utility?: { getGlobalContext?: () => { getClientUrl?: () => string } } } }
  ).Xrm;
  return xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.() ?? '';
}

export class TrackingFieldTrio implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private container: HTMLDivElement;
  private notifyOutputChanged: () => void;
  private context: ComponentFramework.Context<IInputs>;

  // Local state that mirrors the bound fields. We keep them here so the
  // control can render immediately when the user clicks a segment/toggle,
  // then flush the change via notifyOutputChanged() → getOutputs().
  private monitorValue = false;
  private highPriorityValue = false;
  private accessPermissionValue: number | null = null;

  // Access-grant modal state (task 041). `authInitPromise` gates every
  // `authenticatedFetch` call the modal makes so a click before MSAL
  // bootstrap completes still succeeds (awaits, doesn't fail) rather than
  // racing `@spaarke/auth`'s "not initialized" guard.
  private isGrantModalOpen = false;
  private canGrantAccessValue = true;
  private authInitPromise: Promise<void> = Promise.resolve();

  // Email-members state (task 042). `apiBaseUrl` mirrors the value passed to
  // `initializeAuth()` — reused as `SendEmailDialog`'s `bffBaseUrl` prop (same
  // pattern as every other Spaarke PCF that hosts the canonical dialog, e.g.
  // `CommunicationActionsApp.tsx`).
  private apiBaseUrl = '';
  private isSendEmailDialogOpen = false;
  private isEmailEmptyStateOpen = false;
  private emailRecipients: string[] = [];

  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    this.container = container;
    this.notifyOutputChanged = notifyOutputChanged;
    this.context = context;

    this.monitorValue = context.parameters.monitor?.raw ?? false;
    this.highPriorityValue = context.parameters.highPriority?.raw ?? false;
    this.accessPermissionValue = context.parameters.accessPermission?.raw ?? null;
    this.canGrantAccessValue = this.computeCanGrantAccess();

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const params = context.parameters as any;
    const webApi = context.webAPI;
    // task 073 UAT fix: resolve MSAL config from the manifest input properties FIRST, then fall back to
    // the Dataverse ENVIRONMENT VARIABLES (sprk_MsalClientId / sprk_BffApiAppId / sprk_BffApiBaseUrl) —
    // the SAME mechanism SemanticSearchControl uses. The prior code only read the (empty) manifest props,
    // so on an environment configured purely via env vars it threw "MSAL Client ID not configured".
    // getApiBaseUrl() normalizes the URL (strips any trailing /api) so the modal's `/api/...` paths resolve.
    this.authInitPromise = (async () => {
      const clientAppId =
        (params.clientAppId?.raw as string) || (await getEnvironmentVariable(webApi, 'sprk_MsalClientId')) || '';
      const bffAppId =
        (params.bffAppId?.raw as string) || (await getEnvironmentVariable(webApi, 'sprk_BffApiAppId')) || '';
      this.apiBaseUrl = (params.apiBaseUrl?.raw as string) || (await getApiBaseUrl(webApi));
      await initializeAuth(clientAppId, bffAppId, this.apiBaseUrl, getClientUrl());
      // Re-render so surfaces that read this.apiBaseUrl directly (e.g. SendEmailDialog's bffBaseUrl) pick
      // up the resolved value now that auth init has completed.
      this.renderControl();
    })().catch(err => {
      console.error(
        "[TrackingFieldTrio] Auth initialization failed — the access-grant modal's BFF calls will fail until the page is reloaded.",
        err
      );
    });

    this.renderControl();
  }

  public updateView(context: ComponentFramework.Context<IInputs>): void {
    this.context = context;

    // Framework-driven update (e.g., form refresh, another script wrote to
    // the field). Sync local state to the framework's raw values.
    this.monitorValue = context.parameters.monitor?.raw ?? false;
    this.highPriorityValue = context.parameters.highPriority?.raw ?? false;
    this.accessPermissionValue = context.parameters.accessPermission?.raw ?? null;

    this.renderControl();
  }

  /**
   * Extract per-option value/label/color from the bound OptionSet's field
   * metadata so the segmented picker can honor the choice column's real
   * Dataverse values, labels, and colors. Falls back to the hardcoded
   * Standard/Limited/Restricted triple (no color) when metadata isn't
   * available, so the control always renders 3 segments (NFR-04).
   */
  private getAccessPermissionOptions(): IAccessPermissionOption[] {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const attrs = (this.context.parameters.accessPermission as any)?.attributes;
    const options = attrs?.Options as { Value: number; Label: string; Color?: string }[] | undefined;
    if (!options || options.length === 0) return FALLBACK_ACCESS_PERMISSION_OPTIONS;
    return options.map(o => ({
      value: o.Value,
      label: o.Label,
      color: o.Color,
    }));
  }

  /**
   * Resolve a bound field's Dataverse display name from the PCF context.
   * Falls back to the provided default when metadata isn't available (e.g.,
   * harness/test environments).
   */
  private getFieldLabel(param: ComponentFramework.PropertyTypes.Property | undefined, fallback: string): string {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const displayName = (param as any)?.attributes?.DisplayName as string | undefined;
    return displayName || fallback;
  }

  // =========================================================================
  // Access-grant modal wiring (task 041, teams-app-r1). Localized to this
  // file per the task's "no baked-in entity/field values in the shared core"
  // discipline (FR-14) — AccessGrantModal receives everything below via
  // callback props and stays entity-agnostic.
  // =========================================================================

  /** The current record's id, via the standard PCF `context.page.entityId`
   * surface (same pattern as `RelatedDocumentCount`'s index.ts). `null` in a
   * harness/test host where `page` isn't populated. */
  private getRecordId(): string | null {
    const page = (this.context as unknown as { page?: { entityId?: string } }).page;
    return page?.entityId || null;
  }

  /** The bound record's entity logical name, via `context.page.entityTypeName`
   * (task 071 — replaces the R1 `sprk_project` hardcode). Falls back to
   * `sprk_project` in a harness/test host where `page` isn't populated. */
  private getHostEntity(): string {
    const page = (this.context as unknown as { page?: { entityTypeName?: string } }).page;
    return page?.entityTypeName || 'sprk_project';
  }

  /** Resolves the polymorphic external-grant root config for the bound host
   * entity (task 070/071). Unknown entities fall back to the project root. */
  private resolveGrantRoot(): IGrantRootConfig {
    return GRANT_ROOT_BY_ENTITY[this.getHostEntity()] ?? DEFAULT_GRANT_ROOT;
  }

  /** Lazily-constructed shared navigation service backing the side-pane Advanced
   * Lookup (task 071). Cached so the contact + org pickers reuse one instance. */
  private navService?: INavigationService;
  private getNavService(): INavigationService {
    if (!this.navService) {
      this.navService = createXrmNavigationService();
    }
    return this.navService;
  }

  /** Real Create-privilege check on `sprk_externalrecordaccess`
   * (`PrivilegeType.Create = 1`, `PrivilegeDepth.Global = 3` per
   * `@types/powerapps-component-framework`). Fails open (returns `true`) only
   * when `hasEntityPrivilege` itself is unavailable/throws (e.g. a harness
   * environment) — `AccessGrantModal` applies a second, defense-in-depth gate
   * on the same value, so a fail-open here does not bypass real authorization
   * (the BFF's own endpoints enforce it server-side regardless). */
  private computeCanGrantAccess(): boolean {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const utils = this.context.utils as any;
      if (typeof utils?.hasEntityPrivilege === 'function') {
        return utils.hasEntityPrivilege(EXTERNAL_ACCESS_ENTITY, /* Create */ 1, /* Global */ 3) as boolean;
      }
    } catch {
      // fall through to fail-open default
    }
    return true;
  }

  /** Wraps `authenticatedFetch` so a click that races MSAL bootstrap still
   * succeeds (awaits `authInitPromise` first) instead of hitting
   * `@spaarke/auth`'s "not initialized" guard. */
  private authenticatedFetchGated = async (url: string, init?: RequestInit): Promise<Response> => {
    await this.authInitPromise;
    return authenticatedFetch(url, init);
  };

  /**
   * Maps the bound `sprk_project.sprk_accesspermission` raw OptionSet value
   * (task 043, spec FR-14 Option A) to `AccessGrantModal`'s entity-agnostic
   * `AccessPermissionState`. This is the ONLY place that knows the real
   * `ACCESS_PERMISSION_*` integers — the shared modal receives only the
   * semantic 'standard' | 'limited' | 'restricted' vocabulary (ADR-012).
   * Defaults to `'standard'` (all grant types available — task 041's
   * baseline) when the value is unset or unrecognized, matching the modal's
   * own default and preserving zero regression for records without the
   * field populated.
   */
  private mapAccessPermissionToState(value: number | null): AccessPermissionState {
    switch (value) {
      case ACCESS_PERMISSION_RESTRICTED:
        return 'restricted';
      case ACCESS_PERMISSION_LIMITED:
        return 'limited';
      default:
        return 'standard';
    }
  }

  /** Reads the current `sprk_project` record's `sprk_assigned*` contact
   * lookups (host-context, single-entity, one `$expand` read — per
   * `DATA-ACCESS-DECISION-CRITERIA.md`) and returns the populated ones as
   * membership candidates, de-duplicated by contact (a contact assigned to
   * two role fields appears once, tagged with the first role encountered). */
  private fetchCandidates = async (): Promise<IAccessGrantCandidate[]> => {
    const recordId = this.getRecordId();
    if (!recordId) return [];

    // task 073 UAT fix: read the role lookups by their FK VALUE fields (`_sprk_X_value`), NOT by the
    // lookup logical name. You cannot `$select` a lookup by its logical name (Dataverse 400 "Could not
    // find a property named 'sprk_assignedattorney1'"), and `$expand` would depend on the PascalCase
    // navigation-property name. The `_X_value` form is stable and carries the contact's display name via
    // the FormattedValue annotation — no nav-property-casing dependency. Emails (which drive the
    // external-vs-internal grant routing) are batch-fetched separately below.
    const valueFields = CANDIDATE_ROLE_FIELDS.map(f => `_${f.attr}_value`).join(',');
    let record: Record<string, unknown>;
    try {
      record = (await this.context.webAPI.retrieveRecord(
        this.getHostEntity(),
        recordId,
        `?$select=${valueFields}`
      )) as unknown as Record<string, unknown>;
    } catch {
      // A host entity may not carry every role field — candidates are a convenience (the named-contact
      // picker still works), so fail SOFT to an empty list rather than breaking the whole modal load.
      return [];
    }

    const FORMATTED = '@OData.Community.Display.V1.FormattedValue';
    const seen = new Set<string>();
    const byId = new Map<string, { role: string; fullName: string }>();
    for (const field of CANDIDATE_ROLE_FIELDS) {
      const contactId = record[`_${field.attr}_value`] as string | undefined;
      if (contactId && !seen.has(contactId)) {
        seen.add(contactId);
        byId.set(contactId, {
          role: field.role,
          fullName: (record[`_${field.attr}_value${FORMATTED}`] as string) ?? '(no name)',
        });
      }
    }
    if (byId.size === 0) return [];

    const emailById = await this.fetchContactEmails([...byId.keys()]);
    return [...byId.entries()].map(([contactId, meta]) => ({
      contactId,
      fullName: meta.fullName,
      email: emailById.get(contactId),
      role: meta.role,
    }));
  };

  /** Batch-fetches emails for a set of contacts (host-context). Email is best-effort — it drives the
   * modal's external-vs-internal grant routing but a miss simply routes as grant-only. */
  private fetchContactEmails = async (contactIds: string[]): Promise<Map<string, string>> => {
    const map = new Map<string, string>();
    if (contactIds.length === 0) return map;
    const filter = contactIds.map(id => `contactid eq ${id}`).join(' or ');
    try {
      const res = await this.context.webAPI.retrieveMultipleRecords(
        'contact',
        `?$select=contactid,emailaddress1&$filter=${filter}`
      );
      for (const e of res.entities) {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const row = e as any;
        if (row.contactid && row.emailaddress1) map.set(row.contactid as string, row.emailaddress1 as string);
      }
    } catch {
      /* email is best-effort — routing falls back to grant-only when unknown */
    }
    return map;
  };

  /** Reads the current record's active `sprk_externalrecordaccess` grants
   * (host-context, single-entity, one `$expand` read). */
  private fetchExistingGrants = async (): Promise<IAccessGrantRecord[]> => {
    const recordId = this.getRecordId();
    if (!recordId) return [];

    // Filter by the bound root's lookup value field (task 070/071 polymorphic
    // read) — e.g. `_sprk_matter_value` on a Matter form. Replaces the R1
    // `_sprk_projectid_value` (invalid field name → matched zero rows).
    // task 073 UAT fix: read the contact + grantedby by their FK VALUE fields + FormattedValue
    // annotations (the contact/systemuser display names) instead of `$expand=sprk_contactid,sprk_grantedby`
    // — those lowercase names are NOT the navigation properties (Dataverse 400 "Could not find a property
    // named 'sprk_contactid'"; the real nav props are PascalCase per task 070). The `_X_value` form is
    // stable and needs no nav-property-casing.
    const FORMATTED = '@OData.Community.Display.V1.FormattedValue';
    const rootValueField = this.resolveGrantRoot().rootValueField;
    const options =
      `?$filter=${rootValueField} eq ${recordId} and statecode eq 0` +
      `&$select=_sprk_contact_value,_sprk_grantedby_value,sprk_accesslevel,sprk_granteddate`;

    let result: ComponentFramework.WebApi.RetrieveMultipleResponse;
    try {
      result = await this.context.webAPI.retrieveMultipleRecords(EXTERNAL_ACCESS_ENTITY, options);
    } catch {
      return [];
    }

    return result.entities.map(e => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const row = e as any;
      return {
        accessRecordId: row.sprk_externalrecordaccessid as string,
        contactId: (row['_sprk_contact_value'] as string) ?? '',
        fullName: (row[`_sprk_contact_value${FORMATTED}`] as string) ?? '(unknown contact)',
        email: undefined,
        accessLevel: row.sprk_accesslevel as number,
        grantedByName: (row[`_sprk_grantedby_value${FORMATTED}`] as string) ?? undefined,
        grantedDate: row.sprk_granteddate ?? undefined,
      };
    });
  };

  /** Named-contact person-picker search (host-context, single-entity, capped
   * at 10 results). */
  private searchContacts = async (query: string): Promise<IContactSearchResult[]> => {
    const escaped = query.replace(/'/g, "''");
    const options =
      `?$filter=contains(fullname,'${escaped}') or contains(emailaddress1,'${escaped}')` +
      `&$select=fullname,emailaddress1&$top=10`;
    const result = await this.context.webAPI.retrieveMultipleRecords('contact', options);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    return result.entities.map((e: any) => ({
      contactId: e.contactid as string,
      fullName: e.fullname as string,
      email: e.emailaddress1 as string | undefined,
    }));
  };

  /** Opens the SHARED side-pane Advanced Lookup for a single Contact (task 071 —
   * INavigationService.openLookup → Xrm.Utility.lookupObjects) and enriches the
   * pick with the contact's email via a host-context read, so the modal's
   * external-vs-internal grant routing (which keys off email) stays correct.
   * Returns `null` when the user cancels. The GUID is already `cleanGuid`-
   * normalized by the adapter, so it is safe to send in an `@odata.bind`. */
  private pickContact = async (): Promise<IContactSearchResult | null> => {
    const results = await this.getNavService().openLookup({
      entityType: 'contact',
      entityTypes: ['contact'],
      allowMultiSelect: false,
    });
    const picked = results[0];
    if (!picked) return null;
    try {
      const rec = (await this.context.webAPI.retrieveRecord(
        'contact',
        picked.id,
        '?$select=fullname,emailaddress1'
      )) as unknown as { fullname?: string; emailaddress1?: string };
      return {
        contactId: picked.id,
        fullName: rec?.fullname ?? picked.name,
        email: rec?.emailaddress1 ?? undefined,
      };
    } catch {
      // Email enrichment failed — still return the pick (routes as grant-only).
      return { contactId: picked.id, fullName: picked.name };
    }
  };

  /** Opens the SHARED side-pane Advanced Lookup for a single `sprk_organization`
   * (task 071) — the optional grantee firm/org sent to the BFF as
   * `organizationId` (the grant's `sprk_Organization` firm-scoping lookup,
   * task 070). Returns `null` when the user cancels. */
  private pickOrganization = async (): Promise<IOrganizationPick | null> => {
    const results = await this.getNavService().openLookup({
      entityType: 'sprk_organization',
      entityTypes: ['sprk_organization'],
      allowMultiSelect: false,
    });
    const picked = results[0];
    return picked ? { id: picked.id, name: picked.name } : null;
  };

  /** Classifies a contact internal-workforce (has a linked `systemuser` via
   * `sprk_primarycontact`) vs external — drives `AccessGrantModal`'s
   * invite-and-grant vs grant-only routing decision. */
  private isInternalContact = async (contactId: string): Promise<boolean> => {
    const options = `?$filter=_sprk_primarycontact_value eq ${contactId}&$select=systemuserid&$top=1`;
    const result = await this.context.webAPI.retrieveMultipleRecords('systemuser', options);
    return result.entities.length > 0;
  };

  /** Sets/clears the contact's standing-grant flag — a single-field Contact
   * write via `context.webAPI` (host-context, NOT a `sprk_externalrecordaccess`
   * write, so it is intentionally outside the BFF grant-endpoint reuse
   * constraint). */
  private onSetStandingGrant = async (contactId: string, standingGrant: boolean): Promise<void> => {
    await this.context.webAPI.updateRecord('contact', contactId, { sprk_standinggrant: standingGrant });
  };

  /** Reads the record's STANDING-grant members (task 073 UAT #2): the record's
   * role-member candidates (`fetchCandidates()`) whose global
   * `contact.sprk_standinggrant` flag is set. The intersection with THIS
   * record's role-members mirrors the server-side union in
   * `AccessibleRecordSetService.ComposeForContactAsync` — a standing contact
   * with no access-conferring role on this record confers no access TO it, so
   * it is intentionally NOT listed here (Eyal Iffergan shows only if he holds a
   * `sprk_assigned*` role on the bound record). Returned rows carry
   * `provenance: 'standing'` and NO `accessRecordId` (there is no per-record
   * `sprk_externalrecordaccess` row to revoke) — the modal renders them
   * non-revocable. `sprk_standinggrant` is field-level-secured: a signed-in user
   * without FLS read on it silently gets an empty list (the query returns no
   * matches), same fail-soft as the server-side reader. */
  private fetchStandingContacts = async (): Promise<IAccessGrantRecord[]> => {
    const candidates = await this.fetchCandidates();
    if (candidates.length === 0) return [];
    const filter = candidates.map(c => `contactid eq ${c.contactId}`).join(' or ');
    let result: ComponentFramework.WebApi.RetrieveMultipleResponse;
    try {
      result = await this.context.webAPI.retrieveMultipleRecords(
        'contact',
        `?$select=contactid&$filter=sprk_standinggrant eq true and (${filter})`
      );
    } catch {
      // FLS denial or query error — standing rows are additive; fail soft so the
      // rest of the Current Access list still loads.
      return [];
    }
    const standingIds = new Set<string>();
    for (const e of result.entities) {
      const row = e as unknown as { contactid?: string };
      if (row.contactid) standingIds.add(row.contactid);
    }
    return candidates
      .filter(c => standingIds.has(c.contactId))
      .map(c => ({
        // No accessRecordId — a standing grant has no per-record row to revoke.
        contactId: c.contactId,
        fullName: c.fullName,
        email: c.email,
        // Placeholder — standing rows render a "Standing" badge, not an
        // access level (the effective level is role-derived server-side). Value
        // is unused by the standing-row render path; 100000000 = ViewOnly.
        accessLevel: 100000000,
        provenance: 'standing' as const,
      }));
  };

  // =========================================================================
  // Email-members wiring (task 042, teams-app-r1). Reuses `fetchCandidates()`
  // above verbatim — the SAME allowlist-filtered `sprk_assigned*` membership-
  // contact data source the grant modal (task 041) uses — per this task's
  // "MUST NOT invent a separate recipient-derivation rule" constraint.
  // =========================================================================

  /** Resolves the email-members recipient list: the current record's
   * membership contacts, deduplicated by email, dropping any candidate with
   * no populated email address (it cannot be pre-filled as a To recipient).
   * An empty result means "no emailable membership contacts" — the caller
   * (the click handler below) shows an empty state instead of opening
   * `SendEmailDialog` with zero recipients. */
  private resolveEmailMembersRecipients = async (): Promise<string[]> => {
    const candidates = await this.fetchCandidates();
    const emails = new Set<string>();
    for (const candidate of candidates) {
      if (candidate.email && candidate.email.trim().length > 0) {
        emails.add(candidate.email.trim());
      }
    }
    return Array.from(emails);
  };

  private closeSendEmailDialog = (): void => {
    this.isSendEmailDialogOpen = false;
    this.renderControl();
  };

  private closeEmailEmptyState = (): void => {
    this.isEmailEmptyStateOpen = false;
    this.renderControl();
  };

  private renderControl(): void {
    // `!== false` treats unset / null / undefined as "not explicitly false"
    // so the manifest default (showTitle=true) wins. Same pattern as
    // VisualHost's showToolbar/showVersion reads.
    const showTitle = this.context.parameters.showTitle?.raw !== false;
    const showVersion = this.context.parameters.showVersion?.raw === true;

    const props: ITrackingFieldTrioProps = {
      monitor: this.monitorValue,
      highPriority: this.highPriorityValue,
      accessPermission: this.accessPermissionValue,
      showTitle,
      showVersion,
      versionText: 'v1.0.17 • Built 2026-08-11',
      accessPermissionOptions: this.getAccessPermissionOptions(),
      // Labels pulled from each bound field's Dataverse metadata so they
      // reflect the actual field display name (localizable, and stays in
      // sync if the field is renamed).
      monitorLabel: this.getFieldLabel(this.context.parameters.monitor, 'Monitor'),
      highPriorityLabel: this.getFieldLabel(this.context.parameters.highPriority, 'High Priority'),
      accessPermissionLabel: this.getFieldLabel(this.context.parameters.accessPermission, 'Access Permission'),
      onMonitorChange: v => {
        this.monitorValue = v;
        this.notifyOutputChanged();
      },
      onHighPriorityChange: v => {
        this.highPriorityValue = v;
        this.notifyOutputChanged();
      },
      onAccessPermissionChange: v => {
        this.accessPermissionValue = v;
        this.notifyOutputChanged();
      },
      // Governance toolbar — person icon opens the real access-grant modal
      // (task 041); email icon opens the canonical SendEmailDialog (task 042).
      onOpenGrantModal: () => {
        this.isGrantModalOpen = true;
        this.renderControl();
      },
      // Email icon (task 042) — resolves the record's membership contacts
      // (reusing fetchCandidates(), same as the grant modal) then either
      // opens the canonical SendEmailDialog pre-populated with those emails,
      // or — if none are emailable — shows the empty-state alert instead of
      // opening a dialog with zero recipients.
      onOpenEmailMembers: () => {
        void (async () => {
          const recipients = await this.resolveEmailMembersRecipients();
          if (recipients.length === 0) {
            this.isEmailEmptyStateOpen = true;
          } else {
            this.emailRecipients = recipients;
            this.isSendEmailDialogOpen = true;
          }
          this.renderControl();
        })();
      },
      // Real Create-privilege check (task 041) — see computeCanGrantAccess().
      canGrantAccess: this.canGrantAccessValue,
    };

    const recordId = this.getRecordId();

    // React 16 API per ADR-022 - use ReactDOM.render, NOT createRoot
    ReactDOM.render(
      React.createElement(
        FluentProvider,
        { theme: webLightTheme, style: { width: '100%' } },
        React.createElement(
          React.Fragment,
          null,
          React.createElement(SharedTrackingFieldTrio, props),
          // task 073 UAT #1 — wrap the dialog subtree in the shared
          // WidgetErrorBoundary so a render error inside a shared dialog (e.g.
          // the EmailComposer engine) degrades to a small inline card instead of
          // blanking the entire control. Defense-in-depth: the root cause (a
          // duplicate React 19 bundled via Lexical's react/jsx-runtime subpath)
          // is fixed at the build layer in ../webpack.config.js.
          React.createElement(WidgetErrorBoundary, {
            widgetType: 'tracking-field-trio-dialogs',
            displayName: 'Access & Email',
            surface: 'TrackingFieldTrio',
            // Children via the `children` prop (not createElement rest-args):
            // WidgetErrorBoundaryProps types `children` as required, which the
            // rest-args overload of React.createElement does not satisfy (same
            // reason as the empty-state Dialog below). React.Fragment DOES
            // accept rest-args, so wrap the dialog subtree in one.
            children: React.createElement(
              React.Fragment,
              null,
              // Access-grant modal (task 041) — always mounted so AccessGrantModal's
              // own `open`-driven effect controls data loading; `recordId` is only
              // resolvable once the control is bound to a real record (harness
              // environments render the toolbar but the modal has nothing to open).
              recordId
                ? React.createElement(AccessGrantModal, {
                    open: this.isGrantModalOpen,
                    onClose: () => {
                      this.isGrantModalOpen = false;
                      this.renderControl();
                    },
                    recordId,
                    // Polymorphic root type derived from the bound host entity
                    // (task 071) — the modal sends {recordType, recordId}.
                    recordType: this.resolveGrantRoot().recordType,
                    canGrantAccess: this.canGrantAccessValue,
                    authenticatedFetch: this.authenticatedFetchGated,
                    fetchCandidates: this.fetchCandidates,
                    fetchExistingGrants: this.fetchExistingGrants,
                    // Standing-grant members (task 073 UAT #2) — role-members whose
                    // global sprk_standinggrant flag is set, merged into Current Access.
                    fetchStandingContacts: this.fetchStandingContacts,
                    searchContacts: this.searchContacts,
                    // Shared side-pane Advanced Lookup (task 071) — replaces the
                    // inline Combobox contact picker; adds an optional org picker.
                    pickContact: this.pickContact,
                    pickOrganization: this.pickOrganization,
                    isInternalContact: this.isInternalContact,
                    onSetStandingGrant: this.onSetStandingGrant,
                    // Access-Permission sharing gate (task 043, FR-14 Option A) —
                    // mapped from the bound field's raw OptionSet value; see
                    // mapAccessPermissionToState()'s doc comment.
                    accessPermissionState: this.mapAccessPermissionToState(this.accessPermissionValue),
                  })
                : null,
              // Canonical SendEmailDialog (task 042) — pre-populated with the
              // record's membership-contact emails (resolveEmailMembersRecipients()
              // above). Send flows through the engine's OWN sendCommunication()
              // call (ADR-045) — no custom send logic here. Gated on `recordId`
              // for the same reason as the grant modal above.
              recordId
                ? React.createElement(SendEmailDialog, {
                    open: this.isSendEmailDialogOpen,
                    onClose: this.closeSendEmailDialog,
                    initialTo: this.emailRecipients,
                    authenticatedFetch: this.authenticatedFetchGated,
                    bffBaseUrl: this.apiBaseUrl,
                    titleOverride: 'Email Members',
                    regarding: { entityType: this.getHostEntity(), id: recordId },
                    onSent: () => {
                      this.isSendEmailDialogOpen = false;
                      this.renderControl();
                    },
                    onError: (err: Error) => {
                      console.error('[TrackingFieldTrio] Email-members send failed.', err);
                    },
                  })
                : null,
              // Empty-state alert (task 042) — shown INSTEAD of SendEmailDialog
              // when the record has no membership contacts with a populated
              // email, so the dialog never opens with zero recipients.
              React.createElement(Dialog, {
                open: this.isEmailEmptyStateOpen,
                onOpenChange: (_event: unknown, data: { open: boolean }) => {
                  if (!data.open) this.closeEmailEmptyState();
                },
                // Passed via the `children` prop (not createElement rest-args) —
                // Fluent v9's `Dialog` types `children` as required on `DialogProps`,
                // which the rest-args overload of `React.createElement` does not
                // satisfy.
                children: React.createElement(
                  DialogSurface,
                  null,
                  React.createElement(
                    DialogBody,
                    null,
                    React.createElement(DialogTitle, null, 'Email members'),
                    React.createElement(
                      DialogContent,
                      null,
                      'This record has no membership contacts with an email address yet. Grant access or assign a role first.'
                    ),
                    React.createElement(
                      DialogActions,
                      null,
                      React.createElement(Button, { appearance: 'primary', onClick: this.closeEmailEmptyState }, 'OK')
                    )
                  )
                ),
              })
            ),
          })
        )
      ),
      this.container
    );
  }

  public getOutputs(): IOutputs {
    return {
      monitor: this.monitorValue,
      highPriority: this.highPriorityValue,
      accessPermission: this.accessPermissionValue ?? undefined,
    };
  }

  public destroy(): void {
    // React 16 API per ADR-022 - use unmountComponentAtNode, NOT root.unmount()
    ReactDOM.unmountComponentAtNode(this.container);
  }
}
