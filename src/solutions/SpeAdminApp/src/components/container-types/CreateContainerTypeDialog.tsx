/**
 * CreateContainerTypeDialog — modal dialog for creating a new SPE container type.
 *
 * Fields:
 *   - Name (displayName) — required, 1-50 chars
 *   - Billing Classification — required, Dropdown (trial | standard | directToCustomer)
 *
 * Submits to POST /api/spe/containertypes via the onSubmit callback.
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
  tokens,
} from "@fluentui/react-components";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/** Available billing classification values for container types. */
const BILLING_OPTIONS = [
  { value: "trial", label: "Trial" },
  { value: "standard", label: "Standard" },
  { value: "directToCustomer", label: "Direct to Customer" },
] as const;

export type BillingClassificationValue =
  (typeof BILLING_OPTIONS)[number]["value"];

export interface CreateContainerTypeDialogProps {
  /** Whether the dialog is open. */
  open: boolean;
  /** Whether the form submission is in progress. */
  isSaving: boolean;
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
// CreateContainerTypeDialog Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * CreateContainerTypeDialog — form dialog for creating a new container type.
 *
 * Validates locally before calling onSubmit. The parent (ContainerTypesPage)
 * is responsible for the actual API call and closing the dialog on success.
 */
export const CreateContainerTypeDialog: React.FC<
  CreateContainerTypeDialogProps
> = ({ open, isSaving, onClose, onSubmit }) => {
  const [displayName, setDisplayName] = React.useState("");
  const [billingClassification, setBillingClassification] =
    React.useState<BillingClassificationValue>("trial");
  const [nameError, setNameError] = React.useState<string | undefined>();

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
    setNameError(undefined);
    onSubmit(trimmed, billingClassification);
  }, [displayName, billingClassification, onSubmit]);

  const handleClose = React.useCallback(() => {
    // Reset form state on close
    setDisplayName("");
    setBillingClassification("trial");
    setNameError(undefined);
    onClose();
  }, [onClose]);

  /** Submit on Enter key in the name field. */
  const handleNameKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key === "Enter") handleSubmit();
    },
    [handleSubmit]
  );

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
          <DialogContent>
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
            <Field
              label="Billing Classification"
              required
              style={{ marginTop: tokens.spacingVerticalM }}
            >
              <Dropdown
                value={
                  BILLING_OPTIONS.find((o) => o.value === billingClassification)
                    ?.label ?? "Trial"
                }
                selectedOptions={[billingClassification]}
                onOptionSelect={(_e, d) => {
                  if (d.optionValue) {
                    setBillingClassification(
                      d.optionValue as BillingClassificationValue
                    );
                  }
                }}
                disabled={isSaving}
                aria-label="Billing classification"
              >
                {BILLING_OPTIONS.map((opt) => (
                  <Option key={opt.value} value={opt.value}>
                    {opt.label}
                  </Option>
                ))}
              </Dropdown>
            </Field>
          </DialogContent>

          <DialogActions>
            <Button
              appearance="secondary"
              onClick={handleClose}
              disabled={isSaving}
            >
              Cancel
            </Button>
            <Button
              appearance="primary"
              onClick={handleSubmit}
              disabled={isSaving || !displayName.trim()}
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
