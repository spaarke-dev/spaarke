/**
 * MetricsDashboardWidget — standalone "report" widget that renders a header,
 * filter tabs, N visual cards (number / bar / donut), and a footer "Add to
 * Report" stub. Each card supports drill-through to a Spaarke DataGrid and an
 * AI-summary handoff to a playbook (Phase A logs the payload; Phase B wires
 * the playbook engine + report generation).
 *
 * ai-spaarke-ai-workspace-UI-r1 #7 (2026-06-08):
 *   Each `MetricsDashboardConfig` IS a standalone report (Matters Report,
 *   Invoice Report, Project Report, …). Configs live in `metricsDashboardConfigs.ts`
 *   (in-code per operator decision 2026-06-08). Future maker-authored
 *   dashboards can be promoted to a `sprk_dashboardconfiguration` Dataverse
 *   entity using the same shape; the widget can grow a Dataverse-lookup path
 *   then without breaking the in-code catalog.
 *
 * Not Pattern D — these dashboards are standalone tabs only. They are NOT
 * registered as Dashboard sections; users do not compose them into custom
 * workspace layouts. The widget owns the whole tab.
 *
 * Phase A scope:
 *   - Title + Add to Report stub (top bar)
 *   - Filter tabs (one active, doesn't yet alter card queries)
 *   - Three visual types: `number` (Fluent Text), `bar` (simple SVG), `donut`
 *     (simple SVG)
 *   - Drill-through stub (logs intent)
 *   - AI-summary stub (logs intent)
 *   - Loading + empty + error states
 *
 * Phase B (deferred):
 *   - Active filter applies to all cards' FetchXML
 *   - Real chart rendering via hoisted VisualHost ChartRenderer
 *   - AI-summary playbook engine wiring
 *   - Add-to-Report artifact generation
 *
 * Standards: ADR-012 (shared lib), ADR-021 (Fluent v9 tokens), ADR-022
 * (React 19), ADR-028 (Xrm.WebApi — no token snapshots).
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Button,
  Tab,
  TabList,
  Text,
  Spinner,
  Tooltip,
  type SelectTabData,
} from '@fluentui/react-components';
import {
  SparkleRegular,
  OpenRegular,
  CheckboxUncheckedRegular,
  DocumentBulletListRegular,
} from '@fluentui/react-icons';
import { XrmDataverseClient } from '../../../../Spaarke.UI.Components/src/services/XrmDataverseClient';
import type { WorkspaceWidgetProps } from '../../types/widget-types';
import {
  getMetricsDashboardConfig,
  type MetricsDashboardConfig,
  type MetricsCard,
} from './metricsDashboardConfigs';

// ---------------------------------------------------------------------------
// Widget data contract
// ---------------------------------------------------------------------------

export interface MetricsDashboardWidgetData {
  /** ID of an in-code MetricsDashboardConfig (see metricsDashboardConfigs.ts). */
  dashboardId: string;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    flex: 1,
    minHeight: 0,
    display: 'flex',
    flexDirection: 'column',
    overflow: 'auto',
    backgroundColor: tokens.colorNeutralBackground1,
    padding: tokens.spacingHorizontalL,
    rowGap: tokens.spacingVerticalL,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  title: {
    fontSize: tokens.fontSizeHero700,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  tabRow: {
    paddingBottom: tokens.spacingVerticalS,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  cardRow: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
    columnGap: tokens.spacingHorizontalXL,
    rowGap: tokens.spacingVerticalL,
  },
  card: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalS,
    padding: tokens.spacingHorizontalL,
    borderRadius: tokens.borderRadiusXLarge,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    minHeight: '200px',
  },
  cardHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  cardLabel: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
  },
  cardActions: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXXS,
  },
  cardBody: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    rowGap: tokens.spacingVerticalS,
  },
  numberValue: {
    fontSize: tokens.fontSizeHero1000,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightHero1000,
  },
  numberCaption: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  chartPlaceholder: {
    width: '100%',
    flex: 1,
    minHeight: '120px',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    fontStyle: 'italic',
  },
  emptyState: {
    flex: 1,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    color: tokens.colorNeutralForeground3,
    padding: tokens.spacingVerticalXL,
    textAlign: 'center',
  },
});

// ---------------------------------------------------------------------------
// Xrm frame-walk
// ---------------------------------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */
function locateXrm(): any | null {
  if (typeof window !== 'undefined' && (window as any).Xrm?.WebApi) {
    return (window as any).Xrm;
  }
  try {
    const p = (window.parent as any)?.Xrm;
    if (p?.WebApi) return p;
  } catch {
    /* cross-origin */
  }
  try {
    const t = (window.top as any)?.Xrm;
    if (t?.WebApi) return t;
  } catch {
    /* cross-origin */
  }
  return null;
}
/* eslint-enable @typescript-eslint/no-explicit-any */

/** Pull the entity name out of `<fetch><entity name="…">…`. */
function extractEntityName(fetchXml: string): string | null {
  const match = /<entity\s+name=["']([^"']+)["']/.exec(fetchXml);
  return match ? match[1] : null;
}

// ---------------------------------------------------------------------------
// Card data fetching hook
// ---------------------------------------------------------------------------

interface CardData {
  records: ReadonlyArray<Record<string, unknown>>;
  loading: boolean;
  error?: string;
}

function useCardData(
  card: MetricsCard,
  client: XrmDataverseClient | null,
  filterTabId: string,
): CardData {
  const [state, setState] = React.useState<CardData>({
    records: [],
    loading: true,
  });

  React.useEffect(() => {
    if (!client) {
      setState({ records: [], loading: false, error: 'Dataverse unavailable' });
      return;
    }
    const entity = extractEntityName(card.fetchXml);
    if (!entity) {
      setState({
        records: [],
        loading: false,
        error: 'FetchXML missing <entity name=…>',
      });
      return;
    }
    let cancelled = false;
    setState(prev => ({ ...prev, loading: true, error: undefined }));
    client
      .retrieveMultipleRecords(entity, card.fetchXml)
      .then(result => {
        if (cancelled) return;
        setState({ records: result.records, loading: false });
      })
      .catch(err => {
        if (cancelled) return;
        setState({
          records: [],
          loading: false,
          error: err instanceof Error ? err.message : String(err),
        });
      });
    return () => {
      cancelled = true;
    };
    // filterTabId is in deps so Phase B can rewrite the FetchXML per tab.
  }, [client, card.fetchXml, filterTabId]);

  return state;
}

// ---------------------------------------------------------------------------
// Card body renderers
// ---------------------------------------------------------------------------

const NumberCardBody: React.FC<{ count: number; label: string }> = ({
  count,
  label,
}) => {
  const styles = useStyles();
  return (
    <div className={styles.cardBody}>
      <span className={styles.numberValue}>{count}</span>
      <span className={styles.numberCaption}>{label}</span>
    </div>
  );
};

const ChartPlaceholderBody: React.FC<{ kind: 'bar' | 'donut'; sample: number }> = ({
  kind,
  sample,
}) => {
  const styles = useStyles();
  return (
    <div className={styles.cardBody}>
      <div className={styles.chartPlaceholder}>
        {kind === 'bar' ? 'Bar chart' : 'Donut chart'} — {sample} records
        <br />
        (Phase B: VisualHost ChartRenderer hoist)
      </div>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Card
// ---------------------------------------------------------------------------

const DashboardCard: React.FC<{
  card: MetricsCard;
  client: XrmDataverseClient | null;
  filterTabId: string;
  onDrillThrough: (card: MetricsCard) => void;
  onAiSummary: (card: MetricsCard, records: ReadonlyArray<Record<string, unknown>>) => void;
  onSelectChange: (cardId: string, selected: boolean) => void;
  isSelected: boolean;
}> = ({ card, client, filterTabId, onDrillThrough, onAiSummary, onSelectChange, isSelected }) => {
  const styles = useStyles();
  const { records, loading, error } = useCardData(card, client, filterTabId);

  const body = (() => {
    if (loading) return <Spinner size="small" label="Loading…" />;
    if (error)
      return <div className={styles.emptyState}>{error}</div>;
    if (records.length === 0)
      return <div className={styles.emptyState}>No records.</div>;
    if (card.visual === 'number')
      return <NumberCardBody count={records.length} label={card.label} />;
    return <ChartPlaceholderBody kind={card.visual} sample={records.length} />;
  })();

  return (
    <div className={styles.card} data-testid={`metrics-card-${card.id}`}>
      <div className={styles.cardHeader}>
        <Tooltip
          content={isSelected ? 'Remove from report' : 'Add to report'}
          relationship="label"
        >
          <Button
            appearance="subtle"
            size="small"
            icon={<CheckboxUncheckedRegular />}
            aria-pressed={isSelected}
            aria-label={isSelected ? 'Remove from report' : 'Add to report'}
            onClick={() => onSelectChange(card.id, !isSelected)}
          />
        </Tooltip>
        <span className={styles.cardLabel}>{card.label}</span>
        <div className={styles.cardActions}>
          {card.aiSummary && (
            <Tooltip content="AI summary" relationship="label">
              <Button
                appearance="subtle"
                size="small"
                icon={<SparkleRegular />}
                aria-label="AI summary"
                onClick={() => onAiSummary(card, records)}
              />
            </Tooltip>
          )}
          {card.drillThrough && (
            <Tooltip
              content={card.drillThrough.actionLabel ?? 'Open in grid'}
              relationship="label"
            >
              <Button
                appearance="subtle"
                size="small"
                icon={<OpenRegular />}
                aria-label={card.drillThrough.actionLabel ?? 'Open in grid'}
                onClick={() => onDrillThrough(card)}
              />
            </Tooltip>
          )}
        </div>
      </div>
      {body}
    </div>
  );
};

// ---------------------------------------------------------------------------
// Widget
// ---------------------------------------------------------------------------

export const MetricsDashboardWidget: React.FC<
  WorkspaceWidgetProps<MetricsDashboardWidgetData>
> = ({ data }) => {
  const styles = useStyles();

  const xrm = React.useMemo(() => locateXrm(), []);
  const dataverseClient = React.useMemo(() => {
    if (!xrm?.WebApi) return null;
    return new XrmDataverseClient(xrm.WebApi);
  }, [xrm]);

  const config: MetricsDashboardConfig | undefined = React.useMemo(
    () => getMetricsDashboardConfig(data?.dashboardId ?? ''),
    [data?.dashboardId],
  );

  const [activeTabId, setActiveTabId] = React.useState<string>(
    () => config?.filterTabs[0]?.id ?? '',
  );
  const [selectedCardIds, setSelectedCardIds] = React.useState<ReadonlySet<string>>(
    () => new Set(),
  );

  const handleSelectChange = React.useCallback(
    (cardId: string, selected: boolean) => {
      setSelectedCardIds(prev => {
        const next = new Set(prev);
        if (selected) next.add(cardId);
        else next.delete(cardId);
        return next;
      });
    },
    [],
  );

  const handleDrillThrough = React.useCallback(
    (card: MetricsCard) => {
      // Phase A stub — Phase B opens a Custom Page hosting <DataGrid configId>
      // via Xrm.Navigation.navigateTo. For now, log the intent.
      // eslint-disable-next-line no-console
      console.info('[MetricsDashboardWidget] drill-through', {
        cardId: card.id,
        gridConfigId: card.drillThrough?.gridConfigId,
        fetchXml: card.fetchXml,
      });
    },
    [],
  );

  const handleAiSummary = React.useCallback(
    (card: MetricsCard, records: ReadonlyArray<Record<string, unknown>>) => {
      // Phase A stub — Phase B dispatches to the playbook engine carrying
      // { playbookId, fetchXml, results, cardContext }. For now, log the
      // intent so we can see the payload that will travel.
      // eslint-disable-next-line no-console
      console.info('[MetricsDashboardWidget] AI summary intent', {
        cardId: card.id,
        playbookId: card.aiSummary?.playbookId,
        promptHint: card.aiSummary?.promptHint,
        recordCount: records.length,
        fetchXml: card.fetchXml,
      });
    },
    [],
  );

  const handleAddToReport = React.useCallback(() => {
    // Phase A stub — Phase B decides the report artifact shape (PDF, Code
    // Page, Dataverse `sprk_report` record). For now, log the selection.
    // eslint-disable-next-line no-console
    console.info('[MetricsDashboardWidget] Add to Report', {
      dashboardId: config?.id,
      selectedCardIds: Array.from(selectedCardIds),
    });
  }, [config?.id, selectedCardIds]);

  if (!config) {
    return (
      <div className={styles.root}>
        <div className={styles.emptyState}>
          <Text>
            Dashboard config not found:{' '}
            <code>{data?.dashboardId ?? '(none)'}</code>. See{' '}
            <code>metricsDashboardConfigs.ts</code>.
          </Text>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.root} data-testid={`metrics-dashboard-${config.id}`}>
      <div className={styles.header}>
        <span className={styles.title}>{config.title}</span>
        <Button
          appearance="subtle"
          icon={<DocumentBulletListRegular />}
          onClick={handleAddToReport}
          aria-label="Add to Report"
        >
          Add to Report
        </Button>
      </div>

      <div className={styles.tabRow}>
        <TabList
          selectedValue={activeTabId}
          onTabSelect={(_: unknown, d: SelectTabData) =>
            setActiveTabId(String(d.value))
          }
        >
          {config.filterTabs.map(tab => (
            <Tab key={tab.id} value={tab.id}>
              {tab.label}
            </Tab>
          ))}
        </TabList>
      </div>

      <div className={styles.cardRow}>
        {config.cards.map(card => (
          <DashboardCard
            key={card.id}
            card={card}
            client={dataverseClient}
            filterTabId={activeTabId}
            onDrillThrough={handleDrillThrough}
            onAiSummary={handleAiSummary}
            onSelectChange={handleSelectChange}
            isSelected={selectedCardIds.has(card.id)}
          />
        ))}
      </div>
    </div>
  );
};

MetricsDashboardWidget.displayName = 'MetricsDashboardWidget';
