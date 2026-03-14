/**
 * SelectContainerTypeStep — Step 1 of the RegisterWizard.
 *
 * Lets the administrator pick the container type to register.
 * When the wizard is opened from a specific container type (typeId prop),
 * that type is pre-selected and the dropdown shows type details read-only.
 *
 * ADR-006: Code Page — React 18 patterns, no PCF/ComponentFramework.
 * ADR-021: All styles use makeStyles + Fluent design tokens (no hard-coded colors).
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Dropdown,
  Option,
  Field,
  Badge,
  Spinner,
  MessageBar,
  MessageBarBody,
  shorthands,
} from "@fluentui/react-components";
import type { ContainerType, SpeContainerTypeConfig } from "../../../types/spe";
import { speApiClient, ApiError } from "../../../services/speApiClient";
import { useBuContext } from "../../../contexts/BuContext";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalL),
  },
  description: {
    color: tokens.colorNeutralForeground2,
  },
  typeInfo: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalS),
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingHorizontalM,
    borderWidth: "1px",
    borderStyle: "solid",
    borderColor: tokens.colorNeutralStroke2,
  },
  typeInfoRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalS),
  },
  typeInfoLabel: {
    color: tokens.colorNeutralForeground3,
    minWidth: "140px",
  },
  feedbackArea: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalS),
    color: tokens.colorNeutralForeground3,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

function billingLabel(classification: string): string {
  switch (classification) {
    case "standard":
      return "Standard";
    case "trial":
      return "Trial";
    case "directToCustomer":
      return "Direct to Customer";
    default:
      return classification;
  }
}

function billingBadgeColor(
  classification: string
): "success" | "warning" | "informative" {
  switch (classification) {
    case "standard":
      return "success";
    case "trial":
      return "warning";
    default:
      return "informative";
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface SelectContainerTypeStepProps {
  /** Pre-selected container type ID (opened from a specific type). If provided, dropdown is pre-filled. */
  initialTypeId?: string | null;
  /** Currently selected container type (controlled by parent — RegisterWizard). */
  selectedType: ContainerType | null;
  /** Called when user selects a container type. */
  onTypeSelected: (ct: ContainerType | null) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// SelectContainerTypeStep
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Step 1 of the registration wizard — select or confirm the container type.
 *
 * Loads all container types for the selected config. If an initialTypeId
 * is provided, auto-selects that type and notifies the parent.
 */
export const SelectContainerTypeStep: React.FC<SelectContainerTypeStepProps> = ({
  initialTypeId,
  selectedType,
  onTypeSelected,
}) => {
  const styles = useStyles();
  const { selectedConfig } = useBuContext();

  const [containerTypes, setContainerTypes] = React.useState<ContainerType[]>([]);
  const [loading, setLoading] = React.useState(false);
  const [loadError, setLoadError] = React.useState<string | null>(null);

  // Load container types on mount
  React.useEffect(() => {
    if (!selectedConfig) return;
    let cancelled = false;

    setLoading(true);
    setLoadError(null);

    speApiClient.containerTypes.list(selectedConfig.id)
      .then((types) => {
        if (cancelled) return;
        setContainerTypes(types);

        // Auto-select if initialTypeId provided and not yet selected
        if (initialTypeId && !selectedType) {
          const found = types.find((t) => t.containerTypeId === initialTypeId);
          if (found) onTypeSelected(found);
        }
      })
      .catch((err) => {
        if (cancelled) return;
        setLoadError(
          err instanceof ApiError
            ? err.message
            : "Failed to load container types."
        );
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => { cancelled = true; };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedConfig?.id]);

  // ── Render ─────────────────────────────────────────────────────────────────

  return (
    <div className={styles.root}>
      <Text size={400} weight="semibold">
        Select Container Type
      </Text>
      <Text size={300} className={styles.description}>
        Choose the container type you want to register on the consuming tenant.
        Registration grants the consuming application permission to create
        containers of this type.
      </Text>

      {loadError && (
        <MessageBar intent="error">
          <MessageBarBody>{loadError}</MessageBarBody>
        </MessageBar>
      )}

      {loading ? (
        <div className={styles.feedbackArea}>
          <Spinner size="tiny" />
          <Text size={300}>Loading container types…</Text>
        </div>
      ) : (
        <Field label="Container Type" required>
          <Dropdown
            placeholder="Select a container type…"
            value={selectedType?.displayName ?? ""}
            selectedOptions={selectedType ? [selectedType.containerTypeId] : []}
            onOptionSelect={(_e, d) => {
              const found = containerTypes.find(
                (t) => t.containerTypeId === d.optionValue
              );
              onTypeSelected(found ?? null);
            }}
            aria-label="Container type selection"
          >
            {containerTypes.map((ct) => (
              <Option key={ct.containerTypeId} value={ct.containerTypeId}>
                {ct.displayName}
              </Option>
            ))}
          </Dropdown>
        </Field>
      )}

      {/* Show type details when a type is selected */}
      {selectedType && (
        <div className={styles.typeInfo}>
          <Text size={200} weight="semibold" style={{ color: tokens.colorNeutralForeground2, marginBottom: tokens.spacingVerticalXS }}>
            Container Type Details
          </Text>
          <div className={styles.typeInfoRow}>
            <Text size={200} className={styles.typeInfoLabel}>Display Name</Text>
            <Text size={200} weight="semibold">{selectedType.displayName}</Text>
          </div>
          <div className={styles.typeInfoRow}>
            <Text size={200} className={styles.typeInfoLabel}>Container Type ID</Text>
            <Text size={200} style={{ fontFamily: "monospace", color: tokens.colorNeutralForeground2 }}>
              {selectedType.containerTypeId}
            </Text>
          </div>
          <div className={styles.typeInfoRow}>
            <Text size={200} className={styles.typeInfoLabel}>Billing Classification</Text>
            <Badge
              color={billingBadgeColor(selectedType.billingClassification)}
              appearance="filled"
              size="small"
            >
              {billingLabel(selectedType.billingClassification)}
            </Badge>
          </div>
          {selectedType.isRegistered && (
            <div className={styles.typeInfoRow}>
              <Text size={200} className={styles.typeInfoLabel}>Registration Status</Text>
              <Badge color="success" appearance="filled" size="small">
                Already Registered
              </Badge>
            </div>
          )}
        </div>
      )}
    </div>
  );
};
