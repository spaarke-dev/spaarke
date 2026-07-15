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
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  Text,
  makeStyles,
  tokens,
  mergeClasses,
} from '@fluentui/react-components';

import { sendCommunication, SendCommunicationError } from '../../services/communicationApi';
import type { SendCommunicationOptions } from '../../services/communicationApi';

import { emailComposerReducer, initialState, validateState } from './EmailComposer.reducer';
import type {
  EmailComposerState,
  IEmailComposerHandle,
  IEmailComposerProps,
  IComposerAttachmentSource,
  IValidationResult,
} from './EmailComposer.types';

import { RecipientField } from './subcomponents/RecipientField';
import { BodyEditor } from './subcomponents/BodyEditor';
import { AttachmentList } from './subcomponents/AttachmentList';
import { SendModeRadio } from './subcomponents/SendModeRadio';
import { AssociationChips } from './subcomponents/AssociationChips';
import { ComposerActionBar } from './subcomponents/ComposerActionBar';

// ---------------------------------------------------------------------------
// Mapping: engine state → sendCommunication() request
// ---------------------------------------------------------------------------

function mapStateToSendRequest(state: EmailComposerState): SendCommunicationOptions {
  return {
    to: state.to.map(r => r.email),
    cc: state.cc.length > 0 ? state.cc.map(r => r.email) : undefined,
    bcc: state.bcc.length > 0 ? state.bcc.map(r => r.email) : undefined,
    subject: state.subject,
    body: state.body,
    bodyFormat: state.bodyFormat === 'HTML' ? 'html' : 'text',
    // `attachmentDocumentIds` correctly carries `sprk_document` GUIDs (R4 W0
    // owner decision, 2026-07-14 — NO rename; see communicationApi.ts
    // file-level note). Only items with a resolved `documentId` AND not
    // forward-deselected are sent; locally-picked files without a resolved
    // Document yet are excluded (see AttachmentList.tsx doc comment).
    attachmentDocumentIds: state.attachments
      .filter(a => a.selected !== false && a.documentId)
      .map(a => a.documentId as string),
    archiveToSpe: state.archiveToSpe,
    associations: state.associations,
    sendMode: state.sendMode,
    fromMailbox: state.fromMailbox,
  };
}

function defaultAttachmentSources(
  attachmentSources: IComposerAttachmentSource[] | undefined,
  wizardContext: IEmailComposerProps['wizardContext']
): IComposerAttachmentSource[] {
  if (attachmentSources) return attachmentSources;
  return wizardContext
    ? [{ kind: 'wizard' }, { kind: 'related' }, { kind: 'local' }, { kind: 'spe' }]
    : [{ kind: 'local' }, { kind: 'related' }, { kind: 'spe' }];
}

// ---------------------------------------------------------------------------
// Styles — three mount variants (design §5.10). Layout density/chrome only;
// all share the same Fluent semantic tokens (dark mode passes through the
// host FluentProvider — no hardcoded colors anywhere in this file).
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  // Shared across all mounts.
  base: {
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
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },
  liveRegion: {
    position: 'absolute',
    width: '1px',
    height: '1px',
    overflow: 'hidden',
    clip: 'rect(0 0 0 0)',
  },

  // `page` — full-width first-class entity-form chrome (replaces the OOB
  // sprk_communication form; the highest-value visual deliverable per §5.10).
  page: {
    maxWidth: '960px',
    marginLeft: 'auto',
    marginRight: 'auto',
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
    paddingLeft: tokens.spacingHorizontalXXL,
    paddingRight: tokens.spacingHorizontalXXL,
  },

  // `dialog` — compact, bounded width; body editor sized for shorter messages.
  dialog: {
    width: '100%',
    maxWidth: '600px',
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
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
      const request = mapStateToSendRequest(stateRef.current);
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
  }, [props.authenticatedFetch, props.bffBaseUrl, props.allowEmptyBody, props.maxRecipients]);

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

  return (
    <div
      ref={rootRef}
      tabIndex={-1}
      className={mergeClasses(styles.base, mountClass, props.className)}
      role="region"
      aria-label="Email composer"
    >
      {props.mount !== 'inline' && (
        <div className={styles.header}>
          <Text as="h2" size={600} weight="semibold">
            {state.mode === 'view'
              ? 'Email'
              : state.mode === 'reply'
                ? 'Reply'
                : state.mode === 'forward'
                  ? 'Forward'
                  : state.mode === 'draft'
                    ? 'Edit Draft'
                    : 'New Email'}
          </Text>
        </div>
      )}

      {showAssociations && state.associations.length > 0 && (
        <div className={styles.section} role="region" aria-label="Linked records">
          <AssociationChips associations={state.associations} />
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
          errorMessage={fieldErrors.to}
        />
        <RecipientField
          label="Cc"
          disabled={state.readOnly}
          value={state.cc}
          onChange={recipients => dispatch({ type: 'SET_RECIPIENTS', field: 'cc', value: recipients })}
          onSearch={props.onSearchRecipients}
        />
        <RecipientField
          label="Bcc"
          disabled={state.readOnly}
          value={state.bcc}
          onChange={recipients => dispatch({ type: 'SET_RECIPIENTS', field: 'bcc', value: recipients })}
          onSearch={props.onSearchRecipients}
        />
      </div>

      <div className={styles.section} role="region" aria-label="Subject">
        <Field label="Subject" required validationState={fieldErrors.subject ? 'error' : 'none'}>
          <Input
            value={state.subject}
            onChange={e => dispatch({ type: 'SET_FIELD', field: 'subject', value: e.target.value })}
            placeholder="Subject"
            aria-label="Subject"
            disabled={state.readOnly}
          />
        </Field>
        {fieldErrors.subject && (
          <Text size={200} role="alert" style={{ color: tokens.colorPaletteRedForeground1 }}>
            {fieldErrors.subject}
          </Text>
        )}
      </div>

      <BodyEditor
        value={state.body}
        format={state.bodyFormat}
        onChange={value => dispatch({ type: 'SET_FIELD', field: 'body', value })}
        onFormatChange={value => dispatch({ type: 'SET_BODY_FORMAT', value })}
        readOnly={state.readOnly}
        required={!props.allowEmptyBody}
        errorMessage={fieldErrors.body}
        minHeight={props.mount === 'dialog' ? 140 : 220}
      />

      <div className={styles.section} role="region" aria-label="Attachments">
        <AttachmentList
          mode={state.mode}
          sources={attachmentSources}
          items={state.attachments}
          onAdd={item => dispatch({ type: 'ADD_ATTACHMENT', item })}
          onRemove={id => dispatch({ type: 'REMOVE_ATTACHMENT', id })}
          onToggleSelected={id => dispatch({ type: 'TOGGLE_ATTACHMENT_SELECTED', id })}
          readOnly={state.readOnly}
          errorMessage={fieldErrors.attachments}
        />
      </div>

      {showSendModeRadio && !state.readOnly && (
        <SendModeRadio
          value={state.sendMode}
          onChange={value => dispatch({ type: 'SET_SEND_MODE', value })}
          disabled={state.readOnly}
        />
      )}

      <ComposerActionBar
        mount={props.mount}
        mode={state.mode}
        isSending={state.isSending}
        isSavingDraft={state.isSavingDraft}
        canSend={canSend}
        isDraftRecord={props.isDraftRecord}
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
