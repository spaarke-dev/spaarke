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
  Input,
  Link,
  MessageBar,
  MessageBarBody,
  Text,
  Tooltip,
  ToolbarButton,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  makeStyles,
  tokens,
  mergeClasses,
} from '@fluentui/react-components';
import {
  Dismiss20Regular,
  Attach20Regular,
  SearchRegular,
  Connector20Regular,
  ChevronDown20Regular,
  ChevronUp20Regular,
} from '@fluentui/react-icons';

import { sendCommunication, SendCommunicationError } from '../../services/communicationApi';
import type { ICommunicationAssociation } from '../../services/communicationApi';

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
  IValidationResult,
} from './EmailComposer.types';
import type { RichTextEditorRef } from '../RichTextEditor';

import { RecipientField } from './subcomponents/RecipientField';
import { BodyEditor } from './subcomponents/BodyEditor';
import { AttachmentList } from './subcomponents/AttachmentList';
import { AssociationChips } from './subcomponents/AssociationChips';
import { ComposerActionBar } from './subcomponents/ComposerActionBar';

// The Dataverse logical name for a governed Document — the paperclip's "Link documents"
// picks this type and ATTACHES it, so it's intentionally excluded from the record-search
// catalog (which inserts a body LINK for every other type). Owner UAT 2026-07-24.
const DOCUMENT_LOGICAL_NAME = 'sprk_document';

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
  header: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
  },
  headerActions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
  },
  bccToggleRow: {
    display: 'flex',
    justifyContent: 'flex-end',
  },
  // Collapsible section (Attachments, Related to) — a clickable header row (label + chevron)
  // over the section body. Owner UAT 2026-07-24.
  collapsibleHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
    cursor: 'pointer',
    // +4px padding over the section header (owner UAT 2026-07-24).
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
  },
  // Section label — standard Segoe UI 14px semibold, neutral foreground 1 (UI-DESIGN-STANDARDS
  // section-header spec; owner UAT 2026-07-24). Token-only so both themes resolve (ADR-021).
  sectionLabel: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  // Trailing controls placed into the RichTextEditor toolbar slot (paperclip / search /
  // connector). Kept visually grouped at the toolbar's trailing end.
  toolbarSlot: {
    display: 'flex',
    alignItems: 'center',
    gap: '2px',
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

  // Connector toolbar icon → add a relationship (owner UAT 2026-07-24, Option B). The host
  // runs the OOB lookup + writes the association; we reflect the picked record in "Related to".
  const onAddRelationship = props.onAddRelationship;
  const handleAddRelationship = React.useCallback(() => {
    if (!onAddRelationship) return;
    setRelatedCollapsed(false);
    void onAddRelationship()
      .then(picked => {
        if (!picked) return;
        const association: ICommunicationAssociation = {
          entityType: picked.entityType,
          entityId: picked.id,
          entityName: picked.name,
          entityUrl: picked.url,
        };
        dispatch({ type: 'ADD_ASSOCIATION', association });
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
  const showConnector = !state.readOnly && !!props.onAddRelationship;

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

  const toolbarSlot =
    canAddLocal || canLinkDocument || showRecordSearch || showConnector ? (
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
        {showConnector && (
          <Tooltip content="Relate this email to a record" relationship="label">
            <ToolbarButton
              icon={<Connector20Regular />}
              aria-label="Add relationship"
              onClick={handleAddRelationship}
            />
          </Tooltip>
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
      const request = mapStateToSendRequest(stateRef.current, props.threadId);
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

      {/* Related to — what this email is associated to (owner UAT 2026-07-24). Collapsible;
          the connector toolbar icon adds a new relationship (Option B). */}
      {showAssociations && (
        <div className={styles.section} role="region" aria-label="Related to">
          <div
            className={styles.collapsibleHeader}
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
          {!relatedCollapsed &&
            (state.associations.length > 0 ? (
              <AssociationChips associations={state.associations} />
            ) : (
              <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                Not related to any record yet.
              </Text>
            ))}
        </div>
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
          <Text as="h2" size={600} weight="semibold">
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
          {props.onCancel && (
            <div className={styles.headerActions}>
              <Button
                appearance="subtle"
                size="small"
                icon={<Dismiss20Regular />}
                aria-label="Close dialog"
                onClick={() => props.onCancel?.()}
              />
            </div>
          )}
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
        canSend={canSend}
        isDraftRecord={props.isDraftRecord}
        sendMode={state.sendMode}
        showSendModeChoice={showSendModeRadio && !state.readOnly}
        onSendModeChange={value => dispatch({ type: 'SET_SEND_MODE', value })}
        onSend={() => {
          send().catch(() => {
            /* onError callback already notified; swallow here so the button
               click doesn't produce an unhandled promise rejection. */
          });
        }}
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
