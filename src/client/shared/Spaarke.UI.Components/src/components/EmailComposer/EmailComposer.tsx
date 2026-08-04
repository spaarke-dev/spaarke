/**
 * EmailComposer.tsx
 *
 * THE canonical email-composer engine (task 020, FR-12). The only React
 * component in Spaarke that knows email-send mechanics — every other
 * email-send UI (task 021 wrappers, and downstream W6 caller migrations)
 * mounts this engine, directly or via a thin wrapper. See ADR-045 and
 * `reference/r3-send-side-design.md` §5.
 *
 * Architecture:
 *   - `forwardRef<IEmailComposerHandle, IEmailComposerProps>` — hosts (wizards,
 *     dialogs, Code Pages) drive `validate()`/`send()`/`saveDraft()`/`getState()`
 *     via a `composerRef`, mirroring the `CreateRecordWizard`/`WizardShell`
 *     `useImperativeHandle` idiom already established in this shared lib.
 *   - Single `useReducer(emailComposerReducer, props, initialState)` — the
 *     ONLY engine-state store (task 020 constraint: no scattered `useState`
 *     for engine state; refs/transient UI state are the sanctioned exception).
 *   - Three `makeStyles` layout objects keyed on `mount` (page/dialog/inline) —
 *     variants differ in density/chrome only, not color/typography tokens
 *     (design §5.10).
 *   - No `@spaarke/auth` import (ADR-028) — `authenticatedFetch` is injected
 *     via props and forwarded into `sendCommunication()`.
 */
import * as React from 'react';
import { forwardRef, useImperativeHandle } from 'react';
import {
  Button,
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  Input,
  Textarea,
  Spinner,
  Link,
  MessageBar,
  MessageBarBody,
  Text,
  Tooltip,
  ToolbarButton,
  Menu,
  MenuButton,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  MenuItemRadio,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  makeStyles,
  shorthands,
  tokens,
  mergeClasses,
} from '@fluentui/react-components';
import {
  Attach20Regular,
  SearchRegular,
  Search20Regular,
  ChevronDown20Regular,
  ChevronUp20Regular,
  DocumentText20Regular,
  Sparkle20Regular,
  Add20Regular,
} from '@fluentui/react-icons';
import type { CommunicationSendMode } from '../../services/communicationApi';
import { ModalWindowControls } from '../ModalWindowControls';

import { sendCommunication, SendCommunicationError } from '../../services/communicationApi';

import {
  emailComposerReducer,
  initialState,
  validateState,
  mapStateToSendRequest,
  validateLocalAttachmentFile,
} from './EmailComposer.reducer';
import type {
  EmailComposerState,
  IAttachmentItem,
  IEmailComposerHandle,
  IEmailComposerProps,
  IComposerAttachmentSource,
  IPickedRecord,
  IEmailTemplateSummary,
  IEmailAiDraftAction,
  IValidationResult,
} from './EmailComposer.types';
import type { RichTextEditorRef } from '../RichTextEditor';

import { RecipientField } from './subcomponents/RecipientField';
import { BodyEditor } from './subcomponents/BodyEditor';
import { AttachmentList } from './subcomponents/AttachmentList';
import { AssociationChips } from './subcomponents/AssociationChips';
import { ComposerActionBar } from './subcomponents/ComposerActionBar';
import { ComposerSendButton } from './subcomponents/ComposerSendButton';

// The Dataverse logical name for a governed Document — the paperclip's "Link documents"
// picks this type and ATTACHES it, so it's intentionally excluded from the record-search
// catalog (which inserts a body LINK for every other type). Owner UAT 2026-07-24.
const DOCUMENT_LOGICAL_NAME = 'sprk_document';

// "From:" sender option labels (owner UAT 2026-07-30, item 3). The engine has NO current-user
// identity data source (context-agnostic, ADR-012), so the "user" option falls back to the
// host-supplied `fromMailbox` address when present, else a generic "My mailbox" label — see the
// report note on this gap. The Spaarke shared mailbox is a fixed, named destination.
const SHARED_MAILBOX_LABEL = 'Spaarke shared mailbox';
function userMailboxLabel(fromMailbox: string | undefined): string {
  return fromMailbox && fromMailbox.trim() ? fromMailbox : 'My mailbox';
}

// Whether the compose body has any real content (Wave E template picker) — HTML tags,
// &nbsp;, and whitespace don't count, so an "empty" editor (which serializes to <p><br></p>)
// reads as empty and a template applies without an overwrite prompt.
function isComposeBodyEmpty(body: string): boolean {
  return (
    (body || '')
      .replace(/<[^>]*>/g, '')
      .replace(/&nbsp;/g, '')
      .trim().length === 0
  );
}

// Default AI "sparkle" quick-actions (Wave E). Intent keys are stable + map to a server-side prompt
// (the label is UI-only). A host may override via `props.aiDraftActions`; the free-text "Enter prompt"
// action is always appended by the composer.
const DEFAULT_AI_DRAFT_ACTIONS: IEmailAiDraftAction[] = [
  { intent: 'reply', label: 'Draft a reply' },
  { intent: 'summarize', label: 'Summarize the thread' },
  { intent: 'concise', label: 'Make it concise' },
  { intent: 'formal', label: 'Formal tone' },
  { intent: 'friendly', label: 'Friendly tone' },
  { intent: 'grammar', label: 'Fix grammar & tone' },
];

// Quick-actions that TRANSFORM a text selection rather than the whole draft (owner UAT
// 2026-08-03 R5 item 5). They only appear when the author has text selected in the body,
// and their result replaces that selection (not the whole body). Everything else acts on
// the full draft.
const SELECTION_SCOPED_AI_INTENTS = new Set<string>(['concise', 'formal', 'friendly']);

/**
 * Resolve recipient-openable SPE sharing links for the attachments the author toggled **Link** on
 * (owner UAT 2026-07-30 R2 item 12). Runs at SEND: for each `linkSelected` attachment that has a
 * `documentId`, calls the host `onResolveShareLink(documentId)` and overrides `linkUrl` with the
 * returned sharing URL so the body-link block points at the actual file (not the internal
 * Dataverse/SPE-storage URL). Best-effort + non-blocking: a null/throw keeps the prior `linkUrl`, so
 * a share-link hiccup never fails the send. No handler → attachments returned unchanged. Exported for
 * unit tests. This lives here (not the pure reducer) because it performs host I/O.
 */
export async function resolveAttachmentShareLinks(
  attachments: readonly IAttachmentItem[],
  onResolveShareLink: ((documentId: string) => Promise<string | null>) | undefined
): Promise<IAttachmentItem[]> {
  if (!onResolveShareLink) return attachments.slice();
  return Promise.all(
    attachments.map(async a => {
      if (a.linkSelected === true && a.documentId) {
        try {
          const url = await onResolveShareLink(a.documentId);
          if (url) return { ...a, linkUrl: url };
        } catch {
          /* best-effort — keep the prior linkUrl; never block the send */
        }
      }
      return a;
    })
  );
}

// ---------------------------------------------------------------------------
// Attachment source defaults
// ---------------------------------------------------------------------------

// NOTE: `mapStateToSendRequest` now lives in `EmailComposer.reducer.ts` (moved
// there by email-r4 task 104); it is imported above. R3 task 020's `threadId`
// argument was re-applied to that reducer copy during the origin/master merge.
function defaultAttachmentSources(
  attachmentSources: IComposerAttachmentSource[] | undefined,
  wizardContext: IEmailComposerProps['wizardContext']
): IComposerAttachmentSource[] {
  if (attachmentSources) return attachmentSources;
  return wizardContext
    ? [{ kind: 'wizard' }, { kind: 'related' }, { kind: 'local' }, { kind: 'spe' }]
    : [{ kind: 'local' }, { kind: 'related' }, { kind: 'spe' }];
}

/**
 * Blocks script-executing schemes (`javascript:`/`data:`/`vbscript:`) before an
 * (untrusted) `recordLink.url` is rendered into an `<a href>`. This is the
 * shared send engine — a caller could build the url from record-derived data.
 * Everything else (http(s), app-relative, mailto) is allowed.
 */
function isSafeHref(url: string): boolean {
  return !/^\s*(javascript|data|vbscript):/i.test(url);
}

/** Escape a plain string for safe interpolation into HTML (record-link label/href). */
function escapeHtml(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

// ---------------------------------------------------------------------------
// Styles — three mount variants (design §5.10). Layout density/chrome only;
// all share the same Fluent semantic tokens (dark mode passes through the
// host FluentProvider — no hardcoded colors anywhere in this file).
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  // Shared across all mounts.
  base: {
    // border-box so `width:100%` + padding fits the host container (the PCF/Dataverse
    // host has no global border-box reset; without this the padded page mount overflowed
    // its modal and got clipped on the left — owner UAT round 5).
    boxSizing: 'border-box',
    width: '100%',
    maxWidth: '100%',
    overflowX: 'hidden',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground1,
    backgroundColor: tokens.colorNeutralBackground1,
    // Programmatic focus target on mode transitions (NFR-03) — visible ring
    // via token, invisible otherwise (root is not in the normal tab order).
    ':focus-visible': {
      outlineStyle: 'solid',
      outlineWidth: tokens.strokeWidthThick,
      outlineColor: tokens.colorBrandStroke1,
    },
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  // Window controls (maximize + close) cluster on the LEFT, title beside them (owner UAT
  // 2026-07-30, item 11 — "upper-left, next to the X close").
  header: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    // Title on the left, window-controls cluster pushed to the upper-RIGHT
    // (owner UAT 2026-07-30 R2 item 8 — corrected 2026-07-31: the maximize
    // button belongs in the upper-right corner, Outlook-style, not the left).
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
  },
  headerActions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
  },
  // Dialog/page header title (owner UAT 2026-07-30, item 10): 18px. No token lands exactly on
  // 18px (base ramp is 16/20/24), so an explicit px is used per ADR-021's "explicit px only
  // when no token matches" carve-out; color/weight stay token-driven.
  headerTitle: {
    fontSize: '18px',
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  // "From:" row (owner UAT 2026-07-30, item 3): an Outlook-style sender line ABOVE To/Cc.
  fromRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  // Label box — visually matches the To/Cc label boxes (RecipientField `labelBox`).
  labelBox: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    flexShrink: 0,
    minWidth: '44px',
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    ...shorthands.border(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke1),
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase300,
  },
  // The sender value cell — a subtle menu button (choice) or plain text (fixed sender).
  fromValue: {
    flexGrow: 1,
    minWidth: 0,
    display: 'flex',
    alignItems: 'center',
  },
  fromStaticText: {
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
  },
  // "From:" label — plain inline text (NOT a boxed button), Segoe UI 14px semibold, to match the
  // reading-pane "From:" line (owner UAT 2026-07-30 R2 item 9). Token-only so both themes resolve
  // (ADR-021). No `fontFamily` — Segoe UI is Fluent's default family.
  fromLabel: {
    flexShrink: 0,
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  bccToggleRow: {
    display: 'flex',
    justifyContent: 'flex-end',
  },
  // Section label — standard Segoe UI 14px semibold, neutral foreground 1 (UI-DESIGN-STANDARDS
  // section-header spec; owner UAT 2026-07-24). Token-only so both themes resolve (ADR-021).
  sectionLabel: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  // "Related to" body — owner UAT 2026-07-30 (item 8): the label, chips, AND the "Link another
  // record" affordance flow on ONE wrapping row (not stacked in a column), so the link reads as a
  // sibling of the chips inline with the label.
  relatedRow: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  // The label + chevron collapse toggle — a clickable cluster that sits at the head of the
  // wrapping row.
  relatedToggle: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    cursor: 'pointer',
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
  },
  // "Link another record" tile — the ONLY way to relate an email now that the connector
  // toolbar icon is gone (item 11). Non-bold label + leading search icon; bordered box that
  // matches the reading-pane resolver's link tile. Font normalized to Segoe UI 12px
  // (`fontSizeBase200`) with `fontFamily: 'inherit'` so it reads as a SIBLING of the chips —
  // a native <button> otherwise falls back to the UA font (Arial) at a larger size (owner UAT
  // 2026-07-30, item 8). Token-only so both themes resolve (ADR-021).
  linkTile: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    borderRadius: tokens.borderRadiusMedium,
    border: `${tokens.strokeWidthThin} dashed ${tokens.colorNeutralStroke1}`,
    backgroundColor: 'transparent',
    color: tokens.colorNeutralForeground2,
    fontFamily: 'inherit',
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightRegular,
    cursor: 'pointer',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      color: tokens.colorNeutralForeground1,
    },
  },
  // Trailing controls placed into the RichTextEditor toolbar slot (paperclip / search).
  // Kept visually grouped at the toolbar's trailing end.
  toolbarSlot: {
    display: 'flex',
    alignItems: 'center',
    gap: '2px',
  },
  // Vertical "|" separator between the attach/record-link group and the template/AI group
  // (owner UAT 2026-08-03 R5 item 3). `alignSelf: stretch` sizes it to the toolbar row height.
  toolbarDivider: {
    flexShrink: 0,
    width: '1px',
    alignSelf: 'stretch',
    marginTop: tokens.spacingVerticalXS,
    marginBottom: tokens.spacingVerticalXS,
    marginLeft: tokens.spacingHorizontalXS,
    marginRight: tokens.spacingHorizontalXS,
    backgroundColor: tokens.colorNeutralStroke2,
  },
  // AI "Draft with AI" inline Popover (owner UAT 2026-08-03 R5 item 5): a titled
  // free-text prompt over an actions row ("+" quick responses on the left, Generate/
  // Cancel on the right). Fluent v9 semantic tokens only (ADR-021).
  aiPopover: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    minWidth: '320px',
    maxWidth: '420px',
  },
  aiPromptInput: {
    width: '100%',
  },
  aiPopoverActions: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
  },
  aiPopoverButtons: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  liveRegion: {
    position: 'absolute',
    width: '1px',
    height: '1px',
    overflow: 'hidden',
    clip: 'rect(0 0 0 0)',
  },

  // Extra breathing room between the recipients block (Bcc line) and Subject (#1).
  subjectSpacer: {
    marginTop: tokens.spacingVerticalS,
  },

  // `page` — fills the full host container (owner UAT round 4: the composer must fill
  // the modal, not shrink-to-content and center). `height: 100%` + the flex layout
  // gives pinned header / fields / flex-grow message editor (its own shared scroll) /
  // pinned footer. NO margin:auto / maxWidth — those made the composer collapse to
  // content width inside the flex dialog body.
  page: {
    height: '100%',
    minHeight: 0,
    width: '100%',
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalXXL,
    paddingRight: tokens.spacingHorizontalXXL,
  },

  // `dialog` — fills the host dialog width + height for the pinned layout.
  dialog: {
    height: '100%',
    minHeight: 0,
    width: '100%',
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
  },

  // `inline` — no chrome; fills the wizard step container; wizard owns
  // heading + navigation, so no header/action-bar padding here.
  inline: {
    width: '100%',
  },
});

// ---------------------------------------------------------------------------
// EmailComposer (exported — forwardRef)
// ---------------------------------------------------------------------------

export const EmailComposer = forwardRef<IEmailComposerHandle, IEmailComposerProps>((props, ref) => {
  const styles = useStyles();
  const [state, dispatch] = React.useReducer(emailComposerReducer, props, initialState);

  // Ref mirror so the imperative handle always reads current state without
  // recreating validate/send/saveDraft/getState on every keystroke.
  const stateRef = React.useRef(state);
  stateRef.current = state;

  const showAssociations = props.showAssociations ?? true;
  const attachmentSources = React.useMemo(
    () => defaultAttachmentSources(props.attachmentSources, props.wizardContext),
    [props.attachmentSources, props.wizardContext]
  );
  const showSendModeRadio = props.sendMode === undefined;
  // The top "From:" row offers the sender choice whenever the host hasn't fixed `sendMode`
  // and the composer is editable (owner UAT 2026-07-30, item 3).
  const showSenderChoice = showSendModeRadio && !state.readOnly;

  // Bcc is hidden by default (owner UAT mockup 2026-07-22 shows To/Cc only); a small
  // "Bcc" toggle reveals it, and it auto-reveals when the field already carries values.
  const [showBccToggle, setShowBccToggle] = React.useState(false);
  const bccVisible = showBccToggle || state.bcc.length > 0;

  // Imperative handle to the HTML body editor — used to insert a record link AT THE CURSOR
  // (owner UAT 2026-07-24) rather than appending to the end. Null in plain-text mode.
  const bodyEditorRef = React.useRef<RichTextEditorRef>(null);
  // Local-file picker input (trigger lives in the RTF toolbar's paperclip menu, owner UAT
  // 2026-07-24). Pick-time policy rejections surface here.
  const fileInputRef = React.useRef<HTMLInputElement>(null);
  // Late-bound handle to handleAddAttachment (defined below) so the file-pick callback,
  // declared earlier, can invoke it without a temporal-dead-zone reference.
  const handleAddAttachmentRef = React.useRef<((item: IAttachmentItem) => void) | null>(null);
  const [pickErrors, setPickErrors] = React.useState<string[]>([]);
  // Related-to section defaults expanded (shows what the email is associated to); Attachments
  // defaults collapsed (owner UAT 2026-07-24 — the latter is owned by AttachmentList).
  const [relatedCollapsed, setRelatedCollapsed] = React.useState(false);
  // Single-primary "Related to" model (owner UAT 2026-07-30, item 8): a freshly-picked record is
  // NOT appended silently — it is held here pending a "set as primary regarding?" confirmation.
  // Non-null → the replace-confirm Dialog is open. Confirming promotes it to associations[0].

  // Compose template picker (Wave E, owner UAT 2026-07-30). Templates load lazily when the
  // menu opens; picking one whose apply would overwrite existing subject/body prompts a confirm.
  const [templates, setTemplates] = React.useState<IEmailTemplateSummary[] | null>(null);
  const [templatesLoading, setTemplatesLoading] = React.useState(false);
  const [templateError, setTemplateError] = React.useState<string | null>(null);
  const [pendingTemplateId, setPendingTemplateId] = React.useState<string | null>(null);

  // AI "sparkle" draft (Wave E; redesigned owner UAT 2026-08-03 R5 item 5). `aiDrafting`
  // disables the sparkle while a completion is in flight. The sparkle is now an inline
  // Popover: a free-text prompt (type directly + Generate) plus a "+" quick-responses menu.
  // `aiMenuOpen` drives the popover; `aiSelectionText` is the editor text that was selected
  // WHEN the popover opened (Lexical retains its selection across the focus change), which
  // gates the selection-scoped quick actions (make concise / formal / friendly).
  const [aiDrafting, setAiDrafting] = React.useState(false);
  const [aiError, setAiError] = React.useState<string | null>(null);
  const [aiMenuOpen, setAiMenuOpen] = React.useState(false);
  const [aiSelectionText, setAiSelectionText] = React.useState('');
  const [aiPromptText, setAiPromptText] = React.useState('');
  const aiPromptInputRef = React.useRef<HTMLTextAreaElement>(null);

  // Record lookup (owner UAT round 5, RegardingResolver pattern): a document pick attaches
  // (Attach/Link per row); any other record type is inserted as a link in the message body
  // AT THE CURSOR (owner UAT 2026-07-24 — HTML mode via the editor's insertAtCursor; plain
  // text appends since the textarea has no cursor handle here).
  const handleRecordPicked = React.useCallback((picked: IPickedRecord) => {
    if (picked.entityType === DOCUMENT_LOGICAL_NAME) {
      dispatch({
        type: 'ADD_ATTACHMENT',
        item: {
          id: `doc:${picked.id}`,
          source: 'related',
          fileName: picked.name,
          sizeBytes: picked.sizeBytes ?? 0,
          documentId: picked.id,
          linkUrl: picked.url,
          selected: true,
          linkSelected: false,
        },
      });
      return;
    }
    if (!picked.url || !isSafeHref(picked.url)) return;
    const cur = stateRef.current;
    const label = picked.name || 'record';
    if (cur.bodyFormat === 'HTML' && bodyEditorRef.current) {
      // Insert at the caret; the editor's onChange propagates the new HTML into state.body.
      bodyEditorRef.current.insertAtCursor(`<a href="${escapeHtml(picked.url)}">${escapeHtml(label)}</a>`, 'html');
      return;
    }
    const nextBody =
      cur.bodyFormat === 'HTML'
        ? `${cur.body}<p><a href="${escapeHtml(picked.url)}">${escapeHtml(label)}</a></p>`
        : `${cur.body}${cur.body ? '\n' : ''}${label}: ${picked.url}`;
    dispatch({ type: 'SET_FIELD', field: 'body', value: nextBody });
  }, []);

  // Local-file pick (paperclip → "Add files"). CHAT-ATTACHMENT-POLICY gate BEFORE state entry;
  // rejected files are dropped and surfaced (never silently). Moved out of AttachmentList when
  // the add controls hoisted to the RTF toolbar (owner UAT 2026-07-24).
  const handleLocalFilesPicked = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const files = e.target.files;
      if (!files) return;
      const rejections: string[] = [];
      for (let i = 0; i < files.length; i += 1) {
        const file = files[i];
        const rejection = validateLocalAttachmentFile(file);
        if (rejection) {
          rejections.push(rejection.message);
          continue;
        }
        handleAddAttachmentRef.current?.({
          id: `local:${file.name}:${file.size}:${i}:${state.attachments.length}`,
          source: 'local',
          fileName: file.name,
          sizeBytes: file.size,
          mimeType: file.type || undefined,
          file,
          selected: true,
        });
      }
      setPickErrors(rejections);
      e.target.value = ''; // reset so re-selecting the same file fires onChange again
    },
    [state.attachments.length]
  );

  // "Link another record" → pick a record via the host polymorphic picker and APPEND it to the
  // in-memory "Related to" list (owner UAT 2026-07-31). No record exists yet in compose/reply/
  // forward, so associations are held in reducer state and ride the send payload; the user can add
  // SEVERAL and remove any (the chip ×). `ADD_ASSOCIATION` dedups on entityType+entityId. The BFF
  // maps associations[0] as the primary regarding at send time (CLIENT-ONLY ordering). No
  // replace-confirm — adding is additive, not a single-primary swap.
  const onAddRelationship = props.onAddRelationship;
  const handleAddRelationship = React.useCallback(() => {
    if (!onAddRelationship) return;
    setRelatedCollapsed(false);
    void onAddRelationship()
      .then(picked => {
        if (!picked) return;
        dispatch({
          type: 'ADD_ASSOCIATION',
          association: {
            entityType: picked.entityType,
            entityId: picked.id,
            entityName: picked.name,
            entityUrl: picked.url,
          },
        });
      })
      .catch(err => console.warn('[EmailComposer] add relationship failed:', err));
  }, [onAddRelationship]);

  // ── Attach-on-compose: local-file → Document resolution (task 042 / FR-20) ──
  // A picked local file (already passed the CHAT-ATTACHMENT-POLICY 25 MB + MIME
  // gate in AttachmentList) is uploaded to a governed sprk_document via the
  // host-injected `onUploadLocalAttachment`, then patched with its `documentId`
  // so it flows into the EXISTING send path (ADR-045 — no new send/upload path).
  // Absent resolver → local picks stay display-only (pre-task-042 behavior).
  const [resolvingIds, setResolvingIds] = React.useState<ReadonlySet<string>>(() => new Set());
  const [uploadError, setUploadError] = React.useState<string | undefined>();
  const mountedRef = React.useRef(true);
  React.useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  const onUploadLocalAttachment = props.onUploadLocalAttachment;
  const handleAddAttachment = React.useCallback(
    (item: IAttachmentItem) => {
      dispatch({ type: 'ADD_ATTACHMENT', item });
      if (item.source !== 'local' || !item.file || !onUploadLocalAttachment) return;
      const file = item.file;
      setUploadError(undefined);
      setResolvingIds(prev => new Set(prev).add(item.id));
      onUploadLocalAttachment(file)
        .then(res => {
          if (!mountedRef.current) return;
          dispatch({
            type: 'RESOLVE_ATTACHMENT_DOCUMENT',
            id: item.id,
            documentId: res.documentId,
            driveItemId: res.driveItemId,
            linkUrl: res.linkUrl,
          });
        })
        .catch((err: unknown) => {
          if (!mountedRef.current) return;
          // Upload failed → drop the unsendable item and surface a visible
          // error. Never blocks sending the already-resolved attachments.
          dispatch({ type: 'REMOVE_ATTACHMENT', id: item.id });
          setUploadError(`Could not attach ${item.fileName}: ${err instanceof Error ? err.message : 'upload failed'}.`);
        })
        .finally(() => {
          if (!mountedRef.current) return;
          setResolvingIds(prev => {
            const next = new Set(prev);
            next.delete(item.id);
            return next;
          });
        });
    },
    [onUploadLocalAttachment]
  );
  handleAddAttachmentRef.current = handleAddAttachment;

  // ── RTF toolbar slot (owner UAT 2026-07-24) ────────────────────────────────
  // The attachment / record-lookup / connector actions live INLINE in the RichTextEditor
  // toolbar (a divider precedes them; see ToolbarPlugin). Built here so they have access to
  // dispatch, the file input, the editor ref (insert-at-cursor), and the host callbacks.
  const canAddLocal = !state.readOnly && attachmentSources.some(s => s.kind === 'local');
  const canLinkDocument = !state.readOnly && !!props.onLookupRecord;
  // Record search excludes Document (owner UAT 2026-07-24): documents are attached via the
  // paperclip's "Link documents"; every other catalog type inserts a body link.
  const recordSearchCatalog = React.useMemo(
    () => (props.recordLookupCatalog ?? []).filter(t => t.logicalName !== DOCUMENT_LOGICAL_NAME),
    [props.recordLookupCatalog]
  );
  const showRecordSearch = !state.readOnly && recordSearchCatalog.length > 0 && !!props.onLookupRecord;
  // Owner UAT 2026-07-30 (item 11): the connector toolbar icon is REMOVED. Relating an email to
  // a record now happens via the "Link another record" tile inside the "Related to" section (item 10).
  const canLinkRecord = !state.readOnly && !!props.onAddRelationship;

  const runDocumentLink = React.useCallback(() => {
    if (!props.onLookupRecord) return;
    void props.onLookupRecord(DOCUMENT_LOGICAL_NAME).then(picked => {
      if (picked) handleRecordPicked(picked);
    });
  }, [props.onLookupRecord, handleRecordPicked]);

  const runRecordSearch = React.useCallback(
    (logicalName: string) => {
      if (!props.onLookupRecord) return;
      void props.onLookupRecord(logicalName).then(picked => {
        if (picked) handleRecordPicked(picked);
      });
    },
    [props.onLookupRecord, handleRecordPicked]
  );

  // ── Template picker (Wave E) ───────────────────────────────────────────────
  // The toolbar template button renders only when the host supplies BOTH the list + render
  // callbacks and the composer is editable. Applying a template REPLACES subject + body
  // (mirrors Outlook "Apply template"); the field codes are merged server-side against the
  // confirmed primary regarding (`associations[0]`).
  const showTemplatePicker = !state.readOnly && !!props.onListEmailTemplates && !!props.onRenderEmailTemplate;

  const loadTemplates = React.useCallback(() => {
    const list = props.onListEmailTemplates;
    if (!list) return;
    setTemplatesLoading(true);
    setTemplateError(null);
    void list()
      .then(items => setTemplates(items ?? []))
      .catch(() => setTemplateError('Could not load templates.'))
      .finally(() => setTemplatesLoading(false));
  }, [props.onListEmailTemplates]);

  const applyTemplate = React.useCallback(
    (templateId: string) => {
      const render = props.onRenderEmailTemplate;
      if (!render) return;
      const primary = stateRef.current.associations[0];
      void render({
        templateId,
        regardingEntityType: primary?.entityType,
        regardingRecordId: primary?.entityId,
      })
        .then(result => {
          if (!result) return;
          dispatch({ type: 'SET_BODY_FORMAT', value: result.isHtml ? 'HTML' : 'PlainText' });
          dispatch({ type: 'SET_FIELD', field: 'subject', value: result.subject ?? '' });
          dispatch({ type: 'SET_FIELD', field: 'body', value: result.body ?? '' });
        })
        .catch(() => setTemplateError('Could not apply the template.'));
    },
    [props.onRenderEmailTemplate]
  );

  const handleTemplatePick = React.useCallback(
    (templateId: string) => {
      const hasContent = stateRef.current.subject.trim().length > 0 || !isComposeBodyEmpty(stateRef.current.body);
      if (hasContent) {
        setPendingTemplateId(templateId);
      } else {
        applyTemplate(templateId);
      }
    },
    [applyTemplate]
  );

  const confirmTemplate = React.useCallback(() => {
    if (pendingTemplateId) applyTemplate(pendingTemplateId);
    setPendingTemplateId(null);
  }, [pendingTemplateId, applyTemplate]);
  const cancelTemplate = React.useCallback(() => setPendingTemplateId(null), []);

  // ── AI "sparkle" draft (Wave E) ────────────────────────────────────────────
  // Rendered only when the host wires `onDraftWithAi` and the composer is editable. The engine passes
  // the CURRENT body/subject; the host builds the prompt + calls the BFF; the returned text REPLACES the
  // body (the user explicitly invoked a draft). A short in-flight guard prevents double-submits.
  const showSparkle = !state.readOnly && !!props.onDraftWithAi;
  const aiDraftActions = props.aiDraftActions ?? DEFAULT_AI_DRAFT_ACTIONS;
  const hasAiSelection = aiSelectionText.trim().length > 0;
  // Hide selection-scoped quick actions unless the author has a live text selection.
  const visibleAiActions = React.useMemo(
    () => aiDraftActions.filter(a => !SELECTION_SCOPED_AI_INTENTS.has(a.intent) || hasAiSelection),
    [aiDraftActions, hasAiSelection]
  );

  const runAiDraft = React.useCallback(
    (intent: string, opts?: { userInstruction?: string; selectionText?: string }) => {
      const draft = props.onDraftWithAi;
      if (!draft) return;
      const selectionText = opts?.selectionText?.trim();
      // Only scope to the selection in HTML mode — that's where Lexical retains the range
      // we can replace via insertAtCursor (the plain textarea exposes no selection here).
      const scopeToSelection = !!selectionText && stateRef.current.bodyFormat === 'HTML' && !!bodyEditorRef.current;
      setAiDrafting(true);
      setAiError(null);
      setAiMenuOpen(false);
      const isHtml = stateRef.current.bodyFormat === 'HTML';
      void draft({
        intent,
        userInstruction: opts?.userInstruction,
        // Selection-scoped actions transform ONLY the selected text; whole-draft actions
        // send the full body.
        currentBody: scopeToSelection ? (selectionText as string) : stateRef.current.body,
        isHtml,
        subject: stateRef.current.subject,
      })
        .then(result => {
          if (!result || !result.text) {
            setAiError('No draft was produced. Please try again.');
            return;
          }
          const resultIsHtml = result.isHtml ?? isHtml;
          if (scopeToSelection) {
            // Replace the retained editor selection with the transformed text (leaves the
            // rest of the draft untouched). The editor's onChange propagates the new body.
            bodyEditorRef.current?.insertAtCursor(result.text, resultIsHtml ? 'html' : 'text');
          } else {
            // owner UAT 2026-08-03 R5 items 1/2 — on a reply/forward, KEEP the quoted previous
            // thread: the AI generates the author's message, then we re-append the stored quoted
            // block below it (so the thread survives the draft AND is included when sent).
            const quoted = stateRef.current.quotedThread;
            const nextBody = quoted
              ? `${result.text}${resultIsHtml ? '<p></p>' : '\n\n'}${quoted}`
              : result.text;
            dispatch({ type: 'SET_BODY_FORMAT', value: resultIsHtml ? 'HTML' : 'PlainText' });
            dispatch({ type: 'SET_FIELD', field: 'body', value: nextBody });
          }
        })
        .catch(() => setAiError('AI drafting is unavailable right now.'))
        .finally(() => setAiDrafting(false));
    },
    [props.onDraftWithAi]
  );

  const submitAiPrompt = React.useCallback(() => {
    const instruction = aiPromptText.trim();
    if (!instruction) return;
    setAiPromptText('');
    runAiDraft('custom', { userInstruction: instruction });
  }, [aiPromptText, runAiDraft]);

  const runAiQuickAction = React.useCallback(
    (intent: string) => {
      runAiDraft(intent, SELECTION_SCOPED_AI_INTENTS.has(intent) ? { selectionText: aiSelectionText } : undefined);
    },
    [runAiDraft, aiSelectionText]
  );

  // Capture the live editor selection the moment the sparkle Popover opens (HTML mode:
  // Lexical retains it across the focus change; plain mode reports none), then focus the
  // prompt field so the user can type immediately.
  const handleAiMenuOpenChange = React.useCallback((open: boolean) => {
    if (open) {
      const selected = bodyEditorRef.current?.getSelectedText?.() ?? '';
      setAiSelectionText(selected);
      setAiError(null);
    }
    setAiMenuOpen(open);
  }, []);

  React.useEffect(() => {
    if (!aiMenuOpen) return;
    const t = window.setTimeout(() => aiPromptInputRef.current?.focus(), 0);
    return () => window.clearTimeout(t);
  }, [aiMenuOpen]);

  const toolbarSlot =
    canAddLocal || canLinkDocument || showRecordSearch || showTemplatePicker || showSparkle ? (
      <div className={styles.toolbarSlot}>
        {(canAddLocal || canLinkDocument) && (
          <Menu positioning="below-end">
            <MenuTrigger disableButtonEnhancement>
              <Tooltip content="Attach files or link a document" relationship="label">
                <ToolbarButton icon={<Attach20Regular />} aria-label="Attach" />
              </Tooltip>
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                {canAddLocal && <MenuItem onClick={() => fileInputRef.current?.click()}>Add files</MenuItem>}
                {canLinkDocument && <MenuItem onClick={runDocumentLink}>Link documents</MenuItem>}
              </MenuList>
            </MenuPopover>
          </Menu>
        )}
        {showRecordSearch && (
          // Grouped in the SAME toolbar section as the paperclip — no divider between them
          // (owner UAT 2026-07-27). The section-leading divider is still supplied once by
          // ToolbarPlugin, separating this whole group from the editor controls.
          <Menu positioning="below-end">
            <MenuTrigger disableButtonEnhancement>
              <Tooltip content="Insert a link to a record" relationship="label">
                <ToolbarButton icon={<SearchRegular />} aria-label="Insert record link" />
              </Tooltip>
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                {recordSearchCatalog.map(t => (
                  <MenuItem key={t.logicalName} onClick={() => runRecordSearch(t.logicalName)}>
                    {t.displayName}
                  </MenuItem>
                ))}
              </MenuList>
            </MenuPopover>
          </Menu>
        )}
        {/* "|" separator between the attach/record-link group and the template/AI group
            (owner UAT 2026-08-03 R5 item 3) — only when both groups have at least one tool. */}
        {(showTemplatePicker || showSparkle) && (canAddLocal || canLinkDocument || showRecordSearch) && (
          <span className={styles.toolbarDivider} role="separator" aria-orientation="vertical" />
        )}
        {showTemplatePicker && (
          // Apply an email template (Wave E). Grouped with the paperclip/search in the same
          // toolbar section. Templates load on menu open; picking one fills subject + body.
          <Menu
            positioning="below-end"
            onOpenChange={(_e, data) => {
              if (data.open) loadTemplates();
            }}
          >
            <MenuTrigger disableButtonEnhancement>
              <Tooltip content="Apply an email template" relationship="label">
                <ToolbarButton icon={<DocumentText20Regular />} aria-label="Apply template" />
              </Tooltip>
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                {templatesLoading && <MenuItem disabled>Loading…</MenuItem>}
                {!templatesLoading && templateError && <MenuItem disabled>{templateError}</MenuItem>}
                {!templatesLoading && !templateError && (templates?.length ?? 0) === 0 && (
                  <MenuItem disabled>No templates available</MenuItem>
                )}
                {!templatesLoading &&
                  !templateError &&
                  templates?.map(t => (
                    <MenuItem key={t.id} onClick={() => handleTemplatePick(t.id)}>
                      {t.name}
                    </MenuItem>
                  ))}
              </MenuList>
            </MenuPopover>
          </Menu>
        )}
        {showSparkle && (
          // AI draft "sparkle" (Wave E; redesigned owner UAT 2026-08-03 R5 item 5): an
          // inline Popover with a free-text prompt (type + Generate) and a "+" quick-
          // responses menu. Selection-scoped actions (concise / formal / friendly) appear
          // only when the author has text selected. Spinner + disabled while in flight.
          <Popover
            open={aiMenuOpen}
            onOpenChange={(_e, data) => handleAiMenuOpenChange(data.open)}
            positioning="above-end"
            trapFocus
          >
            <PopoverTrigger disableButtonEnhancement>
              <Tooltip content="Draft or refine with AI" relationship="label">
                <ToolbarButton
                  icon={aiDrafting ? <Spinner size="tiny" /> : <Sparkle20Regular />}
                  aria-label="Draft with AI"
                  disabled={aiDrafting}
                />
              </Tooltip>
            </PopoverTrigger>
            <PopoverSurface>
              <div className={styles.aiPopover} role="group" aria-label="Draft with AI">
                <Text weight="semibold">Draft with AI</Text>
                <Textarea
                  ref={aiPromptInputRef}
                  className={styles.aiPromptInput}
                  value={aiPromptText}
                  onChange={(_e, data) => setAiPromptText(data.value)}
                  onKeyDown={e => {
                    // Ctrl/Cmd+Enter submits (a bare Enter keeps newlines for multi-line prompts).
                    if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) {
                      e.preventDefault();
                      submitAiPrompt();
                    }
                  }}
                  placeholder="e.g. Draft a polite reply asking for the signed contract by Friday."
                  aria-label="AI prompt"
                  resize="vertical"
                />
                <div className={styles.aiPopoverActions}>
                  <Menu positioning="above-start">
                    <MenuTrigger disableButtonEnhancement>
                      <Tooltip content="Quick responses" relationship="label">
                        <Button appearance="subtle" icon={<Add20Regular />} aria-label="Quick responses" />
                      </Tooltip>
                    </MenuTrigger>
                    <MenuPopover>
                      <MenuList>
                        {visibleAiActions.map(a => (
                          <MenuItem key={a.intent} onClick={() => runAiQuickAction(a.intent)}>
                            {a.label}
                          </MenuItem>
                        ))}
                      </MenuList>
                    </MenuPopover>
                  </Menu>
                  <div className={styles.aiPopoverButtons}>
                    <Button appearance="primary" onClick={submitAiPrompt} disabled={!aiPromptText.trim() || aiDrafting}>
                      Generate
                    </Button>
                    <Button appearance="secondary" onClick={() => setAiMenuOpen(false)}>
                      Cancel
                    </Button>
                  </div>
                </div>
              </div>
            </PopoverSurface>
          </Popover>
        )}
      </div>
    ) : undefined;

  // ── Re-derive state when mode/sourceRecord/communicationId change on an
  //    already-mounted instance (host swaps props rather than remounting) ──
  // Also moves focus to the section heading on transition (NFR-03 "focus
  // management on mode transitions") — screen-reader + keyboard users get an
  // announcement + a sane focus target instead of losing their place when the
  // host flips e.g. view → reply on the same mounted instance.
  const rootRef = React.useRef<HTMLDivElement>(null);
  const initKeyRef = React.useRef(`${props.mode}:${props.communicationId ?? ''}`);
  React.useEffect(() => {
    const key = `${props.mode}:${props.communicationId ?? ''}`;
    if (key !== initKeyRef.current) {
      initKeyRef.current = key;
      dispatch({ type: 'RESET', state: initialState(props) });
      rootRef.current?.focus();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [props.mode, props.communicationId, props.sourceRecord]);

  // ── onStateChange (inline mount — wizard polls this for Next/Send gating) ──
  React.useEffect(() => {
    props.onStateChange?.(state);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [state]);

  // ── Autofocus the body at the top on open (owner UAT 2026-08-03 R5 item 4) ──
  // When the composer opens editable (new / reply / reply-all / forward), drop the
  // caret at the TOP of the drafting field so the user types immediately without a
  // click. Mount-only, so mode transitions on an already-mounted instance still get
  // the a11y heading-focus above (the reset effect). A microtask defer lets the
  // Lexical editor mount before we focus. Read-only (view mode) never grabs focus.
  React.useEffect(() => {
    if (stateRef.current.readOnly) return;
    const t = window.setTimeout(() => bodyEditorRef.current?.focus('start'), 0);
    return () => window.clearTimeout(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ── Imperative handle ──────────────────────────────────────────────────
  const validate = React.useCallback((): IValidationResult => {
    const result = validateState(stateRef.current, {
      forSend: true,
      allowEmptyBody: props.allowEmptyBody,
      maxRecipients: props.maxRecipients,
    });
    dispatch({ type: 'SET_VALIDATION_ERRORS', result });
    return result;
  }, [props.allowEmptyBody, props.maxRecipients]);

  const send = React.useCallback(async (): Promise<{ communicationId: string }> => {
    const result = validateState(stateRef.current, {
      forSend: true,
      allowEmptyBody: props.allowEmptyBody,
      maxRecipients: props.maxRecipients,
    });
    dispatch({ type: 'SET_VALIDATION_ERRORS', result });
    if (!result.ok) {
      throw new Error(
        'EmailComposer.send(): validation failed — call validate() first and surface the errors before sending.'
      );
    }

    dispatch({ type: 'BEGIN_SEND' });
    try {
      // R2 item 12: swap linked document attachments' internal URL for a recipient-openable SPE
      // sharing link (best-effort; a failure keeps the prior URL and never blocks the send).
      const resolvedAttachments = await resolveAttachmentShareLinks(
        stateRef.current.attachments,
        props.onResolveShareLink
      );
      const request = mapStateToSendRequest({ ...stateRef.current, attachments: resolvedAttachments }, props.threadId);
      const response = await sendCommunication(request, {
        authenticatedFetch: props.authenticatedFetch,
        bffBaseUrl: props.bffBaseUrl,
      });
      dispatch({ type: 'END_SEND' });
      props.onSent?.(response);
      return response;
    } catch (err) {
      dispatch({ type: 'END_SEND' });
      if (err instanceof SendCommunicationError) {
        props.onError?.(err);
      }
      throw err;
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [props.authenticatedFetch, props.bffBaseUrl, props.allowEmptyBody, props.maxRecipients, props.threadId]);

  const saveDraft = React.useCallback(async (): Promise<{ communicationId: string }> => {
    if (!props.onSaveDraftRequest) {
      // No BFF draft-persistence endpoint exists yet (CommunicationEndpoints.cs
      // has /send, /send-bulk, /{id}/status only) — see EmailComposer.reducer.ts
      // `mapStateToDraftUpdate` doc comment + task 020 Decisions Made.
      throw new Error(
        'EmailComposer.saveDraft(): no onSaveDraftRequest handler was provided by the host. ' +
          'No BFF draft-persistence endpoint exists yet — wire onSaveDraftRequest once one ships.'
      );
    }
    dispatch({ type: 'BEGIN_SAVE_DRAFT' });
    try {
      const response = await props.onSaveDraftRequest(stateRef.current);
      dispatch({ type: 'END_SAVE_DRAFT' });
      props.onSaveDraft?.(response);
      return response;
    } catch (err) {
      dispatch({ type: 'END_SAVE_DRAFT' });
      throw err;
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [props.onSaveDraftRequest, props.onSaveDraft]);

  const getState = React.useCallback((): EmailComposerState => stateRef.current, []);

  useImperativeHandle(ref, () => ({ validate, send, saveDraft, getState }), [validate, send, saveDraft, getState]);

  // ── Field handlers ──────────────────────────────────────────────────────
  const fieldErrors = React.useMemo(() => {
    const map: Partial<Record<'to' | 'subject' | 'body' | 'attachments' | 'from', string>> = {};
    for (const e of state.validation.errors) {
      map[e.field] = map[e.field] ? `${map[e.field]}; ${e.message}` : e.message;
    }
    return map;
  }, [state.validation.errors]);

  const canSend = state.to.length > 0 && !!state.subject.trim() && !state.isSending;

  // ── Render ──────────────────────────────────────────────────────────────
  const mountClass = props.mount === 'page' ? styles.page : props.mount === 'dialog' ? styles.dialog : styles.inline;
  const isChromed = props.mount !== 'inline';

  const middle = (
    <>
      {/* R3 task 020 (FR-07): optional record-link affordance when opened from a
          conversation. Presentational only — semantic tokens, no hardcoded color.
          Unsafe-scheme urls degrade to a non-clickable label (isSafeHref). */}
      {props.recordLink && (
        <div className={styles.section} role="region" aria-label="Related record">
          {isSafeHref(props.recordLink.url) ? (
            <Link href={props.recordLink.url} target="_blank" rel="noopener noreferrer">
              {props.recordLink.label}
            </Link>
          ) : (
            <Text>{props.recordLink.label}</Text>
          )}
        </div>
      )}

      {/* Live region — announces validation errors to assistive tech (NFR-03). */}
      <div aria-live="polite" className={styles.liveRegion}>
        {!state.validation.ok &&
          `${state.validation.errors.length} validation error(s): ${state.validation.errors.map(e => e.message).join('; ')}`}
      </div>

      {!state.validation.ok && state.validation.errors.length > 0 && (
        <MessageBar intent="error" role="alert">
          <MessageBarBody>{state.validation.errors.map(e => e.message).join(' ')}</MessageBarBody>
        </MessageBar>
      )}

      {/* From: — Outlook-style sender line ABOVE To/Cc (owner UAT 2026-07-30, item 3). Shows the
          current sender; a dropdown switches between the user's mailbox and the Spaarke shared
          mailbox. Wired to the engine's existing `sendMode` state. Hidden in read-only (view)
          mode — there is nothing to send. The primary Send control lives HERE, left of "From:"
          (owner UAT 2026-08-03 item 1 — Outlook-style Send in the address section, not a bottom bar). */}
      {!state.readOnly && (
        <div className={styles.fromRow} role="group" aria-label="From">
          <ComposerSendButton
            isSending={state.isSending}
            canSend={canSend}
            sendMode={state.sendMode}
            showSendModeChoice={showSendModeRadio && !state.readOnly}
            onSendModeChange={value => dispatch({ type: 'SET_SEND_MODE', value })}
            onSend={() => {
              send().catch(() => {
                /* onError already notified; swallow so the click doesn't reject. */
              });
            }}
          />
          {/* "From:" as plain semibold text, not a boxed label (owner UAT 2026-07-30 R2 item 9);
              the mailbox value beside it stays a subtle switcher (user ↔ Spaarke shared mailbox). */}
          <span className={styles.fromLabel} aria-hidden="true">
            From:
          </span>
          <div className={styles.fromValue}>
            {showSenderChoice ? (
              <Menu
                positioning="below-start"
                checkedValues={{ sendFrom: [state.sendMode] }}
                onCheckedValueChange={(_e, data) => {
                  const next = data.checkedItems[0] as CommunicationSendMode | undefined;
                  if (next) dispatch({ type: 'SET_SEND_MODE', value: next });
                }}
              >
                <MenuTrigger disableButtonEnhancement>
                  <MenuButton appearance="subtle" size="small" aria-label="From mailbox">
                    {state.sendMode === 'user' ? userMailboxLabel(state.fromMailbox) : SHARED_MAILBOX_LABEL}
                  </MenuButton>
                </MenuTrigger>
                <MenuPopover>
                  <MenuList>
                    <MenuItemRadio name="sendFrom" value="user">
                      {userMailboxLabel(state.fromMailbox)}
                    </MenuItemRadio>
                    <MenuItemRadio name="sendFrom" value="sharedMailbox">
                      {SHARED_MAILBOX_LABEL}
                    </MenuItemRadio>
                  </MenuList>
                </MenuPopover>
              </Menu>
            ) : (
              <Text className={styles.fromStaticText}>
                {state.sendMode === 'user' ? userMailboxLabel(state.fromMailbox) : SHARED_MAILBOX_LABEL}
              </Text>
            )}
          </div>
        </div>
      )}

      <div className={styles.section} role="region" aria-label="Recipients">
        <RecipientField
          label="To"
          required
          disabled={state.readOnly}
          value={state.to}
          onChange={recipients => dispatch({ type: 'SET_RECIPIENTS', field: 'to', value: recipients })}
          onSearch={props.onSearchRecipients}
          onLookup={props.onLookupRecipients ? () => props.onLookupRecipients!('to') : undefined}
          errorMessage={fieldErrors.to}
        />
        <RecipientField
          label="Cc"
          disabled={state.readOnly}
          value={state.cc}
          onChange={recipients => dispatch({ type: 'SET_RECIPIENTS', field: 'cc', value: recipients })}
          onSearch={props.onSearchRecipients}
          onLookup={props.onLookupRecipients ? () => props.onLookupRecipients!('cc') : undefined}
        />
        {bccVisible ? (
          <RecipientField
            label="Bcc"
            disabled={state.readOnly}
            value={state.bcc}
            onChange={recipients => dispatch({ type: 'SET_RECIPIENTS', field: 'bcc', value: recipients })}
            onSearch={props.onSearchRecipients}
            onLookup={props.onLookupRecipients ? () => props.onLookupRecipients!('bcc') : undefined}
          />
        ) : (
          !state.readOnly && (
            <div className={styles.bccToggleRow}>
              <Button appearance="subtle" size="small" onClick={() => setShowBccToggle(true)}>
                Bcc
              </Button>
            </div>
          )
        )}
      </div>

      <div className={mergeClasses(styles.section, styles.subjectSpacer)} role="region" aria-label="Subject">
        <Input
          value={state.subject}
          onChange={e => dispatch({ type: 'SET_FIELD', field: 'subject', value: e.target.value })}
          placeholder="Add a subject"
          aria-label="Subject"
          disabled={state.readOnly}
          appearance="underline"
        />
        {fieldErrors.subject && (
          <Text size={200} role="alert" style={{ color: tokens.colorPaletteRedForeground1 }}>
            {fieldErrors.subject}
          </Text>
        )}
      </div>

      {/* Hidden local-file input — triggered by the RTF toolbar's paperclip "Add files"
          (owner UAT 2026-07-24). */}
      <input
        ref={fileInputRef}
        type="file"
        multiple
        hidden
        onChange={handleLocalFilesPicked}
        aria-label="Choose files from your computer"
      />

      {/* Attachments — display-only collapsible list, DEFAULT COLLAPSED (owner UAT 2026-07-24);
          the add/link controls now live in the RTF toolbar. */}
      <div className={styles.section} role="region" aria-label="Attachments">
        <AttachmentList
          items={state.attachments}
          onRemove={id => dispatch({ type: 'REMOVE_ATTACHMENT', id })}
          onToggleSelected={id => dispatch({ type: 'TOGGLE_ATTACHMENT_SELECTED', id })}
          onToggleLink={id => dispatch({ type: 'TOGGLE_ATTACHMENT_LINK', id })}
          resolvingIds={resolvingIds}
          readOnly={state.readOnly}
          errorMessage={[fieldErrors.attachments, uploadError, ...pickErrors].filter(Boolean).join(' ') || undefined}
        />
      </div>

      {/* Related to — what this email is associated to. In compose/reply/forward NO record exists
          yet, so associations are held IN MEMORY (reducer state) and ride the send payload (owner
          UAT 2026-07-31). The user can ADD several via "Link another record" (each pick is appended,
          deduped) and REMOVE any via the chip ×. Index 0 is the GREEN primary regarding (the BFF
          maps associations[0] onto sprk_regardingrecord* at send). The label, chips, and the "Link
          another record" tile flow on ONE wrapping row; the tile renders whenever the host can add a
          relationship (`canLinkRecord`) — even with ZERO associations, so the first can be added. */}
      {(showAssociations || canLinkRecord) && (
        <div className={styles.section} role="region" aria-label="Related to">
          <div className={styles.relatedRow}>
            <div
              className={styles.relatedToggle}
              role="button"
              tabIndex={0}
              aria-expanded={!relatedCollapsed}
              onClick={() => setRelatedCollapsed(c => !c)}
              onKeyDown={e => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault();
                  setRelatedCollapsed(c => !c);
                }
              }}
            >
              <Text className={styles.sectionLabel}>
                Related to{state.associations.length > 0 ? ` (${state.associations.length})` : ''}
              </Text>
              {relatedCollapsed ? <ChevronDown20Regular /> : <ChevronUp20Regular />}
            </div>
            {!relatedCollapsed && state.associations.length > 0 && (
              <AssociationChips
                associations={state.associations}
                primaryIndex={0}
                onRemove={
                  state.readOnly
                    ? undefined
                    : a =>
                        dispatch({
                          type: 'REMOVE_ASSOCIATION',
                          entityType: a.entityType,
                          entityId: a.entityId,
                        })
                }
              />
            )}
            {!relatedCollapsed &&
              (canLinkRecord ? (
                <button type="button" className={styles.linkTile} onClick={handleAddRelationship}>
                  <Search20Regular />
                  <span>{state.associations.length > 0 ? 'Link another record' : 'Link a record'}</span>
                </button>
              ) : state.associations.length === 0 ? (
                <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                  Not related to any record yet.
                </Text>
              ) : null)}
          </div>
        </div>
      )}

      {/* Template overwrite confirm (Wave E) — only shown when applying would replace
          existing subject/body. modalType="alert" disables light-dismiss so a stray click
          can't discard the in-progress draft. */}
      <Dialog
        open={pendingTemplateId !== null}
        modalType="alert"
        onOpenChange={(_e, data) => {
          if (!data.open) cancelTemplate();
        }}
      >
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Apply template?</DialogTitle>
            <DialogContent>
              <Text>This will replace the current subject and message body.</Text>
            </DialogContent>
            <DialogActions>
              <Button appearance="primary" onClick={confirmTemplate}>
                Apply
              </Button>
              <Button appearance="secondary" onClick={cancelTemplate}>
                Cancel
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>

      {aiError && (
        <MessageBar intent="warning">
          <MessageBarBody>{aiError}</MessageBarBody>
        </MessageBar>
      )}

      <BodyEditor
        ref={bodyEditorRef}
        value={state.body}
        format={state.bodyFormat}
        onChange={value => dispatch({ type: 'SET_FIELD', field: 'body', value })}
        onFormatChange={value => dispatch({ type: 'SET_BODY_FORMAT', value })}
        readOnly={state.readOnly}
        required={!props.allowEmptyBody}
        errorMessage={fieldErrors.body}
        minHeight={props.mount === 'dialog' ? 200 : 220}
        toolbarSlot={toolbarSlot}
      />
    </>
  );

  return (
    <div
      ref={rootRef}
      tabIndex={-1}
      className={mergeClasses(styles.base, mountClass, props.className)}
      role="region"
      aria-label="Email composer"
    >
      {isChromed && (
        <div className={styles.header}>
          <Text as="h2" weight="semibold" className={styles.headerTitle}>
            {props.titleOverride ??
              (state.mode === 'view'
                ? 'Email'
                : state.mode === 'reply'
                  ? 'Reply'
                  : state.mode === 'forward'
                    ? 'Forward'
                    : state.mode === 'draft'
                      ? 'Edit Draft'
                      : 'New Email')}
          </Text>
          {/* Standard Spaarke modal window controls (maximize/restore + close ×) in the
              upper-RIGHT — the shared `ModalWindowControls` so every modal matches (owner
              UAT 2026-07-31 item 4). Close routes to the SAME `props.onCancel` the
              ComposerActionBar Cancel button uses. Maximize shows only when the host wires
              `onToggleMaximize` (e.g. SendEmailDialog, which owns the surface sizing). */}
          <ModalWindowControls
            isMaximized={props.isMaximized}
            onToggleMaximize={props.onToggleMaximize}
            onClose={props.onCancel}
          />
        </div>
      )}

      {/* Fields + message flow directly in the flex column: the message editor
          (BodyEditor, flex-grow) fills the remaining space and owns the single scroll
          region via the shared RichTextEditor (owner UAT round 4). */}
      {middle}

      <ComposerActionBar
        mount={props.mount}
        mode={state.mode}
        isSending={state.isSending}
        isSavingDraft={state.isSavingDraft}
        isDraftRecord={props.isDraftRecord}
        onSaveDraft={() => {
          saveDraft().catch(() => {
            /* surfaced via onSaveDraft/props.onError-equivalent is not
               defined for drafts yet; the thrown error is still available
               to callers awaiting composerRef.current.saveDraft() directly. */
          });
        }}
        onCancel={() => props.onCancel?.()}
        onEdit={props.onEdit}
        onReply={props.onReply}
        onForward={props.onForward}
      />
    </div>
  );
});

EmailComposer.displayName = 'EmailComposer';
