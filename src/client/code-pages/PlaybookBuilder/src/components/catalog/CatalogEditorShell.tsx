/**
 * CatalogEditorShell — the PlaybookBuilder page after the FR-P4-04 de-scope.
 *
 * "A new AI capability is a catalog row a business analyst authors": two tabs
 * over the two closed catalogs — Actions (`sprk_analysisaction`, execution
 * units) and Bindings (`sprk_playbookconsumer`, invocation units). No graph
 * canvas exists anywhere on this page (ratified OQ-2; the engine is frozen).
 *
 * Save path: Dataverse Web API via catalogService (validation-gated — see
 * catalogService.ts header for the DATA-ACCESS-DECISION-CRITERIA citation).
 *
 * NFR-06: every successful save surfaces the eval-suite reminder — catalog /
 * prompt changes must add or refresh an eval case before merge.
 *
 * ADR-021: Fluent v9 tokens only; verified in light + dark themes.
 */

import { useCallback, useEffect, useState } from 'react';
import {
  Button,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  Tab,
  TabList,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { Add20Regular, Save20Regular } from '@fluentui/react-icons';
import {
  CatalogValidationError,
  listActions,
  listBindings,
  saveAction,
  saveBinding,
  validateActionRow,
  validateBindingRow,
} from '../../services/catalogService';
import type { ValidationErrors } from '../../services/catalogService';
import { newActionRow, newBindingRow } from '../../types/catalog';
import type { ActionRow, BindingRow } from '../../types/catalog';
import { ActionEditorForm } from './ActionEditorForm';
import { BindingEditorForm } from './BindingEditorForm';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalL}`,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground3,
  },
  title: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
  },
  body: {
    display: 'flex',
    flex: 1,
    overflow: 'hidden',
  },
  listPane: {
    width: '320px',
    flexShrink: 0,
    display: 'flex',
    flexDirection: 'column',
    borderRight: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2,
    overflowY: 'auto',
  },
  listActionsBar: {
    display: 'flex',
    justifyContent: 'flex-end',
    padding: tokens.spacingVerticalS,
  },
  listItem: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    cursor: 'pointer',
    borderBottom: `1px solid ${tokens.colorNeutralStroke3}`,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground2Hover,
    },
  },
  listItemSelected: {
    backgroundColor: tokens.colorNeutralBackground1Selected,
    borderLeft: `3px solid ${tokens.colorBrandStroke1}`,
  },
  listItemCode: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    fontFamily: tokens.fontFamilyMonospace,
  },
  editorPane: {
    flex: 1,
    overflowY: 'auto',
    padding: tokens.spacingHorizontalL,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  editorToolbar: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
  },
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    flex: 1,
    gap: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground3,
  },
  loading: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    flex: 1,
    gap: tokens.spacingHorizontalM,
  },
});

type CatalogTab = 'actions' | 'bindings';

export function CatalogEditorShell(): JSX.Element {
  const styles = useStyles();

  const [tab, setTab] = useState<CatalogTab>('actions');
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [actions, setActions] = useState<ActionRow[]>([]);
  const [bindings, setBindings] = useState<BindingRow[]>([]);

  const [draftAction, setDraftAction] = useState<ActionRow | null>(null);
  const [draftBinding, setDraftBinding] = useState<BindingRow | null>(null);
  const [formErrors, setFormErrors] = useState<ValidationErrors>({});

  const [isSaving, setIsSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [savedRowLabel, setSavedRowLabel] = useState<string | null>(null);

  const reload = useCallback(async () => {
    setIsLoading(true);
    setLoadError(null);
    try {
      const [actionRows, bindingRows] = await Promise.all([listActions(), listBindings()]);
      setActions(actionRows);
      setBindings(bindingRows);
    } catch (err) {
      setLoadError(err instanceof Error ? err.message : 'Failed to load catalogs.');
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    void reload();
  }, [reload]);

  const switchTab = (next: CatalogTab): void => {
    setTab(next);
    setFormErrors({});
    setSaveError(null);
  };

  const selectAction = (row: ActionRow): void => {
    setDraftAction({ ...row });
    setFormErrors({});
    setSaveError(null);
    setSavedRowLabel(null);
  };

  const selectBinding = (row: BindingRow): void => {
    setDraftBinding({ ...row });
    setFormErrors({});
    setSaveError(null);
    setSavedRowLabel(null);
  };

  const handleSave = async (): Promise<void> => {
    setSaveError(null);
    setSavedRowLabel(null);

    if (tab === 'actions' && draftAction) {
      const errors = validateActionRow(draftAction);
      setFormErrors(errors);
      if (Object.keys(errors).length > 0) return;

      setIsSaving(true);
      try {
        const id = await saveAction(draftAction);
        setDraftAction({ ...draftAction, id });
        setSavedRowLabel(`Action ${draftAction.actionCode}`);
        await reload();
      } catch (err) {
        if (err instanceof CatalogValidationError) setFormErrors(err.errors);
        else setSaveError(err instanceof Error ? err.message : 'Save failed.');
      } finally {
        setIsSaving(false);
      }
    }

    if (tab === 'bindings' && draftBinding) {
      const errors = validateBindingRow(draftBinding);
      setFormErrors(errors);
      if (Object.keys(errors).length > 0) return;

      setIsSaving(true);
      try {
        const id = await saveBinding(draftBinding);
        setDraftBinding({ ...draftBinding, id });
        setSavedRowLabel(`Binding ${draftBinding.consumerType}`);
        await reload();
      } catch (err) {
        if (err instanceof CatalogValidationError) setFormErrors(err.errors);
        else setSaveError(err instanceof Error ? err.message : 'Save failed.');
      } finally {
        setIsSaving(false);
      }
    }
  };

  const hasDraft = tab === 'actions' ? draftAction !== null : draftBinding !== null;

  const renderList = (): JSX.Element => {
    if (tab === 'actions') {
      return (
        <>
          {actions.map(row => (
            <div
              key={row.id}
              className={draftAction?.id === row.id ? `${styles.listItem} ${styles.listItemSelected}` : styles.listItem}
              role="button"
              tabIndex={0}
              data-testid={`action-list-item-${row.actionCode}`}
              onClick={() => selectAction(row)}
              onKeyDown={ev => {
                if (ev.key === 'Enter' || ev.key === ' ') selectAction(row);
              }}
            >
              <Text weight="semibold">{row.name || '(unnamed)'}</Text>
              <Text className={styles.listItemCode}>{row.actionCode}</Text>
            </div>
          ))}
        </>
      );
    }
    return (
      <>
        {bindings.map(row => (
          <div
            key={row.id}
            className={draftBinding?.id === row.id ? `${styles.listItem} ${styles.listItemSelected}` : styles.listItem}
            role="button"
            tabIndex={0}
            data-testid={`binding-list-item-${row.consumerType}`}
            onClick={() => selectBinding(row)}
            onKeyDown={ev => {
              if (ev.key === 'Enter' || ev.key === ' ') selectBinding(row);
            }}
          >
            <Text weight="semibold">{row.name || row.consumerType || '(unnamed)'}</Text>
            <Text className={styles.listItemCode}>
              {row.consumerType}
              {row.enabled ? '' : ' · disabled'}
            </Text>
          </div>
        ))}
      </>
    );
  };

  return (
    <div className={styles.root} data-testid="catalog-editor-shell">
      <div className={styles.header}>
        <Text className={styles.title}>AI Capability Catalog</Text>
        <TabList selectedValue={tab} onTabSelect={(_ev, data) => switchTab(data.value as CatalogTab)}>
          <Tab value="actions" data-testid="tab-actions">
            Actions
          </Tab>
          <Tab value="bindings" data-testid="tab-bindings">
            Bindings
          </Tab>
        </TabList>
      </div>

      {isLoading ? (
        <div className={styles.loading}>
          <Spinner size="medium" />
          <Text>Loading catalogs…</Text>
        </div>
      ) : loadError ? (
        <div className={styles.emptyState}>
          <MessageBar intent="error">
            <MessageBarBody>
              <MessageBarTitle>Failed to load catalogs</MessageBarTitle>
              {loadError}
            </MessageBarBody>
          </MessageBar>
          <Button appearance="secondary" onClick={() => void reload()}>
            Retry
          </Button>
        </div>
      ) : (
        <div className={styles.body}>
          <div className={styles.listPane}>
            <div className={styles.listActionsBar}>
              <Button
                appearance="primary"
                size="small"
                icon={<Add20Regular />}
                data-testid="new-row-button"
                onClick={() => {
                  if (tab === 'actions') selectAction(newActionRow());
                  else selectBinding(newBindingRow());
                }}
              >
                {tab === 'actions' ? 'New Action' : 'New Binding'}
              </Button>
            </div>
            {renderList()}
          </div>

          <div className={styles.editorPane}>
            {savedRowLabel && (
              <MessageBar intent="success" data-testid="save-success-bar">
                <MessageBarBody>
                  <MessageBarTitle>{savedRowLabel} saved.</MessageBarTitle>
                  NFR-06 reminder: catalog and prompt changes must add or refresh an eval case (tests/unit
                  GoldenUtteranceEval suite) before merge — the eval gate is green-to-merge.
                </MessageBarBody>
              </MessageBar>
            )}
            {saveError && (
              <MessageBar intent="error" data-testid="save-error-bar">
                <MessageBarBody>
                  <MessageBarTitle>Save failed</MessageBarTitle>
                  {saveError}
                </MessageBarBody>
              </MessageBar>
            )}
            {Object.keys(formErrors).length > 0 && (
              <MessageBar intent="error" data-testid="validation-error-bar">
                <MessageBarBody>
                  <MessageBarTitle>Fix authoring errors before saving</MessageBarTitle>
                  Invalid catalog rows are never written — an invalid input schema previously took down every assistant
                  turn (G-P3 round 1).
                </MessageBarBody>
              </MessageBar>
            )}

            {!hasDraft ? (
              <div className={styles.emptyState}>
                <Text size={400} weight="semibold">
                  {tab === 'actions' ? 'Select an Action or create a new one' : 'Select a Binding or create a new one'}
                </Text>
                <Text>
                  {tab === 'actions'
                    ? 'Actions are execution units: prompt + schemas + model tier (sprk_analysisaction).'
                    : 'Bindings are invocation units: routing + disposition + chips + events (sprk_playbookconsumer).'}
                </Text>
              </div>
            ) : (
              <>
                <div className={styles.editorToolbar}>
                  <Button
                    appearance="primary"
                    icon={<Save20Regular />}
                    disabled={isSaving}
                    data-testid="save-row-button"
                    onClick={() => void handleSave()}
                  >
                    {isSaving ? 'Saving…' : 'Save'}
                  </Button>
                  {tab === 'actions' && draftAction?.id && (
                    <Text className={styles.listItemCode}>id: {draftAction.id}</Text>
                  )}
                  {tab === 'bindings' && draftBinding?.id && (
                    <Text className={styles.listItemCode}>id: {draftBinding.id}</Text>
                  )}
                </div>

                {tab === 'actions' && draftAction && (
                  <ActionEditorForm row={draftAction} errors={formErrors} onChange={setDraftAction} />
                )}
                {tab === 'bindings' && draftBinding && (
                  <BindingEditorForm
                    row={draftBinding}
                    actions={actions}
                    bindings={bindings}
                    errors={formErrors}
                    onChange={setDraftBinding}
                  />
                )}
              </>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
