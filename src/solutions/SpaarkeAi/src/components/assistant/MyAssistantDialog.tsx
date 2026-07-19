/**
 * MyAssistantDialog.tsx — the "My Assistant" stated-profile questionnaire (task 042, FR-F3 / FR-E1).
 *
 * P2-4 (UAT 2026-07-18): reworked from a single-page form into a 3-STEP WIZARD with richer
 * guidance + (i) info popovers, per the owner UX request:
 *   Step 1 "Your role"      — intro + Primary role + Office location (with an info popover).
 *   Step 2 "Practice areas" — description + Practice areas + Focus areas (with an info popover).
 *   Step 3 "Preferences"    — Assistant preferences (with examples in an info popover).
 * The field state, save path, cold-start banner, and GDPR erase (F5) are UNCHANGED — only the
 * layout is stepped. (The earlier "dropdown doesn't work / can't save" defect was a data-access
 * bug fixed separately in userProfileService — the lookup-alternate-key 400.)
 *
 * Standards: ADR-021 (Fluent v9 semantic tokens only — dark mode adapts; no hex/rgba), ADR-022
 * (functional component), ADR-025 (v9 icons).
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
  InfoLabel,
  Textarea,
  Input,
  Text,
  MessageBar,
  MessageBarBody,
  Spinner,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { PersonRegular, DeleteRegular, ArrowLeftRegular, ArrowRightRegular } from '@fluentui/react-icons';
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
  surface: {
    minWidth: '460px',
    maxWidth: '520px',
  },
  content: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalL,
    minHeight: '300px',
  },
  stepIndicator: {
    color: tokens.colorNeutralForeground3,
  },
  stepIntro: {
    color: tokens.colorNeutralForeground2,
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

const STEP_TITLES = ['Your role', 'Practice areas', 'Preferences'] as const;
const TOTAL_STEPS = STEP_TITLES.length;

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * The My Assistant questionnaire, presented as a 3-step wizard. Controlled `open` state; internal
 * field state seeded from `initialValues` whenever the dialog (re)opens.
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

  const [step, setStep] = React.useState(0);
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
      setStep(0);
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

  const isLastStep = step === TOTAL_STEPS - 1;

  return (
    <Dialog
      open={open}
      onOpenChange={(_ev, data) => {
        if (!data.open && !busy) onClose();
      }}
      modalType="modal"
    >
      <DialogSurface data-testid="my-assistant-dialog" className={styles.surface}>
        <DialogBody>
          <DialogTitle>My Assistant</DialogTitle>
          <DialogContent className={styles.content}>
            <Text size={200} className={styles.stepIndicator} data-testid="my-assistant-step-indicator">
              Step {step + 1} of {TOTAL_STEPS} · {STEP_TITLES[step]}
            </Text>

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

            {/* ── Step 1: Your role ─────────────────────────────────────────── */}
            {step === 0 ? (
              <>
                <Text size={200} className={styles.stepIntro}>
                  Your role and location help the assistant tailor its tone, prioritize the tasks
                  you do most, and give jurisdiction-aware guidance.
                </Text>

                <Field
                  label={
                    <InfoLabel
                      info="Your role tailors the assistant's tone and the tasks it surfaces first (for example, a partner sees different defaults than a paralegal)."
                    >
                      Primary role
                    </InfoLabel>
                  }
                >
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

                <Field
                  label={
                    <InfoLabel
                      info={
                        <>
                          <b>What:</b> the office you primarily work from.
                          <br />
                          <b>How it's used:</b> the assistant uses it for jurisdiction-aware guidance
                          and working-hours context.
                          <br />
                          <b>Why:</b> optional — leave blank if you'd rather not say. Stored only on
                          your profile.
                        </>
                      }
                    >
                      Office location
                    </InfoLabel>
                  }
                >
                  <Input
                    aria-label="Office location"
                    data-testid="my-assistant-office"
                    disabled={busy}
                    value={office}
                    onChange={(_e, data) => setOffice(data.value)}
                  />
                </Field>
              </>
            ) : null}

            {/* ── Step 2: Practice areas ────────────────────────────────────── */}
            {step === 1 ? (
              <>
                <Text size={200} className={styles.stepIntro}>
                  Tell the assistant which practice areas you work in and what you focus on, so it can
                  prioritize the most relevant matters, documents, and suggestions.
                </Text>

                <Field
                  label={
                    <InfoLabel info="Select every practice area you work in. The assistant uses these to prioritize relevant matters, documents, and next-step suggestions.">
                      Practice areas
                    </InfoLabel>
                  }
                >
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

                <Field
                  label={
                    <InfoLabel
                      info={
                        <>
                          Describe what you concentrate on. Examples:
                          <br />• &ldquo;M&amp;A and joint ventures&rdquo;
                          <br />• &ldquo;Commercial real-estate leasing&rdquo;
                          <br />• &ldquo;Employment disputes and wage-and-hour&rdquo;
                        </>
                      }
                    >
                      Describe your focus areas
                    </InfoLabel>
                  }
                >
                  <Textarea
                    aria-label="Focus areas"
                    data-testid="my-assistant-focus"
                    disabled={busy}
                    resize="vertical"
                    value={focusAreas}
                    onChange={(_e, data) => setFocusAreas(data.value)}
                  />
                </Field>
              </>
            ) : null}

            {/* ── Step 3: Preferences ───────────────────────────────────────── */}
            {step === 2 ? (
              <>
                <Text size={200} className={styles.stepIntro}>
                  Tell the assistant how you like it to work — the style, format, and priorities you
                  prefer in its responses.
                </Text>

                <Field
                  label={
                    <InfoLabel
                      info={
                        <>
                          Examples:
                          <br />• &ldquo;Be concise and cite sources&rdquo;
                          <br />• &ldquo;Always show a short summary first&rdquo;
                          <br />• &ldquo;Prefer bullet points over prose&rdquo;
                          <br />• &ldquo;Flag risks and deadlines explicitly&rdquo;
                        </>
                      }
                    >
                      Describe your preferences in using the Assistant
                    </InfoLabel>
                  }
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
              </>
            ) : null}
          </DialogContent>

          <DialogActions>
            <div className={styles.actionsRow}>
              <div>
                {/* Clear my profile (F5 erase) lives on the LAST step, next to Save. */}
                {onErase && isLastStep ? (
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
                {step > 0 ? (
                  <Button
                    appearance="secondary"
                    icon={<ArrowLeftRegular />}
                    disabled={busy}
                    onClick={() => setStep((s) => Math.max(0, s - 1))}
                    data-testid="my-assistant-back"
                  >
                    Back
                  </Button>
                ) : (
                  <Button
                    appearance="secondary"
                    onClick={onClose}
                    disabled={busy}
                    data-testid="my-assistant-cancel"
                  >
                    Cancel
                  </Button>
                )}
                {isLastStep ? (
                  <Button
                    appearance="primary"
                    icon={saving ? <Spinner size="tiny" /> : <PersonRegular />}
                    disabled={busy}
                    onClick={handleSubmit}
                    data-testid="my-assistant-save"
                  >
                    Save profile
                  </Button>
                ) : (
                  <Button
                    appearance="primary"
                    icon={<ArrowRightRegular />}
                    iconPosition="after"
                    disabled={busy}
                    onClick={() => setStep((s) => Math.min(TOTAL_STEPS - 1, s + 1))}
                    data-testid="my-assistant-next"
                  >
                    Next
                  </Button>
                )}
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
