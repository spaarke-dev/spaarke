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
} from '@spaarke/ui-components/dist/components/AccessGrantModal';
// Canonical `SendEmailDialog` (task 042, ADR-045) — the `EmailComposer`
// wrapper that owns the Dialog chrome + `sendCommunication()` send flow.
// This is the ONLY email-send surface this control is allowed to open (no
// forked dialog, no ad-hoc fetch to the send endpoint).
import { SendEmailDialog } from '@spaarke/ui-components/dist/components/EmailComposer';
import { initializeAuth } from './authInit';

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

// task 041 (teams-app-r1) — the ONLY entity this control's access-grant
// modal supports in R1: `sprk_externalrecordaccess` is `sprk_project`-scoped
// per design.md §5 ("known gap #2" — extending to matters/other entities is
// R2). This constant + the role-field map below are entity-specific
// knowledge that lives ONLY in this file (FR-14 discipline), never in the
// shared `AccessGrantModal` core.
const GRANT_RECORD_ENTITY = 'sprk_project';
const EXTERNAL_ACCESS_ENTITY = 'sprk_externalrecordaccess';

// The R1-verified access-conferring role-field set on `sprk_project`
// (task 021's convention-based discovery, mirrored client-side per
// `notes/pipeline-run-guidance.md`). A newly-added `sprk_assigned*` field
// does NOT auto-qualify here the way it does in the server-side metadata
// discovery (task 021/022) — this client-side list is a UI convenience for
// the candidate section, not the security enforcement path (enforcement is
// entirely server-side per design.md §5). If the role-field set changes,
// update this list; nothing security-load-bearing depends on it being
// exhaustive.
const CANDIDATE_ROLE_FIELDS: ReadonlyArray<{ attr: string; role: string }> = [
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
  const xrm = (window as unknown as { Xrm?: { Utility?: { getGlobalContext?: () => { getClientUrl?: () => string } } } }).Xrm;
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
    this.apiBaseUrl = params.apiBaseUrl?.raw ?? '';
    this.authInitPromise = initializeAuth(
      params.clientAppId?.raw ?? '',
      params.bffAppId?.raw ?? '',
      this.apiBaseUrl,
      getClientUrl()
    ).catch(err => {
      // eslint-disable-next-line no-console
      console.error(
        '[TrackingFieldTrio] Auth initialization failed — the access-grant modal\'s BFF calls will fail until the page is reloaded.',
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

  /** Reads the current `sprk_project` record's `sprk_assigned*` contact
   * lookups (host-context, single-entity, one `$expand` read — per
   * `DATA-ACCESS-DECISION-CRITERIA.md`) and returns the populated ones as
   * membership candidates, de-duplicated by contact (a contact assigned to
   * two role fields appears once, tagged with the first role encountered). */
  private fetchCandidates = async (): Promise<IAccessGrantCandidate[]> => {
    const recordId = this.getRecordId();
    if (!recordId) return [];

    const select = CANDIDATE_ROLE_FIELDS.map(f => f.attr).join(',');
    const expand = CANDIDATE_ROLE_FIELDS.map(f => `${f.attr}($select=fullname,emailaddress1)`).join(',');
    const record = (await this.context.webAPI.retrieveRecord(
      GRANT_RECORD_ENTITY,
      recordId,
      `?$select=${select}&$expand=${expand}`
    )) as unknown as Record<string, { contactid?: string; fullname?: string; emailaddress1?: string } | null | undefined>;

    const seen = new Set<string>();
    const candidates: IAccessGrantCandidate[] = [];
    for (const field of CANDIDATE_ROLE_FIELDS) {
      const nav = record[field.attr];
      if (nav?.contactid && !seen.has(nav.contactid)) {
        seen.add(nav.contactid);
        candidates.push({
          contactId: nav.contactid,
          fullName: nav.fullname ?? '(no name)',
          email: nav.emailaddress1 ?? undefined,
          role: field.role,
        });
      }
    }
    return candidates;
  };

  /** Reads the current record's active `sprk_externalrecordaccess` grants
   * (host-context, single-entity, one `$expand` read). */
  private fetchExistingGrants = async (): Promise<IAccessGrantRecord[]> => {
    const recordId = this.getRecordId();
    if (!recordId) return [];

    const options =
      `?$filter=_sprk_projectid_value eq ${recordId} and statecode eq 0` +
      `&$select=sprk_accesslevel,sprk_granteddate` +
      `&$expand=sprk_contactid($select=fullname,emailaddress1),sprk_grantedby($select=fullname)`;
    const result = await this.context.webAPI.retrieveMultipleRecords(EXTERNAL_ACCESS_ENTITY, options);

    return result.entities.map(e => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const row = e as any;
      const contact = row.sprk_contactid as { contactid?: string; fullname?: string; emailaddress1?: string } | undefined;
      const grantedBy = row.sprk_grantedby as { fullname?: string } | undefined;
      return {
        accessRecordId: row.sprk_externalrecordaccessid as string,
        contactId: contact?.contactid ?? '',
        fullName: contact?.fullname ?? '(unknown contact)',
        email: contact?.emailaddress1 ?? undefined,
        accessLevel: row.sprk_accesslevel as number,
        grantedByName: grantedBy?.fullname ?? undefined,
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
      versionText: 'v1.0.10 • Built 2026-08-04',
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
                canGrantAccess: this.canGrantAccessValue,
                authenticatedFetch: this.authenticatedFetchGated,
                fetchCandidates: this.fetchCandidates,
                fetchExistingGrants: this.fetchExistingGrants,
                searchContacts: this.searchContacts,
                isInternalContact: this.isInternalContact,
                onSetStandingGrant: this.onSetStandingGrant,
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
                regarding: { entityType: GRANT_RECORD_ENTITY, id: recordId },
                onSent: () => {
                  this.isSendEmailDialogOpen = false;
                  this.renderControl();
                },
                onError: (err: Error) => {
                  // eslint-disable-next-line no-console
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
