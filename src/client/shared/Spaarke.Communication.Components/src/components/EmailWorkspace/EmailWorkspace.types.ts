/**
 * EmailWorkspace.types.ts
 *
 * Prop contract for `<EmailWorkspace />` — the SINGLE shared React 19
 * composition root for the Outlook-style Email surface (email-communication-
 * solution-r5 task 040, spec FR-01 / NFR-06 / Success Criterion 1). Both
 * mounts (the `email` SpaarkeAi workspace widget, task 041, and the
 * standalone Email code page, task 042) render THIS component unchanged —
 * dual-mount parity means a fix here propagates to both surfaces.
 *
 * Every prop is host-agnostic (ADR-012): no PCF `ComponentFramework` type, no
 * `Xrm` import. The assembling mount resolves the concrete Xrm-backed or
 * BFF-backed adapters (`createXrmDataService()`/`createXrmNavigationService()`
 * from `@spaarke/ui-components`, or a BFF equivalent) and hands them in as
 * plain objects/functions — `EmailWorkspace` never knows which host it runs
 * under (NFR-06: no `isWidget`/`isCodePage` branch anywhere in this package).
 */
import type * as React from 'react';
import type {
  IDataverseClient,
  IDataService,
  INavigationService,
  AuthenticatedFetchFn,
  IAccessPermissionOption,
  RecordTypeCatalogEntry,
  IPolymorphicPickerWebApi,
  ILookupItem,
  IRecordLookupTarget,
  IPickedRecord,
  IRecipient,
  IEmailTemplateSummary,
  IEmailTemplateRenderResult,
  IEmailAiDraftAction,
  IEmailAiDraftRequest,
  IEmailAiDraftResult,
} from '@spaarke/ui-components';
import type { IResolverWriteContext } from '../../logic/connections';
import type { EmailWorkspaceVisibleState } from './EmailWorkspace.mapping';

/**
 * The webApi bridge this workspace needs for the additive associations write
 * path (`IResolverWriteContext['webApi']` — `retrieveMultipleRecords` +
 * `updateRecord`, task 020) AND the embedded `PolymorphicPicker`'s optional
 * `retrieveRecord`/`retrieveMultipleRecords` surface (task 035). One object
 * satisfies both — an Xrm.WebApi-compatible bridge or a BFF equivalent.
 */
export type EmailWorkspaceWebApi = IResolverWriteContext['webApi'] & IPolymorphicPickerWebApi;

export interface EmailWorkspaceProps {
  /**
   * Dataverse access adapter for saved-view discovery + FetchXML execution
   * (ADR-012/ADR-028) — drives the left `ViewSelector` + `EmailCardList` via
   * the task-031 `useEmailViews` hook. Xrm- or BFF-backed.
   */
  dataverseClient: IDataverseClient;
  /**
   * Structural `IDataService` used by the header (034), attachments (034),
   * connections (035), and tracking (035) sub-views, plus this component's
   * own per-selection record read (envelope/tracking/associations state) and
   * the `.eml` archive-document lookup. Typically the SAME underlying access
   * as `dataverseClient` exposed via the narrower `IDataService` shape (e.g.
   * `createXrmDataService()`) — not necessarily the same object instance,
   * but MUST resolve the same `sprk_communication` rows.
   */
  dataService: IDataService;
  /**
   * Navigation adapter — "open record" (attachments, task 034) and "open full
   * form" (FR-15, the 85% `navigateTo` modal via `useEmailComposeActions`).
   */
  navigationService: INavigationService;
  /**
   * webApi bridge for the associations additive-write path (035) — see
   * {@link EmailWorkspaceWebApi}.
   */
  webApi: EmailWorkspaceWebApi;
  /** Auth-aware fetch (ADR-028) — `.eml` render (033) + composer send (036). */
  authenticatedFetch: AuthenticatedFetchFn;
  /** BFF base URL (no `/api` suffix) — composer + attachments preview/open-link resolution. */
  bffBaseUrl: string;
  /**
   * `sprk_communication` Access Permission OptionSet metadata (value + label +
   * optional color; FR-14). Entity-specific — supplied by the host, mirroring
   * the `TrackingFieldTrio` PCF caller's own `getAccessPermissionOptions()`
   * convention (task 023). Defaults to the same Standard/Limited/Restricted
   * fallback triple the PCF uses when live OptionSet metadata is unavailable.
   */
  accessPermissionOptions?: IAccessPermissionOption[];
  /** Recipient directory typeahead for the composer (optional; forwarded to `RecipientField`). */
  onSearchRecipients?: (query: string) => Promise<ILookupItem[]>;
  /**
   * Advanced OOB recipient lookup for the composer (To/Cc/Bcc label click →
   * people picker). Optional — the assembling mount supplies the Xrm-backed
   * handler (`createXrmEmailComposeHandlers`); omitted → typeahead only.
   */
  onLookupRecipients?: (field: 'to' | 'cc' | 'bcc') => Promise<IRecipient[] | null>;
  /**
   * Body-toolbar record-lookup catalog + host lookup ("link a document" /
   * "insert a link to a record"). Optional — mount-supplied Xrm handlers;
   * omitted → those toolbar tools are hidden.
   */
  recordLookupCatalog?: IRecordLookupTarget[];
  onLookupRecord?: (entityType: string) => Promise<IPickedRecord | null>;
  /**
   * Add-a-relationship (connector toolbar icon). Optional — mount-supplied Xrm
   * handler; omitted → the connector tool is hidden.
   */
  onAddRelationship?: () => Promise<IPickedRecord | null>;
  /**
   * New-file upload for the composer (item 9b) — resolves a locally-picked file to a
   * governed `sprk_document`. Optional — mount-supplied Xrm handler
   * (`createXrmEmailComposeHandlers` with auth + BFF URL); omitted → local picks stay
   * display-only.
   */
  onUploadLocalAttachment?: (file: File) => Promise<{ documentId: string; driveItemId?: string; linkUrl?: string }>;
  /**
   * Resolve a recipient-openable SPE sharing link for a linked document (R2 item 12). Mount-supplied
   * Xrm/BFF handler; omitted → links keep their original internal URL.
   */
  onResolveShareLink?: (documentId: string) => Promise<string | null>;
  /**
   * Compose template picker (Wave E) — `onListEmailTemplates` lists OOB `template` records,
   * `onRenderEmailTemplate` renders the chosen one (merging field codes from the primary
   * regarding). Forwarded to the composer via `useEmailComposeActions`. Mount-supplied Xrm/BFF
   * handlers; omit either → the toolbar template button is hidden.
   */
  onListEmailTemplates?: () => Promise<IEmailTemplateSummary[]>;
  onRenderEmailTemplate?: (args: {
    templateId: string;
    regardingEntityType?: string;
    regardingRecordId?: string;
  }) => Promise<IEmailTemplateRenderResult>;
  /**
   * AI "sparkle" draft (Wave E). `onDraftWithAi` generates/refines the compose body via the BFF;
   * optional `aiDraftActions` overrides the preset quick-actions. Mount-supplied Xrm/BFF handler;
   * omit `onDraftWithAi` → the sparkle button is hidden.
   */
  onDraftWithAi?: (req: IEmailAiDraftRequest) => Promise<IEmailAiDraftResult>;
  aiDraftActions?: IEmailAiDraftAction[];
  /**
   * The signed-in user's mailbox address (item 3) — the compose "From:" row defaults to
   * send-as the current user and shows this address. Mount-resolved (Xrm). Optional.
   */
  fromMailbox?: string;
  /**
   * Dataverse client URL (no trailing slash) — used to build deep-links for the
   * parent email's carried-forward attachments. Optional.
   */
  dataverseUrl?: string;
  /** "Link another record…" catalog override for the associations review (optional — defaults to the `TODO_REGARDING_CATALOG`-derived catalog already built into `EmailConnectionsReview`). */
  linkAnotherCatalog?: readonly RecordTypeCatalogEntry[];
  /** Optional id to select on first mount (forwarded to `EmailReadingPaneShell`). */
  initialSelectedId?: string;
  /**
   * Single-record ("hide list") mode. When `true` AND an `initialSelectedId` is
   * set, the left list pane is hidden and only the reading pane for that record
   * renders — so the surface reads like a per-record "form" (Pattern B
   * `single`/`view=record` launch) rather than the list+reading workspace.
   * Additive + optional: default (falsy) preserves the exact list+reading layout,
   * so the SpaarkeAi widget mount and the standalone code-page list mode are
   * unaffected. Forwarded to `EmailReadingPaneShell`'s `hideList`.
   */
  hideList?: boolean;
  /**
   * spaarkeai-assistant-enhancements-r2 task 042b (FR-C1) — additive, optional
   * "values out" callback (same pattern as the shell's `onSelectedIdChange` /
   * the attachments view's `onCountChange`): surfaces the COMPACT,
   * agent-visible-adjacent snapshot of the currently-selected email
   * ({@link EmailWorkspaceVisibleState}) whenever the selection or its resolved
   * record changes, and `null` when nothing is selected / the read is loading or
   * failed. Recomputed from the SAME single per-selection read (`record`) — no
   * second Dataverse call. The SpaarkeAi `email` widget mount forwards this to
   * `WorkspaceWidgetProps.onDataChange` so the compact shape persists into the
   * tab's `WorkspaceTab.widgetData` (Path 1 persisted-Email carrier). The
   * standalone code-page mount omits it → zero behavior change (NFR-06).
   *
   * spaarkeai-assistant-enhancements-r3 task 012 (FR-05): the surfaced state now
   * also carries `communicationId` (the selected `sprk_communicationid`) — the
   * identity anchor the SpaarkeAi `email` widget wrapper redirects to the
   * widget-agnostic active-item conduit as `{ id, type: 'email', label: subject }`
   * (id/label only, NEVER the email body/content — ADR-015). `EmailWorkspace`
   * itself stays host-agnostic: it only emits the compact shape via this prop —
   * it does NOT know about the conduit (ADR-012).
   */
  onVisibleEmailChange?: (state: EmailWorkspaceVisibleState | null) => void;
}
