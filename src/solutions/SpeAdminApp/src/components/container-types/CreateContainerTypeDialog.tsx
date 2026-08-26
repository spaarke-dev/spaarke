/**
 * CreateContainerTypeDialog — create a new SPE container type, with its lifecycle constraints
 * stated BEFORE submit.
 *
 * Fields:
 *   - Name (displayName) — required, 1-50 chars
 *   - Billing Classification — required (trial | standard | directToCustomer)
 *
 * WHY THIS DIALOG IS MORE THAN TWO FIELDS (spec FR-C13 / task 030). Creating a container type is
 * close to irreversible, and none of that was visible here. The billing classification can never be
 * changed; the owning application is bound 1:1 and permanently; a non-trial container type **can
 * never be deleted**; a trial type silently expires after 30 days and cannot be registered on another
 * tenant. Previously an admin picked a value from a dropdown and found all of this out by failing —
 * or, in the expiry case, never found out at all. Every consequence is now shown for the selected
 * classification, and the permanent ones must be acknowledged before Create is enabled.
 *
 * The facts themselves live in `containerTypeLifecycle.ts`, each carrying a line reference into
 * `knowledge/sharepoint-embedded/docs/learn-containertypes.md`.
 *
 * ADR-021: All styles use Fluent UI v9 design tokens — no hard-coded colors.
 * ADR-006: Code Page component (React 18); no PCF dependencies.
 */

import * as React from "react";
import {
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  Button,
  Input,
  Field,
  Dropdown,
  Option,
  Spinner,
  Checkbox,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Text,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import {
  LockClosed16Regular,
  Warning16Regular,
  Info16Regular,
} from "@fluentui/react-icons";
import {
  BILLING_CLASSIFICATION_PROFILES,
  UNIVERSAL_CONSEQUENCES,
  assessTrialQuota,
  describeProductionQuota,
  profileFor,
  type BillingClassificationValue,
  type ConsequenceSeverity,
  type LifecycleConsequence,
} from "./containerTypeLifecycle";

export type { BillingClassificationValue };

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021 — Fluent tokens only)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  /** The dialog body can grow past the viewport once consequences are listed. */
  content: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
    maxHeight: "60vh",
    overflowY: "auto",
  },

  /** Explanatory line under the classification dropdown. */
  classificationSummary: {
    color: tokens.colorNeutralForeground2,
  },

  /** Panel holding the consequences of the current selection. */
  consequencePanel: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    padding: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    borderLeftWidth: tokens.strokeWidthThicker,
    borderLeftStyle: "solid",
    borderLeftColor: tokens.colorPaletteRedBorder2,
  },

  consequenceHeading: {
    color: tokens.colorNeutralForeground1,
  },

  /** One consequence line: icon + text. */
  consequence: {
    display: "flex",
    alignItems: "flex-start",
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground2,
  },

  /** Icon colours carry the severity without relying on the text alone. */
  iconIrreversible: {
    color: tokens.colorPaletteRedForeground1,
    flexShrink: 0,
    marginTop: "2px",
  },
  iconLimit: {
    color: tokens.colorPaletteDarkOrangeForeground1,
    flexShrink: 0,
    marginTop: "2px",
  },
  iconObligation: {
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
    marginTop: "2px",
  },

  quotaNote: {
    color: tokens.colorNeutralForeground3,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Consequence rendering
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Icon per severity. Irreversible reads loudest because it is the class an admin cannot recover from.
 */
const ConsequenceIcon: React.FC<{ severity: ConsequenceSeverity; className: string }> = ({
  severity,
  className,
}) => {
  if (severity === "irreversible") return <LockClosed16Regular className={className} />;
  if (severity === "limit") return <Warning16Regular className={className} />;
  return <Info16Regular className={className} />;
};

const ConsequenceList: React.FC<{ items: readonly LifecycleConsequence[] }> = ({ items }) => {
  const styles = useStyles();

  const classFor = (severity: ConsequenceSeverity): string =>
    severity === "irreversible"
      ? styles.iconIrreversible
      : severity === "limit"
        ? styles.iconLimit
        : styles.iconObligation;

  return (
    <>
      {items.map((item) => (
        <div key={item.text} className={styles.consequence}>
          <ConsequenceIcon severity={item.severity} className={classFor(item.severity)} />
          <Text size={200}>{item.text}</Text>
        </div>
      ))}
    </>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

/** The single field the quota assessment needs; keeps this dialog decoupled from the full DTO. */
export interface ContainerTypeQuotaInput {
  billingClassification?: string | null;
}

export interface CreateContainerTypeDialogProps {
  /** Whether the dialog is open. */
  open: boolean;
  /** Whether the form submission is in progress. */
  isSaving: boolean;
  /**
   * Container types currently visible to the caller, used to assess the documented quotas.
   *
   * Treated as a LOWER BOUND, never a tenant census — visibility depends on an Entra directory role
   * the BFF cannot observe (task 012). See `containerTypeLifecycle.ts` for how that shapes what the
   * dialog is willing to assert.
   */
  existingContainerTypes?: readonly ContainerTypeQuotaInput[];
  /** Called when the user closes or cancels the dialog. */
  onClose: () => void;
  /**
   * Called when the user submits the form.
   * @param displayName - The container type display name.
   * @param billingClassification - The billing classification value.
   */
  onSubmit: (
    displayName: string,
    billingClassification: BillingClassificationValue
  ) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// CreateContainerTypeDialog
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Validates locally before calling onSubmit. The parent (ContainerTypesPage) owns the API call and
 * closes the dialog on success.
 */
export const CreateContainerTypeDialog: React.FC<CreateContainerTypeDialogProps> = ({
  open,
  isSaving,
  existingContainerTypes = [],
  onClose,
  onSubmit,
}) => {
  const styles = useStyles();

  const [displayName, setDisplayName] = React.useState("");
  const [billingClassification, setBillingClassification] =
    React.useState<BillingClassificationValue>("trial");
  const [nameError, setNameError] = React.useState<string | undefined>();
  const [acknowledged, setAcknowledged] = React.useState(false);

  const profile = profileFor(billingClassification);

  // ── Quota ──────────────────────────────────────────────────────────────────
  // Assessed against the visible list. Only the trial limit can be proven from it: seeing a trial
  // type proves one exists. Not seeing one proves nothing, so we never claim the slot is free.

  const quota = React.useMemo(
    () =>
      billingClassification === "trial"
        ? assessTrialQuota(existingContainerTypes)
        : describeProductionQuota(existingContainerTypes),
    [billingClassification, existingContainerTypes]
  );

  /**
   * A container type that can never be deleted is a different kind of decision from one that expires
   * in 30 days, so the gate is proportionate: production classifications require an explicit
   * acknowledgment, trial only requires that the consequences were shown.
   */
  const requiresAcknowledgment = !profile.deletable;
  const blockedByQuota = quota.atLimit;

  // Reset the acknowledgment whenever the decision it refers to changes.
  React.useEffect(() => {
    setAcknowledged(false);
  }, [billingClassification]);

  // ── Handlers ──────────────────────────────────────────────────────────────

  const handleSubmit = React.useCallback(() => {
    const trimmed = displayName.trim();
    if (!trimmed) {
      setNameError("Container type name is required.");
      return;
    }
    if (trimmed.length > 50) {
      setNameError("Name must be 50 characters or fewer.");
      return;
    }
    // Never submit a request the documented limits say will be rejected.
    if (blockedByQuota) return;
    if (requiresAcknowledgment && !acknowledged) return;

    setNameError(undefined);
    onSubmit(trimmed, billingClassification);
  }, [
    displayName,
    billingClassification,
    blockedByQuota,
    requiresAcknowledgment,
    acknowledged,
    onSubmit,
  ]);

  const handleClose = React.useCallback(() => {
    // Reset form state on close
    setDisplayName("");
    setBillingClassification("trial");
    setNameError(undefined);
    setAcknowledged(false);
    onClose();
  }, [onClose]);

  /** Submit on Enter key in the name field. */
  const handleNameKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key === "Enter") handleSubmit();
    },
    [handleSubmit]
  );

  const submitDisabled =
    isSaving ||
    !displayName.trim() ||
    blockedByQuota ||
    (requiresAcknowledgment && !acknowledged);

  // ── Render ─────────────────────────────────────────────────────────────────

  return (
    <Dialog
      open={open}
      onOpenChange={(_e, { open: isOpen }) => {
        if (!isOpen) handleClose();
      }}
    >
      <DialogSurface>
        <DialogTitle>Create Container Type</DialogTitle>
        <DialogBody>
          <DialogContent className={styles.content}>
            {/* Display Name */}
            <Field
              label="Container Type Name"
              required
              validationMessage={nameError}
              validationState={nameError ? "error" : "none"}
            >
              <Input
                value={displayName}
                onChange={(_e, d) => {
                  setDisplayName(d.value);
                  if (nameError) setNameError(undefined);
                }}
                onKeyDown={handleNameKeyDown}
                placeholder="e.g. Legal Documents"
                disabled={isSaving}
                autoFocus
                maxLength={50}
              />
            </Field>

            {/* Billing Classification */}
            <Field label="Billing Classification" required>
              <Dropdown
                value={profile.label}
                selectedOptions={[billingClassification]}
                onOptionSelect={(_e, d) => {
                  if (d.optionValue) {
                    setBillingClassification(d.optionValue as BillingClassificationValue);
                  }
                }}
                disabled={isSaving}
                aria-label="Billing classification"
              >
                {BILLING_CLASSIFICATION_PROFILES.map((opt) => (
                  <Option key={opt.value} value={opt.value} text={opt.label}>
                    {opt.label}
                  </Option>
                ))}
              </Dropdown>
            </Field>
            <Text size={200} className={styles.classificationSummary}>
              {profile.summary}
            </Text>

            {/* ── Quota ──
                Stated, never computed into a "remaining" figure. The visible list is a lower bound on
                the tenant's true count, so a remaining number would be a guess presented as a fact. */}
            <MessageBar intent={blockedByQuota ? "error" : "info"}>
              <MessageBarBody>
                {blockedByQuota && <MessageBarTitle>Limit already reached</MessageBarTitle>}
                {quota.message}
              </MessageBarBody>
            </MessageBar>

            {/* ── Consequences of this choice, before submit ── */}
            <div className={styles.consequencePanel}>
              <Text size={300} weight="semibold" className={styles.consequenceHeading}>
                These choices are permanent
              </Text>
              <ConsequenceList items={profile.consequences} />
              <ConsequenceList items={UNIVERSAL_CONSEQUENCES} />
            </div>

            {/* A type that can never be deleted warrants an explicit acknowledgment; a trial, which
                expires and can be removed, does not. */}
            {requiresAcknowledgment && (
              <Checkbox
                checked={acknowledged}
                onChange={(_e, d) => setAcknowledged(d.checked === true)}
                disabled={isSaving || blockedByQuota}
                label={
                  <Text size={200}>
                    I understand that a <strong>{profile.label}</strong> container type{" "}
                    <strong>cannot be deleted or converted</strong> after it is created.
                  </Text>
                }
              />
            )}
          </DialogContent>

          <DialogActions>
            <Button appearance="secondary" onClick={handleClose} disabled={isSaving}>
              Cancel
            </Button>
            <Button
              appearance="primary"
              onClick={handleSubmit}
              disabled={submitDisabled}
              icon={isSaving ? <Spinner size="tiny" /> : undefined}
            >
              {isSaving ? "Creating…" : "Create"}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};
