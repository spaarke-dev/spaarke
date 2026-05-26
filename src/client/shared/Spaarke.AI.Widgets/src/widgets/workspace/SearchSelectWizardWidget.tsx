/**
 * @spaarke/ai-widgets — SearchSelectWizardWidget
 *
 * Workspace widget that renders a two-step search-and-select picker in an
 * embedded workspace tab (no modal overlay). Replaces the modal
 * SearchSelectDialog for use cases where the selection should happen in the
 * workspace pane without interrupting the document or chat context.
 *
 * Behavior:
 * - Step 1 "Search": a search input + debounced result list (Fluent v9 List).
 *   The AI can pre-fill the search query via a `set-field` wizard_step event.
 * - Step 2 "Confirm": shows the selected item with entity details and a
 *   "Select" button. The AI can advance directly from step 1 → 2 when a
 *   result matches the requested entity name.
 *
 * On selection confirmed:
 * - Dispatches `context_update` on the `context` channel with the selected
 *   entity's id, type, and name as entity context so the Context pane can
 *   update its entity chip and ContextPaneController can load entity-specific
 *   context widgets.
 * - The parent (ConversationPane or shell) can listen on the `context` channel
 *   for `context_update { contextType: 'entity' }` to get the selection result.
 *
 * PaneEventBus integration:
 * - Subscribes to `wizard_step` events filtered by `wizardId`.
 * - `set-field { fieldName: 'searchQuery', fieldValue: '...' }` updates the
 *   search input and triggers a search automatically.
 * - `next` advances from search results to confirm step.
 * - `back` returns from confirm to search.
 *
 * Session restore (D-08):
 * - Serializes `wizardId`, `stepIndex`, and the `entityType` search scope.
 * - Search query and results are NOT serialized.
 *
 * React 19, NOT PCF-safe.
 *
 * Task: AIPU2-104
 *
 * @see WizardStepEvent    — PaneEventBus event type for AI-driven step control
 * @see WorkspaceWidgetProps — required component contract
 * @see ADR-012            — Shared component library (reuse, not copy)
 * @see ADR-021            — Fluent UI v9, no hard-coded colors
 */

import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  Button,
  Input,
  List,
  ListItem,
  Spinner,
  Text,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import {
  ArrowLeft24Regular,
  CheckmarkCircle24Regular,
  Search24Regular,
} from '@fluentui/react-icons';

import type { WorkspaceWidgetProps } from '../../types/widget-types';
import type { WidgetState } from '../../types/shared';
import { usePaneEvent } from '../../events/usePaneEvent';
import { useDispatchPaneEvent } from '../../events/useDispatchPaneEvent';
import type { WizardStepEvent } from '../../events/PaneEventTypes';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** A single search result item. */
export interface SearchResultItem {
  /** Dataverse record GUID. */
  id: string;
  /** Display name. */
  name: string;
  /** Entity logical name (e.g. `"sprk_matter"`, `"account"`, `"contact"`). */
  entityType: string;
  /** Optional secondary label (e.g. matter type, account number). */
  subtitle?: string;
}

/**
 * Data payload delivered to this widget on mount or via SSE.
 */
export interface SearchSelectWizardData {
  /** Stable identifier for this wizard instance. */
  wizardId: string;

  /**
   * Async search function injected by the shell.
   * Called with the current query string; returns matching items.
   */
  onSearch?: (query: string) => Promise<SearchResultItem[]>;

  /**
   * Entity type scope for the search (e.g. `"sprk_matter"`, `"account"`).
   * Used as a display label and for session restore.
   */
  entityType?: string;

  /**
   * Human-readable label for the entity type (e.g. `"Matter"`, `"Account"`).
   * Used in the UI for step headings and confirmation text.
   */
  entityLabel?: string;

  /** Initial step index to restore to (0-based). Default: 0. */
  initialStepIndex?: number;
}

// ---------------------------------------------------------------------------
// Serialized query params (D-08)
// ---------------------------------------------------------------------------

export interface SearchSelectWizardQueryParams extends Record<string, string> {
  wizardId: string;
  stepIndex: string;
  entityType: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
    backgroundColor: tokens.colorNeutralBackground1,
    boxSizing: 'border-box',
  },

  // Stepper (replaces modal title chrome)
  stepper: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalL}`,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    flexShrink: 0,
  },
  stepItem: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground4,
    fontSize: tokens.fontSizeBase300,
  },
  stepItemActive: {
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  stepItemCompleted: {
    color: tokens.colorNeutralForeground3,
  },
  stepConnector: {
    width: '24px',
    height: '1px',
    backgroundColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
  stepDot: {
    width: '8px',
    height: '8px',
    borderRadius: '50%',
    backgroundColor: 'currentColor',
    flexShrink: 0,
  },

  // Content
  content: {
    flex: 1,
    minHeight: 0,
    overflow: 'auto',
    padding: `${tokens.spacingVerticalL} ${tokens.spacingHorizontalL}`,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },

  // Search input row
  searchRow: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
    alignItems: 'flex-start',
  },
  searchInput: {
    flex: 1,
  },

  // Results list
  resultsList: {
    flex: 1,
    minHeight: 0,
    overflow: 'auto',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
  },
  resultItem: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    cursor: 'pointer',
    borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground3,
    },
    ':last-child': {
      borderBottom: 'none',
    },
  },
  resultItemSelected: {
    backgroundColor: tokens.colorBrandBackground2,
    ':hover': {
      backgroundColor: tokens.colorBrandBackground2Hover,
    },
  },
  resultName: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
  },
  resultSubtitle: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },

  // Confirm card
  confirmCard: {
    padding: tokens.spacingHorizontalL,
    border: `2px solid ${tokens.colorBrandStroke1}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorBrandBackground2,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  confirmIcon: {
    color: tokens.colorBrandForeground1,
    fontSize: '32px',
    alignSelf: 'flex-start',
  },
  confirmName: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
  },
  confirmSubtitle: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase300,
  },

  // Footer
  footer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalL}`,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    flexShrink: 0,
  },

  centered: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    flex: 1,
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },

  successIcon: {
    color: tokens.colorPaletteGreenForeground1,
    fontSize: '48px',
  },

  noResults: {
    padding: tokens.spacingHorizontalM,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase300,
    fontStyle: 'italic',
    textAlign: 'center',
  },
});

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const STEP_LABELS = ['Search', 'Confirm'] as const;
const DEBOUNCE_MS = 350;

// ---------------------------------------------------------------------------
// Main component
// ---------------------------------------------------------------------------

/**
 * SearchSelectWizardWidget
 *
 * Embedded search-and-select picker for the workspace pane. Replaces the
 * modal SearchSelectDialog pattern with an inline two-step flow that keeps
 * the document and conversation context visible.
 */
const SearchSelectWizardWidget: React.FC<WorkspaceWidgetProps<SearchSelectWizardData>> = ({
  data,
  isLoading,
  error,
  className,
}) => {
  const styles = useStyles();
  const dispatch = useDispatchPaneEvent();

  const wizardId = data?.wizardId ?? 'search-select';
  const entityLabel = data?.entityLabel ?? 'Record';

  // ── Step state ────────────────────────────────────────────────────────────
  const [stepIndex, setStepIndex] = useState<number>(data?.initialStepIndex ?? 0);

  // ── Search state ──────────────────────────────────────────────────────────
  const [searchQuery, setSearchQuery] = useState('');
  const [results, setResults] = useState<SearchResultItem[]>([]);
  const [isSearching, setIsSearching] = useState(false);
  const [searchError, setSearchError] = useState<string | null>(null);
  const [selectedItem, setSelectedItem] = useState<SearchResultItem | null>(null);

  // ── Completion state ──────────────────────────────────────────────────────
  const [isConfirmed, setIsConfirmed] = useState(false);

  // ── Debounced search ─────────────────────────────────────────────────────
  const debounceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const runSearch = useCallback(async (query: string) => {
    if (!data?.onSearch) return;
    if (!query.trim()) {
      setResults([]);
      return;
    }

    setIsSearching(true);
    setSearchError(null);

    try {
      const items = await data.onSearch(query.trim());
      setResults(items);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Search failed. Please try again.';
      setSearchError(message);
      setResults([]);
    } finally {
      setIsSearching(false);
    }
  }, [data]);

  const handleSearchQueryChange = useCallback((newQuery: string) => {
    setSearchQuery(newQuery);
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current);
    }
    debounceTimerRef.current = setTimeout(() => {
      void runSearch(newQuery);
    }, DEBOUNCE_MS);
  }, [runSearch]);

  // Cleanup debounce on unmount
  useEffect(() => {
    return () => {
      if (debounceTimerRef.current) {
        clearTimeout(debounceTimerRef.current);
      }
    };
  }, []);

  // ── PaneEventBus: wizard_step events ─────────────────────────────────────
  usePaneEvent('workspace', useCallback((event) => {
    if (event.type !== 'wizard_step') return;
    const wizardEvent = event as WizardStepEvent;
    if (wizardEvent.wizardId !== wizardId) return;

    switch (wizardEvent.wizardAction) {
      case 'next':
        if (stepIndex === 0 && selectedItem) {
          setStepIndex(1);
          dispatch('context', {
            type: 'stage_change',
            contextType: 'wizard-step',
            contextData: { wizardId, wizardType: 'search-select', stepIndex: 1, stepLabel: 'Confirm' },
          });
        }
        break;

      case 'back':
        if (stepIndex === 1) {
          setStepIndex(0);
          dispatch('context', {
            type: 'stage_change',
            contextType: 'wizard-step',
            contextData: { wizardId, wizardType: 'search-select', stepIndex: 0, stepLabel: 'Search' },
          });
        }
        break;

      case 'set-field':
        if (wizardEvent.fieldName === 'searchQuery' && typeof wizardEvent.fieldValue === 'string') {
          handleSearchQueryChange(wizardEvent.fieldValue);
        }
        break;
    }
  }, [wizardId, stepIndex, selectedItem, dispatch, handleSearchQueryChange]));

  // ── Item selection ────────────────────────────────────────────────────────
  const handleSelectItem = useCallback((item: SearchResultItem) => {
    setSelectedItem(item);
  }, []);

  const handleAdvanceToConfirm = useCallback(() => {
    if (!selectedItem) return;
    setStepIndex(1);
    dispatch('context', {
      type: 'stage_change',
      contextType: 'wizard-step',
      contextData: { wizardId, wizardType: 'search-select', stepIndex: 1, stepLabel: 'Confirm' },
    });
  }, [selectedItem, wizardId, dispatch]);

  const handleBack = useCallback(() => {
    setStepIndex(0);
    dispatch('context', {
      type: 'stage_change',
      contextType: 'wizard-step',
      contextData: { wizardId, wizardType: 'search-select', stepIndex: 0, stepLabel: 'Search' },
    });
  }, [wizardId, dispatch]);

  // ── Confirm selection ─────────────────────────────────────────────────────
  const handleConfirm = useCallback(() => {
    if (!selectedItem) return;

    setIsConfirmed(true);

    // Dispatch the selected entity as context so the Context pane updates
    // and the ConversationPane can use it as entity context for subsequent turns.
    dispatch('context', {
      type: 'context_update',
      contextType: 'entity',
      contextData: {
        entityId: selectedItem.id,
        entityType: selectedItem.entityType,
        entityName: selectedItem.name,
        entitySubtitle: selectedItem.subtitle,
        wizardId,
        source: 'search-select-wizard',
      },
    });
  }, [selectedItem, wizardId, dispatch]);

  // ── Render: loading ───────────────────────────────────────────────────────
  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.centered}>
          <Spinner size="medium" label="Loading search wizard..." />
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.centered}>
          <Text style={{ color: tokens.colorStatusDangerForeground1 }}>{error}</Text>
        </div>
      </div>
    );
  }

  // ── Render: confirmed ─────────────────────────────────────────────────────
  if (isConfirmed && selectedItem) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.centered}>
          <CheckmarkCircle24Regular className={styles.successIcon} />
          <Text size={400} weight="semibold">
            {entityLabel} selected
          </Text>
          <Text style={{ color: tokens.colorNeutralForeground3 }}>
            {selectedItem.name} has been set as the active {entityLabel.toLowerCase()}.
          </Text>
        </div>
      </div>
    );
  }

  // ── Render: wizard ────────────────────────────────────────────────────────
  return (
    <div className={mergeClasses(styles.root, className)}>
      {/* Stepper */}
      <div className={styles.stepper} role="list" aria-label="Wizard progress">
        {STEP_LABELS.map((label, i) => (
          <React.Fragment key={label}>
            {i > 0 && <div className={styles.stepConnector} aria-hidden />}
            <div
              className={mergeClasses(
                styles.stepItem,
                i === stepIndex && styles.stepItemActive,
                i < stepIndex && styles.stepItemCompleted
              )}
              role="listitem"
              aria-current={i === stepIndex ? 'step' : undefined}
              aria-label={`Step ${i + 1}: ${label}${i < stepIndex ? ' (completed)' : ''}`}
            >
              <div className={styles.stepDot} aria-hidden />
              <span>{label}</span>
            </div>
          </React.Fragment>
        ))}
      </div>

      {/* Step content */}
      <div className={styles.content}>
        {stepIndex === 0 && (
          <>
            <Text as="h2" size={500} weight="semibold">
              Search for a {entityLabel}
            </Text>
            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
              Type to search. Select a result to continue.
            </Text>

            <div className={styles.searchRow}>
              <Input
                className={styles.searchInput}
                value={searchQuery}
                onChange={(_, v) => handleSearchQueryChange(v.value)}
                placeholder={`Search ${entityLabel.toLowerCase()}s...`}
                contentBefore={<Search24Regular style={{ color: tokens.colorNeutralForeground3 }} />}
                aria-label={`Search ${entityLabel.toLowerCase()}s`}
              />
              {isSearching && <Spinner size="tiny" />}
            </div>

            {searchError && (
              <Text style={{ color: tokens.colorStatusDangerForeground1 }}>{searchError}</Text>
            )}

            {!isSearching && results.length === 0 && searchQuery.trim() && (
              <div className={styles.noResults}>
                No {entityLabel.toLowerCase()}s found matching &ldquo;{searchQuery}&rdquo;
              </div>
            )}

            {results.length > 0 && (
              <div className={styles.resultsList} role="listbox" aria-label={`${entityLabel} search results`}>
                <List>
                  {results.map(item => (
                    <ListItem
                      key={item.id}
                      className={mergeClasses(
                        styles.resultItem,
                        selectedItem?.id === item.id && styles.resultItemSelected
                      )}
                      onClick={() => handleSelectItem(item)}
                      role="option"
                      aria-selected={selectedItem?.id === item.id}
                      aria-label={`${item.name}${item.subtitle ? `, ${item.subtitle}` : ''}`}
                      tabIndex={0}
                      onKeyDown={e => {
                        if (e.key === 'Enter' || e.key === ' ') {
                          e.preventDefault();
                          handleSelectItem(item);
                        }
                      }}
                    >
                      <Text className={styles.resultName}>{item.name}</Text>
                      {item.subtitle && (
                        <Text className={styles.resultSubtitle}>{item.subtitle}</Text>
                      )}
                    </ListItem>
                  ))}
                </List>
              </div>
            )}
          </>
        )}

        {stepIndex === 1 && selectedItem && (
          <>
            <Text as="h2" size={500} weight="semibold">
              Confirm Selection
            </Text>
            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
              Confirm your {entityLabel.toLowerCase()} selection below.
            </Text>

            <div className={styles.confirmCard}>
              <CheckmarkCircle24Regular className={styles.confirmIcon} />
              <Text className={styles.confirmName}>{selectedItem.name}</Text>
              {selectedItem.subtitle && (
                <Text className={styles.confirmSubtitle}>{selectedItem.subtitle}</Text>
              )}
              <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                Type: {selectedItem.entityType}
              </Text>
            </div>
          </>
        )}
      </div>

      {/* Footer — workspace-native navigation, no modal chrome */}
      <div className={styles.footer}>
        {stepIndex === 1 && (
          <Button
            appearance="subtle"
            icon={<ArrowLeft24Regular />}
            onClick={handleBack}
            aria-label="Back"
            data-testid="wizard-back-button"
          >
            Back
          </Button>
        )}

        {stepIndex === 0 ? (
          <Button
            appearance="primary"
            onClick={handleAdvanceToConfirm}
            disabled={!selectedItem}
            aria-label="Next"
            data-testid="wizard-next-button"
          >
            Next
          </Button>
        ) : (
          <Button
            appearance="primary"
            icon={<CheckmarkCircle24Regular />}
            onClick={handleConfirm}
            disabled={!selectedItem}
            aria-label={`Select ${selectedItem?.name ?? entityLabel}`}
            data-testid="wizard-next-button"
          >
            Select {entityLabel}
          </Button>
        )}
      </div>
    </div>
  );
};

// ---------------------------------------------------------------------------
// serializeState helper (D-08 — query params only)
// ---------------------------------------------------------------------------

/**
 * Serialize the widget's recoverable state for Cosmos DB persistence.
 * Stores the wizardId, stepIndex, and entityType scope so the shell can
 * reopen the correct search scope on restore. Search query and results
 * are NOT serialized.
 */
export function serializeSearchSelectWizardState(
  wizardId: string,
  stepIndex: number,
  entityType: string
): WidgetState<SearchSelectWizardData> {
  return {
    widgetType: 'search-select-wizard',
    version: 1,
    queryParams: {
      wizardId,
      stepIndex: String(stepIndex),
      entityType,
    },
    timestamp: new Date().toISOString(),
  };
}

SearchSelectWizardWidget.displayName = 'SearchSelectWizardWidget';

export default SearchSelectWizardWidget;
