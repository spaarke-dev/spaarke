/**
 * CommunicationActionsApp — action bar for the OOB sprk_communication form.
 *
 * Exposes Reply / Forward / Send / Save Draft / Save to SharePoint, hosting the
 * shared `<SendEmailPage/>` (task 020/021) in a dialog for compose/reply/forward
 * and calling the EXISTING BFF endpoints via `@spaarke/auth` `authenticatedFetch`
 * (ADR-028): `/api/communications/send` (through the composer) and the new
 * `/api/communications/{id}/archive` (task 044a). Replaces the 1,150-LOC ribbon
 * `sprk_communication_send.js` (W4 pivot).
 *
 * No re-implemented send/archival — the composer + endpoints own that. No
 * self-built credential: auth is bootstrapped once via `initializeAuth`.
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Toolbar,
  ToolbarButton,
  ToolbarDivider,
  Tooltip,
  Spinner,
  Dialog,
  DialogSurface,
  DialogBody,
  MessageBar,
  MessageBarBody,
  Text,
} from '@fluentui/react-components';
import {
  ArrowReply20Regular,
  ArrowReplyAll20Regular,
  ArrowForward20Regular,
  Mail20Regular,
  CloudArrowUp20Regular,
  CalendarLtr20Regular,
  CheckmarkCircle20Regular,
  Receipt20Regular,
} from '@fluentui/react-icons';
import { authenticatedFetch } from '@spaarke/auth';
import {
  SendEmailPage,
  type ISendEmailPageProps,
  type EmailComposerMode,
  type IAttachmentItem,
  type IRecordLookupTarget,
  type IPickedRecord,
  type IRecipient,
  type ICommunicationAssociation,
  searchUsersAndContacts,
  createXrmDataService,
} from '@spaarke/ui-components';
import { IInputs } from './generated/ManifestTypes';
import { initializeAuth, resolveDataverseUrl } from './authInit';
// Task 022: the Layer-1 action-bar / composer-prefill / suggested-create logic now
// lives in `@spaarke/communication-components` — the local `./composerPrefill`,
// `./attachmentsSource`, and `./launchCreate` copies are deleted
// (email-communication-solution-r5 FR-08/FR-18 two-layer split).
import {
  deriveComposerFields,
  type ComposerMode,
  fetchSourceAttachments,
  launchCreate,
  type CreateKind,
} from '@spaarke/communication-components/logic/actions';
import { getMsalClientId, getBffApiAppId, getApiBaseUrl } from '../../shared/utils/environmentVariables';

// React 16 type seam: the shared lib's .d.ts is emitted against React 19 types,
// whose FC return type is incompatible with React 16's JSX element type. Cast at
// the boundary (same pattern as RegardingResolver's PolymorphicPicker). Runtime
// is unaffected — the compiled module is identical.
const SendEmailPageR16 = SendEmailPage as unknown as React.ComponentType<ISendEmailPageProps>;

// sprk_communicationtype = Email (task-002 verified) — the only interactive channel.
const COMMUNICATION_TYPE_EMAIL = 100000000;

// Record-lookup targets for the composer's "look up a record" tool (owner UAT round 5,
// RegardingResolver set). Document first (the primary attach case); the rest are linked.
const RECORD_LOOKUP_CATALOG: IRecordLookupTarget[] = [
  { logicalName: 'sprk_document', displayName: 'Document' },
  { logicalName: 'sprk_matter', displayName: 'Matter' },
  { logicalName: 'sprk_project', displayName: 'Project' },
  { logicalName: 'sprk_event', displayName: 'Event' },
  { logicalName: 'sprk_communication', displayName: 'Communication' },
  { logicalName: 'sprk_workassignment', displayName: 'Work Assignment' },
  { logicalName: 'sprk_invoice', displayName: 'Invoice' },
  { logicalName: 'sprk_budget', displayName: 'Budget' },
  { logicalName: 'sprk_analysis', displayName: 'Analysis' },
  { logicalName: 'sprk_organization', displayName: 'Organization' },
  { logicalName: 'contact', displayName: 'Contact' },
];

// Entity types the connector's "add a relationship" picker offers — the regarding-able
// records (a Document is an attachment, not a regarding relationship, so it's excluded).
const REGARDING_ENTITY_TYPES = RECORD_LOOKUP_CATALOG.filter(c => c.logicalName !== 'sprk_document').map(
  c => c.logicalName
);

// The `sprk_communication` denormalized regarding lookups (field → target entity), used to
// read a parent's filed associations so reply/reply-all/forward INHERIT them into the child
// (owner UAT 2026-07-24). These are the COMMUNICATION regarding columns (NOT the sprk_todo
// TODO_REGARDING_CATALOG names) — mirrors CommunicationConnections' ENTITY_TO_SLOT.
const COMMUNICATION_REGARDING_FIELDS: { field: string; entityType: string }[] = [
  { field: 'sprk_regardingmatter', entityType: 'sprk_matter' },
  { field: 'sprk_regardingproject', entityType: 'sprk_project' },
  { field: 'sprk_regardingorganization', entityType: 'sprk_organization' },
  { field: 'sprk_regardingaccount', entityType: 'account' },
  { field: 'sprk_regardingperson', entityType: 'contact' },
  { field: 'sprk_regardinginvoice', entityType: 'sprk_invoice' },
  { field: 'sprk_regardingservicerequest', entityType: 'sprk_servicerequest' },
  { field: 'sprk_regardingevent', entityType: 'sprk_event' },
  { field: 'sprk_regardingworkassignment', entityType: 'sprk_workassignment' },
];

// "Create from this email" flows. Target-entity mapping + the modal launch itself
// live in the `launchCreate` seam (CreateKind imported above) so OOB↔custom is
// swappable without touching the button call sites (UAT R3 C11-3, owner requirement).

/**
 * Parse the association provenance JSON and decide which "create from this email"
 * actions the engine's structural signals suggest (mirrors the Connections PCF's
 * deriveCreateActions). Best-effort: bad/empty JSON → no suggestions.
 */
function deriveSuggestedCreates(provenanceRaw: string | null | undefined): Set<CreateKind> {
  const out = new Set<CreateKind>();
  if (!provenanceRaw) return out;
  try {
    const doc = JSON.parse(provenanceRaw) as {
      signals?: { category?: string; obligations?: string[] }[];
    };
    for (const sig of doc.signals ?? []) {
      const obligations = sig.obligations ?? [];
      if (sig.category === 'invoice') out.add('invoice');
      if (sig.category === 'event' || obligations.includes('calendar-response')) out.add('event');
      if (obligations.includes('deadline-response') || obligations.includes('payment-review')) out.add('todo');
    }
  } catch {
    /* ignore malformed provenance */
  }
  return out;
}

const useStyles = makeStyles({
  root: { height: '100%', width: '100%', display: 'flex', flexDirection: 'column' },
  // Compact single row to match the OOB command bar height (~32px).
  barRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    paddingBlock: tokens.spacingVerticalXXS,
    paddingInline: tokens.spacingHorizontalXS,
    minHeight: '32px',
  },
  bar: { flexWrap: 'wrap', flexGrow: 1 },
  // Match the OOB command-bar typography — 14px, regular weight (the default
  // ToolbarButton renders heavier/larger than the native bar). C11-1: icon slot
  // pinned to 20×20 to match the OOB command-bar icon sizing.
  toolbarBtn: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightRegular,
    '& .fui-Button__icon': { fontSize: '20px', width: '20px', height: '20px' },
    '& svg': { fontSize: '20px', width: '20px', height: '20px' },
  },
  // Icon-only actions (C11-1): 20×20 glyphs.
  iconBtn: {
    '& .fui-Button__icon': { fontSize: '20px', width: '20px', height: '20px' },
    '& svg': { fontSize: '20px', width: '20px', height: '20px' },
  },
  rightGroup: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalXS },
  // C11-2: the divider + icon-only cluster are right-aligned within the toolbar.
  // `marginInlineStart:auto` on the divider consumes the free space so the divider and
  // the icon group hug the toolbar's right edge (labelled buttons keep their left placement).
  dividerPush: { marginInlineStart: 'auto' },
  // Suggested-by-engine create icons get a subtle brand tint (the icon-only ✨ cue).
  suggestedIcon: { color: tokens.colorBrandForeground1 },
  notice: { paddingInline: tokens.spacingHorizontalM, paddingBottom: tokens.spacingVerticalXS },
  // Standard Spaarke mid-size modal rectangle (owner UAT 2026-07-22): 720px × 70vh — matches the
  // shared SendEmailDialog (Assistant / FilePreview / SummarizeFiles). Surface owns the bounded
  // height + flex column; dialogBody is the scroll region so the composer content scrolls while the
  // modal stays a clean rectangle (was 900px × 85vh).
  dialogSurface: {
    // Landscape mid-size rectangle (owner UAT 2026-07-22 #1): 720px read as portrait; widen.
    maxWidth: '1040px',
    width: '92vw',
    height: '72vh',
    padding: 0,
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
  },
  // The composer owns the scroll region (pinned header/footer + scrollable body, owner
  // UAT round 3), so the dialog body fills the fixed surface and never scrolls itself.
  dialogBody: { minHeight: 0, flexGrow: 1, overflow: 'hidden', display: 'flex', flexDirection: 'column' },
  versionText: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground4,
    whiteSpace: 'nowrap',
    paddingRight: tokens.spacingHorizontalS,
  },
});

/** Walk window/parent frames to locate Xrm (PCF runs in an iframe). */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
function getXrm(): any {
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const w = window as any;
    return w.Xrm ?? w.parent?.Xrm ?? w.top?.Xrm;
  } catch {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    return (window as any).Xrm;
  }
}

function getHostRecordId(): string | undefined {
  const xrm = getXrm();
  try {
    const id = xrm?.Page?.data?.entity?.getId?.();
    if (typeof id === 'string' && id.length > 0) return id.replace(/[{}]/g, '');
  } catch {
    /* ignore */
  }
  return undefined;
}

async function refreshHostForm(): Promise<void> {
  const xrm = getXrm();
  try {
    const refresh = xrm?.Page?.data?.refresh;
    if (typeof refresh === 'function') {
      const r = refresh.call(xrm.Page.data, false);
      if (r && typeof r.then === 'function') await r;
    }
  } catch (err) {
    console.warn('[CommunicationActions] form refresh failed:', err);
  }
}

interface IRecordPrefill {
  from: string;
  to: string;
  cc: string;
  subject: string;
  body: string;
  /** Formatted sent/created date for the quoted-thread header (owner UAT 2026-07-24). */
  sent?: string;
}

export interface ICommunicationActionsAppProps {
  context: ComponentFramework.Context<IInputs>;
  readOnly: boolean;
  version: string;
}

export const CommunicationActionsApp: React.FC<ICommunicationActionsAppProps> = ({ context, readOnly, version }) => {
  const s = useStyles();

  const manifestClientAppId = (context.parameters.clientAppId?.raw ?? '').trim();
  const manifestBffAppId = (context.parameters.bffAppId?.raw ?? '').trim();
  const manifestApiBaseUrl = (context.parameters.apiBaseUrl?.raw ?? '').trim();
  const showVersionFooter = context.parameters.showVersionFooter?.raw !== false;
  const channelValue = context.parameters.communicationType?.raw;
  const isEmail = channelValue == null || channelValue === COMMUNICATION_TYPE_EMAIL;

  const communicationId = React.useMemo(() => getHostRecordId(), []);
  const webApi = context.webAPI;

  const [authReady, setAuthReady] = React.useState(false);
  const [authError, setAuthError] = React.useState<string | null>(null);
  const [bffBaseUrl, setBffBaseUrl] = React.useState('');
  const [error, setError] = React.useState<string | null>(null);
  const [status, setStatus] = React.useState<string | null>(null);
  const [busy, setBusy] = React.useState(false);
  const [composerMode, setComposerMode] = React.useState<ComposerMode | null>(null);
  const [prefill, setPrefill] = React.useState<IRecordPrefill | null>(null);
  // Source-communication attachment documents, offered for inclusion on reply/forward
  // (task 104). Enumerated once from the record via the shared attachment data model.
  const [sourceAttachments, setSourceAttachments] = React.useState<IAttachmentItem[]>([]);
  // Parent communication's filed associations — inherited into reply/reply-all/forward so the
  // child "Related to" carries them (owner UAT 2026-07-24). Written onto the child on send.
  const [parentAssociations, setParentAssociations] = React.useState<ICommunicationAssociation[]>([]);
  // Which "create from this email" actions the engine flagged (from the provenance
  // signals) — drives the subtle ✨ brand tint on the icon-only create buttons.
  const [suggestedCreates, setSuggestedCreates] = React.useState<Set<CreateKind>>(new Set());

  // Bootstrap @spaarke/auth once. Config resolves from the PCF manifest inputs FIRST,
  // then falls back to the Dataverse environment variables (sprk_MsalClientId /
  // sprk_BffApiAppId / sprk_BffApiBaseUrl) — the same pattern SemanticSearch uses, so
  // the control works with zero form config where those env vars are set.
  React.useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const clientAppId = manifestClientAppId || (await getMsalClientId(webApi));
        const bffAppId = manifestBffAppId || (await getBffApiAppId(webApi));
        // getApiBaseUrl returns host-only (strips /api); @spaarke/auth re-adds /api.
        const baseUrl = manifestApiBaseUrl || (await getApiBaseUrl(webApi));
        if (!clientAppId || !bffAppId || !baseUrl) {
          if (!cancelled) {
            setAuthError(
              'Communication actions are not configured. Set the sprk_MsalClientId / sprk_BffApiAppId / ' +
                'sprk_BffApiBaseUrl Dataverse environment variables (or the PCF Client App ID / BFF App ID / API URL inputs).'
            );
          }
          return;
        }
        await initializeAuth(clientAppId, bffAppId, baseUrl, resolveDataverseUrl());
        if (cancelled) return;
        setBffBaseUrl(baseUrl);
        setAuthReady(true);
      } catch (err) {
        if (!cancelled) setAuthError(err instanceof Error ? err.message : 'Authentication failed to initialize.');
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [manifestClientAppId, manifestBffAppId, manifestApiBaseUrl, webApi]);

  // Load the record fields the composer needs for reply/forward pre-fill.
  React.useEffect(() => {
    if (!communicationId) return;
    let cancelled = false;
    void (async () => {
      try {
        // Lookup columns are queryable in $select ONLY via their `_<field>_value` form — the bare
        // navigation name (e.g. `sprk_regardingmatter`) returns 400, which would throw the whole
        // retrieve and leave `prefill` null → Reply/Reply All/Forward stay greyed (owner UAT 2026-07-27).
        const regardingSelect = COMMUNICATION_REGARDING_FIELDS.map(m => `_${m.field}_value`).join(',');
        const rec = await context.webAPI.retrieveRecord(
          'sprk_communication',
          communicationId,
          `?$select=sprk_from,sprk_to,sprk_cc,sprk_subject,sprk_body,sprk_associationprovenance,createdon,${regardingSelect}`
        );
        if (cancelled) return;
        const recAny = rec as Record<string, unknown>;
        setPrefill({
          from: (rec.sprk_from as string) ?? '',
          to: (rec.sprk_to as string) ?? '',
          cc: (rec.sprk_cc as string) ?? '',
          subject: (rec.sprk_subject as string) ?? '',
          body: (rec.sprk_body as string) ?? '',
          sent: (recAny['createdon@OData.Community.Display.V1.FormattedValue'] as string) ?? '',
        });
        // Inherit the parent's filed regarding associations (owner UAT 2026-07-24) — read each
        // populated denormalized lookup + its formatted name/logical-name annotations.
        const base = resolveDataverseUrl();
        const inherited: ICommunicationAssociation[] = [];
        for (const m of COMMUNICATION_REGARDING_FIELDS) {
          const val = recAny[`_${m.field}_value`];
          if (typeof val !== 'string' || val.length === 0) continue;
          const entityId = val.replace(/[{}]/g, '').toLowerCase();
          const entityType =
            (recAny[`_${m.field}_value@Microsoft.Dynamics.CRM.lookuplogicalname`] as string) ?? m.entityType;
          const entityName = recAny[`_${m.field}_value@OData.Community.Display.V1.FormattedValue`] as
            | string
            | undefined;
          inherited.push({
            entityType,
            entityId,
            entityName,
            entityUrl: base ? `${base}/main.aspx?pagetype=entityrecord&etn=${entityType}&id=${entityId}` : undefined,
          });
        }
        setParentAssociations(inherited);
        setSuggestedCreates(deriveSuggestedCreates(rec.sprk_associationprovenance as string | null));
      } catch (err) {
        console.warn('[CommunicationActions] prefill retrieve failed:', err);
      }
      // Enumerate the source communication's attachment documents (best-effort) so
      // Reply / Reply All / Forward can offer to carry them as files and/or body links.
      try {
        const atts = await fetchSourceAttachments(context.webAPI, communicationId, resolveDataverseUrl());
        if (!cancelled) setSourceAttachments(atts);
      } catch (err) {
        console.warn('[CommunicationActions] source attachment enumeration failed:', err);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [communicationId, context.webAPI]);

  const openComposer = (mode: ComposerMode) => {
    setError(null);
    setStatus(null);
    setComposerMode(mode);
  };

  // To/Cc/Bcc directory typeahead. Host-context Xrm.WebApi search over the
  // systemuser + contact tables (per docs/standards/DATA-ACCESS-DECISION-CRITERIA.md
  // — single-entity, in-session, no OBO/AI/cross-system → Xrm.WebApi, NOT BFF).
  // Runs in the user's Dataverse session, so it respects the caller's read
  // permissions. `createXrmDataService()` is the canonical shared adapter; the
  // engine's RecipientField owns debounce (300 ms), min-length (≥2), and top-N.
  const dataService = React.useMemo(() => createXrmDataService(), []);
  const handleSearchRecipients = React.useCallback(
    (query: string) => searchUsersAndContacts(dataService, query),
    [dataService]
  );

  // Record lookup (owner UAT round 5): the attachments search icon opens a menu of these
  // entity types (RegardingResolver pattern). A Document pick attaches; any other record is
  // linked in the body. Runs the OOB Xrm.Utility.lookupObjects picker for the chosen type.
  const handleLookupRecord = React.useCallback(async (entityType: string): Promise<IPickedRecord | null> => {
    type XrmLike = {
      Utility?: { lookupObjects?: (o: unknown) => Promise<Array<{ id: string; name: string }>> };
    };
    const scope = window as unknown as { Xrm?: XrmLike; parent?: { Xrm?: XrmLike }; top?: { Xrm?: XrmLike } };
    const xrm = scope.Xrm ?? scope.parent?.Xrm ?? scope.top?.Xrm;
    if (!xrm?.Utility?.lookupObjects) return null;
    const results = await xrm.Utility.lookupObjects({
      entityTypes: [entityType],
      defaultEntityType: entityType,
      allowMultiSelect: false,
    });
    if (!results || results.length === 0) return null;
    const picked = results[0];
    const id = String(picked.id).replace(/[{}]/g, '').toLowerCase();
    const base = resolveDataverseUrl();
    return {
      entityType,
      id,
      name: picked.name,
      url: base ? `${base}/main.aspx?pagetype=entityrecord&etn=${entityType}&id=${id}` : undefined,
    };
  }, []);

  // Advanced recipient lookup (owner UAT 2026-07-24): clicking a To/Cc/Bcc label box opens the
  // OOB people picker over contact + systemuser; the picked record's primary email is resolved
  // (contact → emailaddress1, user → internalemailaddress) into a chip. Multi-select supported.
  const handleLookupRecipients = React.useCallback(
    async (_field: 'to' | 'cc' | 'bcc'): Promise<IRecipient[] | null> => {
      type XrmLike = {
        Utility?: {
          lookupObjects?: (o: unknown) => Promise<Array<{ id: string; name: string; entityType: string }>>;
        };
      };
      const scope = window as unknown as { Xrm?: XrmLike; parent?: { Xrm?: XrmLike }; top?: { Xrm?: XrmLike } };
      const xrm = scope.Xrm ?? scope.parent?.Xrm ?? scope.top?.Xrm;
      if (!xrm?.Utility?.lookupObjects) return null;
      const results = await xrm.Utility.lookupObjects({
        entityTypes: ['contact', 'systemuser'],
        allowMultiSelect: true,
      });
      if (!results || results.length === 0) return null;
      const EMAIL_FIELD: Record<string, string> = { contact: 'emailaddress1', systemuser: 'internalemailaddress' };
      const recipients: IRecipient[] = [];
      for (const p of results) {
        const field = EMAIL_FIELD[p.entityType];
        if (!field) continue;
        const id = String(p.id).replace(/[{}]/g, '');
        try {
          const rec = await context.webAPI.retrieveRecord(p.entityType, id, `?$select=${field}`);
          const email = rec?.[field];
          if (typeof email === 'string' && email.includes('@')) {
            recipients.push({
              email,
              displayName: p.name,
              resolved: true,
              sourceId: id,
              entityType: p.entityType as 'contact' | 'systemuser',
            });
          }
        } catch (err) {
          console.warn('[CommunicationActions] recipient email resolve failed:', err);
        }
      }
      return recipients.length > 0 ? recipients : null;
    },
    [context.webAPI]
  );

  // Connector toolbar icon → add a relationship (owner UAT 2026-07-24, Option B). Runs the OOB
  // lookup across the regarding-able entity types (same as the Connections PCF "Link another")
  // and returns the picked record; the composer shows it in "Related to" and it is written onto
  // the communication when the email is SENT (the send payload carries `associations`).
  const handleAddRelationship = React.useCallback(async (): Promise<IPickedRecord | null> => {
    type XrmLike = {
      Utility?: {
        lookupObjects?: (o: unknown) => Promise<Array<{ id: string; name: string; entityType: string }>>;
      };
    };
    const scope = window as unknown as { Xrm?: XrmLike; parent?: { Xrm?: XrmLike }; top?: { Xrm?: XrmLike } };
    const xrm = scope.Xrm ?? scope.parent?.Xrm ?? scope.top?.Xrm;
    if (!xrm?.Utility?.lookupObjects) return null;
    const results = await xrm.Utility.lookupObjects({ entityTypes: REGARDING_ENTITY_TYPES, allowMultiSelect: false });
    const picked = results?.[0];
    if (!picked?.id || !picked?.entityType) return null;
    const id = String(picked.id).replace(/[{}]/g, '').toLowerCase();
    const base = resolveDataverseUrl();
    return {
      entityType: picked.entityType,
      id,
      name: picked.name,
      url: base ? `${base}/main.aspx?pagetype=entityrecord&etn=${picked.entityType}&id=${id}` : undefined,
    };
  }, []);

  // Launch a "create from this email" form (Event / To Do / Invoice) as an in-app
  // MODAL (UAT R3 C11-3). All three route through the single `launchCreate` seam so
  // the OOB `navigateTo` dialog can later be swapped for a custom Fluent dialog
  // WITHOUT editing these call sites (owner requirement). R4 launches the target
  // create form; full create-and-link is the Notification-Spine project.
  const handleCreate = React.useCallback((kind: CreateKind) => {
    launchCreate(kind, {
      onError: err => console.warn('[CommunicationActions] create-from-email launch failed:', err),
    });
  }, []);

  const handleArchive = React.useCallback(() => {
    if (!communicationId) {
      setError('Save the communication before archiving to SharePoint.');
      return;
    }
    void (async () => {
      setBusy(true);
      setError(null);
      setStatus(null);
      try {
        // Relative path — the @spaarke/auth resolver prepends /api/.
        const resp = await authenticatedFetch(`/communications/${communicationId}/archive`, { method: 'POST' });
        if (!resp.ok) {
          setError(`Save to SharePoint failed (${resp.status}).`);
          return;
        }
        const result = (await resp.json()) as { alreadyArchived?: boolean; attachmentDocumentsCreated?: number };
        setStatus(
          result.alreadyArchived
            ? 'Already saved to SharePoint.'
            : `Saved to SharePoint${result.attachmentDocumentsCreated ? ` (+${result.attachmentDocumentsCreated} attachment${result.attachmentDocumentsCreated === 1 ? '' : 's'})` : ''}.`
        );
        await refreshHostForm();
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Save to SharePoint failed.');
      } finally {
        setBusy(false);
      }
    })();
  }, [communicationId]);

  // Compose/reply/replyAll/forward pre-fill derived from the loaded record (pure helper).
  const composerProps = React.useMemo<ISendEmailPageProps | null>(() => {
    if (!composerMode) return null;
    // Reply All reuses the shared 'reply' composer mode (Re:, reply chrome) but with
    // the wider recipient set; '+ New' is a blank 'compose' not tied to this record.
    const sendMode: EmailComposerMode = composerMode === 'replyAll' ? 'reply' : composerMode;
    // Offer the source attachments on reply/replyAll/forward (not '+ New'). The engine
    // applies the per-mode attach default (forward → on, reply → off).
    const carryAttachments = composerMode === 'compose' ? undefined : sourceAttachments;
    return {
      mode: sendMode,
      communicationId: composerMode === 'compose' ? undefined : communicationId,
      authenticatedFetch,
      bffBaseUrl,
      onSearchRecipients: handleSearchRecipients,
      onLookupRecipients: handleLookupRecipients,
      recordLookupCatalog: RECORD_LOOKUP_CATALOG,
      onLookupRecord: handleLookupRecord,
      onAddRelationship: handleAddRelationship,
      // Reply / Reply All / Forward inherit the parent's filed associations (owner UAT 2026-07-24);
      // '+ New' (compose) starts with none.
      associations: composerMode === 'compose' ? undefined : parentAssociations,
      initialAttachments: carryAttachments && carryAttachments.length > 0 ? carryAttachments : undefined,
      onSent: () => {
        setComposerMode(null);
        setStatus('Sent.');
        void refreshHostForm();
      },
      onClose: () => setComposerMode(null),
      ...deriveComposerFields(composerMode, prefill),
    };
  }, [
    composerMode,
    prefill,
    communicationId,
    bffBaseUrl,
    handleSearchRecipients,
    handleLookupRecipients,
    handleLookupRecord,
    handleAddRelationship,
    sourceAttachments,
    parentAssociations,
  ]);

  if (authError) {
    return (
      <div className={s.notice}>
        <MessageBar intent="error">
          <MessageBarBody>{authError}</MessageBarBody>
        </MessageBar>
      </div>
    );
  }

  if (!isEmail) {
    return (
      <div className={s.notice}>
        <MessageBar intent="info">
          <MessageBarBody>This channel is read-only — email actions are unavailable.</MessageBarBody>
        </MessageBar>
      </div>
    );
  }

  if (!authReady) {
    return (
      <div className={s.notice}>
        <Spinner size="tiny" label="Preparing actions…" />
      </div>
    );
  }

  const disabled = readOnly || busy;
  // Compose/reply/forward pre-fill from the record; disable those actions until the
  // record has loaded so the composer never opens blank (code-review S3).
  const composeDisabled = disabled || (communicationId != null && prefill == null);

  return (
    <div className={s.root}>
      <div className={s.barRow}>
        <Toolbar size="small" className={s.bar} aria-label="Communication actions">
          {/* Left group — the email verbs (icon + label). All open the composer. */}
          <ToolbarButton
            className={s.toolbarBtn}
            icon={<ArrowReply20Regular />}
            disabled={composeDisabled}
            onClick={() => openComposer('reply')}
          >
            Reply
          </ToolbarButton>
          <ToolbarButton
            className={s.toolbarBtn}
            icon={<ArrowReplyAll20Regular />}
            disabled={composeDisabled}
            onClick={() => openComposer('replyAll')}
          >
            Reply All
          </ToolbarButton>
          <ToolbarButton
            className={s.toolbarBtn}
            icon={<ArrowForward20Regular />}
            disabled={composeDisabled}
            onClick={() => openComposer('forward')}
          >
            Forward
          </ToolbarButton>
          <ToolbarButton
            className={s.toolbarBtn}
            icon={<Mail20Regular />}
            disabled={disabled}
            onClick={() => openComposer('compose')}
          >
            New
          </ToolbarButton>

          {/* C11-2: divider + icon-only cluster are pushed to the toolbar's far right. */}
          <ToolbarDivider className={s.dividerPush} />

          {/* Right group — record/email actions (icon-only, tooltip). ✨ = engine-suggested. */}
          <div className={s.rightGroup}>
            <Tooltip content="Save to SharePoint" relationship="label">
              <ToolbarButton
                className={s.iconBtn}
                icon={<CloudArrowUp20Regular />}
                aria-label="Save to SharePoint"
                disabled={disabled}
                onClick={handleArchive}
              />
            </Tooltip>
            <Tooltip
              content={suggestedCreates.has('event') ? 'Create Event (suggested from this email)' : 'Create Event'}
              relationship="label"
            >
              <ToolbarButton
                className={s.iconBtn}
                icon={<CalendarLtr20Regular className={suggestedCreates.has('event') ? s.suggestedIcon : undefined} />}
                aria-label="Create Event"
                disabled={disabled}
                onClick={() => handleCreate('event')}
              />
            </Tooltip>
            <Tooltip
              content={suggestedCreates.has('todo') ? 'Create To Do (suggested from this email)' : 'Create To Do'}
              relationship="label"
            >
              <ToolbarButton
                className={s.iconBtn}
                icon={
                  <CheckmarkCircle20Regular className={suggestedCreates.has('todo') ? s.suggestedIcon : undefined} />
                }
                aria-label="Create To Do"
                disabled={disabled}
                onClick={() => handleCreate('todo')}
              />
            </Tooltip>
            <Tooltip
              content={suggestedCreates.has('invoice') ? 'Link Invoice (suggested from this email)' : 'Link Invoice'}
              relationship="label"
            >
              <ToolbarButton
                className={s.iconBtn}
                icon={<Receipt20Regular className={suggestedCreates.has('invoice') ? s.suggestedIcon : undefined} />}
                aria-label="Link Invoice"
                disabled={disabled}
                onClick={() => handleCreate('invoice')}
              />
            </Tooltip>
          </div>
        </Toolbar>
        {showVersionFooter && <Text className={s.versionText}>v{version}</Text>}
      </div>

      {status && (
        <div className={s.notice}>
          <MessageBar intent="success">
            <MessageBarBody>{status}</MessageBarBody>
          </MessageBar>
        </div>
      )}
      {error && (
        <div className={s.notice}>
          <MessageBar intent="error">
            <MessageBarBody>{error}</MessageBarBody>
          </MessageBar>
        </div>
      )}

      {/* modalType="non-modal": renders NO backdrop scrim and no focus trap. Required so the
          native Xrm.Utility.lookupObjects record-lookup pane — which renders OUTSIDE this dialog's
          DOM at page level — stays fully interactive (a "modal"/"alert" backdrop covers the whole
          viewport and swallows clicks on the lookup pane; "modal" also light-dismisses the composer
          on that focus loss). Non-modal fixes both the UAT-r6 auto-close and the UAT-r7 "lookup
          behind the modal / can't select" issue. The composer still renders as an elevated floating
          surface; all closes route through its own X / Cancel / Send → onClose/onSent. */}
      <Dialog
        modalType="non-modal"
        open={composerMode !== null}
        onOpenChange={(_, d) => !d.open && setComposerMode(null)}
      >
        <DialogSurface className={s.dialogSurface}>
          <DialogBody className={s.dialogBody}>{composerProps && <SendEmailPageR16 {...composerProps} />}</DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  );
};
