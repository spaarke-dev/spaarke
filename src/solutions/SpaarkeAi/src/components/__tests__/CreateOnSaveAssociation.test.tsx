/**
 * CreateOnSaveAssociation — unit tests (FR-05)
 *
 * Covers:
 *   - association-choices-render: all five choices render (None / Matter /
 *     Project / Invoice / Work Assignment) in Fluent v9.
 *   - write-on-select: selecting a parent type + record fires onChange with
 *     an AssociationResult, and associateDocumentToParent files the document
 *     through the BFF re-file (PUT /api/v1/documents/{id}; UAC-r2 task 147 r1).
 *   - none-is-standalone: choosing "None" fires onChange(null); the
 *     write path (associateDocumentToParent / useCreateOnSaveAssociation's
 *     associate()) no-ops without calling the re-file -- Save is never
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
import type { AssociationResult, INavigationService } from '@spaarke/ui-components';

import { CreateOnSaveAssociationPrompt } from '../compose/CreateOnSaveAssociationPrompt';
import { associateDocumentToParent, bffDocumentRefile, type DocumentRefile } from '../compose/documentAssociationWrite';
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

/** UAC-r2 task 147 r1: the document re-file (PUT /api/v1/documents/{id}) the write path goes through. */
function createMockRefile(): jest.MockedFunction<DocumentRefile> {
  return jest.fn<Promise<void>, Parameters<DocumentRefile>>().mockResolvedValue(undefined);
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

    it('filesTheDocumentThroughTheBffRefileWithTheParentLookup', async () => {
      const refile = createMockRefile();
      const association: AssociationResult = {
        entityType: 'sprk_matter',
        recordId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        recordName: 'Smith v. Jones',
      };

      const result = await associateDocumentToParent(refile, 'doc-guid-1', association);

      expect(result.success).toBe(true);
      expect(refile).toHaveBeenCalledWith('doc-guid-1', { matterLookup: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee' });
    });

    it.each([
      ['sprk_project', 'projectLookup'],
      ['sprk_invoice', 'invoiceLookup'],
      ['sprk_workassignment', 'workAssignmentLookup'],
    ])('files a document under a %s through the documents PUT property %s', async (entityType, property) => {
      const refile = createMockRefile();

      await associateDocumentToParent(refile, 'doc-guid-1', {
        entityType,
        recordId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        recordName: 'Parent',
      });

      expect(refile).toHaveBeenCalledWith('doc-guid-1', { [property]: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee' });
    });

    it('aRefusedRefileReturnsTheServersMessageAsTheWarning', async () => {
      const response = {
        ok: false,
        status: 404,
        json: async () => ({ detail: 'The record was not found, or you do not have access to it.' }),
      } as Response;
      const fetchMock = jest.fn().mockResolvedValue(response);

      const result = await associateDocumentToParent(bffDocumentRefile(fetchMock, 'https://bff.example'), 'doc-guid-1', {
        entityType: 'sprk_project',
        recordId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        recordName: 'Secure project',
      });

      expect(result.success).toBe(false);
      expect(result.warning).toContain('The record was not found, or you do not have access to it.');
    });

    it('bffDocumentRefilePutsTheLookupToTheDocumentsRoute', async () => {
      const fetchMock = jest.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({}) } as Response);

      await bffDocumentRefile(fetchMock, 'https://bff.example/')('doc-guid-1', { matterLookup: 'm-1' });

      expect(fetchMock).toHaveBeenCalledWith('https://bff.example/api/v1/documents/doc-guid-1', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ matterLookup: 'm-1' }),
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

    it('associateDocumentToParentNoOpsForNoneWithoutCallingTheRefile', async () => {
      const refile = createMockRefile();

      const result = await associateDocumentToParent(refile, 'doc-guid-1', null);

      expect(result.success).toBe(true);
      expect(refile).not.toHaveBeenCalled();
    });

    it('useCreateOnSaveAssociationAssociateNoOpsWhenSelectionIsNone', async () => {
      const refile = createMockRefile();
      const { result } = renderHook(() => useCreateOnSaveAssociation({ refileDocument: refile }));

      expect(result.current.association).toBeNull();

      let outcome: { success: boolean } | undefined;
      await act(async () => {
        outcome = await result.current.associate('doc-guid-1');
      });

      expect(outcome?.success).toBe(true);
      expect(refile).not.toHaveBeenCalled();
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
  refileDocument: DocumentRefile;
  documentId: string;
}) {
  const { onCreateOnSaveComplete, dialogProps } = useCreateOnSaveAssociationGate({
    refileDocument: props.refileDocument,
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

describe('CreateOnSaveAssociationGate (Tier-2c gate hosting)', () => {
  describe('picker-renders-in-gate', () => {
    it('gateIsClosedUntilCreateOnSaveCompletes', () => {
      renderLight(
        <GateHarness
          navigationService={createMockNavigationService()}
          refileDocument={createMockRefile()}
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
          refileDocument={createMockRefile()}
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
      const refile = createMockRefile();
      // Braced + uppercase parent id from the Xrm lookup, AND a braced + uppercase
      // document id from the server-minted sprk_documentid — both MUST be
      // cleanGuid-normalized before the re-file.
      const navigationService = createMockNavigationService({
        id: '{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}',
        entityType: 'sprk_matter',
        name: 'Smith v. Jones',
      });

      renderLight(
        <GateHarness
          navigationService={navigationService}
          refileDocument={refile}
          documentId="{FFFFFFFF-1111-2222-3333-444444444444}"
        />
      );

      // create-on-save completes → gate opens.
      await user.click(screen.getByTestId('fire-create-on-save'));
      await screen.findByTestId('create-on-save-association-gate');

      // User picks Matter, then selects a record (→ onChange → setAssociation).
      await user.click(screen.getByTestId('association-choice-sprk_matter'));
      await user.click(screen.getByTestId('associate-to-step-select-record-button'));

      // Confirm ("Done") → associate(newDocumentId) re-files the document through the BFF.
      await user.click(screen.getByTestId('association-gate-confirm'));

      await waitFor(() => {
        expect(refile).toHaveBeenCalledWith(
          // documentId cleanGuid-normalized (braces stripped, lowercased)
          'ffffffff-1111-2222-3333-444444444444',
          // parent id cleanGuid-normalized
          { matterLookup: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee' }
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
      const refile = createMockRefile();

      renderLight(
        <GateHarness
          navigationService={createMockNavigationService()}
          refileDocument={refile}
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
      expect(refile).not.toHaveBeenCalled();
    });

    it('skippingTheGateWritesNothingAndLeavesAStandaloneDocument', async () => {
      const user = userEvent.setup();
      const refile = createMockRefile();

      renderLight(
        <GateHarness
          navigationService={createMockNavigationService({
            id: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
            entityType: 'sprk_matter',
            name: 'Smith v. Jones',
          })}
          refileDocument={refile}
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
      expect(refile).not.toHaveBeenCalled();
    });
  });

  describe('dark-mode', () => {
    it('rendersTheGateDialogUnderDarkThemeWithoutError', async () => {
      const user = userEvent.setup();
      renderDark(
        <GateHarness
          navigationService={createMockNavigationService()}
          refileDocument={createMockRefile()}
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
