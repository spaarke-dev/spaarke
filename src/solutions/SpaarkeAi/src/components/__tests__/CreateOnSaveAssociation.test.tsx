/**
 * CreateOnSaveAssociation — unit tests (FR-05)
 *
 * Covers:
 *   - association-choices-render: all five choices render (None / Matter /
 *     Project / Invoice / Work Assignment) in Fluent v9.
 *   - write-on-select: selecting a parent type + record fires onChange with
 *     an AssociationResult, and associateDocumentToParent writes the
 *     resolved @odata.bind lookup onto sprk_document via IDataService.
 *   - none-is-standalone: choosing "None" fires onChange(null); the
 *     write path (associateDocumentToParent / useCreateOnSaveAssociation's
 *     associate()) no-ops without touching IDataService -- Save is never
 *     blocked on a parent.
 *   - dark-mode: the prompt renders under webDarkTheme without error and
 *     with no bespoke dialog/overlay chrome of its own (plain section,
 *     meant to be hosted inside the existing Tier-2c gate dialog).
 *
 * @see CreateOnSaveAssociationPrompt.tsx — component under test
 * @see documentAssociationWrite.ts — write path under test
 * @see useCreateOnSaveAssociation.ts — hook under test
 * @see spec.md FR-05
 * @see ADR-021 — Fluent v9 + dark mode
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, waitFor, act, renderHook } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import type { AssociationResult, IDataService, INavigationService } from '@spaarke/ui-components';

import { CreateOnSaveAssociationPrompt } from '../compose/CreateOnSaveAssociationPrompt';
import { associateDocumentToParent } from '../compose/documentAssociationWrite';
import { useCreateOnSaveAssociation } from '../compose/useCreateOnSaveAssociation';
import { useCreateOnSaveAssociationGate } from '../compose/useCreateOnSaveAssociationGate';
import { CreateOnSaveAssociationGateDialog } from '../compose/CreateOnSaveAssociationGateDialog';

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

function renderLight(ui: React.ReactElement) {
  return render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);
}

function renderDark(ui: React.ReactElement) {
  return render(<FluentProvider theme={webDarkTheme}>{ui}</FluentProvider>);
}

function createMockNavigationService(pickedResult?: { id: string; entityType: string; name: string }): INavigationService {
  return {
    openLookup: jest.fn().mockResolvedValue(pickedResult ? [pickedResult] : []),
  } as unknown as INavigationService;
}

function createMockDataService(overrides: Partial<IDataService> = {}): IDataService {
  return {
    createRecord: jest.fn().mockResolvedValue('00000000-0000-0000-0000-000000000001'),
    retrieveRecord: jest.fn().mockResolvedValue({}),
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    updateRecord: jest.fn().mockResolvedValue(undefined),
    deleteRecord: jest.fn().mockResolvedValue(undefined),
    ...overrides,
  } as IDataService;
}

/** Controlled test harness -- mirrors how a real host wires value/onChange. */
function Harness(props: {
  navigationService: INavigationService;
  onChangeSpy: (result: AssociationResult | null) => void;
}) {
  const [value, setValue] = React.useState<AssociationResult | null>(null);
  return (
    <CreateOnSaveAssociationPrompt
      navigationService={props.navigationService}
      value={value}
      onChange={result => {
        setValue(result);
        props.onChangeSpy(result);
      }}
    />
  );
}

describe('CreateOnSaveAssociationPrompt', () => {
  describe('association-choices-render', () => {
    it('rendersAllFiveChoices', () => {
      renderLight(<CreateOnSaveAssociationPrompt navigationService={createMockNavigationService()} value={null} onChange={jest.fn()} />);

      expect(screen.getByTestId('association-choice-none')).toBeInTheDocument();
      expect(screen.getByTestId('association-choice-sprk_matter')).toBeInTheDocument();
      expect(screen.getByTestId('association-choice-sprk_project')).toBeInTheDocument();
      expect(screen.getByTestId('association-choice-sprk_invoice')).toBeInTheDocument();
      expect(screen.getByTestId('association-choice-sprk_workassignment')).toBeInTheDocument();
    });

    it('defaultsToNoneWithNoBespokeDialogChrome', () => {
      renderLight(<CreateOnSaveAssociationPrompt navigationService={createMockNavigationService()} value={null} onChange={jest.fn()} />);

      // Plain section -- not a dialog/overlay of its own (meant to be hosted
      // inside the existing Tier-2c gate dialog).
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(screen.getByTestId('association-standalone-note')).toBeInTheDocument();
    });
  });

  describe('write-on-select', () => {
    it('firesOnChangeWithAssociationResultWhenRecordSelected', async () => {
      const user = userEvent.setup();
      const onChangeSpy = jest.fn();
      const navigationService = createMockNavigationService({
        id: '{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}',
        entityType: 'sprk_matter',
        name: 'Smith v. Jones',
      });

      renderLight(<Harness navigationService={navigationService} onChangeSpy={onChangeSpy} />);

      await user.click(screen.getByTestId('association-choice-sprk_matter'));
      await user.click(screen.getByTestId('associate-to-step-select-record-button'));

      await waitFor(() => {
        expect(onChangeSpy).toHaveBeenCalledWith({
          entityType: 'sprk_matter',
          recordId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
          recordName: 'Smith v. Jones',
        });
      });
    });

    it('writesTheResolvedLookupOntoSprkDocument', async () => {
      const fetchMock = jest.fn().mockResolvedValue({
        ok: true,
        json: async () => ({
          value: [
            {
              ReferencingAttribute: 'sprk_matter',
              ReferencingEntityNavigationPropertyName: 'sprk_Matter',
              ReferencedEntity: 'sprk_matter',
            },
          ],
        }),
      });
      (global as unknown as { fetch: typeof fetch }).fetch = fetchMock as unknown as typeof fetch;

      const dataService = createMockDataService();
      const association: AssociationResult = {
        entityType: 'sprk_matter',
        recordId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        recordName: 'Smith v. Jones',
      };

      const result = await associateDocumentToParent(dataService, 'doc-guid-1', association);

      expect(result.success).toBe(true);
      expect(dataService.updateRecord).toHaveBeenCalledWith('sprk_document', 'doc-guid-1', {
        'sprk_Matter@odata.bind': '/sprk_matters(aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee)',
      });
    });
  });

  describe('none-is-standalone', () => {
    it('firesOnChangeNullWhenNoneChosen', async () => {
      const user = userEvent.setup();
      const onChangeSpy = jest.fn();
      const navigationService = createMockNavigationService({
        id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        entityType: 'sprk_matter',
        name: 'Smith v. Jones',
      });

      renderLight(<Harness navigationService={navigationService} onChangeSpy={onChangeSpy} />);

      // Pick a type first, then explicitly return to "None".
      await user.click(screen.getByTestId('association-choice-sprk_project'));
      await user.click(screen.getByTestId('association-choice-none'));

      expect(onChangeSpy).toHaveBeenLastCalledWith(null);
      expect(screen.getByTestId('association-standalone-note')).toBeInTheDocument();
    });

    it('associateDocumentToParentNoOpsForNoneWithoutTouchingDataService', async () => {
      const dataService = createMockDataService();

      const result = await associateDocumentToParent(dataService, 'doc-guid-1', null);

      expect(result.success).toBe(true);
      expect(dataService.updateRecord).not.toHaveBeenCalled();
    });

    it('useCreateOnSaveAssociationAssociateNoOpsWhenSelectionIsNone', async () => {
      const dataService = createMockDataService();
      const { result } = renderHook(() => useCreateOnSaveAssociation({ dataService }));

      expect(result.current.association).toBeNull();

      let outcome: { success: boolean } | undefined;
      await act(async () => {
        outcome = await result.current.associate('doc-guid-1');
      });

      expect(outcome?.success).toBe(true);
      expect(dataService.updateRecord).not.toHaveBeenCalled();
      expect(result.current.isAssociating).toBe(false);
      expect(result.current.error).toBeNull();
    });
  });

  describe('dark-mode', () => {
    it('rendersWithoutErrorUnderDarkTheme', () => {
      renderDark(<CreateOnSaveAssociationPrompt navigationService={createMockNavigationService()} value={null} onChange={jest.fn()} />);

      expect(screen.getByTestId('create-on-save-association-prompt')).toBeInTheDocument();
      expect(screen.getByTestId('association-choice-radio-group')).toBeInTheDocument();
      // No bespoke confirmation banner/dialog surface of its own.
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });
});

// ---------------------------------------------------------------------------
// Gate-dialog hosting (task 014-split) — the REMAINING half: render the picker
// INSIDE the Tier-2c gate dialog and wire selection → setAssociation → the
// already-landed associate() write on create-on-save completion.
// ---------------------------------------------------------------------------

/**
 * Harness mirroring the real ThreePaneShell wiring: `useCreateOnSaveAssociationGate`
 * owns the gate; a button stands in for the ComposeLaunchContext
 * `onCreateOnSaveComplete(newDocumentId)` callback that ComposeWorkspace fires
 * once a transient draft is persisted.
 */
function GateHarness(props: {
  navigationService: INavigationService;
  dataService: IDataService;
  documentId: string;
}) {
  const { onCreateOnSaveComplete, dialogProps } = useCreateOnSaveAssociationGate({
    dataService: props.dataService,
    navigationService: props.navigationService,
  });
  return (
    <>
      <button data-testid="fire-create-on-save" onClick={() => onCreateOnSaveComplete(props.documentId)}>
        fire create-on-save
      </button>
      <CreateOnSaveAssociationGateDialog {...dialogProps} />
    </>
  );
}

/** Nav-prop discovery fetch mock — resolves sprk_document → sprk_Matter nav prop. */
function stubNavPropFetch() {
  const fetchMock = jest.fn().mockResolvedValue({
    ok: true,
    json: async () => ({
      value: [
        {
          ReferencingAttribute: 'sprk_matter',
          ReferencingEntityNavigationPropertyName: 'sprk_Matter',
          ReferencedEntity: 'sprk_matter',
        },
      ],
    }),
  });
  (global as unknown as { fetch: typeof fetch }).fetch = fetchMock as unknown as typeof fetch;
  return fetchMock;
}

describe('CreateOnSaveAssociationGate (Tier-2c gate hosting)', () => {
  describe('picker-renders-in-gate', () => {
    it('gateIsClosedUntilCreateOnSaveCompletes', () => {
      renderLight(
        <GateHarness
          navigationService={createMockNavigationService()}
          dataService={createMockDataService()}
          documentId="doc-guid-1"
        />
      );

      // Inert until a create-on-save fires — no dialog, no picker mounted.
      expect(screen.queryByTestId('create-on-save-association-gate')).not.toBeInTheDocument();
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('rendersThePickerInsideTheGateDialogOnCreateOnSave', async () => {
      const user = userEvent.setup();
      renderLight(
        <GateHarness
          navigationService={createMockNavigationService()}
          dataService={createMockDataService()}
          documentId="doc-guid-1"
        />
      );

      await user.click(screen.getByTestId('fire-create-on-save'));

      // The gate dialog is now open and HOSTS the FR-05 picker (all five choices),
      // inside the real Tier-2c dialog surface — not a bespoke banner.
      expect(await screen.findByTestId('create-on-save-association-gate')).toBeInTheDocument();
      expect(screen.getByRole('dialog')).toBeInTheDocument();
      expect(screen.getByTestId('create-on-save-association-prompt')).toBeInTheDocument();
      expect(screen.getByTestId('association-choice-none')).toBeInTheDocument();
      expect(screen.getByTestId('association-choice-sprk_matter')).toBeInTheDocument();
      expect(screen.getByTestId('association-choice-sprk_project')).toBeInTheDocument();
      expect(screen.getByTestId('association-choice-sprk_invoice')).toBeInTheDocument();
      expect(screen.getByTestId('association-choice-sprk_workassignment')).toBeInTheDocument();
    });
  });

  describe('select→setAssociation→associate (with cleanGuid)', () => {
    it('writesTheChosenParentWithACleanGuidWrappedDocumentIdOnConfirm', async () => {
      const user = userEvent.setup();
      stubNavPropFetch();
      const dataService = createMockDataService();
      // Braced + uppercase parent id from the Xrm lookup, AND a braced + uppercase
      // document id from the server-minted sprk_documentid — both MUST be
      // cleanGuid-normalized before the @odata.bind / entityset URL.
      const navigationService = createMockNavigationService({
        id: '{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}',
        entityType: 'sprk_matter',
        name: 'Smith v. Jones',
      });

      renderLight(
        <GateHarness
          navigationService={navigationService}
          dataService={dataService}
          documentId="{FFFFFFFF-1111-2222-3333-444444444444}"
        />
      );

      // create-on-save completes → gate opens.
      await user.click(screen.getByTestId('fire-create-on-save'));
      await screen.findByTestId('create-on-save-association-gate');

      // User picks Matter, then selects a record (→ onChange → setAssociation).
      await user.click(screen.getByTestId('association-choice-sprk_matter'));
      await user.click(screen.getByTestId('associate-to-step-select-record-button'));

      // Confirm ("Done") → associate(newDocumentId) writes the @odata.bind.
      await user.click(screen.getByTestId('association-gate-confirm'));

      await waitFor(() => {
        expect(dataService.updateRecord).toHaveBeenCalledWith(
          'sprk_document',
          // documentId cleanGuid-normalized (braces stripped, lowercased)
          'ffffffff-1111-2222-3333-444444444444',
          {
            // parent id cleanGuid-normalized inside the @odata.bind entityset URL
            'sprk_Matter@odata.bind': '/sprk_matters(aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee)',
          }
        );
      });

      // Gate closes after a successful write.
      await waitFor(() => {
        expect(screen.queryByTestId('create-on-save-association-gate')).not.toBeInTheDocument();
      });
    });
  });

  describe('none-path-is-a-graceful-no-op (Save never blocked)', () => {
    it('confirmingWithNoneWritesNothingAndClosesTheGate', async () => {
      const user = userEvent.setup();
      const dataService = createMockDataService();

      renderLight(
        <GateHarness
          navigationService={createMockNavigationService()}
          dataService={dataService}
          documentId="doc-guid-1"
        />
      );

      await user.click(screen.getByTestId('fire-create-on-save'));
      await screen.findByTestId('create-on-save-association-gate');

      // Default choice is "None" — confirm immediately (a standalone document is valid).
      await user.click(screen.getByTestId('association-gate-confirm'));

      await waitFor(() => {
        expect(screen.queryByTestId('create-on-save-association-gate')).not.toBeInTheDocument();
      });
      expect(dataService.updateRecord).not.toHaveBeenCalled();
    });

    it('skippingTheGateWritesNothingAndLeavesAStandaloneDocument', async () => {
      const user = userEvent.setup();
      const dataService = createMockDataService();

      renderLight(
        <GateHarness
          navigationService={createMockNavigationService({
            id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
            entityType: 'sprk_matter',
            name: 'Smith v. Jones',
          })}
          dataService={dataService}
          documentId="doc-guid-1"
        />
      );

      await user.click(screen.getByTestId('fire-create-on-save'));
      await screen.findByTestId('create-on-save-association-gate');

      // Even after picking a parent type, "Skip" abandons the association — the
      // document stays standalone; Save is never blocked on a parent.
      await user.click(screen.getByTestId('association-choice-sprk_matter'));
      await user.click(screen.getByTestId('association-gate-skip'));

      await waitFor(() => {
        expect(screen.queryByTestId('create-on-save-association-gate')).not.toBeInTheDocument();
      });
      expect(dataService.updateRecord).not.toHaveBeenCalled();
    });
  });

  describe('dark-mode', () => {
    it('rendersTheGateDialogUnderDarkThemeWithoutError', async () => {
      const user = userEvent.setup();
      renderDark(
        <GateHarness
          navigationService={createMockNavigationService()}
          dataService={createMockDataService()}
          documentId="doc-guid-1"
        />
      );

      await user.click(screen.getByTestId('fire-create-on-save'));

      expect(await screen.findByTestId('create-on-save-association-gate')).toBeInTheDocument();
      expect(screen.getByTestId('create-on-save-association-prompt')).toBeInTheDocument();
      expect(screen.getByTestId('association-gate-confirm')).toBeInTheDocument();
    });
  });
});
