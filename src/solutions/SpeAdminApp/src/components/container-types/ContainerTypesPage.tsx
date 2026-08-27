/**
 * ContainerTypesPage — SPE container type management interface.
 *
 * Displays all SPE container types for the selected BU/config context
 * in a Fluent v9 DataGrid. Administrators can:
 *   - View all container types (name, description, billing classification, created date)
 *   - Create a new container type via the "New" toolbar button
 *   - Initiate registration of a container type via the "Register" toolbar button
 *   - Click a row to open the ContainerTypeDetail panel (SPE-062)
 *
 * ADR-006: Code Page — React 18 patterns, no PCF / ComponentFramework dependencies.
 * ADR-012: Reuses Fluent v9 DataGrid (same as ContainersPage).
 * ADR-021: All styles use makeStyles + Fluent design tokens (no hard-coded colors).
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Spinner,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  MessageBarActions,
  Badge,
  Toolbar,
  ToolbarButton,
  ToolbarDivider,
  Tooltip,
  DataGrid,
  DataGridBody,
  DataGridCell,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridRow,
  createTableColumn,
  type TableColumnDefinition,
  Button,
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
  shorthands,
} from "@fluentui/react-components";
import {
  Add20Regular,
  ArrowClockwise20Regular,
  CloudLink20Regular,
  DocumentBulletList20Regular,
  Settings20Regular,
} from "@fluentui/react-icons";
import { useBuContext } from "../../contexts/BuContext";
import {
  speApiClient,
  describeApiError,
  describePermissionPrerequisite,
} from "../../services/speApiClient";
import type { PermissionPrerequisite } from "../../services/speApiClient";
import type { ContainerType } from "../../types/spe";
import { assessBilling, assessTrialExpiry } from "./containerTypeLifecycle";
import { CreateContainerTypeDialog } from "./CreateContainerTypeDialog";
import { RegisterWizard } from "./RegisterWizard";
import { ContainerTypeConfig } from "../settings/ContainerTypeConfig";
import { useGridStyles } from "../layout/gridStyles";

// ─────────────────────────────────────────────────────────────────────────────
// Utilities
// ─────────────────────────────────────────────────────────────────────────────

/** Format an ISO date string to a localised short date. */
function formatDate(iso: string | undefined): string {
  if (!iso) return "—";
  try {
    return new Intl.DateTimeFormat(undefined, {
      year: "numeric",
      month: "short",
      day: "numeric",
    }).format(new Date(iso));
  } catch {
    return iso;
  }
}

/** Map billing classification to a badge color. */
function billingBadgeColor(
  classification: string | undefined
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

/** Capitalize the first letter of a string for display. */
function capitalize(s: string): string {
  if (!s) return s;
  return s.charAt(0).toUpperCase() + s.slice(1);
}

/**
 * Reconcile the wire shape with the client's `ContainerType` type.
 *
 * The BFF's `ContainerTypeDto` serialises the identifier as `id`; this client declares it as
 * `containerTypeId`. Nothing converted between them, and because the response is cast rather than
 * parsed, TypeScript never noticed. The effect was silent and total: `getRowId` returned `undefined`
 * for every row, so no row could be selected, `hasSelectedType` was permanently false, and the
 * Register wizard always opened with no type. Selecting a row appeared to do nothing.
 *
 * Normalising here keeps the fix inside this screen.
 *
 * ⚠️ This comment used to end by asserting the BFF "never sends" `owningAppId`, `azureTenantId`, or
 * `expiryDateTime`. That was true when written and **task 030 made two thirds of it false the same
 * day** — `owningAppId` and `expiryDateTime` now flow Graph → summary → DTO → client. The stale half
 * mattered: it read as documentation that the trial-expiry data was unavailable, which is part of
 * why an expired trial went unsurfaced for eleven months. Only `azureTenantId` is still absent.
 *
 * (A comment asserting what another layer does is a claim with an expiry date — the same lesson as
 * the `Graph SDK 5.101.0` comment that kept `billingClassification` null for ten days.)
 */
function normalizeContainerType(raw: ContainerType & { id?: string }): ContainerType {
  return {
    ...raw,
    containerTypeId: raw.containerTypeId ?? raw.id ?? "",
  };
}

/**
 * Column sizing — a MODULE-LEVEL CONSTANT. See the identical note in `ContainersPage`: an inline
 * object literal is a new reference every render, which makes Fluent re-apply `defaultWidth` and
 * discard the operator's drag the moment anything re-renders (UAT round 8).
 */
/** localStorage key holding the container type IDs whose expiry banner has been dismissed. */
const DISMISSED_EXPIRY_KEY = "speadmin_dismissedTrialExpiry";

const COLUMN_SIZING = {
  displayName: { minWidth: 140, defaultWidth: 200, idealWidth: 200 },
  billingClassification: { minWidth: 110, defaultWidth: 160, idealWidth: 160 },
  billingStatus: { minWidth: 100, defaultWidth: 140, idealWidth: 140 },
  trialExpiry: { minWidth: 100, defaultWidth: 140, idealWidth: 140 },
  owningAppId: { minWidth: 140, defaultWidth: 260, idealWidth: 260 },
  isRegistered: { minWidth: 90, defaultWidth: 120, idealWidth: 120 },
  createdDateTime: { minWidth: 90, defaultWidth: 130, idealWidth: 130 },
} as const;

/**
 * Whether a container type can be registered on a consuming tenant.
 *
 * A trial container type "is restricted to work in the developer tenant. It can't be deployed in
 * other consuming tenants" (knowledge/sharepoint-embedded/docs/learn-containertypes.md:71), so
 * offering Register for one is offering an action that cannot succeed — the failure mode this task
 * exists to remove (spec FR-C13).
 */
function canRegister(ct: ContainerType | undefined): boolean {
  if (!ct) return false;
  return (ct.billingClassification ?? "").toLowerCase() !== "trial";
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021 — Fluent tokens only)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },

  header: {
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    flexShrink: 0,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
  },

  pageTitle: {
    marginBottom: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground1,
  },

  /**
   * `display: block` so the scope line sits BELOW the title. Fluent's `Text` is inline by
   * default, so "Container Types" and "Spaarke PAYGO 1 · Spaarke Dev · 4 types" rendered on one
   * line with no space between them — "Container TypesSpaarke PAYGO 1" (UAT round 6 screenshot).
   */
  pageSubtitle: {
    display: "block",
    color: tokens.colorNeutralForeground2,
  },

  /** Command toolbar sits between the page header and the data grid. */
  toolbar: {
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    minHeight: "44px",
    display: "flex",
    alignItems: "center",
    flexShrink: 0,
  },

  /** Scrollable content area containing the data grid. */
  content: {
    flex: "1 1 auto",
    overflow: "auto",
    minHeight: 0,
  },

  /** Feedback area (loading / error / empty state). */
  feedback: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingVerticalXXL,
    ...shorthands.gap(tokens.spacingVerticalM),
    height: "100%",
    color: tokens.colorNeutralForeground2,
  },

  /**
   * Scoping explanation under the empty state. Secondary foreground token so it reads as context
   * rather than as an error, and adapts with the theme (ADR-021 — no hard-coded colours).
   */
  scopeNote: {
    maxWidth: "46rem",
    textAlign: "center",
    color: tokens.colorNeutralForeground3,
  },

  /** Message bar wrapper */
  messageBarWrapper: {
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
  },

  /** DataGrid fills its parent. */
  dataGrid: {
    width: "100%",
  },

  /** DataGrid header row uses a subtle background. */
  dataGridHeaderRow: {
    backgroundColor: tokens.colorNeutralBackground2,
  },

  /** Row hover highlight — cursor indicates clickability. */
  dataGridRow: {
    cursor: "pointer",
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },

  buttonLabel: {
    marginLeft: tokens.spacingHorizontalXS,
  },

  /** Context-prompt shown when no BU/config is selected. */
  noContextBanner: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalM,
    height: "100%",
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
    color: tokens.colorNeutralForeground2,
  },

  /** Owning app ID shown in muted color. */
  mutedText: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },

  /**
   * Stacked lines inside the invalid-billing warning: affected types, then the operational
   * consequence, then the remediation. Column layout so each stays a separate readable statement
   * instead of running together into one paragraph.
   */
  billingWarningBody: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalXS),
    marginTop: tokens.spacingVerticalXS,
  },

  /**
   * The configurations dialog hosts a full CRUD grid, so it needs far more room than Fluent's
   * 600px DialogSurface default. Capped in viewport units so it still fits a laptop screen.
   */
  configsDialogSurface: {
    maxWidth: "min(1200px, 95vw)",
    width: "min(1200px, 95vw)",
  },

  /** Gives the embedded config grid a workable height inside the dialog. */
  configsDialogContent: {
    minHeight: "60vh",
    maxHeight: "70vh",
    overflow: "auto",
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Column Definitions
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Build typed Fluent DataGrid column definitions for ContainerType rows.
 *
 * @param now Evaluation instant for trial expiry, passed in rather than read from the clock so the
 *            column is deterministic and the grid does not re-render on every tick.
 */
function buildColumns(now: Date): TableColumnDefinition<ContainerType>[] {
  return [
    createTableColumn<ContainerType>({
      columnId: "displayName",
      renderHeaderCell: () => "Name",
      renderCell: (ct) => (
        <Text weight="semibold" truncate wrap={false}>
          {ct.displayName}
        </Text>
      ),
    }),
    createTableColumn<ContainerType>({
      columnId: "billingClassification",
      renderHeaderCell: () => "Billing Classification",
      /*
       * Three states, like Registered above. An absent classification used to reach `capitalize`,
       * which returns its falsy input unchanged — so the cell rendered an EMPTY `informative` badge,
       * visually a real neutral state rather than a gap. That is not hypothetical: the value was null
       * for every container type between the Graph 6 upgrade (2026-08-13) and task 030's fix
       * (2026-08-23), and nothing on this screen said so.
       */
      renderCell: (ct) => {
        if (!ct.billingClassification) {
          return (
            <Tooltip
              content="Microsoft Graph did not return a billing classification for this container type."
              relationship="label"
            >
              <Badge color="informative" appearance="outline" size="small">
                Unknown
              </Badge>
            </Tooltip>
          );
        }
        return (
          <Badge
            color={billingBadgeColor(ct.billingClassification)}
            appearance="filled"
            size="small"
          >
            {capitalize(ct.billingClassification)}
          </Badge>
        );
      },
    }),
    createTableColumn<ContainerType>({
      columnId: "billingStatus",
      renderHeaderCell: () => "Billing Status",
      /*
       * Task 029 / spec FR-C12. Until this column existed, invalid billing had no route to an
       * administrator at all — "billingStatus" did not appear anywhere in the codebase, server or
       * client. Unknown is rendered as Unknown, never as valid (NFR-06).
       */
      renderCell: (ct) => {
        const billing = assessBilling(ct);
        const badge = (
          <Badge
            color={billing.tone}
            appearance={billing.standing === "unknown" ? "outline" : "filled"}
            size="small"
          >
            {billing.label}
          </Badge>
        );
        const tip = [billing.consequence, billing.remediation]
          .filter(Boolean)
          .join(" ");
        return tip ? (
          <Tooltip content={tip} relationship="label">
            {badge}
          </Tooltip>
        ) : (
          badge
        );
      },
    }),
    createTableColumn<ContainerType>({
      columnId: "trialExpiry",
      renderHeaderCell: () => "Trial Expiry",
      /*
       * Spec FR-C13. The live tenant's trial expired 2025-10-10 — eleven months ago — and the app
       * said so NOWHERE. `expiryDateTime` was rendered in exactly one place (the detail panel) as a
       * plain date with no indication it was in the past, and task 030's 30-day warning went to the
       * CREATE dialog, which warns the only audience that cannot yet be affected.
       *
       * A trial is not renewable and simply stops working at expiry, so an expired type is dead
       * weight an admin must be able to see at a glance. Non-trial rows render an em-dash: they have
       * no expiry, and that IS the fact, unlike the absent states elsewhere on this grid.
       */
      renderCell: (ct) => {
        const expiry = assessTrialExpiry(ct, now);
        if (expiry.state === "not-a-trial") {
          return (
            <Text style={{ color: tokens.colorNeutralForeground3 }} aria-label="Not a trial">
              —
            </Text>
          );
        }
        const badge = (
          <Badge
            color={expiry.tone}
            appearance={expiry.state === "unknown" ? "outline" : "filled"}
            size="small"
          >
            {expiry.label}
          </Badge>
        );
        return expiry.consequence ? (
          <Tooltip content={expiry.consequence} relationship="label">
            {badge}
          </Tooltip>
        ) : (
          badge
        );
      },
    }),
    createTableColumn<ContainerType>({
      columnId: "owningAppId",
      renderHeaderCell: () => "Owning App",
      // An absent owning app is unknown, not absent — a blank cell would claim there isn't one.
      renderCell: (ct) => (
        <Text truncate wrap={false} style={{ color: tokens.colorNeutralForeground2 }}>
          {ct.owningAppId ?? "—"}
        </Text>
      ),
    }),
    createTableColumn<ContainerType>({
      columnId: "isRegistered",
      renderHeaderCell: () => "Registered",
      /*
       * Three states, not two. Registration status comes from the containerTypeRegistrations
       * endpoint, which this screen does not call — so here the value is `undefined`, meaning NOT
       * DETERMINED. Collapsing that to "No" (as this did until 2026-08-23) told an administrator
       * that every container type in the tenant was unregistered, which the data never said.
       */
      renderCell: (ct) => {
        if (ct.isRegistered === undefined) {
          return (
            <Tooltip
              content="Registration status is not returned with the container type list. Open the type to check its consuming tenants."
              relationship="label"
            >
              <Badge color="informative" appearance="outline" size="small">
                Unknown
              </Badge>
            </Tooltip>
          );
        }
        return (
          <Badge
            color={ct.isRegistered ? "success" : "warning"}
            appearance="filled"
            size="small"
          >
            {ct.isRegistered ? "Yes" : "No"}
          </Badge>
        );
      },
    }),
    createTableColumn<ContainerType>({
      columnId: "createdDateTime",
      renderHeaderCell: () => "Created",
      renderCell: (ct) => <Text>{formatDate(ct.createdDateTime)}</Text>,
    }),
  ];
}

// ─────────────────────────────────────────────────────────────────────────────
// ContainerTypesPage
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Props for ContainerTypesPage.
 *
 * onOpenDetail — called when a row is clicked.
 * The parent (App.tsx) will eventually wire this to open a ContainerTypeDetail
 * panel (SPE-062). For now the prop is optional; clicking a row stores the
 * selected type ID in state for the parent to observe.
 */
export interface ContainerTypesPageProps {
  /**
   * Called when the user clicks a row to open the detail panel (SPE-062).
   * If omitted the row click is a no-op (detail panel not yet wired).
   */
  onOpenDetail?: (containerTypeId: string) => void;
}

/**
 * ContainerTypesPage — primary container type management view.
 *
 * Uses `useBuContext()` to obtain the selected container type config.
 * When no config is selected, renders a prompt to select a BU/config first.
 */
export const ContainerTypesPage: React.FC<ContainerTypesPageProps> = ({
  onOpenDetail,
}) => {
  const styles = useStyles();
  const { selectedConfig } = useBuContext();

  // ── Data State ──────────────────────────────────────────────────────────────

  const [containerTypes, setContainerTypes] = React.useState<ContainerType[]>([]);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  /**
   * Set when the load failed because of an authorization prerequisite rather than a malfunction.
   * Titling those "Failed to load container types" sends an admin hunting for a bug when what they
   * actually need is a role grant (spec FR-B03).
   */
  const [errorNotice, setErrorNotice] = React.useState<PermissionPrerequisite | null>(null);

  // ── Action State ────────────────────────────────────────────────────────────

  /** ID of the selected container type (set on row click). */
  const [selectedTypeId, setSelectedTypeId] = React.useState<string | null>(null);

  /** Action status or error messages from toolbar actions. */
  const [actionError, setActionError] = React.useState<string | null>(null);
  const [actionStatus, setActionStatus] = React.useState<string | null>(null);

  // ── Create Dialog State ─────────────────────────────────────────────────────

  const [createOpen, setCreateOpen] = React.useState(false);
  const [createSaving, setCreateSaving] = React.useState(false);

  // ── Register Wizard State ───────────────────────────────────────────────────

  /** Whether the RegisterWizard is open. */
  const [registerOpen, setRegisterOpen] = React.useState(false);

  // ── Configurations Dialog State ─────────────────────────────────────────────

  /**
   * Whether the container-type CONFIGURATIONS dialog is open.
   *
   * A "config" (`sprk_specontainertypeconfig`) binds a container type to a business unit, an
   * environment, an owning app registration and its Key Vault secret name. It lived under
   * Settings until 2026-08-26, which put it two clicks from the container types it configures and
   * one click from Environments, which it is not.
   */
  const [configsOpen, setConfigsOpen] = React.useState(false);

  // ── Dismissed trial-expiry warnings ─────────────────────────────────────────

  /*
   * Container type IDs whose expiry banner the operator has dismissed (UAT round 8: "how do we
   * remove the warning message").
   *
   * Persisted, and keyed by container type ID rather than being a single global "hide warnings"
   * flag. Two reasons that distinction matters:
   *   - An expired trial is a real, permanent condition. There is no state in which it resolves
   *     itself, so a session-only dismiss would nag forever about something the operator has
   *     already decided to live with.
   *   - Keying by ID means a DIFFERENT type expiring later still raises its own banner. A global
   *     flag would silence that too — turning an acknowledgement of one dead trial into blanket
   *     deafness, which is how monitoring stops working.
   */
  const [dismissedExpiry, setDismissedExpiry] = React.useState<Set<string>>(() => {
    try {
      const raw = window.localStorage.getItem(DISMISSED_EXPIRY_KEY);
      return new Set(raw ? (JSON.parse(raw) as string[]) : []);
    } catch {
      return new Set();
    }
  });

  const dismissExpiryWarning = React.useCallback((ids: string[]) => {
    setDismissedExpiry((prev) => {
      const next = new Set(prev);
      ids.forEach((id) => next.add(id));
      try {
        window.localStorage.setItem(
          DISMISSED_EXPIRY_KEY,
          JSON.stringify([...next])
        );
      } catch {
        // A full or blocked localStorage must not break the page — the banner simply returns
        // on next load, which is the safe direction to fail for a warning.
      }
      return next;
    });
  }, []);

  // ── Column Definitions (stable reference) ──────────────────────────────────

  /*
   * One evaluation instant for the whole render, captured on mount. Calling `new Date()` inside
   * renderCell would make every row's expiry read at a slightly different moment and re-render the
   * grid needlessly; `assessTrialExpiry` takes `now` for exactly this reason.
   */
  const now = React.useMemo(() => new Date(), []);
  const columns = React.useMemo(() => buildColumns(now), [now]);

  /*
   * Container types whose billing Graph reports as INVALID (task 029 / spec FR-C12).
   *
   * Only a reported `invalid` qualifies — an unknown standing is surfaced per-row but must not raise
   * a page-level alarm, because "we could not read it" is not "it is broken".
   *
   * The remediation differs by classification (a standard type is repaired here, a passthrough type
   * is repaired in the consuming tenant), so distinct remediations are shown rather than merged into
   * one instruction that would be wrong for some of the rows.
   */
  const invalidBilling = React.useMemo(() => {
    const rows = containerTypes
      .map((ct) => ({ ct, billing: assessBilling(ct) }))
      .filter((r) => r.billing.needsAttention);

    const remediations = [
      ...new Set(
        rows
          .map((r) => r.billing.remediation)
          .filter((r): r is string => Boolean(r))
      ),
    ];

    return { rows, remediations };
  }, [containerTypes]);

  /**
   * Trial container types that have expired or are about to (spec FR-C13).
   *
   * A column alone is not enough here. An expired trial is not a per-row curiosity — it means those
   * containers have stopped working — and the live tenant sat with one expired for eleven months
   * without anything ever saying so. Surfaced at page level for the same reason invalid billing is.
   */
  const trialExpiry = React.useMemo(() => {
    const rows = containerTypes
      .map((ct) => ({ ct, expiry: assessTrialExpiry(ct, now) }))
      .filter((r) => r.expiry.needsAttention)
      // Dismissed types drop out of the PAGE-LEVEL banner only. Their per-row "Trial expired"
      // badge in the grid stays — dismissing an alert should quiet the alarm, not edit the record.
      .filter((r) => !dismissedExpiry.has(r.ct.containerTypeId));

    return {
      rows,
      // Expired outranks expiring: one is a live outage, the other is a deadline.
      hasExpired: rows.some((r) => r.expiry.state === "expired"),
    };
  }, [containerTypes, now, dismissedExpiry]);

  // ── Data Loading ────────────────────────────────────────────────────────────

  const loadContainerTypes = React.useCallback(async () => {
    if (!selectedConfig) return;
    setLoading(true);
    setError(null);
    setErrorNotice(null);
    setActionError(null);
    setActionStatus(null);
    setSelectedTypeId(null);
    try {
      const data = await speApiClient.containerTypes.list(selectedConfig.id);
      setContainerTypes(data.map(normalizeContainerType));
    } catch (err) {
      const message =
        describeApiError(err, "Failed to load container types. Please try again.");
      setError(message);
      // Null for ordinary failures — the banner then falls back to its "failed to load" heading.
      setErrorNotice(describePermissionPrerequisite(err));
    } finally {
      setLoading(false);
    }
  }, [selectedConfig]);

  // Auto-load when selectedConfig changes
  React.useEffect(() => {
    if (selectedConfig) {
      void loadContainerTypes();
    } else {
      setContainerTypes([]);
      setSelectedTypeId(null);
      setError(null);
    }
  }, [selectedConfig, loadContainerTypes]);

  // ── Row Click Handler (opens detail panel) ──────────────────────────────────

  const handleRowClick = React.useCallback(
    (typeId: string) => {
      setSelectedTypeId(typeId);
      // Wire to SPE-062 ContainerTypeDetail when implemented
      onOpenDetail?.(typeId);
    },
    [onOpenDetail]
  );

  // ── Create Container Type ───────────────────────────────────────────────────

  const handleCreateSubmit = React.useCallback(
    async (displayName: string, billingClassification: string) => {
      if (!selectedConfig) return;
      setCreateSaving(true);
      setActionError(null);
      setActionStatus(null);
      try {
        await speApiClient.containerTypes.create(selectedConfig.id, {
          displayName,
          billingClassification,
        });
        setCreateOpen(false);
        setActionStatus(`Container type "${displayName}" created successfully.`);
        await loadContainerTypes();
      } catch (err) {
        const message =
          describeApiError(err, "Failed to create container type.");
        setActionError(message);
      } finally {
        setCreateSaving(false);
      }
    },
    [selectedConfig, loadContainerTypes]
  );

  // ── Configurations Dialog (shared by both render branches) ──────────────────

  /*
   * Rendered in BOTH branches on purpose. `ContainerTypeConfig` is the only surface that can
   * CREATE a config, and the branch below runs precisely when none is selected — which on a fresh
   * environment means none exists. Reachable only from the configured branch, this would be a
   * bootstrap deadlock: you would need a config to reach the screen that makes the first config.
   */
  const configsDialog = (
    <Dialog
      open={configsOpen}
      onOpenChange={(_e, data) => setConfigsOpen(data.open)}
    >
      <DialogSurface className={styles.configsDialogSurface}>
        <DialogBody>
          <DialogTitle>Container Type Configurations</DialogTitle>
          <DialogContent className={styles.configsDialogContent}>
            <ContainerTypeConfig />
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={() => setConfigsOpen(false)}>
              Close
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );

  // ── Render: No Config Selected ──────────────────────────────────────────────

  if (!selectedConfig) {
    return (
      <div className={styles.root}>
        <div className={styles.header}>
          <Text as="h1" size={500} weight="semibold" className={styles.pageTitle}>
            Container Types
          </Text>
          <Text size={300} className={styles.pageSubtitle}>
            Manage SharePoint Embedded container type definitions
          </Text>
        </div>
        <div className={styles.noContextBanner}>
          <DocumentBulletList20Regular style={{ fontSize: "48px", opacity: 0.4 }} />
          <Text size={400} weight="semibold">
            No configuration selected
          </Text>
          <Text size={300} align="center">
            Select a Business Unit and Container Type Configuration in the top
            navigation bar to view and manage container types.
          </Text>
          {/* The escape hatch from an empty environment — see configsDialog above. */}
          <Button
            appearance="secondary"
            icon={<Settings20Regular />}
            onClick={() => setConfigsOpen(true)}
          >
            Manage configurations
          </Button>
        </div>
        {configsDialog}
      </div>
    );
  }

  // ── Render: Main View ───────────────────────────────────────────────────────

  const selectedType = containerTypes.find((ct) => ct.containerTypeId === selectedTypeId);
  const hasSelectedType = !!selectedType;
  /** Trial types cannot be registered on a consuming tenant — see canRegister(). */
  const registerAllowed = canRegister(selectedType);

  return (
    <div className={styles.root}>
      {/* ── Page Header ── */}
      <div className={styles.header}>
        <Text as="h1" size={500} weight="semibold" className={styles.pageTitle}>
          Container Types
        </Text>
        <Text size={300} className={styles.pageSubtitle}>
          {selectedConfig.name} &middot; {selectedConfig.environmentName}
          {containerTypes.length > 0 && (
            <>
              {" "}
              &middot; {containerTypes.length} type
              {containerTypes.length !== 1 ? "s" : ""}
            </>
          )}
        </Text>
      </div>

      {/* ── Command Toolbar ── */}
      <Toolbar aria-label="Container type actions" className={styles.toolbar}>
        {/* Create */}
        <Tooltip content="Create a new container type" relationship="description">
          <ToolbarButton
            icon={<Add20Regular />}
            onClick={() => setCreateOpen(true)}
            disabled={loading}
            aria-label="Create container type"
          >
            <span className={styles.buttonLabel}>New</span>
          </ToolbarButton>
        </Tooltip>

        <ToolbarDivider />

        {/* Register — offered only when it can actually succeed.
            Previously this was enabled with nothing selected (the wizard then opened with no type)
            and for trial types (which Microsoft does not allow to be registered on another tenant).
            Both were actions that could only fail. */}
        <Tooltip
          content={
            !hasSelectedType
              ? "Click a row to select a container type to register"
              : registerAllowed
                ? "Register selected container type on consuming tenant"
                : "Trial container types cannot be registered on another tenant — they only work in " +
                  "the tenant that created them. Create a Standard or Direct to Customer type to " +
                  "register it elsewhere."
          }
          relationship="description"
        >
          <ToolbarButton
            icon={<CloudLink20Regular />}
            onClick={() => setRegisterOpen(true)}
            disabled={loading || !hasSelectedType || !registerAllowed}
            aria-label="Register container type"
          >
            <span className={styles.buttonLabel}>Register</span>
          </ToolbarButton>
        </Tooltip>

        <ToolbarDivider />

        {/* Configurations — moved here from Settings (UAT 2026-08-26).
            A config binds THIS page's subject (a container type) to a business unit, an
            environment and an owning app registration, so it belongs beside the types rather
            than under a Settings tab shared with Environments. */}
        <Tooltip
          content="Add, edit or delete the container type configurations that bind a container type to a business unit, environment and owning app"
          relationship="description"
        >
          <ToolbarButton
            icon={<Settings20Regular />}
            onClick={() => setConfigsOpen(true)}
            aria-label="Manage container type configurations"
          >
            <span className={styles.buttonLabel}>Configurations</span>
          </ToolbarButton>
        </Tooltip>

        <ToolbarDivider />

        {/* Refresh */}
        <Tooltip content="Refresh container type list" relationship="description">
          <ToolbarButton
            icon={
              loading ? <Spinner size="tiny" /> : <ArrowClockwise20Regular />
            }
            onClick={() => { void loadContainerTypes(); }}
            disabled={loading}
            aria-label="Refresh container types"
          >
            <span className={styles.buttonLabel}>Refresh</span>
          </ToolbarButton>
        </Tooltip>
      </Toolbar>

      {/* ── Status / Error Banners ── */}
      {(actionError || actionStatus) && (
        <div className={styles.messageBarWrapper}>
          {actionError && (
            <MessageBar intent="error">
              <MessageBarBody>
                <MessageBarTitle>Action failed</MessageBarTitle>
                {actionError}
              </MessageBarBody>
              <MessageBarActions
                containerAction={
                  <Button
                    appearance="transparent"
                    size="small"
                    onClick={() => setActionError(null)}
                    aria-label="Dismiss error"
                  >
                    Dismiss
                  </Button>
                }
              />
            </MessageBar>
          )}
          {actionStatus && !actionError && (
            <MessageBar intent="success">
              <MessageBarBody>{actionStatus}</MessageBarBody>
              <MessageBarActions
                containerAction={
                  <Button
                    appearance="transparent"
                    size="small"
                    onClick={() => setActionStatus(null)}
                    aria-label="Dismiss"
                  >
                    Dismiss
                  </Button>
                }
              />
            </MessageBar>
          )}
        </div>
      )}

      {/*
        ── Invalid-billing warning (task 029 / spec FR-C12) ──
        Not a bare red badge: it names the affected types, what invalid billing means operationally,
        and where it is remediated — including that SPE Admin is deliberately not the place. Routing
        beats stonewalling. Rendered only for a REPORTED invalid; an unknown standing shows in its
        row's badge and raises nothing here.
      */}
      {invalidBilling.rows.length > 0 && (
        <div className={styles.messageBarWrapper}>
          <MessageBar intent="warning">
            <MessageBarBody>
              <MessageBarTitle>
                {invalidBilling.rows.length === 1
                  ? "Billing is invalid for 1 container type"
                  : `Billing is invalid for ${invalidBilling.rows.length} container types`}
              </MessageBarTitle>
              <div className={styles.billingWarningBody}>
                <Text size={200}>
                  {invalidBilling.rows
                    .map((r) => r.ct.displayName || r.ct.containerTypeId)
                    .join(", ")}
                </Text>
                {invalidBilling.rows.map((r) => (
                  <Text key={`c-${r.ct.containerTypeId}`} size={200}>
                    {r.ct.displayName || r.ct.containerTypeId}: {r.billing.consequence}
                  </Text>
                ))}
                {invalidBilling.remediations.map((text) => (
                  <Text key={text} size={200} weight="semibold">
                    {text}
                  </Text>
                ))}
              </div>
            </MessageBarBody>
          </MessageBar>
        </div>
      )}

      {/*
        ── Trial expiry warning (spec FR-C13) ──
        A trial container type is valid for 30 days, is NOT renewable, and stops working afterwards.
        The live tenant's trial expired 2025-10-10 and this app said nothing anywhere for eleven
        months. `error` intent when one has already expired — that is a live outage, not a deadline.
      */}
      {trialExpiry.rows.length > 0 && (
        <div className={styles.messageBarWrapper}>
          <MessageBar intent={trialExpiry.hasExpired ? "error" : "warning"}>
            <MessageBarBody>
              <MessageBarTitle>
                {trialExpiry.hasExpired
                  ? "A trial container type has expired"
                  : "A trial container type is expiring"}
              </MessageBarTitle>
              <div className={styles.billingWarningBody}>
                {trialExpiry.rows.map((r) => (
                  <Text key={`t-${r.ct.containerTypeId}`} size={200}>
                    {r.ct.displayName || r.ct.containerTypeId}: {r.expiry.consequence}
                  </Text>
                ))}
              </div>
            </MessageBarBody>
            {/* Dismiss persists per container type — see `dismissedExpiry`. The row badge stays. */}
            <MessageBarActions
              containerAction={
                <Tooltip
                  content="Hide this warning. It stays hidden for these container types; a different type expiring will still raise its own warning."
                  relationship="description"
                >
                  <Button
                    appearance="transparent"
                    size="small"
                    onClick={() =>
                      dismissExpiryWarning(
                        trialExpiry.rows.map((r) => r.ct.containerTypeId)
                      )
                    }
                    aria-label="Dismiss trial expiry warning"
                  >
                    Dismiss
                  </Button>
                </Tooltip>
              }
            />
          </MessageBar>
        </div>
      )}

      {/* ── Content: Loading / Error / Grid ── */}
      <div className={styles.content}>
        {loading && containerTypes.length === 0 ? (
          <div className={styles.feedback}>
            <Spinner size="medium" label="Loading container types…" />
          </div>
        ) : error ? (
          <div className={styles.feedback}>
            {/* An authorization prerequisite is not a malfunction — title it for what it is, and let
                the BFF's detail (which names the layer and what grants it) carry the explanation. */}
            <MessageBar intent={errorNotice?.intent ?? "error"}>
              <MessageBarBody>
                <MessageBarTitle>
                  {errorNotice?.title ?? "Failed to load container types"}
                </MessageBarTitle>
                {error}
              </MessageBarBody>
            </MessageBar>
            <Button
              appearance="secondary"
              icon={<ArrowClockwise20Regular />}
              onClick={() => { void loadContainerTypes(); }}
            >
              Retry
            </Button>
          </div>
        ) : containerTypes.length === 0 ? (
          <div className={styles.feedback}>
            <DocumentBulletList20Regular style={{ fontSize: "48px", opacity: 0.4 }} />
            <Text size={400} weight="semibold">
              No container types
            </Text>
            <Text size={300}>
              No container types found for this configuration. Use the{" "}
              <strong>New</strong> button to create one.
            </Text>
            {/* The request SUCCEEDED and returned nothing, so this is scoping, not denial. Graph
                returns the container types this caller can see; the Entra role widens that to the
                whole tenant. Saying so here is what stops an admin reading an empty list as either
                "nothing exists" or "I was blocked" (spec FR-B03 / §4.2b). */}
            <Text size={200} className={styles.scopeNote}>
              This list shows the container types your account can see. Tenant-wide visibility
              requires the <strong>SharePoint Embedded Administrator</strong> or{" "}
              <strong>Global Administrator</strong> role in Microsoft Entra — a Microsoft permission,
              separate from your Spaarke administrator permission.
            </Text>
          </div>
        ) : (
          <ContainerTypeDataGrid
            containerTypes={containerTypes}
            columns={columns}
            selectedTypeId={selectedTypeId}
            onRowClick={handleRowClick}
            className={styles.dataGrid}
          />
        )}
      </div>

      {/* ── Create Container Type Dialog ── */}
      {/* The loaded list feeds the quota assessment. It is a lower bound on the tenant's true count,
          not a census — the dialog is careful about what it will assert from it. */}
      <CreateContainerTypeDialog
        open={createOpen}
        isSaving={createSaving}
        existingContainerTypes={containerTypes}
        onClose={() => setCreateOpen(false)}
        onSubmit={(name, billing) => { void handleCreateSubmit(name, billing); }}
      />

      {/* ── Register Wizard ── */}
      <RegisterWizard
        open={registerOpen}
        onClose={() => setRegisterOpen(false)}
        onRegistered={() => { void loadContainerTypes(); }}
        initialTypeId={selectedTypeId}
      />

      {/* ── Container Type Configurations (moved from Settings) ── */}
      {configsDialog}
    </div>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// ContainerTypeDataGrid (inner component)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Inner component that renders the Fluent v9 DataGrid for container types.
 *
 * Row interaction model:
 *   - Clicking any row selects that type (single-select — for the Register action).
 *   - The selected row is highlighted with the "brand" appearance.
 *   - Row click also calls onRowClick to open the ContainerTypeDetail panel (SPE-062).
 */
interface ContainerTypeDataGridProps {
  containerTypes: ContainerType[];
  columns: TableColumnDefinition<ContainerType>[];
  selectedTypeId: string | null;
  onRowClick: (typeId: string) => void;
  className?: string;
}

const ContainerTypeDataGrid: React.FC<ContainerTypeDataGridProps> = ({
  containerTypes,
  columns,
  selectedTypeId,
  onRowClick,
  className,
}) => {
  const grid = useGridStyles();
  return (
    <DataGrid
      items={containerTypes}
      columns={columns}
      sortable={false}
      selectionMode="single"
      selectedItems={selectedTypeId ? new Set([selectedTypeId]) : new Set()}
      getRowId={(ct) => ct.containerTypeId}
      className={className}
      aria-label="Container types"
      /* Drag a header edge to resize (UAT round 6). The Owning App column holds a GUID and the
         Name column a free-text label — no single default fits both across tenants. */
      resizableColumns
      columnSizingOptions={COLUMN_SIZING}
    >
      <DataGridHeader>
        <DataGridRow>
          {({ renderHeaderCell }) => (
            <DataGridHeaderCell className={grid.headerCell}>
              {renderHeaderCell()}
            </DataGridHeaderCell>
          )}
        </DataGridRow>
      </DataGridHeader>
      <DataGridBody<ContainerType>>
        {({ item, rowId }) => {
          const isSelected = item.containerTypeId === selectedTypeId;
          return (
            <DataGridRow<ContainerType>
              key={rowId}
              aria-selected={isSelected}
              appearance={isSelected ? "brand" : "none"}
              style={{ cursor: "pointer" }}
              onClick={() => onRowClick(item.containerTypeId)}
              onKeyDown={(e: React.KeyboardEvent) => {
                if (e.key === "Enter" || e.key === " ") {
                  e.preventDefault();
                  onRowClick(item.containerTypeId);
                }
              }}
              tabIndex={0}
            >
              {({ renderCell }) => (
                <DataGridCell className={grid.cell}>{renderCell(item)}</DataGridCell>
              )}
            </DataGridRow>
          );
        }}
      </DataGridBody>
    </DataGrid>
  );
};
