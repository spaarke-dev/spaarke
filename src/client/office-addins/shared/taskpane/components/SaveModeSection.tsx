import React from 'react';
import {
  makeStyles,
  tokens,
  Button,
  Checkbox,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  Text,
} from '@fluentui/react-components';
import type { DocumentIdentityState } from '../services/documentIdentityService';
import type { SaveTarget } from '../hooks/useSaveFlow';

/**
 * SaveModeSection — the FR-11 "new version" / "Save as new document" affordance
 * (spaarkeai-word-add-in-r1 task 024).
 *
 * When task 013 has resolved the open Word document to an existing `sprk_document`, Save DEFAULTS to a
 * new version of that record (`document.existingDocumentId`, task 023's server path) and the user may
 * explicitly override to "a new document". Every other identity outcome is handled here too, and none
 * of them dead-ends:
 *
 * | Identity            | Default                       | What the user can do                                |
 * |---------------------|-------------------------------|-----------------------------------------------------|
 * | not applicable      | create (unchanged)            | save                                                 |
 * | checking            | Save disabled                 | wait (resolution is in flight)                       |
 * | resolved            | **new version**               | save, or switch to "a new document"                  |
 * | new                 | create (unchanged)            | save — no version affordance at all                  |
 * | conflict            | Save disabled                 | check again; NO save-as-new is offered (it would     |
 * |                     |                               | collide with `sprk_graphitemid_uk`)                  |
 * | indeterminate/error | Save disabled                 | try again, or explicitly choose "as a new document"  |
 * | denied              | Save disabled                 | explicitly choose "as a new document"                |
 *
 * `indeterminate` and `error` are NEVER treated as "new": an outage or an unexpected failure says nothing
 * about whether Spaarke already tracks this file, and a silent create there is how duplicate rows get
 * minted. Only an explicit, user-initiated choice creates.
 *
 * Thin view under `shared/taskpane/` (ADR-012 Path A — no `@spaarke/ui-components`, no Xrm). Fluent v9
 * semantic tokens only (ADR-021); inline, not a modal (the narrow pane — ADR-050 is not engaged).
 */

/** The user's explicit save mode. `null` = no explicit choice (the identity's default applies). */
export type SaveModeChoice = 'version' | 'new';

/** Which state of this section renders. */
export type SaveModeView = 'none' | 'checking' | 'version' | 'conflict' | 'undetermined' | 'denied';

export interface SaveModeResolution {
  view: SaveModeView;
  /** What the next Save sends. `null` = Save is not allowed until the user acts (or resolution lands). */
  target: SaveTarget | null;
  /** The mode in force, when the user has one to see (`version` view) or has explicitly chosen one. */
  effectiveChoice: SaveModeChoice | null;
  /** The resolved document's display name, for the `version` view. */
  documentLabel: string | null;
  /**
   * Task 120: `false` when the item already in Spaarke has NO version path — an email (no document bytes; an `.eml`
   * is immutable). Then the `version` view's default is "already saved, nothing to send" (`target: null`) and the only
   * save is the explicit "a new document" choice. Absent = `true`.
   */
  versionable?: boolean;
}

const CREATE: SaveTarget = { mode: 'create' };

/** Task 120: options for {@link resolveSaveMode}. */
export interface ResolveSaveModeOptions {
  /** Whether the pane can save a new VERSION of an item already in Spaarke (it has document bytes). Default `true`. */
  versionable?: boolean;
}

/**
 * Pure derivation of the save mode from the identity state and the user's explicit choice. The single
 * source of truth for what Save sends — `SaveFlow` hands `target` straight to `useSaveFlow`.
 *
 * Task 120: with `versionable: false` (an email already saved — Outlook has no document bytes), a resolved identity
 * keeps the `version` view (the item IS in Spaarke: the green box, the "Filed to" card or the filing picker) but sends
 * nothing by default; the explicit `'new'` choice ("Save again as a new document") is the only save. Never a version.
 */
export function resolveSaveMode(
  identity: DocumentIdentityState | undefined,
  choice: SaveModeChoice | null,
  options: ResolveSaveModeOptions = {}
): SaveModeResolution {
  const versionable = options.versionable ?? true;
  if (identity === undefined) {
    return { view: 'none', target: CREATE, effectiveChoice: null, documentLabel: null };
  }
  if (identity === 'checking') {
    return { view: 'checking', target: null, effectiveChoice: null, documentLabel: null };
  }

  switch (identity.kind) {
    case 'resolved': {
      // The ONLY outcome that may default to a version save.
      const effective: SaveModeChoice = choice ?? 'version';
      const keep: SaveTarget | null = versionable ? { mode: 'version', existingDocumentId: identity.documentId } : null;
      return {
        view: 'version',
        target: effective === 'version' ? keep : CREATE,
        effectiveChoice: effective,
        documentLabel: identity.documentName || identity.fileName || null,
        ...(versionable ? {} : { versionable: false }),
      };
    }
    case 'new':
      return { view: 'none', target: CREATE, effectiveChoice: null, documentLabel: null };
    case 'conflict':
      // Never save-as-new, whatever `choice` says: a row already holds this file's item id elsewhere.
      return { view: 'conflict', target: null, effectiveChoice: null, documentLabel: null };
    case 'denied':
      return {
        view: 'denied',
        target: choice === 'new' ? CREATE : null,
        effectiveChoice: choice === 'new' ? 'new' : null,
        documentLabel: null,
      };
    case 'indeterminate':
    case 'error':
      return {
        view: 'undetermined',
        target: choice === 'new' ? CREATE : null,
        effectiveChoice: choice === 'new' ? 'new' : null,
        documentLabel: null,
      };
  }
}

const useStyles = makeStyles({
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  hint: {
    color: tokens.colorNeutralForeground2,
  },
  // A quiet secondary link-style action (task 095): brand foreground, no chrome, left-aligned.
  linkBtn: {
    alignSelf: 'flex-start',
    minWidth: 'auto',
    paddingLeft: 0,
    paddingRight: 0,
    color: tokens.colorBrandForegroundLink,
    ':hover': { color: tokens.colorBrandForegroundLinkHover },
  },
});

export interface SaveModeSectionProps {
  /** The identity state the resolution was derived from (the undetermined copy differs per kind). */
  identity: DocumentIdentityState | undefined;
  /** `resolveSaveMode(identity, choice)` — computed once by the caller. */
  resolution: SaveModeResolution;
  /** The user changed the save mode. `null` clears an explicit "as a new document" choice. */
  onChoiceChange: (choice: SaveModeChoice | null) => void;
  /** Re-runs identity resolution ("Check again" / "Try again"). Omitted → no retry button. */
  onRetryIdentity?: () => void;
  /** True while a save is in flight. */
  disabled?: boolean;
  /** Task 120: what the pane's item is called in the copy. Default `'document'`. */
  itemNoun?: 'document' | 'email';
}

export function SaveModeSection({
  identity,
  resolution,
  onChoiceChange,
  onRetryIdentity,
  disabled = false,
  itemNoun = 'document',
}: SaveModeSectionProps): React.ReactElement | null {
  const styles = useStyles();

  switch (resolution.view) {
    case 'none':
      return null;

    case 'checking':
      return (
        <div className={styles.section}>
          <Spinner
            size="tiny"
            labelPosition="after"
            label={`Checking whether this ${itemNoun} is already in Spaarke…`}
          />
        </div>
      );

    case 'version': {
      // Task 095 (owner, 2026-10-04 — "Hide the choice; Save = new version"): no "Save as" radios. In version
      // mode (the default) there is nothing to show here — the primary button just saves a new version, and
      // the quiet "Save as new document" link lives with the locked name it unlocks (SaveFlow's
      // renderDocumentDetails('locked')). Only after the user chose create mode does this section render,
      // to offer the way back: a "Keep as version" link.
      if (resolution.effectiveChoice !== 'new') return null;
      // Task 120: an item with no version path (an email) — the way back is "don't save again", not "keep as version".
      const versionable = resolution.versionable !== false;
      return (
        <div className={styles.section}>
          <Text size={200} className={styles.hint}>
            {versionable
              ? 'Save will create a separate Spaarke document. The existing document is not changed.'
              : `Save will store this ${itemNoun} in Spaarke again, as a separate document. The saved copy is not changed.`}
          </Text>
          <Button
            appearance="transparent"
            size="small"
            className={styles.linkBtn}
            onClick={() => onChoiceChange(versionable ? 'version' : null)}
            disabled={disabled}
          >
            {versionable ? 'Keep as version' : 'Don’t save again'}
          </Button>
        </div>
      );
    }

    case 'conflict':
      return (
        <MessageBar intent="warning" layout="multiline">
          <MessageBarBody>
            <MessageBarTitle>This document can’t be saved from here</MessageBarTitle>
            Spaarke already has a record for this file in a different storage location, so it can’t tell which record
            this save belongs to. Nothing has been saved. Ask your Spaarke administrator to check the document’s record,
            then check again.
          </MessageBarBody>
          {onRetryIdentity && (
            <MessageBarActions>
              <Button appearance="outline" size="small" onClick={onRetryIdentity} disabled={disabled}>
                Check again
              </Button>
            </MessageBarActions>
          )}
        </MessageBar>
      );

    case 'undetermined': {
      const serviceUnavailable = identity !== undefined && identity !== 'checking' && identity.kind === 'indeterminate';
      return (
        <div className={styles.section}>
          <MessageBar intent="warning" layout="multiline">
            <MessageBarBody>
              <MessageBarTitle>Couldn’t check this document</MessageBarTitle>
              {serviceUnavailable
                ? 'Spaarke couldn’t confirm whether this document is already saved, because the service is unavailable right now.'
                : 'Something went wrong while checking whether this document is already in Spaarke.'}{' '}
              If it is, saving it as a new document creates a second copy. Try again, or save it as a new document if
              you’re sure.
            </MessageBarBody>
            {onRetryIdentity && (
              <MessageBarActions>
                <Button appearance="outline" size="small" onClick={onRetryIdentity} disabled={disabled}>
                  Try again
                </Button>
              </MessageBarActions>
            )}
          </MessageBar>
          <Checkbox
            checked={resolution.effectiveChoice === 'new'}
            onChange={(_e, data) => onChoiceChange(data.checked === true ? 'new' : null)}
            disabled={disabled}
            label="Save it as a new document anyway"
          />
        </div>
      );
    }

    case 'denied':
      return (
        <div className={styles.section}>
          <MessageBar intent="info" layout="multiline">
            <MessageBarBody>
              <MessageBarTitle>You can’t add a version to this document</MessageBarTitle>
              This document is in Spaarke, but you don’t have access to its record, so a new version can’t be added. You
              can save your copy as a separate new document.
            </MessageBarBody>
          </MessageBar>
          <Checkbox
            checked={resolution.effectiveChoice === 'new'}
            onChange={(_e, data) => onChoiceChange(data.checked === true ? 'new' : null)}
            disabled={disabled}
            label="Save my copy as a new document"
          />
        </div>
      );
  }
}

export default SaveModeSection;
