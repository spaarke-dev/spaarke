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
