/**
 * @spaarke/ai-widgets — PinnedMemoryEditDialog
 *
 * Fluent v9 create / edit dialog for a pinned memory item. Drives the
 * {@link PinnedMemoryListWidget} CRUD UX (Q7 scope expansion from R6).
 *
 * Modes:
 *   - `create`: empty form; "Create pin" submit label; defaults pinType to
 *     "user-preference"; submits POST `/api/memory/pins`.
 *   - `edit`:   form pre-filled from the supplied `initial` PinDto; "Save
 *     changes" submit label; submits PUT `/api/memory/pins/{pinId}`.
 *
 * The dialog is fully controlled by its parent — `open`, `onSubmit`, and
 * `onCancel` are required props. The dialog does NOT call the BFF itself;
 * the parent {@link PinnedMemoryListWidget} owns the BFF round-trip and the
 * optimistic list update. Submission is therefore async-safe: when the
 * parent is mid-flight, it passes `isSubmitting=true` and the Save button
 * disables to prevent double-submit.
 *
 * Validation (sync, on Save click):
 *   - title:   required; ≤200 characters (matches PART A backend cap)
 *   - content: required; ≤1000 characters (matches PART A backend cap)
 *   - pinType: must be one of three values (radio defaults guarantee this)
 *   - matterId: required only when `pinType === "matter-fact"`
 *
 * Standards:
 *   - ADR-012: lives in `@spaarke/ai-widgets`; Fluent v9 components.
 *   - ADR-021: zero hardcoded colors; Fluent v9 semantic tokens only.
 *   - ADR-022: React 19 functional component + hooks.
 *
 * Task: R6-070 (D-C-24 / D-C-25, Pillar 7, Q7 scope expansion) — PART B.
 */

import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Field,
  Input,
  Label,
  makeStyles,
  Radio,
  RadioGroup,
  Text,
  Textarea,
  tokens,
} from '@fluentui/react-components';
import type { PinDto, PinType, PinUpsertRequest } from './pinned-memory-contracts';
import {
  MAX_PIN_CONTENT_LENGTH,
  MAX_PIN_TITLE_LENGTH,
  PIN_TYPES,
} from './pinned-memory-contracts';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/** Dialog mode discriminator — drives labels + initial form values. */
export type PinnedMemoryEditDialogMode = 'create' | 'edit';

export interface PinnedMemoryEditDialogProps {
  /** Controlled open state. */
  open: boolean;
  /** Create vs Edit mode. */
  mode: PinnedMemoryEditDialogMode;
  /**
   * The pin being edited — required in `edit` mode, ignored in `create`. The
   * dialog reads only the editable fields (title / content / pinType /
   * matterId); `pinId` + audit fields are not surfaced in the form.
   */
  initial?: PinDto;
  /**
   * Whether the parent is currently performing the BFF round-trip (POST or
   * PUT). When `true`, the submit button disables + shows "Saving…" to
   * prevent double-submit.
   */
  isSubmitting?: boolean;
  /** Optional server-side error message to surface inline. */
  serverError?: string | null;
  /**
   * Invoked when the user confirms — fires with the typed request body the
   * parent should hand to the BFF (POST for create, PUT for edit).
   */
  onSubmit: (req: PinUpsertRequest) => void;
  /** Invoked when the user cancels or closes the dialog. */
  onCancel: () => void;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    minWidth: '420px',
  },
  helpText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    lineHeight: tokens.lineHeightBase200,
  },
  contentTextarea: {
    minHeight: '120px',
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
  },
  characterCount: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
    textAlign: 'right',
  },
  characterCountWarning: {
    color: tokens.colorPaletteRedForeground1,
  },
  pinTypeRow: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  pinTypeLabel: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
  radioGroup: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  radioLabel: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
  radioHint: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    paddingLeft: tokens.spacingHorizontalL,
  },
  matterFieldRow: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  serverError: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorPaletteRedForeground1,
    lineHeight: tokens.lineHeightBase200,
  },
});

// ---------------------------------------------------------------------------
// Internal: form state
// ---------------------------------------------------------------------------

interface FormState {
  title: string;
  content: string;
  pinType: PinType;
  matterId: string;
}

interface ValidationErrors {
  title?: string;
  content?: string;
  matterId?: string;
}

/** Build the initial form state for a given mode + initial pin. */
function buildInitialState(
  mode: PinnedMemoryEditDialogMode,
  initial?: PinDto
): FormState {
  if (mode === 'edit' && initial) {
    return {
      title: initial.title,
      content: initial.content,
      pinType: initial.pinType,
      matterId: initial.matterId ?? '',
    };
  }
  return {
    title: '',
    content: '',
    pinType: 'user-preference',
    matterId: '',
  };
}

/** Sync validation matching backend caps + matter-fact rule. */
function validate(state: FormState): ValidationErrors {
  const errors: ValidationErrors = {};
  const title = state.title.trim();
  const content = state.content.trim();

  if (title.length === 0) {
    errors.title = 'Title is required.';
  } else if (title.length > MAX_PIN_TITLE_LENGTH) {
    errors.title = `Title must be ${MAX_PIN_TITLE_LENGTH} characters or fewer.`;
  }

  if (content.length === 0) {
    errors.content = 'Content is required.';
  } else if (content.length > MAX_PIN_CONTENT_LENGTH) {
    errors.content = `Content must be ${MAX_PIN_CONTENT_LENGTH} characters or fewer.`;
  }

  if (state.pinType === 'matter-fact' && state.matterId.trim().length === 0) {
    errors.matterId = 'Matter is required when pin type is "Matter fact".';
  }

  return errors;
}

// ---------------------------------------------------------------------------
// PinnedMemoryEditDialog
// ---------------------------------------------------------------------------

/**
 * Fluent v9 create / edit dialog for a pinned memory item.
 *
 * Fully controlled — the parent owns open state, BFF round-trip, and
 * optimistic list update. The dialog itself is a stateless form-with-state:
 * it manages local form state + validation + dirty tracking, and calls
 * `onSubmit` with a typed {@link PinUpsertRequest} when the user confirms.
 */
export const PinnedMemoryEditDialog: React.FC<PinnedMemoryEditDialogProps> = ({
  open,
  mode,
  initial,
  isSubmitting = false,
  serverError = null,
  onSubmit,
  onCancel,
}) => {
  const styles = useStyles();

  const [state, setState] = useState<FormState>(() => buildInitialState(mode, initial));
  const [errors, setErrors] = useState<ValidationErrors>({});
  const [hasAttemptedSubmit, setHasAttemptedSubmit] = useState<boolean>(false);

  // Reset form state when the dialog TRANSITIONS from closed → open. We
  // intentionally avoid resetting on every re-render while open: a parent that
  // passes a fresh `initial` object on each render would otherwise wipe
  // user-typed input. We track the previous-open value with a ref to detect
  // the transition edge.
  const prevOpenRef = useRef<boolean>(open);
  useEffect(() => {
    const wasClosed = !prevOpenRef.current;
    prevOpenRef.current = open;
    if (open && wasClosed) {
      setState(buildInitialState(mode, initial));
      setErrors({});
      setHasAttemptedSubmit(false);
    }
    // Note: dependencies intentionally limited to `open` + `mode`. The form
    // state is captured at the open-edge from `initial`; subsequent renders
    // with a different `initial` are ignored until the next open transition.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, mode]);

  // NOTE: previously this component re-validated on every keystroke after the
  // first submit attempt — that caused `validationMessage` churn in the
  // `Field` component which (depending on Fluent v9 internals) could unmount
  // / remount the Input child and drop user-event keystrokes in the test
  // environment. Validation is now run ONLY at submit time. The form still
  // surfaces error text inline because `handleSubmit` calls `setErrors`
  // before returning.

  // ── Field handlers ──────────────────────────────────────────────────────
  const handleTitleChange = useCallback(
    (_e: React.ChangeEvent<HTMLInputElement>, data: { value: string }) => {
      setState(prev => ({ ...prev, title: data.value }));
    },
    []
  );

  const handleContentChange = useCallback(
    (_e: React.ChangeEvent<HTMLTextAreaElement>, data: { value: string }) => {
      setState(prev => ({ ...prev, content: data.value }));
    },
    []
  );

  const handlePinTypeChange = useCallback(
    (_e: unknown, data: { value: string }) => {
      const next = data.value as PinType;
      setState(prev => ({
        ...prev,
        pinType: next,
        // Clearing matterId when leaving matter-fact mode keeps the request
        // body honest — only the matter-fact branch carries a matterId.
        matterId: next === 'matter-fact' ? prev.matterId : '',
      }));
    },
    []
  );

  const handleMatterIdChange = useCallback(
    (_e: React.ChangeEvent<HTMLInputElement>, data: { value: string }) => {
      setState(prev => ({ ...prev, matterId: data.value }));
    },
    []
  );

  // ── Submit / cancel ─────────────────────────────────────────────────────
  const handleSubmit = useCallback(() => {
    setHasAttemptedSubmit(true);
    const v = validate(state);
    setErrors(v);
    if (Object.keys(v).length > 0) return;
    if (isSubmitting) return;

    const req: PinUpsertRequest = {
      title: state.title.trim(),
      content: state.content.trim(),
      pinType: state.pinType,
      // Only attach matterId when matter-fact; the backend rejects matterId
      // for other pin types as part of its validation. Empty-string guard
      // mirrors the backend's null-or-omitted treatment.
      matterId:
        state.pinType === 'matter-fact' && state.matterId.trim().length > 0
          ? state.matterId.trim()
          : undefined,
    };
    onSubmit(req);
  }, [isSubmitting, onSubmit, state]);

  const handleCancel = useCallback(() => {
    if (isSubmitting) return;
    onCancel();
  }, [isSubmitting, onCancel]);

  const handleOpenChange = useCallback(
    (_e: unknown, data: { open: boolean }) => {
      if (!data.open && !isSubmitting) {
        onCancel();
      }
    },
    [isSubmitting, onCancel]
  );

  // ── Derived render state ────────────────────────────────────────────────
  const titleRemaining = MAX_PIN_TITLE_LENGTH - state.title.length;
  const contentRemaining = MAX_PIN_CONTENT_LENGTH - state.content.length;
  const titleNearLimit = titleRemaining <= 20;
  const contentNearLimit = contentRemaining <= 50;

  const titleId = useMemo(() => 'pin-edit-title', []);
  const contentId = useMemo(() => 'pin-edit-content', []);
  const matterIdId = useMemo(() => 'pin-edit-matterid', []);
  const pinTypeLabelId = useMemo(() => 'pin-edit-pintype-label', []);

  const submitLabel = mode === 'create' ? 'Create pin' : 'Save changes';
  const titleLabel = mode === 'create' ? 'New pinned memory' : 'Edit pinned memory';

  return (
    <Dialog open={open} onOpenChange={handleOpenChange} modalType="modal">
      <DialogSurface data-testid="pinned-memory-edit-dialog">
        <DialogBody>
          <DialogTitle>{titleLabel}</DialogTitle>
          <DialogContent>
            <form
              className={styles.form}
              onSubmit={e => {
                e.preventDefault();
                handleSubmit();
              }}
              noValidate
            >
              {/* Title */}
              <Field
                label="Title"
                required
                validationState={errors.title ? 'error' : 'none'}
                validationMessage={errors.title}
              >
                <Input
                  id={titleId}
                  value={state.title}
                  onChange={handleTitleChange}
                  disabled={isSubmitting}
                  maxLength={MAX_PIN_TITLE_LENGTH}
                  placeholder="Short, descriptive label"
                  data-testid="pinned-memory-edit-title"
                />
              </Field>
              <Text
                className={styles.characterCount + (titleNearLimit ? ' ' + styles.characterCountWarning : '')}
                aria-live="polite"
              >
                {titleRemaining} characters remaining
              </Text>

              {/* Content */}
              <Field
                label="Content"
                required
                validationState={errors.content ? 'error' : 'none'}
                validationMessage={errors.content}
                hint="What should the assistant remember about this preference, rule, or fact?"
              >
                <Textarea
                  id={contentId}
                  className={styles.contentTextarea}
                  value={state.content}
                  onChange={handleContentChange}
                  disabled={isSubmitting}
                  maxLength={MAX_PIN_CONTENT_LENGTH}
                  data-testid="pinned-memory-edit-content"
                />
              </Field>
              <Text
                className={styles.characterCount + (contentNearLimit ? ' ' + styles.characterCountWarning : '')}
                aria-live="polite"
              >
                {contentRemaining} characters remaining
              </Text>

              {/* Pin type */}
              <div className={styles.pinTypeRow}>
                <Label id={pinTypeLabelId} className={styles.pinTypeLabel} required>
                  Pin type
                </Label>
                <RadioGroup
                  className={styles.radioGroup}
                  value={state.pinType}
                  onChange={handlePinTypeChange}
                  disabled={isSubmitting}
                  aria-labelledby={pinTypeLabelId}
                  data-testid="pinned-memory-edit-pintype"
                >
                  {PIN_TYPES.map(pt => (
                    <React.Fragment key={pt.value}>
                      <Radio
                        value={pt.value}
                        label={<span className={styles.radioLabel}>{pt.label}</span>}
                        data-testid={`pinned-memory-edit-pintype-${pt.value}`}
                      />
                      <Text className={styles.radioHint}>{pt.hint}</Text>
                    </React.Fragment>
                  ))}
                </RadioGroup>
              </div>

              {/* Matter id — only when matter-fact selected */}
              {state.pinType === 'matter-fact' && (
                <div className={styles.matterFieldRow}>
                  <Field
                    label="Matter"
                    required
                    validationState={errors.matterId ? 'error' : 'none'}
                    validationMessage={errors.matterId}
                    hint="The matter this fact applies to. Required for matter-scoped pins."
                  >
                    <Input
                      id={matterIdId}
                      value={state.matterId}
                      onChange={handleMatterIdChange}
                      disabled={isSubmitting}
                      placeholder="Matter ID"
                      data-testid="pinned-memory-edit-matterid"
                    />
                  </Field>
                </div>
              )}

              {/* Server error */}
              {serverError && (
                <Text
                  className={styles.serverError}
                  role="alert"
                  data-testid="pinned-memory-edit-server-error"
                >
                  {serverError}
                </Text>
              )}
            </form>
          </DialogContent>
          <DialogActions>
            {/* See PinnedMemoryDeleteConfirmation for the no-DialogTrigger
                rationale: avoids double-firing onCancel. */}
            <Button
              appearance="secondary"
              onClick={handleCancel}
              disabled={isSubmitting}
              data-testid="pinned-memory-edit-cancel"
            >
              Cancel
            </Button>
            <Button
              appearance="primary"
              onClick={handleSubmit}
              disabled={isSubmitting}
              data-testid="pinned-memory-edit-submit"
            >
              {isSubmitting ? 'Saving…' : submitLabel}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

PinnedMemoryEditDialog.displayName = 'PinnedMemoryEditDialog';

export default PinnedMemoryEditDialog;
