/**
 * MyAssistantDialog.tsx — the "My Assistant" stated-profile questionnaire (task 042, FR-F3 / FR-E1).
 *
 * Launched from the Assistant pane's `AssistantToolMenu` "My Assistant" entry (task 040). Collects the
 * stated profile — primary role (closed global choice), practice areas (constrained N:N), focus areas,
 * office location, assistant preferences — and, on submit, persists it via `saveMyAssistantProfile`
 * (keyed upsert + N:N reconcile + `sprk_profilecompletedon`). Also hosts the F5 GDPR erasure action.
 *
 * COLD-START GATE: the parent auto-opens this dialog while `sprk_profilecompletedon` is unset; once the
 * profile is complete it opens only on demand from the menu. This component is presentation +
 * field-state only — the fetch/write/gate live in `useMyAssistant`.
 *
 * Standards: ADR-021 (Fluent v9 semantic tokens only — dark mode adapts via the host FluentProvider;
 * no hex/rgba literals), ADR-022 (React functional component), ADR-025 (v9 icons).
 *
 * @see userProfileService.ts — the write/erase path + PRIMARY_ROLE_OPTIONS
 * @see useMyAssistant.ts — open-state + cold-start + save/erase orchestration
 * @see AssistantToolMenu.tsx — the launcher (onMyAssistant)
 */

import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  Dropdown,
  Option,
  Field,
  Textarea,
  Input,
  MessageBar,
  MessageBarBody,
  Spinner,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { PersonRegular, DeleteRegular } from '@fluentui/react-icons';
import {
  PRIMARY_ROLE_OPTIONS,
  type PracticeArea,
  type UserProfileFormValues,
} from './userProfileService';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface MyAssistantDialogProps {
  /** Whether the questionnaire is open. */
  open: boolean;
  /** Close request (Cancel / dismiss / after a successful save). */
  onClose: () => void;
  /** True on first-run (profile not yet complete) — shows the cold-start guidance banner. */
  coldStart?: boolean;
  /** The constrained practice-area options (from the resolver entity). */
  practiceAreas: ReadonlyArray<PracticeArea>;
  /** Initial field values (prefilled from an existing profile, or empty on first run). */
  initialValues?: Partial<UserProfileFormValues>;
  /** Persist the questionnaire. Rejects on failure (surfaced as an inline error). */
  onSubmit: (values: UserProfileFormValues) => Promise<void>;
  /** GDPR erasure (F5): delete the profile + N:N + seeded memory. Rejects on failure. */
  onErase?: () => Promise<void>;
  /** True while any load of practice areas / initial values is in flight. */
  loading?: boolean;
}

// ---------------------------------------------------------------------------
// Styles (Fluent v9 tokens only — ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  content: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalL,
    minWidth: '420px',
  },
  actionsRow: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    width: '100%',
  },
  rightActions: {
    display: 'flex',
    columnGap: tokens.spacingHorizontalS,
  },
  eraseButton: {
    color: tokens.colorPaletteRedForeground1,
  },
});

const ROLE_PLACEHOLDER = 'Select your role';
const PRACTICE_PLACEHOLDER = 'Select practice areas';

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * The My Assistant questionnaire modal. Controlled `open` state; internal field state seeded from
 * `initialValues` whenever the dialog (re)opens.
 */
export const MyAssistantDialog: React.FC<MyAssistantDialogProps> = ({
  open,
  onClose,
  coldStart = false,
  practiceAreas,
  initialValues,
  onSubmit,
  onErase,
  loading = false,
}) => {
  const styles = useStyles();

  const [role, setRole] = React.useState<number | null>(initialValues?.primaryRole ?? null);
  const [selectedPractice, setSelectedPractice] = React.useState<string[]>(
    initialValues?.practiceAreaIds ?? []
  );
  const [focusAreas, setFocusAreas] = React.useState<string>(initialValues?.focusAreas ?? '');
  const [office, setOffice] = React.useState<string>(initialValues?.officeLocation ?? '');
  const [preferences, setPreferences] = React.useState<string>(initialValues?.assistantPreferences ?? '');

  const [saving, setSaving] = React.useState(false);
  const [erasing, setErasing] = React.useState(false);
  const [confirmErase, setConfirmErase] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // Re-seed field state each time the dialog opens (prefill from the latest server values).
  React.useEffect(() => {
    if (open) {
      setRole(initialValues?.primaryRole ?? null);
      setSelectedPractice(initialValues?.practiceAreaIds ?? []);
      setFocusAreas(initialValues?.focusAreas ?? '');
      setOffice(initialValues?.officeLocation ?? '');
      setPreferences(initialValues?.assistantPreferences ?? '');
      setError(null);
      setConfirmErase(false);
    }
  }, [open, initialValues]);

  const busy = saving || erasing || loading;

  const roleLabel = React.useMemo(
    () => PRIMARY_ROLE_OPTIONS.find((o) => o.value === role)?.label ?? '',
    [role]
  );
  const practiceNamesById = React.useMemo(() => {
    const m = new Map<string, string>();
    for (const p of practiceAreas) m.set(p.id, p.name);
    return m;
  }, [practiceAreas]);
  const selectedPracticeText = React.useMemo(
    () => selectedPractice.map((id) => practiceNamesById.get(id) ?? id).join(', '),
    [selectedPractice, practiceNamesById]
  );

  const handleSubmit = React.useCallback(async () => {
    setError(null);
    setSaving(true);
    try {
      await onSubmit({
        primaryRole: role,
        practiceAreaIds: selectedPractice,
        focusAreas,
        officeLocation: office,
        assistantPreferences: preferences,
      });
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save your profile. Please try again.');
    } finally {
      setSaving(false);
    }
  }, [onSubmit, onClose, role, selectedPractice, focusAreas, office, preferences]);

  const handleErase = React.useCallback(async () => {
    if (!onErase) return;
    setError(null);
    setErasing(true);
    try {
      await onErase();
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not clear your profile. Please try again.');
    } finally {
      setErasing(false);
      setConfirmErase(false);
    }
  }, [onErase, onClose]);

  return (
    <Dialog
      open={open}
      onOpenChange={(_ev, data) => {
        if (!data.open && !busy) onClose();
      }}
      modalType="modal"
    >
      <DialogSurface data-testid="my-assistant-dialog">
        <DialogBody>
          <DialogTitle>My Assistant</DialogTitle>
          <DialogContent className={styles.content}>
            {coldStart ? (
              <MessageBar intent="info" data-testid="my-assistant-coldstart">
                <MessageBarBody>
                  Tell your assistant about yourself so it can tailor its help. You can update this
                  anytime from the Tools menu.
                </MessageBarBody>
              </MessageBar>
            ) : null}

            {error ? (
              <MessageBar intent="error" data-testid="my-assistant-error">
                <MessageBarBody>{error}</MessageBarBody>
              </MessageBar>
            ) : null}

            <Field label="Primary role">
              <Dropdown
                aria-label="Primary role"
                data-testid="my-assistant-role"
                placeholder={ROLE_PLACEHOLDER}
                disabled={busy}
                selectedOptions={role !== null ? [String(role)] : []}
                value={roleLabel}
                onOptionSelect={(_e, data) => {
                  const next = data.optionValue != null ? Number(data.optionValue) : null;
                  setRole(Number.isNaN(next as number) ? null : next);
                }}
              >
                {PRIMARY_ROLE_OPTIONS.map((o) => (
                  <Option key={o.value} value={String(o.value)} text={o.label}>
                    {o.label}
                  </Option>
                ))}
              </Dropdown>
            </Field>

            <Field label="Practice areas">
              <Dropdown
                multiselect
                aria-label="Practice areas"
                data-testid="my-assistant-practice-areas"
                placeholder={PRACTICE_PLACEHOLDER}
                disabled={busy}
                selectedOptions={selectedPractice}
                value={selectedPracticeText}
                onOptionSelect={(_e, data) => setSelectedPractice(data.selectedOptions)}
              >
                {practiceAreas.map((p) => (
                  <Option key={p.id} value={p.id} text={p.name}>
                    {p.name}
                  </Option>
                ))}
              </Dropdown>
            </Field>

            <Field label="Focus areas" hint="What you concentrate on (e.g. M&A, joint ventures).">
              <Textarea
                aria-label="Focus areas"
                data-testid="my-assistant-focus"
                disabled={busy}
                resize="vertical"
                value={focusAreas}
                onChange={(_e, data) => setFocusAreas(data.value)}
              />
            </Field>

            <Field label="Office location">
              <Input
                aria-label="Office location"
                data-testid="my-assistant-office"
                disabled={busy}
                value={office}
                onChange={(_e, data) => setOffice(data.value)}
              />
            </Field>

            <Field
              label="Assistant preferences"
              hint="How you like the assistant to respond (e.g. concise, cite sources)."
            >
              <Textarea
                aria-label="Assistant preferences"
                data-testid="my-assistant-preferences"
                disabled={busy}
                resize="vertical"
                value={preferences}
                onChange={(_e, data) => setPreferences(data.value)}
              />
            </Field>

            {confirmErase ? (
              <MessageBar intent="warning" data-testid="my-assistant-erase-confirm">
                <MessageBarBody>
                  This permanently deletes your stated profile, its practice-area links, and your
                  assistant memory. This cannot be undone.
                </MessageBarBody>
              </MessageBar>
            ) : null}
          </DialogContent>

          <DialogActions>
            <div className={styles.actionsRow}>
              <div>
                {onErase ? (
                  confirmErase ? (
                    <Button
                      appearance="primary"
                      className={styles.eraseButton}
                      icon={erasing ? <Spinner size="tiny" /> : <DeleteRegular />}
                      disabled={busy}
                      onClick={handleErase}
                      data-testid="my-assistant-erase-confirm-btn"
                    >
                      Confirm delete
                    </Button>
                  ) : (
                    <Button
                      appearance="subtle"
                      className={styles.eraseButton}
                      icon={<DeleteRegular />}
                      disabled={busy}
                      onClick={() => setConfirmErase(true)}
                      data-testid="my-assistant-erase"
                    >
                      Clear my profile
                    </Button>
                  )
                ) : null}
              </div>
              <div className={styles.rightActions}>
                <Button appearance="secondary" onClick={onClose} disabled={busy} data-testid="my-assistant-cancel">
                  Cancel
                </Button>
                <Button
                  appearance="primary"
                  icon={saving ? <Spinner size="tiny" /> : <PersonRegular />}
                  disabled={busy}
                  onClick={handleSubmit}
                  data-testid="my-assistant-save"
                >
                  Save profile
                </Button>
              </div>
            </div>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

MyAssistantDialog.displayName = 'MyAssistantDialog';

export default MyAssistantDialog;
