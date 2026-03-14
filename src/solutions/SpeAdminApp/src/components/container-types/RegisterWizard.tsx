/**
 * RegisterWizard — multi-step wizard for registering a container type
 * on the consuming tenant.
 *
 * Registration is the critical step that enables containers to be created
 * from a type. The wizard guides administrators through:
 *   Step 1 — Select Container Type
 *   Step 2 — Choose Delegated Permissions
 *   Step 3 — Choose Application Permissions
 *   Step 4 — Confirm and Register
 *
 * Uses WizardShell from @spaarke/ui-components in embedded mode
 * (the wizard is already hosted inside a Dataverse dialog / Code Page).
 *
 * ADR-012: Uses WizardShell from @spaarke/ui-components.
 * ADR-006: Code Page — React 18 patterns, no PCF/ComponentFramework.
 * ADR-021: All styles use Fluent design tokens (no hard-coded colors).
 *
 * @example
 * <RegisterWizard
 *   open={registerOpen}
 *   onClose={() => setRegisterOpen(false)}
 *   onRegistered={handleRegistrationComplete}
 *   initialTypeId={selectedTypeId}
 * />
 */

import * as React from "react";
import {
  Text,
  Button,
  tokens,
} from "@fluentui/react-components";
import {
  CheckmarkCircle24Regular,
  ArrowLeft20Regular,
} from "@fluentui/react-icons";
import { WizardShell } from "@spaarke/ui-components";
import type {
  IWizardStepConfig,
  IWizardSuccessConfig,
} from "@spaarke/ui-components";
import type { ContainerType } from "../../types/spe";
import { speApiClient, ApiError } from "../../services/speApiClient";
import { useBuContext } from "../../contexts/BuContext";

// Step components
import { SelectContainerTypeStep } from "./steps/SelectContainerTypeStep";
import { DelegatedPermissionsStep } from "./steps/DelegatedPermissionsStep";
import { ApplicationPermissionsStep } from "./steps/ApplicationPermissionsStep";
import { ConfirmRegistrationStep } from "./steps/ConfirmRegistrationStep";

// ─────────────────────────────────────────────────────────────────────────────
// Step IDs (stable string keys for WizardShell)
// ─────────────────────────────────────────────────────────────────────────────

const STEP_SELECT_TYPE = "select-type";
const STEP_DELEGATED = "delegated-permissions";
const STEP_APPLICATION = "application-permissions";
const STEP_CONFIRM = "confirm-register";

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface RegisterWizardProps {
  /** Whether the wizard is open (visible). */
  open: boolean;
  /** Called when the wizard is closed (Cancel or X button). */
  onClose: () => void;
  /**
   * Called after a successful registration, with the registered container type.
   * Use this to refresh the container types list in the parent.
   */
  onRegistered?: (containerTypeId: string) => void;
  /**
   * Optional — pre-select a container type by ID (e.g., opened from a specific row).
   * When provided, Step 1 pre-fills the selected type.
   */
  initialTypeId?: string | null;
}

// ─────────────────────────────────────────────────────────────────────────────
// RegisterWizard
// ─────────────────────────────────────────────────────────────────────────────

/**
 * RegisterWizard — orchestrates a 4-step registration flow using WizardShell.
 *
 * State for each step is managed here and passed down to step components
 * via props and callbacks. The WizardShell handles navigation, footer buttons,
 * and the success/error flow.
 */
export const RegisterWizard: React.FC<RegisterWizardProps> = ({
  open,
  onClose,
  onRegistered,
  initialTypeId,
}) => {
  const { selectedConfig } = useBuContext();

  // ── Shared Wizard State ──────────────────────────────────────────────────

  /** Step 1: selected container type */
  const [selectedType, setSelectedType] = React.useState<ContainerType | null>(null);

  /** Step 2: chosen delegated permissions */
  const [delegatedPermissions, setDelegatedPermissions] = React.useState<string[]>([]);

  /** Step 3: chosen application permissions */
  const [applicationPermissions, setApplicationPermissions] = React.useState<string[]>([]);

  // ── Reset state when wizard opens ─────────────────────────────────────────

  const prevOpenRef = React.useRef(open);
  React.useEffect(() => {
    const wasOpen = prevOpenRef.current;
    prevOpenRef.current = open;
    if (open && !wasOpen) {
      // Reset all state when wizard re-opens
      setSelectedType(null);
      setDelegatedPermissions([]);
      setApplicationPermissions([]);
    }
  }, [open]);

  // ── onFinish: Submit registration ─────────────────────────────────────────

  const handleFinish = React.useCallback(async (): Promise<IWizardSuccessConfig | void> => {
    if (!selectedType || !selectedConfig) {
      throw new Error("No container type or configuration selected.");
    }

    try {
      await speApiClient.containerTypes.register(
        selectedType.containerTypeId,
        selectedConfig.id,
        {
          delegatedPermissions,
          applicationPermissions,
        }
      );
    } catch (err) {
      const message =
        err instanceof ApiError
          ? err.message
          : "Failed to register container type. Please try again.";
      throw new Error(message);
    }

    // Notify parent so it can refresh the list
    onRegistered?.(selectedType.containerTypeId);

    // Return success config for WizardShell success screen
    return {
      icon: (
        <CheckmarkCircle24Regular
          style={{ fontSize: "48px", color: tokens.colorStatusSuccessForeground1 }}
        />
      ),
      title: "Container Type Registered",
      body: (
        <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
          <strong>{selectedType.displayName}</strong> has been successfully
          registered on the consuming tenant with{" "}
          {delegatedPermissions.length} delegated permission
          {delegatedPermissions.length !== 1 ? "s" : ""} and{" "}
          {applicationPermissions.length} application permission
          {applicationPermissions.length !== 1 ? "s" : ""}.
        </Text>
      ),
      actions: (
        <Button
          appearance="primary"
          onClick={onClose}
          icon={<ArrowLeft20Regular />}
        >
          Back to Container Types
        </Button>
      ),
    };
  }, [
    selectedType,
    selectedConfig,
    delegatedPermissions,
    applicationPermissions,
    onRegistered,
    onClose,
  ]);

  // ── Step Configurations ────────────────────────────────────────────────────

  const steps: IWizardStepConfig[] = React.useMemo(
    () => [
      // ── Step 1: Select Container Type ────────────────────────────────────
      {
        id: STEP_SELECT_TYPE,
        label: "Select Container Type",
        canAdvance: () => selectedType !== null,
        renderContent: () => (
          <SelectContainerTypeStep
            initialTypeId={initialTypeId}
            selectedType={selectedType}
            onTypeSelected={setSelectedType}
          />
        ),
      },

      // ── Step 2: Delegated Permissions ────────────────────────────────────
      {
        id: STEP_DELEGATED,
        label: "Delegated Permissions",
        canAdvance: () => delegatedPermissions.length > 0,
        renderContent: () => (
          <DelegatedPermissionsStep
            selectedPermissions={delegatedPermissions}
            onPermissionsChanged={setDelegatedPermissions}
          />
        ),
      },

      // ── Step 3: Application Permissions ─────────────────────────────────
      {
        id: STEP_APPLICATION,
        label: "Application Permissions",
        canAdvance: () => applicationPermissions.length > 0,
        renderContent: () => (
          <ApplicationPermissionsStep
            selectedPermissions={applicationPermissions}
            onPermissionsChanged={setApplicationPermissions}
          />
        ),
      },

      // ── Step 4: Confirm and Register ─────────────────────────────────────
      {
        id: STEP_CONFIRM,
        label: "Confirm & Register",
        canAdvance: () =>
          selectedType !== null &&
          delegatedPermissions.length > 0 &&
          applicationPermissions.length > 0,
        renderContent: () =>
          selectedType ? (
            <ConfirmRegistrationStep
              containerType={selectedType}
              delegatedPermissions={delegatedPermissions}
              applicationPermissions={applicationPermissions}
            />
          ) : null,
      },
    ],
    [
      selectedType,
      delegatedPermissions,
      applicationPermissions,
      initialTypeId,
    ]
  );

  // ── Render ─────────────────────────────────────────────────────────────────

  return (
    <WizardShell
      open={open}
      embedded={true}
      hideTitle={false}
      title="Register Container Type"
      ariaLabel="Register container type wizard"
      steps={steps}
      onClose={onClose}
      onFinish={handleFinish}
      finishLabel="Register"
      finishingLabel="Registering…"
    />
  );
};
