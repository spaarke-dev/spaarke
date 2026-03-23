/**
 * PlaybookLibraryShell — Shared playbook browsing + execution shell.
 *
 * Extracted from AnalysisBuilder/App.tsx (UDSS-020). Provides a 2-tab layout:
 *   Tab 1: Select Playbook — card grid with locked scope preview on selection
 *   Tab 2: Custom Scope — manual action/skills/knowledge/tools selection
 *
 * All Dataverse access is routed through the IDataService prop so the shell
 * remains portable across PCF controls, Code Pages, SPAs, and test harnesses.
 *
 * BFF API calls use the injected `authenticatedFetch` + `bffBaseUrl` props
 * instead of importing from solution-specific modules.
 */

import React from 'react';
import {
  TabList,
  Tab,
  Button,
  Text,
  Spinner,
  MessageBar,
  MessageBarBody,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { PlaybookCardGrid } from '../Playbook/PlaybookCardGrid';
import { ScopeConfigurator } from '../Playbook/ScopeConfigurator';
import { loadAllData, loadPlaybookScopes } from '../Playbook/playbookService';
import type { IPlaybookData } from '../Playbook/playbookService';
import { createAndAssociate } from '../Playbook/analysisService';
import type { AuthenticatedFetchFn } from '../Playbook/analysisService';
import type {
  IPlaybook,
  IAction,
  ISkill,
  IKnowledge,
  ITool,
  IPlaybookScopes,
  IAnalysisConfig,
} from '../Playbook/types';
import type { IDataService } from '../../types/serviceInterfaces';
import { IntentWizardFlow, INTENT_PLAYBOOK_MAP } from './IntentWizardFlow';
import { DocumentSelector } from './DocumentSelector';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IPlaybookLibraryShellProps {
  /** Entity type of the source record (e.g., "sprk_document"). */
  entityType: string;
  /** GUID of the source entity record (the active document when documentIds is also provided). */
  entityId: string;
  /**
   * Optional list of document IDs available for selection.
   *
   * When two or more IDs are provided a DocumentSelector bar is rendered at the
   * top of the shell, allowing the user to switch the active document before
   * running an analysis.  The first ID in the array is selected by default
   * (unless `entityId` already matches one of them).
   *
   * When only one ID is provided (or this prop is omitted) the selector is
   * hidden and `entityId` is used directly.
   */
  documentIds?: string[];
  /** Optional allowlist — only show playbooks whose IDs are in this array. */
  allowedPlaybookIds?: string[];
  /** Display mode: 'browse' shows full 2-tab UI, 'intent' pre-selects a playbook. */
  mode?: 'browse' | 'intent';
  /** When true, suppresses the header/footer chrome for embedding inside another shell. */
  embedded?: boolean;
  /** Pre-select a specific playbook by intent string (matched against playbook name). */
  intent?: string;
  /** Called when analysis creation completes successfully. */
  onComplete?: (result: { analysisId: string }) => void;
  /** Called when the user cancels or closes the shell. */
  onClose?: () => void;
  /** Data access abstraction (Xrm.WebApi adapter, test mock, etc.). */
  dataService: IDataService;
  /** Authenticated fetch function for BFF API calls. */
  authenticatedFetch?: AuthenticatedFetchFn;
  /** Base URL of the BFF API (e.g., "https://spe-api-dev.azurewebsites.net"). */
  bffBaseUrl?: string;
  /** Display name of the source entity (shown in the header subtitle). */
  entityDisplayName?: string;
  /** Label for the primary action button. Defaults to "Run Analysis". */
  executeButtonLabel?: string;
  /** Title shown in the header. Defaults to "New Analysis". */
  title?: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  header: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
  tabBar: {
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke1,
    flexShrink: 0,
  },
  content: {
    flex: 1,
    overflow: 'auto',
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    minHeight: 0,
  },
  scopePreview: {
    marginTop: tokens.spacingVerticalL,
  },
  footer: {
    display: 'flex',
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    borderTopWidth: '1px',
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
  },
  loading: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    flex: 1,
  },
});

// ---------------------------------------------------------------------------
// Tab type
// ---------------------------------------------------------------------------

type BuilderTab = 'playbook' | 'custom';

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const PlaybookLibraryShell: React.FC<IPlaybookLibraryShellProps> = ({
  entityType,
  entityId,
  documentIds,
  allowedPlaybookIds,
  mode = 'browse',
  embedded = false,
  intent,
  onComplete,
  onClose,
  dataService,
  authenticatedFetch,
  bffBaseUrl,
  entityDisplayName,
  executeButtonLabel = 'Run Analysis',
  title = 'New Analysis',
}) => {
  const styles = useStyles();

  // ---------------------------------------------------------------------------
  // Document selector state
  //
  // When documentIds contains 2+ entries a DocumentSelector bar is shown.
  // The activeDocumentId drives analysis execution instead of the raw entityId.
  // ---------------------------------------------------------------------------

  /** True when the caller supplied 2+ document IDs. */
  const hasMultipleDocuments = (documentIds?.length ?? 0) >= 2;

  /**
   * Derive the initial active document ID:
   *  1. If entityId matches one of the provided documentIds, start with it.
   *  2. Otherwise fall back to the first ID in the list.
   *  3. If documentIds is absent/empty, use entityId as-is.
   */
  const initialDocumentId = React.useMemo((): string => {
    if (!documentIds || documentIds.length === 0) return entityId;
    if (documentIds.includes(entityId)) return entityId;
    return documentIds[0];
  }, [documentIds, entityId]);

  const [activeDocumentId, setActiveDocumentId] = React.useState<string>(initialDocumentId);

  /**
   * The document ID to use for analysis execution.
   * When documentIds is provided, this is the user-selected document;
   * otherwise it falls through to the plain entityId prop.
   */
  const effectiveDocumentId = hasMultipleDocuments ? activeDocumentId : entityId;

  // --- Data state ---
  const [isLoading, setIsLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);
  const [playbooks, setPlaybooks] = React.useState<IPlaybook[]>([]);
  const [actions, setActions] = React.useState<IAction[]>([]);
  const [skills, setSkills] = React.useState<ISkill[]>([]);
  const [knowledge, setKnowledge] = React.useState<IKnowledge[]>([]);
  const [tools, setTools] = React.useState<ITool[]>([]);

  // --- Selection state ---
  const [activeTab, setActiveTab] = React.useState<BuilderTab>('playbook');
  const [selectedPlaybook, setSelectedPlaybook] = React.useState<IPlaybook | null>(null);
  const [playbookScopes, setPlaybookScopes] = React.useState<IPlaybookScopes | null>(null);

  // Custom scope selection (Tab 2)
  const [selectedActionIds, setSelectedActionIds] = React.useState<string[]>([]);
  const [selectedSkillIds, setSelectedSkillIds] = React.useState<string[]>([]);
  const [selectedKnowledgeIds, setSelectedKnowledgeIds] = React.useState<string[]>([]);
  const [selectedToolIds, setSelectedToolIds] = React.useState<string[]>([]);

  // --- Execution state ---
  const [isExecuting, setIsExecuting] = React.useState(false);
  const [successMessage, setSuccessMessage] = React.useState<string | null>(null);

  // --- Build a webApi-compatible adapter from IDataService (for playbookService reads) ---
  const webApiAdapter = React.useMemo(() => ({
    retrieveMultipleRecords: (entityName: string, options?: string) =>
      dataService.retrieveMultipleRecords(entityName, options),
    retrieveRecord: (entityName: string, id: string, options?: string) =>
      dataService.retrieveRecord(entityName, id, options),
    createRecord: async (entityName: string, data: Record<string, unknown>) => {
      const id = await dataService.createRecord(entityName, data);
      return { id };
    },
  }), [dataService]);

  // --- Load data on mount ---
  React.useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const data: IPlaybookData = await loadAllData(webApiAdapter);
        if (cancelled) return;

        // Apply allowlist filter if provided
        let filteredPlaybooks = data.playbooks;
        if (allowedPlaybookIds && allowedPlaybookIds.length > 0) {
          const allowSet = new Set(allowedPlaybookIds);
          filteredPlaybooks = data.playbooks.filter(p => allowSet.has(p.id));
        }

        setPlaybooks(filteredPlaybooks);
        setActions(data.actions);
        setSkills(data.skills);
        setKnowledge(data.knowledge);
        setTools(data.tools);

        // Intent mode: auto-select matching playbook.
        // 1. Try INTENT_PLAYBOOK_MAP for a known intent -> playbook ID mapping.
        // 2. Fall back to fuzzy name matching against available playbooks.
        if (mode === 'intent' && intent && filteredPlaybooks.length > 0) {
          const mappedPlaybookId = INTENT_PLAYBOOK_MAP[intent];
          let match: IPlaybook | undefined;

          if (mappedPlaybookId) {
            match = filteredPlaybooks.find(p => p.id === mappedPlaybookId);
          }

          // Fallback: fuzzy name match
          if (!match) {
            const intentLower = intent.toLowerCase();
            match = filteredPlaybooks.find(
              p => p.name.toLowerCase().includes(intentLower)
            );
          }

          if (match) {
            try {
              const scopes = await loadPlaybookScopes(webApiAdapter, match.id);
              if (!cancelled) {
                setSelectedPlaybook(match);
                setPlaybookScopes(scopes);
              }
            } catch (err) {
              console.error('[PlaybookLibraryShell] Failed to load intent playbook scopes:', err);
            }
          }
        }
      } catch (err) {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : 'Failed to load data');
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [webApiAdapter, allowedPlaybookIds, mode, intent]);

  // --- Playbook selection handler ---
  const handlePlaybookSelect = React.useCallback(async (playbook: IPlaybook) => {
    setSelectedPlaybook(playbook);
    try {
      const scopes = await loadPlaybookScopes(webApiAdapter, playbook.id);
      setPlaybookScopes(scopes);
    } catch (err) {
      console.error('[PlaybookLibraryShell] Failed to load playbook scopes:', err);
      setPlaybookScopes(null);
    }
  }, [webApiAdapter]);

  // --- Tab change handler ---
  const handleTabSelect = React.useCallback(
    (_event: unknown, data: { value: unknown }) => {
      setActiveTab(data.value as BuilderTab);
    },
    []
  );

  // --- Intent mode derived flag ---
  const isIntentMode = mode === 'intent' && !!intent && selectedPlaybook !== null && playbookScopes !== null;

  // --- Can execute? ---
  const canExecute = React.useMemo(() => {
    if (!effectiveDocumentId) return false;
    // Intent mode: always ready once playbook + scopes are resolved
    if (isIntentMode) return true;
    if (activeTab === 'playbook') {
      return selectedPlaybook !== null && playbookScopes !== null;
    }
    // Custom scope: need at least an action
    return selectedActionIds.length > 0;
  }, [activeTab, selectedPlaybook, playbookScopes, selectedActionIds, effectiveDocumentId, isIntentMode]);

  // --- Run Analysis handler ---
  const handleExecute = React.useCallback(async () => {
    if (!canExecute) return;
    setIsExecuting(true);
    setError(null);

    try {
      let config: IAnalysisConfig;

      if (activeTab === 'playbook' && selectedPlaybook && playbookScopes) {
        config = {
          documentId: effectiveDocumentId,
          playbookId: selectedPlaybook.id,
          actionId: playbookScopes.actionIds[0] ?? '',
          skillIds: playbookScopes.skillIds,
          knowledgeIds: playbookScopes.knowledgeIds,
          toolIds: playbookScopes.toolIds,
        };
      } else {
        config = {
          documentId: effectiveDocumentId,
          actionId: selectedActionIds[0] ?? '',
          skillIds: selectedSkillIds,
          knowledgeIds: selectedKnowledgeIds,
          toolIds: selectedToolIds,
        };
      }

      if (!authenticatedFetch || !bffBaseUrl) {
        throw new Error('authenticatedFetch and bffBaseUrl are required to create an analysis.');
      }

      const analysisId = await createAndAssociate(authenticatedFetch, bffBaseUrl, config);

      if (onComplete) {
        onComplete({ analysisId });
      } else {
        setSuccessMessage('Analysis created successfully.');
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create analysis');
    } finally {
      setIsExecuting(false);
    }
  }, [
    canExecute,
    activeTab,
    selectedPlaybook,
    playbookScopes,
    selectedActionIds,
    selectedSkillIds,
    selectedKnowledgeIds,
    selectedToolIds,
    effectiveDocumentId,
    authenticatedFetch,
    bffBaseUrl,
    onComplete,
  ]);

  // --- Cancel / Close handler ---
  const handleCancel = React.useCallback(() => {
    if (onClose) {
      onClose();
    }
  }, [onClose]);

  // --- Render: success state ---
  if (successMessage) {
    return (
      <div className={styles.root}>
        {!embedded && (
          <div className={styles.header}>
            <Text size={500} weight="semibold">{title}</Text>
          </div>
        )}
        <div className={styles.content}>
          <MessageBar intent="success">
            <MessageBarBody>{successMessage}</MessageBarBody>
          </MessageBar>
        </div>
        {!embedded && (
          <div className={styles.footer}>
            <Button appearance="primary" onClick={handleCancel}>Close</Button>
          </div>
        )}
      </div>
    );
  }

  // --- Render: loading state ---
  if (isLoading) {
    return (
      <div className={styles.root}>
        <div className={styles.loading}>
          <Spinner size="large" label="Loading playbooks..." />
        </div>
      </div>
    );
  }

  // --- Render: main UI ---
  return (
    <div className={styles.root}>
      {/* Header */}
      {!embedded && (
        <div className={styles.header}>
          <Text size={500} weight="semibold">{title}</Text>
          {entityDisplayName && (
            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
              {entityDisplayName}
            </Text>
          )}
        </div>
      )}

      {/* Document selector — only rendered when 2+ documents are provided */}
      {hasMultipleDocuments && documentIds && (
        <DocumentSelector
          documentIds={documentIds}
          selectedDocumentId={activeDocumentId}
          onSelect={setActiveDocumentId}
          dataService={dataService}
        />
      )}

      {/* Intent mode: streamlined wizard flow (no tabs) */}
      {isIntentMode ? (
        <>
          {/* Error bar */}
          {error && (
            <MessageBar intent="error" style={{ margin: tokens.spacingVerticalS }}>
              <MessageBarBody>{error}</MessageBarBody>
            </MessageBar>
          )}

          <div className={styles.content}>
            <IntentWizardFlow
              playbook={selectedPlaybook!}
              playbookScopes={playbookScopes!}
              actions={actions}
              skills={skills}
              knowledge={knowledge}
              tools={tools}
              isExecuting={isExecuting}
              error={error}
            />
          </div>
        </>
      ) : (
        <>
          {/* Tab bar — browse mode only */}
          <div className={styles.tabBar}>
            <TabList selectedValue={activeTab} onTabSelect={handleTabSelect} size="medium">
              <Tab value="playbook">Select Playbook</Tab>
              <Tab value="custom">Custom Scope</Tab>
            </TabList>
          </div>

          {/* Error bar */}
          {error && (
            <MessageBar intent="error" style={{ margin: tokens.spacingVerticalS }}>
              <MessageBarBody>{error}</MessageBarBody>
            </MessageBar>
          )}

          {/* Content area */}
          <div className={styles.content}>
            {activeTab === 'playbook' ? (
              <>
                <PlaybookCardGrid
                  playbooks={playbooks}
                  selectedId={selectedPlaybook?.id}
                  onSelect={handlePlaybookSelect}
                  isLoading={false}
                />
                {selectedPlaybook && playbookScopes && (
                  <div className={styles.scopePreview}>
                    <ScopeConfigurator
                      actions={actions}
                      skills={skills}
                      knowledge={knowledge}
                      tools={tools}
                      selectedActionIds={playbookScopes.actionIds}
                      selectedSkillIds={playbookScopes.skillIds}
                      selectedKnowledgeIds={playbookScopes.knowledgeIds}
                      selectedToolIds={playbookScopes.toolIds}
                      onActionChange={() => {}}
                      onSkillChange={() => {}}
                      onKnowledgeChange={() => {}}
                      onToolChange={() => {}}
                      readOnly
                    />
                  </div>
                )}
              </>
            ) : (
              <ScopeConfigurator
                actions={actions}
                skills={skills}
                knowledge={knowledge}
                tools={tools}
                selectedActionIds={selectedActionIds}
                selectedSkillIds={selectedSkillIds}
                selectedKnowledgeIds={selectedKnowledgeIds}
                selectedToolIds={selectedToolIds}
                onActionChange={setSelectedActionIds}
                onSkillChange={setSelectedSkillIds}
                onKnowledgeChange={setSelectedKnowledgeIds}
                onToolChange={setSelectedToolIds}
              />
            )}
          </div>
        </>
      )}

      {/* Footer */}
      {!embedded && (
        <div className={styles.footer}>
          <Button appearance="secondary" onClick={handleCancel} disabled={isExecuting}>
            Cancel
          </Button>
          <Button appearance="primary" onClick={handleExecute} disabled={!canExecute || isExecuting}>
            {isExecuting ? 'Creating...' : executeButtonLabel}
          </Button>
        </div>
      )}
    </div>
  );
};

export default PlaybookLibraryShell;
