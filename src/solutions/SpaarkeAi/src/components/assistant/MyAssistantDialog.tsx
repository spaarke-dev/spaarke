/**
 * MyAssistantDialog.tsx — the "My Assistant" stated-profile questionnaire (task 042, FR-F3 / FR-E1).
 *
 * 3-STEP WIZARD:
 *   Step 1 "Your role"   — Primary role + Primary work location (dropdown from `sprk_workoffice`).
 *   Step 2 "Focus areas" — Practice areas (multiselect) + curated Focus-area chips.
 *   Step 3 "Preferences" — curated Assistant-preference chips.
 *
 * UAT 2026-07-19 (MA cluster):
 *   • MA-2 — standard small modal; simplified bottom nav (Cancel · Back · Next/Save); the GDPR
 *     "Clear my profile" affordance was REMOVED from this flow (owner decision — erase can live
 *     elsewhere; the `eraseMyAssistantProfile` service + hook path remain for a future surface).
 *   • MA-3 — "Office location" free-text → "Primary work location" dropdown; the selected office NAME
 *     is stored in the existing `sprk_officelocation` text column (no schema change).
 *   • MA-4 — Focus areas + Preferences free-text boxes → curated multi-select chips. Each selected chip
 *     stores its directive phrase (newline-joined) in the existing `sprk_focusareas` /
 *     `sprk_assistantpreferences` columns, which `ContextBinder.userFragment` already injects into the
 *     agent turn — deterministic behaviour, NO BFF change. See userProfileService chip catalogs.
 *
 * Standards: ADR-021 (Fluent v9 semantic tokens only — dark mode adapts; no hex/rgba), ADR-022
 * (functional component), ADR-025 (v9 icons).
 *
 * @see userProfileService.ts — the write path + PRIMARY_ROLE_OPTIONS + chip catalogs (encode/decode)
 * @see useMyAssistant.ts — open-state + cold-start (needs-profile) + save orchestration
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
  ToggleButton,
  Dropdown,
  Option,
  Field,
  InfoLabel,
  Text,
  MessageBar,
  MessageBarBody,
  Spinner,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { PersonRegular, ArrowLeftRegular, ArrowRightRegular } from '@fluentui/react-icons';
import {
  PRIMARY_ROLE_OPTIONS,
  FOCUS_AREA_CHIPS,
  PREFERENCE_CHIPS,
  encodeChipSelection,
  decodeChipSelection,
  type ProfileChip,
  type PracticeArea,
  type WorkOffice,
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
  /** The primary-work-location options (from `sprk_workoffice`). */
  workOffices?: ReadonlyArray<WorkOffice>;
  /** Initial field values (prefilled from an existing profile, or empty on first run). */
  initialValues?: Partial<UserProfileFormValues>;
  /** Persist the questionnaire. Rejects on failure (surfaced as an inline error). */
  onSubmit: (values: UserProfileFormValues) => Promise<void>;
  /** True while any load of practice areas / offices / initial values is in flight. */
  loading?: boolean;
}

// ---------------------------------------------------------------------------
// Styles (Fluent v9 tokens only — ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  surface: {
    // MA-2: standard small modal — modest width, height driven by content.
    maxWidth: '480px',
  },
  content: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalL,
    minHeight: '260px',
  },
  stepIndicator: {
    color: tokens.colorNeutralForeground3,
  },
  stepIntro: {
    color: tokens.colorNeutralForeground2,
  },
  chipWrap: {
    display: 'flex',
    flexWrap: 'wrap',
    columnGap: tokens.spacingHorizontalS,
    rowGap: tokens.spacingVerticalS,
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
});

const ROLE_PLACEHOLDER = 'Select your role';
const PRACTICE_PLACEHOLDER = 'Select practice areas';
const OFFICE_PLACEHOLDER = 'Select your work location';

const STEP_TITLES = ['Your role', 'Focus areas', 'Preferences'] as const;
const TOTAL_STEPS = STEP_TITLES.length;

// ---------------------------------------------------------------------------
// Chip multi-select (MA-4) — a wrap of Fluent v9 ToggleButtons
// ---------------------------------------------------------------------------

const ChipMultiSelect: React.FC<{
  chips: ReadonlyArray<ProfileChip>;
  selected: ReadonlyArray<string>;
  onToggle: (id: string) => void;
  disabled?: boolean;
  testIdPrefix: string;
}> = ({ chips, selected, onToggle, disabled, testIdPrefix }) => {
  const styles = useStyles();
  return (
    <div className={styles.chipWrap} data-testid={`${testIdPrefix}-chips`}>
      {chips.map((c) => (
        <ToggleButton
          key={c.id}
          size="small"
          shape="circular"
          checked={selected.includes(c.id)}
          disabled={disabled}
          onClick={() => onToggle(c.id)}
          data-testid={`${testIdPrefix}-${c.id}`}
        >
          {c.label}
        </ToggleButton>
      ))}
    </div>
  );
};

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
  workOffices = [],
  initialValues,
  onSubmit,
  loading = false,
}) => {
  const styles = useStyles();

  const [step, setStep] = React.useState(0);
  const [role, setRole] = React.useState<number | null>(initialValues?.primaryRole ?? null);
  const [selectedPractice, setSelectedPractice] = React.useState<string[]>(
    initialValues?.practiceAreaIds ?? []
  );
  const [focusChipIds, setFocusChipIds] = React.useState<string[]>([]);
  const [office, setOffice] = React.useState<string>(initialValues?.officeLocation ?? '');
  const [prefChipIds, setPrefChipIds] = React.useState<string[]>([]);

  const [saving, setSaving] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // Re-seed field state each time the dialog opens (prefill from the latest server values). The
  // free-text focus/preference columns are decoded back into selected chips (MA-4).
  React.useEffect(() => {
    if (open) {
      setStep(0);
      setRole(initialValues?.primaryRole ?? null);
      setSelectedPractice(initialValues?.practiceAreaIds ?? []);
      setFocusChipIds(decodeChipSelection(initialValues?.focusAreas, FOCUS_AREA_CHIPS));
      setOffice(initialValues?.officeLocation ?? '');
      setPrefChipIds(decodeChipSelection(initialValues?.assistantPreferences, PREFERENCE_CHIPS));
      setError(null);
    }
  }, [open, initialValues]);

  const busy = saving || loading;

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

  const toggleFocus = React.useCallback(
    (id: string) => setFocusChipIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id])),
    []
  );
  const togglePref = React.useCallback(
    (id: string) => setPrefChipIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id])),
    []
  );

  const handleSubmit = React.useCallback(async () => {
    setError(null);
    setSaving(true);
    try {
      await onSubmit({
        primaryRole: role,
        practiceAreaIds: selectedPractice,
        focusAreas: encodeChipSelection(focusChipIds, FOCUS_AREA_CHIPS),
        officeLocation: office,
        assistantPreferences: encodeChipSelection(prefChipIds, PREFERENCE_CHIPS),
      });
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save your profile. Please try again.');
    } finally {
      setSaving(false);
    }
  }, [onSubmit, onClose, role, selectedPractice, focusChipIds, office, prefChipIds]);

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
                    <InfoLabel info="Your role tailors the assistant's tone and the tasks it surfaces first (for example, a partner sees different defaults than a paralegal).">
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
                      Primary work location
                    </InfoLabel>
                  }
                >
                  <Dropdown
                    aria-label="Primary work location"
                    data-testid="my-assistant-office"
                    placeholder={OFFICE_PLACEHOLDER}
                    disabled={busy}
                    selectedOptions={office ? [office] : []}
                    value={office}
                    onOptionSelect={(_e, data) => setOffice(data.optionValue ?? '')}
                  >
                    <Option key="__none__" value="" text="Not specified">
                      Not specified
                    </Option>
                    {workOffices.map((o) => (
                      <Option key={o.id} value={o.name} text={o.name}>
                        {o.name}
                      </Option>
                    ))}
                  </Dropdown>
                </Field>
              </>
            ) : null}

            {/* ── Step 2: Focus areas ───────────────────────────────────────── */}
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
                    <InfoLabel info="Pick the areas you concentrate on. The assistant weights these when it surfaces matters, documents, and suggestions.">
                      What do you focus on?
                    </InfoLabel>
                  }
                >
                  <ChipMultiSelect
                    chips={FOCUS_AREA_CHIPS}
                    selected={focusChipIds}
                    onToggle={toggleFocus}
                    disabled={busy}
                    testIdPrefix="my-assistant-focus"
                  />
                </Field>
              </>
            ) : null}

            {/* ── Step 3: Preferences ───────────────────────────────────────── */}
            {step === 2 ? (
              <>
                <Text size={200} className={styles.stepIntro}>
                  Choose how you like the assistant to work. These preferences are applied to every
                  response, so its behaviour stays consistent.
                </Text>

                <Field
                  label={
                    <InfoLabel info="Each selected preference is applied as a standing instruction to the assistant — so its tone and format stay consistent across every response.">
                      How should the assistant respond?
                    </InfoLabel>
                  }
                >
                  <ChipMultiSelect
                    chips={PREFERENCE_CHIPS}
                    selected={prefChipIds}
                    onToggle={togglePref}
                    disabled={busy}
                    testIdPrefix="my-assistant-preferences"
                  />
                </Field>
              </>
            ) : null}
          </DialogContent>

          <DialogActions>
            {/* MA-2: simplified nav — Cancel (bottom-left) · Back · Next/Save (bottom-right). */}
            <div className={styles.actionsRow}>
              <Button
                appearance="subtle"
                onClick={onClose}
                disabled={busy}
                data-testid="my-assistant-cancel"
              >
                Cancel
              </Button>
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
                ) : null}
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
