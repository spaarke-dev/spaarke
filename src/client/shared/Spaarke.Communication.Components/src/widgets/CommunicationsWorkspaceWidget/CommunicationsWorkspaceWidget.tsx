/**
 * CommunicationsWorkspaceWidget — rich Pattern D communications widget (messaging-communication-app-r2 FR-12).
 *
 * Upgrades the thin `communications-list` widget (previously a bare
 * `<DataGrid>` mounted via `DataverseEntityViewWidget`, no cards, no filter
 * chips, no chrome — see `DataverseEntityViewWidget.tsx`) to a first-class
 * dual-use widget matching the shape of the shipped Calendar widget:
 *
 *   filter-chip toolbar (Channel + Sent-date-range popovers)
 *     + card strip (per-channel count cards; click toggles the channel filter)
 *     + embedded `<DataGrid configId hostFilters onRecordsLoaded>`
 *     + row-click modal (via the DataGrid framework's OWN `defaultRecordOpen` —
 *       intentionally NOT overridden; see "Row-click" note below).
 *
 * Copied in SHAPE from
 * `Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx`:
 *   - Popover filter-chip toolbar (Calendar's Event Type / Event Status / Date
 *     Range chips → here: Channel + Sent Date Range).
 *   - Applied-filter state feeds `<DataGrid hostFilters={…}/>` via
 *     `overlayHostFilters` (task 033a), exactly as Calendar does.
 *   - A strip below the toolbar that derives an aggregate from
 *     `onRecordsLoaded` (Calendar: per-date event counts feeding calendar dot
 *     indicators; here: per-channel counts feeding clickable summary cards —
 *     the "card strip" required by task 030). Clicking a card toggles that
 *     channel into/out of the filter, mirroring Calendar's day-click-sets-filter
 *     interaction.
 *
 * Row-click (Layout 1 modal): deliberately does NOT pass a custom
 * `onRecordOpen` prop. Per
 * `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` §6.6 ("Row-click behavior for
 * entity-list widgets"), any DataGrid-backed widget gets Layout 1 row-open
 * (85% × 85% `Xrm.Navigation.navigateTo` modal) for free via the framework's
 * `defaultRecordOpen` — the Communications widget is cited there as the
 * canonical "no per-widget wiring required" example. Overriding it here would
 * violate the task constraint "do NOT re-implement modal mechanics"
 * (`docs/standards/MODAL-DECISION-CRITERIA.md`).
 *
 * Facets: channel (`sprk_communicationtype`, Choice) and sent-date
 * (`sprk_sentat`, DateTime) are the two facets the DataGrid framework
 * auto-derives as column-header filter chips today (per
 * `projects/messaging-communication-app-r2/notes/041-grid-curation.md` §1 —
 * `chipDiscovery.ts` silently skips Lookup chips in auto/allowlist/denylist
 * mode; person/regarding chips require an explicit `filterChips.explicit`
 * config block with a `valueSource`, authored by task 041, live-apply
 * deferred). This widget's OWN toolbar covers channel + date (the two facets
 * it CAN filter without depending on that live config edit); regarding/person
 * filtering is available via the DataGrid's per-column "Filter by" chevron
 * once task 041's config change is applied, per that note's finding: "the
 * widget consumes whatever the config declares."
 *
 * Config: reuses the SAME `sprk_gridconfiguration` GUID as the prior thin
 * widget (`e1826c4c-9575-f111-ab0e-7ced8ddc4a05`, "Active Communications
 * (Workspace)") — NFR-05, no second default.
 *
 * Sizing: follows the two mandatory chains from
 * `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` §7.1 (width — ResizeObserver
 * pixel-cap wrapper, mirrors `DataverseEntityViewWidget.tsx`) and §7.2
 * (height — `height: '100%'` root + `flex: '1 1 auto'` / `minHeight: 0` on
 * every intermediate wrapper, mirrors `CalendarWorkspaceWidget.tsx`).
 *
 * CLAUDE.md §10 (BFF Hygiene): ZERO BFF endpoints. All Dataverse access flows
 * through the DataGrid's `XrmDataverseClient` (Xrm.WebApi per ADR-028).
 *
 * ADR-021 (Fluent v9 tokens only — dark mode passes through the host
 * `FluentProvider`), ADR-012 (shared component library — context-agnostic,
 * no LegalWorkspace coupling), ADR-022 (React 19 functional component).
 *
 * @see src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx
 * @see src/client/shared/Spaarke.UI.Components/src/components/DataGrid/fetchXmlOverlay.ts (overlayHostFilters)
 * @see src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx (Pattern D reference)
 * @see src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/DataverseEntityViewWidget.tsx (the widget this REPLACES for `communications-list`)
 * @see projects/messaging-communication-app-r2/notes/041-grid-curation.md (facet/chip derivation findings)
 * @see docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md §4 (Pattern D), §6.6 (row-click), §7.1/§7.2 (sizing)
 */

import * as React from 'react';
import {
  makeStyles,
  mergeClasses,
  tokens,
  shorthands,
  Text,
  Button,
  Tooltip,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  Checkbox,
  Input,
  Card,
  Badge,
} from '@fluentui/react-components';
import { ChevronDown16Regular, MailRegular, PeopleChatRegular, PhoneRegular, AlertRegular, ChatRegular } from '@fluentui/react-icons';
import type { FluentIcon } from '@fluentui/react-icons';

// Package import from `@spaarke/ui-components` — mirrors `DataverseEntityViewWidget.tsx`
// (the widget this file replaces for `communications-list`), NOT the deep
// relative SOURCE-path style used by `CalendarWorkspaceWidget.tsx`. This
// package resolves `@spaarke/ui-components` to its pre-built `dist/index.d.ts`
// (see tsconfig.json `paths` + `skipLibCheck: true`) so this package's own
// `tsc --noEmit` check never pulls the PCF-framework-dependent surface
// (`UniversalDatasetGrid`, etc.) in as raw source — `dist/*.d.ts` is a
// self-contained declaration, and `skipLibCheck` ignores any internal issues
// in it either way. The two consuming Vite apps (LegalWorkspace/SpaarkeAi)
// alias `@spaarke/ui-components` to SOURCE for their own builds — the same
// arrangement `DataverseEntityViewWidget.tsx` already runs under in production.
import { DataGrid, XrmDataverseClient, type HostFilterCondition } from '@spaarke/ui-components';

// ─────────────────────────────────────────────────────────────────────────────
// Configuration — REUSE the shipped "Active Communications (Workspace)" grid
// config (ai-spaarke-ai-workspace-UI-r2 task 010). NFR-05: single default,
// no second `sprk_gridconfiguration` record is created by this task.
// ─────────────────────────────────────────────────────────────────────────────

const COMMUNICATIONS_CONFIG_ID = 'e1826c4c-9575-f111-ab0e-7ced8ddc4a05';

// ─────────────────────────────────────────────────────────────────────────────
// Field names (sprk_communication) — verified against
// `Services/Communication/Models/CommunicationType.cs` +
// `notes/041-grid-curation.md` §2.
// ─────────────────────────────────────────────────────────────────────────────

const CHANNEL_FIELD = 'sprk_communicationtype';
const SENT_DATE_FIELD = 'sprk_sentat';

/** Mirrors `Sprk.Bff.Api.Services.Communication.Models.CommunicationType`. */
export const CommunicationChannel = {
  Email: 100000000,
  TeamsMessage: 100000001,
  SMS: 100000002,
  Notification: 100000003,
  Message: 100000004,
} as const;

export interface IChannelOption {
  value: number;
  label: string;
  icon: FluentIcon;
}

export const CHANNEL_OPTIONS: ReadonlyArray<IChannelOption> = [
  { value: CommunicationChannel.Email, label: 'Email', icon: MailRegular },
  { value: CommunicationChannel.TeamsMessage, label: 'Teams', icon: PeopleChatRegular },
  { value: CommunicationChannel.SMS, label: 'SMS', icon: PhoneRegular },
  { value: CommunicationChannel.Notification, label: 'Notification', icon: AlertRegular },
  { value: CommunicationChannel.Message, label: 'Message', icon: ChatRegular },
];

// ─────────────────────────────────────────────────────────────────────────────
// Pure helpers — exported for unit testing (no React/DOM dependency).
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Aggregate per-channel record counts from the DataGrid's `onRecordsLoaded`
 * payload. Mirrors `CalendarWorkspaceWidget.handleRecordsLoaded`'s per-date
 * aggregation shape, adapted to a single Choice field.
 */
export function aggregateChannelCounts(records: ReadonlyArray<Record<string, unknown>>): Map<number, number> {
  const counts = new Map<number, number>();
  for (const r of records) {
    const raw = r[CHANNEL_FIELD];
    const value = typeof raw === 'number' ? raw : Number(raw);
    if (!Number.isFinite(value)) continue;
    counts.set(value, (counts.get(value) ?? 0) + 1);
  }
  return counts;
}

/**
 * Build the `HostFilterCondition[]` for the DataGrid's `hostFilters` prop
 * from the widget's applied channel + date-range state. Mirrors
 * `CalendarWorkspaceWidget`'s `hostFilters` useMemo logic (single-day → `on`,
 * range → `between`, one-sided → `on-or-after`/`on-or-before`).
 */
export function buildCommunicationsHostFilters(
  selectedChannels: ReadonlySet<number>,
  fromDate: string,
  toDate: string
): HostFilterCondition[] {
  const conditions: HostFilterCondition[] = [];

  if (selectedChannels.size > 0) {
    conditions.push({
      attribute: CHANNEL_FIELD,
      operator: 'in',
      value: Array.from(selectedChannels),
    });
  }

  if (fromDate && toDate) {
    if (fromDate === toDate) {
      conditions.push({ attribute: SENT_DATE_FIELD, operator: 'on', value: fromDate });
    } else {
      conditions.push({ attribute: SENT_DATE_FIELD, operator: 'between', value: [fromDate, toDate] });
    }
  } else if (fromDate) {
    conditions.push({ attribute: SENT_DATE_FIELD, operator: 'on-or-after', value: fromDate });
  } else if (toDate) {
    conditions.push({ attribute: SENT_DATE_FIELD, operator: 'on-or-before', value: toDate });
  }

  return conditions;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles — `makeStyles` at module scope (ADR-021: Fluent v9 semantic tokens
// only; no hardcoded colors; dark mode passes through the host FluentProvider).
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  // Height chain (BUILD-A-NEW-WORKSPACE-WIDGET.md §7.2): anchor to parent
  // height; every intermediate wrapper below is `display: flex`.
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    width: '100%',
    minWidth: 0,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    boxSizing: 'border-box',
    ...shorthands.padding('8px'),
    ...shorthands.gap('8px'),
  },
  toolbarRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    flexWrap: 'wrap',
    rowGap: tokens.spacingVerticalS,
    columnGap: tokens.spacingHorizontalS,
    flexShrink: 0,
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalM),
    ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke2),
  },
  filterChipButton: {
    minWidth: 'auto',
    fontWeight: tokens.fontWeightRegular,
  },
  filterChipButtonActive: {
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorBrandForeground2,
    fontWeight: tokens.fontWeightSemibold,
  },
  filterPopoverSurface: {
    minWidth: '220px',
    padding: tokens.spacingHorizontalM,
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalS,
  },
  filterPopoverLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    fontWeight: tokens.fontWeightSemibold,
  },
  quickSelectGrid: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: tokens.spacingHorizontalS,
  },
  filterActions: {
    display: 'flex',
    alignItems: 'flex-end',
    flexShrink: 0,
    marginLeft: 'auto',
    ...shorthands.gap('8px'),
  },
  // Card strip — the required "card strip" element (task 030). Per-channel
  // count cards; clicking toggles that channel in/out of the selected set.
  cardStrip: {
    display: 'flex',
    flexDirection: 'row',
    flexWrap: 'wrap',
    flexShrink: 0,
    ...shorthands.gap('8px'),
  },
  channelCard: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    ...shorthands.gap('8px'),
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    cursor: 'pointer',
    minWidth: '120px',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground2Hover,
    },
  },
  channelCardActive: {
    ...shorthands.border('1px', 'solid', tokens.colorBrandStroke1),
    backgroundColor: tokens.colorBrandBackground2,
  },
  channelCardLabel: {
    display: 'flex',
    flexDirection: 'column',
  },
  gridContainer: {
    display: 'flex',
    flexDirection: 'column',
    flex: '1 1 auto',
    minHeight: 0,
    overflow: 'hidden',
    marginTop: tokens.spacingVerticalXS,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface CommunicationsWorkspaceWidgetProps {
  /** Optional override for the reused grid configuration GUID (defaults to the shipped "Active Communications" config). */
  configId?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const CommunicationsWorkspaceWidget: React.FC<CommunicationsWorkspaceWidgetProps> = ({
  configId = COMMUNICATIONS_CONFIG_ID,
}) => {
  const styles = useStyles();

  // ── Channel selection (applied immediately — discrete checkbox/card clicks,
  //    no pending/applied gate needed, unlike the date range below). ────────
  const [selectedChannels, setSelectedChannels] = React.useState<ReadonlySet<number>>(() => new Set());

  const toggleChannel = React.useCallback((value: number) => {
    setSelectedChannels(prev => {
      const next = new Set(prev);
      if (next.has(value)) {
        next.delete(value);
      } else {
        next.add(value);
      }
      return next;
    });
  }, []);

  // ── Sent-date-range — pending/applied gate (mirrors Calendar's Date Range
  //    chip) so typing into the From/To inputs doesn't refetch per keystroke. ─
  const [pendingFrom, setPendingFrom] = React.useState('');
  const [pendingTo, setPendingTo] = React.useState('');
  const [appliedFrom, setAppliedFrom] = React.useState('');
  const [appliedTo, setAppliedTo] = React.useState('');

  const dateRangeDisplay = React.useMemo(() => {
    if (appliedFrom && appliedTo) {
      return appliedFrom === appliedTo ? appliedFrom : `${appliedFrom} → ${appliedTo}`;
    }
    if (appliedFrom) return `from ${appliedFrom}`;
    if (appliedTo) return `to ${appliedTo}`;
    return null;
  }, [appliedFrom, appliedTo]);

  const applyDateRange = React.useCallback(() => {
    setAppliedFrom(pendingFrom);
    setAppliedTo(pendingTo);
  }, [pendingFrom, pendingTo]);

  const clearDateRange = React.useCallback(() => {
    setPendingFrom('');
    setPendingTo('');
    setAppliedFrom('');
    setAppliedTo('');
  }, []);

  const applyPreset = React.useCallback((preset: 'last7' | 'last30' | 'last90' | 'thisMonth') => {
    const today = new Date();
    const toIso = (d: Date) =>
      `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    let from: Date;
    const to = today;
    switch (preset) {
      case 'last7':
        from = new Date(today);
        from.setDate(from.getDate() - 7);
        break;
      case 'last30':
        from = new Date(today);
        from.setDate(from.getDate() - 30);
        break;
      case 'last90':
        from = new Date(today);
        from.setDate(from.getDate() - 90);
        break;
      case 'thisMonth':
        from = new Date(today.getFullYear(), today.getMonth(), 1);
        break;
    }
    const fromIso = toIso(from);
    const toIsoStr = toIso(to);
    setPendingFrom(fromIso);
    setPendingTo(toIsoStr);
    setAppliedFrom(fromIso);
    setAppliedTo(toIsoStr);
  }, []);

  const hasAnyApplied = selectedChannels.size > 0 || Boolean(appliedFrom) || Boolean(appliedTo);
  const clearAll = React.useCallback(() => {
    setSelectedChannels(new Set());
    clearDateRange();
  }, [clearDateRange]);

  // ── hostFilters — composed from applied state; memoized for referential
  //    stability (the framework re-runs its FetchXML composition pipeline
  //    only when this array's identity changes). ───────────────────────────
  const hostFilters = React.useMemo<HostFilterCondition[]>(
    () => buildCommunicationsHostFilters(selectedChannels, appliedFrom, appliedTo),
    [selectedChannels, appliedFrom, appliedTo]
  );

  // ── Card strip — per-channel counts derived from onRecordsLoaded. ────────
  const [channelCounts, setChannelCounts] = React.useState<Map<number, number>>(() => new Map());
  const handleRecordsLoaded = React.useCallback((records: ReadonlyArray<Record<string, unknown>>) => {
    setChannelCounts(aggregateChannelCounts(records));
  }, []);

  // ── DataGrid wiring — stable XrmDataverseClient instance for the widget's
  //    lifetime. NOTE: no `onRecordOpen` is passed — the DataGrid framework's
  //    own `defaultRecordOpen` (Layout 1, 85% × 85% `Xrm.Navigation.navigateTo`)
  //    handles row-click for free; see the module docblock "Row-click" note. ─
  const dataverseClientRef = React.useRef<XrmDataverseClient | null>(null);
  if (!dataverseClientRef.current) {
    dataverseClientRef.current = new XrmDataverseClient();
  }

  return (
    <div className={styles.root}>
      {/* (1) Filter-chip toolbar — Channel + Sent Date Range popovers. */}
      <div className={styles.toolbarRow}>
        {/* Channel chip */}
        <Popover trapFocus>
          <PopoverTrigger disableButtonEnhancement>
            <Button
              className={mergeClasses(
                styles.filterChipButton,
                selectedChannels.size > 0 ? styles.filterChipButtonActive : undefined
              )}
              appearance="subtle"
              size="small"
              iconPosition="after"
              icon={<ChevronDown16Regular />}
              aria-label="Channel filter"
            >
              {`Channel${selectedChannels.size > 0 ? `: ${selectedChannels.size}` : ''}`}
            </Button>
          </PopoverTrigger>
          <PopoverSurface className={styles.filterPopoverSurface}>
            <Text className={styles.filterPopoverLabel}>Channel</Text>
            {CHANNEL_OPTIONS.map(opt => (
              <Checkbox
                key={opt.value}
                label={opt.label}
                checked={selectedChannels.has(opt.value)}
                onChange={() => toggleChannel(opt.value)}
              />
            ))}
          </PopoverSurface>
        </Popover>

        {/* Sent Date Range chip */}
        <Popover trapFocus>
          <PopoverTrigger disableButtonEnhancement>
            <Button
              className={mergeClasses(styles.filterChipButton, dateRangeDisplay ? styles.filterChipButtonActive : undefined)}
              appearance="subtle"
              size="small"
              iconPosition="after"
              icon={<ChevronDown16Regular />}
              aria-label="Sent date range filter"
            >
              {`Sent${dateRangeDisplay ? `: ${dateRangeDisplay}` : ''}`}
            </Button>
          </PopoverTrigger>
          <PopoverSurface className={styles.filterPopoverSurface}>
            <Text className={styles.filterPopoverLabel}>From</Text>
            <Input type="date" value={pendingFrom} onChange={(_e, data) => setPendingFrom(data.value)} />
            <Text className={styles.filterPopoverLabel}>To</Text>
            <Input type="date" value={pendingTo} onChange={(_e, data) => setPendingTo(data.value)} />
            <Text className={styles.filterPopoverLabel}>Quick select</Text>
            <div className={styles.quickSelectGrid}>
              <Button size="small" appearance="subtle" onClick={() => applyPreset('last7')}>
                Last 7 days
              </Button>
              <Button size="small" appearance="subtle" onClick={() => applyPreset('last30')}>
                Last 30 days
              </Button>
              <Button size="small" appearance="subtle" onClick={() => applyPreset('last90')}>
                Last 90 days
              </Button>
              <Button size="small" appearance="subtle" onClick={() => applyPreset('thisMonth')}>
                This month
              </Button>
            </div>
            <div className={styles.filterActions}>
              <Button size="small" appearance="primary" onClick={applyDateRange} aria-label="Apply date range">
                Apply
              </Button>
              {dateRangeDisplay && (
                <Button size="small" appearance="subtle" onClick={clearDateRange} aria-label="Clear date range">
                  Clear
                </Button>
              )}
            </div>
          </PopoverSurface>
        </Popover>

        <div className={styles.filterActions}>
          {hasAnyApplied && (
            <Button appearance="subtle" size="small" onClick={clearAll} aria-label="Clear all filters">
              Clear all
            </Button>
          )}
        </div>
      </div>

      {/* (2) Card strip — per-channel count cards; click toggles that channel. */}
      <div className={styles.cardStrip}>
        {CHANNEL_OPTIONS.map(opt => {
          const Icon = opt.icon;
          const count = channelCounts.get(opt.value) ?? 0;
          const active = selectedChannels.has(opt.value);
          return (
            <Tooltip key={opt.value} content={`Filter to ${opt.label} only`} relationship="label">
              <Card
                className={mergeClasses(styles.channelCard, active ? styles.channelCardActive : undefined)}
                onClick={() => toggleChannel(opt.value)}
                role="button"
                aria-pressed={active}
                tabIndex={0}
                onKeyDown={e => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    toggleChannel(opt.value);
                  }
                }}
              >
                <Icon fontSize={20} />
                <div className={styles.channelCardLabel}>
                  <Text size={200} weight="semibold">
                    {opt.label}
                  </Text>
                  <Badge appearance="tint" size="small">
                    {count}
                  </Badge>
                </div>
              </Card>
            </Tooltip>
          );
        })}
      </div>

      {/* (3) Grid — <DataGrid configId hostFilters onRecordsLoaded>. No custom
              onRecordOpen: the framework's defaultRecordOpen already delivers
              Layout 1 (85% × 85%) row-click for this widget for free. */}
      <div className={styles.gridContainer}>
        <DataGrid
          configId={configId}
          dataverseClient={dataverseClientRef.current}
          hostFilters={hostFilters}
          onRecordsLoaded={handleRecordsLoaded}
        />
      </div>
    </div>
  );
};

CommunicationsWorkspaceWidget.displayName = 'CommunicationsWorkspaceWidget';

export default CommunicationsWorkspaceWidget;
