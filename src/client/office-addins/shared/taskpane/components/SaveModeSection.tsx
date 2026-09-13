import React from 'react';
import {
  makeStyles,
  tokens,
  Button,
  Checkbox,
  Field,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Radio,
  RadioGroup,
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
}

const CREATE: SaveTarget = { mode: 'create' };

/**
 * Pure derivation of the save mode from the identity state and the user's explicit choice. The single
 * source of truth for what Save sends — `SaveFlow` hands `target` straight to `useSaveFlow`.
 */
export function resolveSaveMode(
  identity: DocumentIdentityState | undefined,
  choice: SaveModeChoice | null
): SaveModeResolution {
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
      return {
        view: 'version',
        target: effective === 'version' ? { mode: 'version', existingDocumentId: identity.documentId } : CREATE,
        effectiveChoice: effective,
        documentLabel: identity.documentName || identity.fileName || null,
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
}

function quoted(label: string | null): string {
  return label ? `“${label}”` : 'the existing document';
}

export function SaveModeSection({
  identity,
  resolution,
  onChoiceChange,
  onRetryIdentity,
  disabled = false,
}: SaveModeSectionProps): React.ReactElement | null {
  const styles = useStyles();

  switch (resolution.view) {
    case 'none':
      return null;

    case 'checking':
      return (
        <div className={styles.section}>
          <Spinner size="tiny" labelPosition="after" label="Checking whether this document is already in Spaarke…" />
        </div>
      );

    case 'version': {
      const isVersion = resolution.effectiveChoice !== 'new';
      return (
        <div className={styles.section}>
          <Field label="Save as">
            <RadioGroup
              value={isVersion ? 'version' : 'new'}
              onChange={(_e, data) => onChoiceChange(data.value === 'new' ? 'new' : 'version')}
              disabled={disabled}
            >
              <Radio value="version" label={`A new version of ${quoted(resolution.documentLabel)}`} />
              <Radio value="new" label="A new document" />
            </RadioGroup>
          </Field>
          <Text size={200} className={styles.hint}>
            {isVersion
              ? `Save will add a new version to ${quoted(resolution.documentLabel)} in Spaarke. Its name and related records stay as they are.`
              : 'Save will create a separate Spaarke document. The existing document is not changed.'}
          </Text>
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
