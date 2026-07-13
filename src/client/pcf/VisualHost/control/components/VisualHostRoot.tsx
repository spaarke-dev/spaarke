/**
 * Visual Host Root Component
 * Main React component for the Visual Host PCF control
 */

import * as React from 'react';
import { useState, useEffect, useCallback } from 'react';
import {
  Spinner,
  MessageBar,
  MessageBarBody,
  Button,
  Tooltip,
  makeStyles,
  tokens,
  Text,
  Toast,
  ToastTitle,
  Toaster,
  useToastController,
  useId,
} from '@fluentui/react-components';
import { AddRegular, OpenRegular, SparkleRegular } from '@fluentui/react-icons';
import {
  AiSummaryPopover as RawAiSummaryPopover,
  type IAiSummaryPopoverProps,
  type ISummaryData,
} from '../../../../shared/Spaarke.UI.Components/src/components/AiSummaryPopover';

// React 18/19 types-version drift workaround: see CardChrome.tsx for rationale.
const AiSummaryPopover = RawAiSummaryPopover as unknown as React.ComponentType<IAiSummaryPopoverProps>;
import { AppInsightsService } from '../../../../shared/Spaarke.UI.Components/src/services/AppInsightsService';
import { IInputs } from '../generated/ManifestTypes';
import { IChartDefinition, IChartData, DrillInteraction } from '../types';
import { ChartRenderer } from './ChartRenderer';
import { CardChrome } from './CardChrome';
import type { MatrixJustification } from '../../../../shared/Spaarke.Visuals/src/components/MetricCardMatrix';
import { logger } from '../utils/logger';
import {
  loadChartDefinition as loadChartDefinitionFromDataverse,
  ConfigurationNotFoundError,
  ConfigurationLoadError,
} from '../services/ConfigurationLoader';
import { fetchAndAggregate, AggregationError } from '../services/DataAggregationService';
import { parseFieldPivotConfig, fetchAndPivot } from '../services/FieldPivotService';
import { executeClickAction, hasClickAction } from '../services/ClickActionHandler';
import { getEffectiveDarkMode } from '../providers/ThemeProvider';

// ---------------------------------------------------------------------------
// "+" Create Wizard button — navigateTo cutover (VHVU-030)
// ---------------------------------------------------------------------------
// The "+" launches the wizard Code Pages (Create Event / Invoice / Report
// Card) via `Xrm.Navigation.navigateTo` as a webresource dialog — the SAME
// pattern `src/client/webresources/js/sprk_wizard_commands.js` uses for every
// other ribbon-launched Create wizard. This REPLACES the previous inline
// `React.lazy` embedding, which dragged the shared-lib wizard components (and
// their `@spaarke/auth` / `@spaarke/sdap-client` transitive deps + the React
// 18/19 types skew) into the PCF bundle. The Code Page owns auth (ADR-028);
// the PCF only marshals the launch envelope.
//
// `resolveRecordDisplayNameFieldName` + `cleanGuid` are the ONLY shared-lib
// imports the create flow still needs — both from `PolymorphicResolverService`,
// which is presentational/config resolution (no auth, no MSAL; already imported
// here pre-cutover). `cleanGuid` normalizes the Xrm-sourced GUID before it
// enters the navigate envelope (ADR-044) — never hand-roll `.replace(/[{}]/g,'')`.
import {
  resolveRecordDisplayNameFieldName,
  cleanGuid,
} from '../../../../shared/Spaarke.UI.Components/src/services/PolymorphicResolverService';

// Maps the chart-def wizard key (or entity fallback) to the wizard Code Page
// web-resource name opened via navigateTo. Kept LOCAL to Visual Host — NOT
// imported from the shared `wizardRegistry` — so the cutover does not drag the
// lazy-loaded wizard components (and their auth/sdap-client deps) back into the
// bundle. Mirrors `resolveWizard`'s resolution order + `ENTITY_TO_WIZARD_KEY`
// aliases (single source of truth is small enough to inline; see FR-03).
const WIZARD_KEY_TO_PAGE: Readonly<Record<string, string>> = {
  event: 'sprk_createeventwizard',
  invoice: 'sprk_createinvoicewizard',
  'report-card': 'sprk_createreportcardwizard',
};

const ENTITY_TO_WIZARD_KEY: Readonly<Record<string, string>> = {
  sprk_event: 'event',
  sprk_invoice: 'invoice',
  sprk_reportcard: 'report-card',
  sprk_kpiassessment: 'report-card',
};

const SPRK_PREFIX = 'sprk_';

/**
 * Resolves the wizard Code Page web-resource name for the "+" button.
 * Resolution order (parity with shared `resolveWizard`):
 *   1. `createWizardKey` (`sprk_createwizardkey`) verbatim, if non-empty.
 *   2. else `entityLogicalName` (`sprk_entitylogicalname`), normalized via the
 *      alias map, else `sprk_`-prefix strip.
 * Returns `null` when nothing maps → caller shows a toast (FR-03).
 */
function resolveWizardPage(
  createWizardKey: string | null | undefined,
  entityLogicalName: string | null | undefined
): string | null {
  const trimmedKey = createWizardKey?.trim();
  let resolvedKey = trimmedKey ?? '';
  if (!resolvedKey && entityLogicalName) {
    const lower = entityLogicalName.trim().toLowerCase();
    resolvedKey =
      ENTITY_TO_WIZARD_KEY[lower] ?? (lower.startsWith(SPRK_PREFIX) ? lower.slice(SPRK_PREFIX.length) : lower);
  }
  if (!resolvedKey) return null;
  return WIZARD_KEY_TO_PAGE[resolvedKey] ?? null;
}

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    minWidth: 0, // Allow shrinking below intrinsic content width
    padding: '2px',
    // v1.4.11 — paddingTop kept minimal (2px) so CardChrome's title sits flush
    // with the top of the form section, matching the OOTB section banner
    // position of standard form sections like "MATTER INFORMATION". The
    // "below header" breathing room is handled inside CardChrome itself
    // (its header has a 12px bottom margin → consistent across all 5 cards).
    paddingTop: '2px',
    paddingBottom: '14px', // Minimal space for version badge
    boxSizing: 'border-box',
    position: 'relative',
    overflow: 'hidden', // Prevent content overflow from affecting form column sizing
  },
  toolbar: {
    display: 'flex',
    // v1.4.3 — switched from `flex-end` to `space-between` so the chart name
    // sits left-aligned in this row while the action icons stay right-aligned.
    // Matches the CardChrome layout for the chrome-opt-in path.
    justifyContent: 'space-between',
    alignItems: 'center',
    minHeight: '28px',
    flexShrink: 0,
    marginBottom: '10px',
    gap: '10px',
  },
  // v1.4.8 — Toolbar variant for `showCardTitle:false`. Positioned absolutely
  // in the top-right of the container so the icons remain visible without the
  // toolbar reserving a 32px row of vertical space. Used when the host form
  // section already provides the chart name as a section heading.
  toolbarFloat: {
    position: 'absolute',
    top: 0,
    right: 0,
    display: 'flex',
    alignItems: 'center',
    // v1.4.30 — tightened from 8px per UAT feedback ("reduce spacing between
    // the toolbar buttons a little bit"); matches CardChrome.iconSlots.
    gap: tokens.spacingHorizontalXXS,
    padding: '4px',
    zIndex: 1,
  },
  toolbarTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    // Truncate long titles instead of pushing icons off-screen.
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    flexGrow: 1,
    minWidth: 0,
  },
  toolbarIcons: {
    display: 'flex',
    alignItems: 'center',
    // v1.4.30 — tightened from 10px per UAT feedback ("reduce spacing between
    // the toolbar buttons a little bit"); matches CardChrome.iconSlots.
    gap: tokens.spacingHorizontalXXS,
    flexShrink: 0,
  },
  chartContainer: {
    display: 'flex',
    alignItems: 'stretch',
    width: '100%',
  },
  versionBadge: {
    position: 'absolute',
    bottom: 0,
    left: '4px',
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground4,
    opacity: 0.6,
    pointerEvents: 'none',
  },
  placeholder: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
});

interface IVisualHostRootProps {
  context: ComponentFramework.Context<IInputs>;
  notifyOutputChanged: () => void;
}

/**
 * Visual Host Root - Main component that loads chart definition and renders the appropriate visual
 */
export const VisualHostRoot: React.FC<IVisualHostRootProps> = ({ context, notifyOutputChanged }) => {
  const styles = useStyles();
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [chartDefinition, setChartDefinition] = useState<IChartDefinition | null>(null);
  const [chartData, setChartData] = useState<IChartData | null>(null);

  // "+" Create Wizard button (visual-host-create-button-r1 / task 012;
  // navigateTo cutover VHVU-030). No wizard/auth state lives here anymore —
  // the "+" click marshals a launch envelope and hands off to the wizard Code
  // Page via navigateTo. Only the toaster (unregistered-wizard feedback,
  // FR-03) remains.
  // Per-instance toaster ID — multiple Visual Host instances can be mounted
  // on the same form; a shared/fixed ID would let their toasts collide.
  const toasterId = useId('visualhost-toaster');
  const { dispatchToast } = useToastController(toasterId);

  // v1.1.0: Hybrid chart definition resolution
  // Priority: 1) Lookup binding, 2) Static ID property
  const lookupValue = context.parameters.chartDefinition?.raw;
  const lookupId = lookupValue?.[0]?.id;
  const staticId = context.parameters.chartDefinitionId?.raw?.trim();
  const chartDefinitionId = lookupId || staticId || null;

  // Log resolution source for debugging
  const resolutionSource = lookupId ? 'lookup' : staticId ? 'static' : 'none';

  // v1.1.0: Context filtering parameters
  const contextFieldName = context.parameters.contextFieldName?.raw?.trim() || null;
  // Get current record ID from context for related record filtering
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const contextRecordId = (context.mode as any).contextInfo?.entityId || null;
  // Host entity logical name (task 012 "+" button — seeds the create wizard's
  // initialAssociation). `contextInfo.entityTypeName` is the same PCF SDK
  // extension property used elsewhere in this repo (e.g. SemanticSearchControl,
  // ScopeConfigEditor) — not present in the public ComponentFramework typings.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const contextEntityTypeName = (context.mode as any).contextInfo?.entityTypeName || null;

  // v1.2.0: FetchXML override from PCF property (highest query priority)
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const fetchXmlOverride = (context.parameters as any).fetchXmlOverride?.raw?.trim() || null;

  // v1.2.33: Value format override per-placement
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const valueFormatOverride = (context.parameters as any).valueFormatOverride?.raw?.trim() || null;

  const showToolbar = context.parameters.showToolbar?.raw !== false;
  const enableDrillThrough = context.parameters.enableDrillThrough?.raw !== false;
  const height = context.parameters.height?.raw;
  const width = context.parameters.width?.raw;
  const justification = (context.parameters.justification?.raw?.trim() as MatrixJustification) || null;
  const columns = context.parameters.columns?.raw;

  // v1.2.44: Show/hide chart definition name as title
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const showTitlePcf = (context.parameters as any).showTitle?.raw as boolean | null;
  // v1.2.44: Base title font size
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const titleFontSizePcf = (context.parameters as any).titleFontSize?.raw?.trim() as string | null;
  // v1.2.47: Show/hide version badge
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const showVersionPcf = (context.parameters as any).showVersion?.raw as boolean | null;
  const showVersion = showVersionPcf !== false; // Default: true (show version badge)

  // v1.2.35: Column position for multi-column coordination
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const columnPosition = (context.parameters as any).columnPosition?.raw as number | null;

  // v1.3.0: Parse aiSummaryField from chart definition config JSON
  const aiSummaryField = React.useMemo(() => {
    if (!chartDefinition?.sprk_configurationjson) return null;
    try {
      const config = JSON.parse(chartDefinition.sprk_configurationjson);
      return (config.aiSummaryField as string) || null;
    } catch {
      return null;
    }
  }, [chartDefinition?.sprk_configurationjson]);

  // v1.4.6: Parse showCardTitle from chart definition config JSON. When
  // explicitly `false`, the toolbar suppresses the chart name (icons only)
  // so it doesn't duplicate a form section heading that already names the
  // same chart. Undefined/missing defaults to true (legacy behavior).
  const showCardTitleInToolbar = React.useMemo(() => {
    if (!chartDefinition?.sprk_configurationjson) return true;
    try {
      const config = JSON.parse(chartDefinition.sprk_configurationjson);
      return typeof config.showCardTitle === 'boolean' ? config.showCardTitle : true;
    } catch {
      return true;
    }
  }, [chartDefinition?.sprk_configurationjson]);

  /**
   * Fetch AI summary from the configured Dataverse text field on the current record.
   * Called lazily by AiSummaryPopover on first open.
   */
  const handleFetchAiSummary = useCallback(async (): Promise<ISummaryData> => {
    if (!aiSummaryField || !chartDefinition?.sprk_entitylogicalname || !contextRecordId) {
      return { summary: null, tldr: null };
    }
    try {
      const entityName = chartDefinition.sprk_entitylogicalname;
      const recordId = contextRecordId.replace(/[{}]/g, '');
      const record = await context.webAPI.retrieveRecord(entityName, recordId, `?$select=${aiSummaryField}`);
      const summaryText = record[aiSummaryField] as string | null;
      return { summary: summaryText || null, tldr: null };
    } catch (err) {
      logger.error('VisualHostRoot', 'Failed to fetch AI summary', err);
      return { summary: null, tldr: null };
    }
  }, [aiSummaryField, chartDefinition, contextRecordId, context.webAPI]);

  // ---------------------------------------------------------------------
  // "+" Create Wizard button (visual-host-create-button-r1 / task 012)
  // ---------------------------------------------------------------------

  /**
   * Resolves the host record's display name for the create wizard's
   * `initialAssociation` seed. Reuses the same catalog mechanism
   * `PolymorphicResolverService.applyResolverFields` uses internally
   * (`sprk_recordtype_ref.sprk_recorddisplaynamefield`) rather than a
   * hardcoded per-entity field map — this is generic across every entity
   * registered in the catalog, not just Matter/Project. Falls back to an
   * empty string (NFR-06-style graceful-blank) when there's no host record,
   * no catalog mapping, or the lookup fails — the wizard still opens with an
   * empty display name rather than blocking on this being unavailable.
   */
  const resolveHostRecordName = useCallback(async (): Promise<string> => {
    if (!contextEntityTypeName || !contextRecordId) return '';
    const cleanId = cleanGuid(contextRecordId); // ADR-044: normalize Xrm GUID
    try {
      const displayField = await resolveRecordDisplayNameFieldName(context.webAPI, contextEntityTypeName);
      if (displayField) {
        const record = await context.webAPI.retrieveRecord(contextEntityTypeName, cleanId, `?$select=${displayField}`);
        const rawName = record[displayField];
        if (typeof rawName === 'string' && rawName.trim().length > 0) {
          return rawName.trim();
        }
      }
    } catch (err) {
      logger.warn('VisualHostRoot', 'Failed to resolve host record display name for create-wizard seed', err);
    }
    return '';
  }, [contextEntityTypeName, contextRecordId, context.webAPI]);

  /**
   * "+" button click handler — shared by both the CardChrome slot and the
   * legacy toolbar path (design.md §5.2). Resolves the target wizard Code Page
   * and opens it via `Xrm.Navigation.navigateTo` as a webresource dialog
   * (60% × 70%, matching `sprk_wizard_commands.js`). An unregistered key shows
   * a toast (FR-03) instead of navigating. The Code Page owns auth (ADR-028) —
   * no MSAL/token wiring happens here.
   */
  const handleCreateClick = useCallback(async () => {
    if (!chartDefinition) return;

    const page = resolveWizardPage(chartDefinition.createWizardKey, chartDefinition.sprk_entitylogicalname ?? null);

    if (!page) {
      const displayKey =
        chartDefinition.createWizardKey?.trim() || chartDefinition.sprk_entitylogicalname || '(unknown)';
      logger.warn('VisualHostRoot', `No create wizard registered for "${displayKey}"`);
      dispatchToast(
        <Toast>
          <ToastTitle>{`No create wizard registered for "${displayKey}"`}</ToastTitle>
        </Toast>,
        { intent: 'error' }
      );
      return;
    }

    // Resolve Xrm across frames — PCF controls run in an iframe; parent frame's
    // Xrm is required for navigateTo (same resolution `handleExpandClick` uses).
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm = (window.parent as any)?.Xrm || (window as any).Xrm;
    if (!xrm?.Navigation?.navigateTo) {
      logger.warn('VisualHostRoot', 'Xrm.Navigation not available for create wizard');
      dispatchToast(
        <Toast>
          <ToastTitle>Cannot open the create form — navigation is unavailable.</ToastTitle>
        </Toast>,
        { intent: 'error' }
      );
      return;
    }

    // Resolve the host record's display name first so the wizard can label its
    // pinned parent (parity with sprk_wizard_commands.js, which awaits
    // getParentEntityName before opening). Failure degrades to '' (the wizard
    // still pins the parent by id) rather than blocking the launch.
    const recordName = await resolveHostRecordName();

    // Build the launch envelope. The entityId is an Xrm-sourced GUID → normalize
    // with `cleanGuid` (ADR-044) BEFORE placing it in the navigate data string.
    // `themeOption` mirrors the tolerant reader (VHVU-020); the Code Page also
    // honors the MDA-wide `spaarke-theme` localStorage preference.
    const themeOption = getEffectiveDarkMode(context) ? 'dark' : 'light';
    const parts: string[] = [];
    if (contextEntityTypeName) parts.push(`entityType=${encodeURIComponent(contextEntityTypeName)}`);
    if (contextRecordId) parts.push(`entityId=${encodeURIComponent(cleanGuid(contextRecordId))}`);
    if (recordName) parts.push(`recordName=${encodeURIComponent(recordName)}`);
    parts.push(`themeOption=${themeOption}`);
    const data = parts.join('&');

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const pageInput: any = { pageType: 'webresource', webresourceName: page, data };
    const navOptions = {
      target: 2 as const,
      position: 1 as const,
      width: { value: 60, unit: '%' as const },
      height: { value: 70, unit: '%' as const },
    };

    try {
      await xrm.Navigation.navigateTo(pageInput, navOptions);
    } catch (err) {
      // navigateTo rejects on user-cancel too — log at info, no error toast.
      logger.info('VisualHostRoot', 'Create wizard dialog closed/cancelled', err);
    }
  }, [chartDefinition, context, contextEntityTypeName, contextRecordId, resolveHostRecordName, dispatchToast]);

  // FR-TEL-01: Initialize App Insights once on mount.
  // AppInsightsService.initialize() is idempotent — second + subsequent calls are no-ops,
  // so this is safe even if other PCF surfaces on the same page also initialize.
  useEffect(() => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const appInsightsKey = ((context.parameters as any).appInsightsKey?.raw as string | null | undefined) ?? '';
    if (appInsightsKey) {
      AppInsightsService.initialize(appInsightsKey);
    }
  }, []); // empty deps — manifest properties are stable for the control's lifetime

  useEffect(() => {
    if (!chartDefinitionId) {
      setIsLoading(false);
      setError(null);
      setChartDefinition(null);
      logger.info('VisualHostRoot', 'No chart definition configured');
      return;
    }

    loadChartDefinition(chartDefinitionId, resolutionSource);
  }, [chartDefinitionId, resolutionSource, contextFieldName, contextRecordId]);

  /**
   * Load chart definition from Dataverse
   * @param id - Chart definition GUID
   * @param source - Resolution source for logging ("lookup" | "static" | "none")
   */
  const loadChartDefinition = async (id: string, source: string) => {
    try {
      setIsLoading(true);
      setError(null);

      logger.info('VisualHostRoot', `Loading chart definition: ${id} (source: ${source})`, {
        contextFieldName,
        contextRecordId,
      });

      // Load from Dataverse using ConfigurationLoader service
      const definition = await loadChartDefinitionFromDataverse({ webAPI: context.webAPI }, id);

      setChartDefinition(definition);
      logger.info('VisualHostRoot', `Loaded: ${definition.sprk_name}`, {
        visualType: definition.sprk_visualtype,
        entity: definition.sprk_entitylogicalname,
      });

      // DueDateCard and DueDateCardList fetch their own data — skip aggregation
      const skipAggregation =
        definition.sprk_visualtype === 100000008 || // DueDateCard
        definition.sprk_visualtype === 100000009; // DueDateCardList

      // v1.2.41: Check for field pivot mode — reads multiple fields from
      // the current record and presents each as a card (generic, not KPI-specific)
      const pivotConfig = parseFieldPivotConfig(definition.sprk_configurationjson);

      if (pivotConfig && definition.sprk_entitylogicalname && contextRecordId) {
        try {
          logger.info('VisualHostRoot', 'Field pivot mode detected', {
            entity: definition.sprk_entitylogicalname,
            fields: pivotConfig.fields.length,
          });
          const data = await fetchAndPivot(
            context.webAPI,
            definition.sprk_entitylogicalname,
            contextRecordId,
            pivotConfig
          );
          setChartData(data);
          logger.info('VisualHostRoot', `Field pivot: ${data.dataPoints.length} data points`);
        } catch (pivotErr) {
          logger.error('VisualHostRoot', 'Field pivot error', pivotErr);
          setChartData(null);
        }
      } else if (definition.sprk_entitylogicalname && !skipAggregation) {
        // Existing data aggregation path (VIEW or BASIC mode)
        try {
          const ctxFilter =
            contextFieldName && contextRecordId
              ? { fieldName: contextFieldName, recordId: contextRecordId }
              : undefined;
          logger.info(
            'VisualHostRoot',
            `Context filter for aggregation: ${ctxFilter ? `fieldName="${ctxFilter.fieldName}", recordId="${ctxFilter.recordId}"` : '(none - no context)'}`
          );

          const data = await fetchAndAggregate({ webAPI: context.webAPI }, definition, {
            contextFilter: ctxFilter,
          });
          setChartData(data);
          logger.info(
            'VisualHostRoot',
            `Data loaded: ${data.dataPoints.length} data points from ${data.totalRecords} records`
          );
        } catch (aggErr) {
          if (aggErr instanceof AggregationError) {
            logger.error('VisualHostRoot', 'Data aggregation error', aggErr);
            setChartData(null);
          } else {
            throw aggErr;
          }
        }
      } else {
        logger.warn('VisualHostRoot', 'No entity configured, skipping data fetch');
        setChartData(null);
      }
    } catch (err) {
      if (err instanceof ConfigurationNotFoundError) {
        logger.warn('VisualHostRoot', `Chart definition not found: ${id}`);
        setError(`Chart definition not found. Please verify the ID is correct.`);
      } else if (err instanceof ConfigurationLoadError) {
        logger.error('VisualHostRoot', 'Configuration load error', err);
        setError(`Failed to load chart: ${err.message}`);
      } else {
        const errorMessage = err instanceof Error ? err.message : 'Unknown error';
        logger.error('VisualHostRoot', 'Failed to load chart definition', err);
        setError(`Failed to load chart: ${errorMessage}`);
      }
      setChartDefinition(null);
      setChartData(null);
    } finally {
      setIsLoading(false);
    }
  };

  /**
   * Handle drill interaction from chart components
   */
  const handleDrillInteraction = useCallback(
    (interaction: DrillInteraction) => {
      if (!enableDrillThrough) return;

      logger.info('VisualHostRoot', 'Drill interaction', interaction);

      // TRACKED: GitHub #234 - Navigate to drill-through Custom Page
      // For now, log the interaction for debugging
      console.log('Drill interaction:', interaction);

      // Notify PCF that output has changed (if we add drill output parameter)
      notifyOutputChanged();
    },
    [enableDrillThrough, notifyOutputChanged]
  );

  /**
   * Handle expand button click - drill-through navigation.
   * v1.2.25: If sprk_drillthroughtarget is configured, opens the Custom Page in a
   * dialog with context filter params (entity, view, filter field/value, mode=dialog).
   * Otherwise falls back to navigateTo entitylist dialog (unfiltered).
   */
  const handleExpandClick = useCallback(async () => {
    logger.info('VisualHostRoot', 'Expand clicked - navigating to view', {
      chartDefinitionId,
    });

    if (!chartDefinition) {
      logger.warn('VisualHostRoot', 'No chart definition for drill-through');
      return;
    }

    const entityName = chartDefinition.sprk_entitylogicalname;
    if (!entityName) {
      logger.warn('VisualHostRoot', 'No entity name configured for drill-through');
      return;
    }

    // Resolve Xrm from multiple scopes — PCF controls run in iframes and
    // custom page navigation may require the parent frame's Xrm object.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm = (window.parent as any)?.Xrm || (window as any).Xrm;

    if (!xrm?.Navigation?.navigateTo) {
      logger.warn('VisualHostRoot', 'Xrm.Navigation not available');
      return;
    }

    logger.info('VisualHostRoot', 'Xrm source', {
      fromParent: !!(window.parent as any)?.Xrm,
      fromWindow: !!(window as any).Xrm,
    });

    const viewId = chartDefinition.sprk_baseviewid;
    const drillThroughTarget = chartDefinition.sprk_drillthroughtarget?.trim();

    // Build context filter params
    const ctxField = chartDefinition.sprk_contextfieldname || contextFieldName;
    let filterField: string | null = null;
    let filterValue: string | null = null;
    if (ctxField && contextRecordId) {
      filterField = ctxField.replace(/^_/, '').replace(/_value$/, '');
      filterValue = contextRecordId.replace(/[{}]/g, '');
      logger.info('VisualHostRoot', 'Context filter for drill-through', {
        filterField,
        filterValue,
      });
    }

    try {
      if (drillThroughTarget) {
        // v1.4.13 — `sprk_drillthroughtarget` may be either:
        //   (a) a web resource name ending in `.html` / `.htm`
        //       (e.g., "sprk_eventspage.html") → opens via pageType:'webresource'
        //   (b) an entity logical name (e.g., "sprk_event", "sprk_invoice",
        //       "sprk_kpiassessment") → opens via pageType:'entitylist' for that
        //       entity. This lets a single chart query one entity (sprk_matter)
        //       but drill into related child records of a different entity
        //       (sprk_invoice etc.) without authoring a web resource per case.
        const isWebResource =
          drillThroughTarget.toLowerCase().endsWith('.html') || drillThroughTarget.toLowerCase().endsWith('.htm');

        if (isWebResource) {
          logger.info('VisualHostRoot', 'Opening web resource drill-through dialog', {
            webresource: drillThroughTarget,
            entityName,
            filterField: filterField || '(none)',
            filterValue: filterValue || '(none)',
          });

          // Build query string to pass context to the web resource
          const params = new URLSearchParams();
          if (entityName) params.set('entityName', entityName);
          if (filterField) params.set('filterField', filterField);
          if (filterValue) params.set('filterValue', filterValue);
          if (viewId) params.set('viewId', viewId.replace(/[{}]/g, ''));
          // Drill-through view allowlist (operator-configured on the chart def,
          // delimited by `;` or `,`). Forwarded so the DataGrid page shell can
          // restrict its view-switcher without editing the grid config record.
          const allowedViews = (chartDefinition.sprk_drillthroughviews ?? '')
            .split(/[;,]/)
            .map(g => g.trim().replace(/[{}]/g, ''))
            .filter(g => g.length > 0);
          if (allowedViews.length > 0) params.set('availableViews', allowedViews.join(';'));
          params.set('mode', 'dialog');

          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          const pageInput: any = {
            pageType: 'webresource',
            webresourceName: drillThroughTarget,
            data: params.toString(),
          };

          const navOptions = {
            target: 2 as const,
            position: 1 as const,
            width: { value: 90, unit: '%' as const },
            height: { value: 85, unit: '%' as const },
          };

          try {
            await xrm.Navigation.navigateTo(pageInput, navOptions);
          } catch {
            logger.info('VisualHostRoot', 'Dialog not supported, navigating inline');
            await xrm.Navigation.navigateTo(pageInput, { target: 1 });
          }
        } else {
          // Drill-through target is an entity logical name. Open that entity's
          // list as a dialog. `sprk_baseviewid` (if set) selects a specific
          // view — otherwise the entity's default view is used.
          logger.info('VisualHostRoot', 'Opening entity list dialog for drill-through entity', {
            drillEntity: drillThroughTarget,
            viewId: viewId || '(default)',
          });

          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          const pageInput: any = {
            pageType: 'entitylist',
            entityName: drillThroughTarget,
          };
          if (viewId) pageInput.viewId = viewId.replace(/[{}]/g, '');

          try {
            await xrm.Navigation.navigateTo(pageInput, {
              target: 2,
              position: 1,
              width: { value: 90, unit: '%' },
              height: { value: 85, unit: '%' },
            });
          } catch {
            logger.info('VisualHostRoot', 'Dialog not supported, navigating full page');
            await xrm.Navigation.navigateTo(pageInput, { target: 1 });
          }
        }
      } else {
        // Fallback: entitylist dialog (unfiltered — filterXml not supported by navigateTo)
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const pageInput: any = { pageType: 'entitylist', entityName };
        if (viewId) pageInput.viewId = viewId;

        logger.info('VisualHostRoot', 'Opening entity list dialog (no custom page configured)', {
          entityName,
          viewId: viewId || '(default)',
        });

        try {
          await xrm.Navigation.navigateTo(pageInput, {
            target: 2,
            position: 1,
            width: { value: 90, unit: '%' },
            height: { value: 85, unit: '%' },
          });
        } catch {
          logger.info('VisualHostRoot', 'Dialog not supported for entity list, navigating full page');
          await xrm.Navigation.navigateTo(pageInput, { target: 1 });
        }
      }

      logger.info('VisualHostRoot', 'Drill-through view opened');
    } catch (err) {
      logger.error('VisualHostRoot', 'Failed to open drill-through view', err);
    }
  }, [chartDefinition, contextFieldName, contextRecordId]);

  /**
   * Handle configured click action from ClickActionHandler
   */
  const handleClickAction = useCallback(
    async (recordId: string, entityName?: string, recordData?: Record<string, unknown>) => {
      if (!chartDefinition || !hasClickAction(chartDefinition)) return;

      await executeClickAction(
        {
          chartDefinition,
          recordId,
          entityName,
          recordData,
        },
        handleExpandClick
      );
    },
    [chartDefinition, handleExpandClick]
  );

  /**
   * Handle "View List" navigation - switch to configured tab on current form
   */
  const handleViewListClick = useCallback(() => {
    if (!chartDefinition?.sprk_viewlisttabname) return;

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm = (window as any).Xrm;
    const tabName = chartDefinition.sprk_viewlisttabname;

    logger.info('VisualHostRoot', 'View List click - navigating to tab', {
      tabName,
    });

    try {
      // Navigate to the configured tab on the current form
      const formContext = xrm?.Page;
      if (formContext?.ui?.tabs?.get) {
        const tab = formContext.ui.tabs.get(tabName);
        if (tab) {
          tab.setFocus();
        } else {
          logger.warn('VisualHostRoot', `Tab '${tabName}' not found on current form`);
        }
      }
    } catch (err) {
      logger.error('VisualHostRoot', 'Failed to navigate to tab', err);
    }
  }, [chartDefinition]);

  /**
   * Render the appropriate visual based on chart definition.
   *
   * FR-VH-05: every rendered card is wrapped in CardChrome so the per-card
   * title bar + corner-icon slots are available. To preserve NFR-05
   * backward compatibility, the chrome header renders ONLY when a title is
   * supplied — gated by the existing `showTitle` PCF property (defaults to
   * `false`). Existing chart defs that do not opt in see zero header chrome
   * and render pixel-identical to today.
   *
   * AI sparkle slot is hidden in v1 (`showAiSparkle: false`) — the prop is
   * forward-compat for r2 Insights Engine.
   */
  const renderVisual = () => {
    if (!chartDefinition) {
      return (
        <div className={styles.placeholder}>
          <Text size={400}>No chart configured</Text>
          <Text size={200}>Bind to a lookup column or set Chart Definition ID property</Text>
        </div>
      );
    }

    // Chrome opt-in: BOTH title and expand-icon are gated on `showTitlePcf === true`.
    // This keeps every existing chart def (default showTitle=false) chrome-free
    // for NFR-05 zero-regression compliance — the existing toolbar (rendered
    // above) continues to handle the expand icon for legacy chart defs. When a
    // caller explicitly sets showTitle=true (Phase 3+ Matter cards), CardChrome
    // takes over both the title bar AND the expand icon.
    const chromeOptIn = showTitlePcf === true;
    // v1.4.10 — CardChrome ALWAYS renders its title when `chromeOptIn` is
    // true. The chart-def-level `showCardTitle` option ONLY affects the
    // legacy toolbar's title (it was never meant to also hide CardChrome's
    // title — v1.4.9 misapplied it and the result was that Matter Next Date
    // lost its only header). CardChrome is the canonical title surface for
    // all 5 Matter chart cards, matching FR-VH-05.
    const chromeTitle: string | undefined = chromeOptIn ? chartDefinition.sprk_name || undefined : undefined;

    // Wire expand to existing handleExpandClick so chart-def Drill Through
    // Settings continue to apply (no new ClickActionHandler).
    const chromeOnExpand: (() => void) | undefined =
      chromeOptIn && enableDrillThrough
        ? () => {
            void handleExpandClick();
          }
        : undefined;

    // v1.4.3 — Tell ChartRenderer that the host (CardChrome OR the legacy
    // toolbar above) is rendering the title so each chart visual can suppress
    // its own internal title and avoid double-rendering.
    //
    // Legacy toolbar renders the title when ALL of these are true:
    //   - showToolbar (PCF property, defaults true)
    //   - chartDefinition has a sprk_name
    //   - the toolbar itself is visible (aiSummaryField OR enableDrillThrough)
    const legacyToolbarTitle =
      !chromeOptIn && showToolbar === true && !!chartDefinition.sprk_name && (!!aiSummaryField || enableDrillThrough);
    const hostRenderedTitle = chromeOptIn || legacyToolbarTitle;

    // "+" Create Wizard button (task 012) — same chrome-opt-in gating as
    // `chromeOnExpand`/`showAiSparkle` above. When `chromeOptIn` is false,
    // the legacy toolbar path (below, in the main render) handles the "+"
    // button instead, so it is never rendered in both places at once.
    const chromeOnCreate: (() => void) | undefined =
      chromeOptIn && chartDefinition.createWizardEnabled === true ? handleCreateClick : undefined;

    return (
      <CardChrome
        title={chromeTitle}
        onExpand={chromeOnExpand}
        showAiSparkle={chromeOptIn && !!aiSummaryField}
        onAiSummary={aiSummaryField ? handleFetchAiSummary : undefined}
        onCreateClick={chromeOnCreate}
      >
        <ChartRenderer
          chartDefinition={chartDefinition}
          chartData={chartData || undefined}
          onDrillInteraction={enableDrillThrough ? handleDrillInteraction : undefined}
          height={height || undefined}
          webApi={context.webAPI}
          contextRecordId={contextRecordId || undefined}
          onClickAction={hasClickAction(chartDefinition) ? handleClickAction : undefined}
          onViewListClick={chartDefinition.sprk_viewlisttabname ? handleViewListClick : undefined}
          fetchXmlOverride={fetchXmlOverride || undefined}
          valueFormatOverride={valueFormatOverride || undefined}
          width={width || undefined}
          justification={justification || undefined}
          columns={columns || undefined}
          showTitle={showTitlePcf ?? undefined}
          titleFontSize={titleFontSizePcf || undefined}
          hostRenderedTitle={hostRenderedTitle}
        />
      </CardChrome>
    );
  };

  // Container style with optional height and column-position edge padding
  const containerStyle: React.CSSProperties = {
    ...(height ? { minHeight: `${height}px` } : {}),
    // v1.2.47: No bottom padding when version badge is hidden
    ...(!showVersion ? { paddingBottom: 0 } : {}),
    // v1.2.35: When columnPosition is set, remove padding on inner edges
    // so adjacent PCFs visually merge into one cohesive row
    ...(columnPosition === 1 ? { paddingRight: 0 } : {}),
    ...(columnPosition != null && columnPosition >= 2 && columnPosition <= 3
      ? { paddingLeft: 0, paddingRight: 0 }
      : {}),
    ...(columnPosition != null && columnPosition >= 4 ? { paddingLeft: 0 } : {}),
  };

  return (
    <div className={styles.container} style={containerStyle}>
      {/* Toolbar row — Chart name (left) + AI Summary / View Details (right), above the visual.
          v1.4.3: Title is now rendered here (legacy path) instead of being duplicated inside
          each chart's centered header. When this toolbar renders, ChartRenderer suppresses
          the chart's internal title via the `hostRenderedTitle` prop. */}
      {/* v1.4.8.1 — Toolbar is fully suppressed when CardChrome is active
          (`showTitlePcf === true`). CardChrome owns its own title row + icons,
          so any legacy toolbar render here produces stacked duplicate icons.
          For chart defs without CardChrome opt-in, the toolbar renders in one
          of two modes (see comment below). */}
      {showToolbar &&
        chartDefinition &&
        // task 012: also show the toolbar when ONLY the "+" Create Wizard
        // button is configured (no AI summary field, drill-through disabled).
        (aiSummaryField || enableDrillThrough || chartDefinition.createWizardEnabled === true) &&
        showTitlePcf !== true &&
        // Two render modes when CardChrome is NOT active:
        //   showCardTitle:false → float variant. Icons absolutely positioned in
        //     top-right corner; toolbar reserves zero vertical space. Used when
        //     a form section heading already provides the chart name.
        //   else                → inline variant. Title (left) + icons (right)
        //     occupy a 32px row above the chart content (v1.4.3 layout).
        (showCardTitleInToolbar && chartDefinition.sprk_name ? (
          <div className={styles.toolbar}>
            <Text
              size={300}
              className={styles.toolbarTitle}
              title={chartDefinition.sprk_name}
              aria-label={chartDefinition.sprk_name}
            >
              {chartDefinition.sprk_name}
            </Text>
            <div className={styles.toolbarIcons}>
              {aiSummaryField && (
                <AiSummaryPopover
                  trigger={
                    <Tooltip content="AI Summary" relationship="label">
                      <Button appearance="subtle" icon={<SparkleRegular />} aria-label="View AI summary" />
                    </Tooltip>
                  }
                  onFetchSummary={handleFetchAiSummary}
                  positioning="below"
                />
              )}
              {chartDefinition.createWizardEnabled === true && (
                <Tooltip content="Create new" relationship="label">
                  <Button
                    appearance="subtle"
                    icon={<AddRegular />}
                    onClick={handleCreateClick}
                    aria-label="Create new record"
                  />
                </Tooltip>
              )}
              {enableDrillThrough && (
                <Tooltip content="View details" relationship="label">
                  <Button
                    appearance="subtle"
                    icon={<OpenRegular />}
                    onClick={handleExpandClick}
                    aria-label="View details in expanded workspace"
                  />
                </Tooltip>
              )}
            </div>
          </div>
        ) : (
          <div className={styles.toolbarFloat}>
            {aiSummaryField && (
              <AiSummaryPopover
                trigger={
                  <Tooltip content="AI Summary" relationship="label">
                    <Button appearance="subtle" icon={<SparkleRegular />} aria-label="View AI summary" />
                  </Tooltip>
                }
                onFetchSummary={handleFetchAiSummary}
                positioning="below"
              />
            )}
            {chartDefinition.createWizardEnabled === true && (
              <Tooltip content="Create new" relationship="label">
                <Button
                  appearance="subtle"
                  icon={<AddRegular />}
                  onClick={handleCreateClick}
                  aria-label="Create new record"
                />
              </Tooltip>
            )}
            {enableDrillThrough && (
              <Tooltip content="View details" relationship="label">
                <Button
                  appearance="subtle"
                  icon={<OpenRegular />}
                  onClick={handleExpandClick}
                  aria-label="View details in expanded workspace"
                />
              </Tooltip>
            )}
          </div>
        ))}

      {/* Version badge - lower left, unobtrusive (controlled by showVersion PCF prop) */}
      {showVersion && <span className={styles.versionBadge}>v1.4.37 • 2026-07-12</span>}

      {/* Main chart area */}
      <div className={styles.chartContainer}>
        {isLoading ? (
          <Spinner label="Loading chart..." />
        ) : error ? (
          <MessageBar intent="error">
            <MessageBarBody>{error}</MessageBarBody>
          </MessageBar>
        ) : (
          renderVisual()
        )}
      </div>

      {/* "+" Create Wizard now launches via `Xrm.Navigation.navigateTo`
          (VHVU-030) — the inline Dialog/React.lazy embedding was removed.
          Only the toaster (unregistered-wizard feedback, FR-03) remains. */}
      <Toaster toasterId={toasterId} />
    </div>
  );
};
