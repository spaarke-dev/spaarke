/**
 * AssociateToStep Component Tests
 *
 * Verifies FR-07 / ADR-024:
 *   - The TODO_REGARDING_TARGETS preset lists exactly the 11 canonical entity targets
 *     in spec order (Matter, Project, Event, Communication, WorkAssignment, Invoice,
 *     Budget, Analysis, Organization, Contact (OOB), Document)
 *   - The component renders all 11 options in the entity-type picker when given the preset
 *   - The Select Record button launches the lookup dialog for the selected entity
 *   - Selection returns the correct (entityType, recordId, recordName) triple via onChange
 *   - GUIDs returned by the lookup are normalized (braces stripped, lowercased)
 *   - Changing entity type while a selection exists clears the previous selection
 *   - Disabled state suppresses all interaction
 *   - Component is keyboard + screen-reader accessible (label association + dropdown semantics)
 *
 * @see spec.md FR-07
 * @see ADR-024 — Polymorphic Resolver Pattern
 * @see ADR-012 — Shared Component Library
 * @see ADR-021 — Fluent UI v9 design system
 */

import * as React from 'react';
import { screen, waitFor, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { createMockNavigationService } from '../../../__mocks__/mockNavigationService';
import { AssociateToStep } from '../AssociateToStep';
import { TODO_REGARDING_TARGETS, type RegardingTarget, type AssociationResult } from '../types';

// ─────────────────────────────────────────────────────────────────────────────
// TODO_REGARDING_TARGETS preset — canonical shape verification (FR-07)
// ─────────────────────────────────────────────────────────────────────────────

describe('TODO_REGARDING_TARGETS preset', () => {
  it('listsExactlyElevenTargets', () => {
    expect(TODO_REGARDING_TARGETS).toHaveLength(11);
  });

  it('listsTargetsInSpecOrder', () => {
    expect(TODO_REGARDING_TARGETS.map(t => t.label)).toEqual([
      'Matter',
      'Project',
      'Event',
      'Communication',
      'Work Assignment',
      'Invoice',
      'Budget',
      'Analysis',
      'Organization',
      'Contact',
      'Document',
    ]);
  });

  it('mapsEachLabelToCorrectLogicalName', () => {
    const byLabel = Object.fromEntries(TODO_REGARDING_TARGETS.map(t => [t.label, t.entityType]));

    expect(byLabel['Matter']).toBe('sprk_matter');
    expect(byLabel['Project']).toBe('sprk_project');
    expect(byLabel['Event']).toBe('sprk_event');
    expect(byLabel['Communication']).toBe('sprk_communication');
    expect(byLabel['Work Assignment']).toBe('sprk_workassignment');
    expect(byLabel['Invoice']).toBe('sprk_invoice');
    expect(byLabel['Budget']).toBe('sprk_budget');
    expect(byLabel['Analysis']).toBe('sprk_analysis');
    expect(byLabel['Organization']).toBe('sprk_organization');
    // OOB contact — NOT sprk_contact (entity-schema.md note)
    expect(byLabel['Contact']).toBe('contact');
    expect(byLabel['Document']).toBe('sprk_document');
  });

  it('mapsEachTargetToCorrectLookupAttribute', () => {
    const byEntityType = Object.fromEntries(TODO_REGARDING_TARGETS.map(t => [t.entityType, t.lookupAttribute]));

    expect(byEntityType['sprk_matter']).toBe('sprk_regardingmatter');
    expect(byEntityType['sprk_project']).toBe('sprk_regardingproject');
    expect(byEntityType['sprk_event']).toBe('sprk_regardingevent');
    expect(byEntityType['sprk_communication']).toBe('sprk_regardingcommunication');
    expect(byEntityType['sprk_workassignment']).toBe('sprk_regardingworkassignment');
    expect(byEntityType['sprk_invoice']).toBe('sprk_regardinginvoice');
    expect(byEntityType['sprk_budget']).toBe('sprk_regardingbudget');
    expect(byEntityType['sprk_analysis']).toBe('sprk_regardinganalysis');
    expect(byEntityType['sprk_organization']).toBe('sprk_regardingorganization');
    // Contact uses OOB `contact` entity but custom `sprk_regardingcontact` lookup attribute
    expect(byEntityType['contact']).toBe('sprk_regardingcontact');
    expect(byEntityType['sprk_document']).toBe('sprk_regardingdocument');
  });

  it('hasUniqueEntityTypes', () => {
    const entityTypes = TODO_REGARDING_TARGETS.map(t => t.entityType);
    expect(new Set(entityTypes).size).toBe(entityTypes.length);
  });

  it('hasUniqueLookupAttributes', () => {
    const lookups = TODO_REGARDING_TARGETS.map(t => t.lookupAttribute);
    expect(new Set(lookups).size).toBe(lookups.length);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Component rendering with 11-target preset
// ─────────────────────────────────────────────────────────────────────────────

describe('AssociateToStep — rendering with 11-target preset', () => {
  const renderPreset = (overrides?: {
    onChange?: (r: AssociationResult | null) => void;
    value?: AssociationResult | null;
    disabled?: boolean;
  }) => {
    const navigationService = createMockNavigationService();
    const onChange = overrides?.onChange ?? jest.fn();
    renderWithProviders(
      <AssociateToStep
        entityTypes={[...TODO_REGARDING_TARGETS]}
        navigationService={navigationService}
        value={overrides?.value ?? null}
        onChange={onChange}
        disabled={overrides?.disabled}
      />
    );
    return { navigationService, onChange };
  };

  it('rendersStepHeaderAndSubtitle', () => {
    renderPreset();
    expect(screen.getByText('Associate To')).toBeInTheDocument();
    expect(screen.getByText('Link this record to an existing record.')).toBeInTheDocument();
  });

  it('rendersEntityTypeDropdownAndSelectRecordButton', () => {
    renderPreset();
    expect(screen.getByTestId('associate-to-step-entity-type-dropdown')).toBeInTheDocument();
    expect(screen.getByTestId('associate-to-step-select-record-button')).toBeInTheDocument();
  });

  it('selectsFirstTargetAsDefault', () => {
    renderPreset();
    // The dropdown displays the label of the default-selected target (Matter)
    const dropdown = screen.getByTestId('associate-to-step-entity-type-dropdown');
    expect(dropdown).toHaveTextContent('Matter');
  });

  it('rendersAllElevenOptionsInDropdown', async () => {
    const user = userEvent.setup();
    renderPreset();

    // Open the dropdown by clicking it
    const dropdown = screen.getByTestId('associate-to-step-entity-type-dropdown');
    await user.click(dropdown);

    // All 11 options should be rendered in the listbox
    for (const target of TODO_REGARDING_TARGETS) {
      expect(screen.getByRole('option', { name: target.label })).toBeInTheDocument();
    }
  });

  it('dropdownIsAccessibleViaAriaLabelledby', () => {
    renderPreset();
    const dropdown = screen.getByTestId('associate-to-step-entity-type-dropdown');
    expect(dropdown).toHaveAttribute('aria-labelledby', 'associate-to-step-record-type-label');
    const label = document.getElementById('associate-to-step-record-type-label');
    expect(label).not.toBeNull();
    expect(label).toHaveTextContent('Record Type');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Lookup dialog launch — per-entity verification (FR-07 acceptance)
// ─────────────────────────────────────────────────────────────────────────────

describe('AssociateToStep — lookup dialog launch per entity', () => {
  // Build a parameterized table — one row per target
  it.each(TODO_REGARDING_TARGETS.map(t => [t.label, t.entityType] as [string, string]))(
    'launchesLookupWithCorrectEntityType_%s',
    async (label, expectedEntityType) => {
      const user = userEvent.setup();
      const navigationService = createMockNavigationService();
      const onChange = jest.fn();

      renderWithProviders(
        <AssociateToStep
          entityTypes={[...TODO_REGARDING_TARGETS]}
          navigationService={navigationService}
          value={null}
          onChange={onChange}
        />
      );

      // Open the dropdown and select the target option
      const dropdown = screen.getByTestId('associate-to-step-entity-type-dropdown');
      await user.click(dropdown);
      const option = screen.getByRole('option', { name: label });
      await user.click(option);

      // Click Select Record button
      const button = screen.getByTestId('associate-to-step-select-record-button');
      await user.click(button);

      // The lookup was opened with the expected entity type
      await waitFor(() => {
        expect(navigationService.openLookup).toHaveBeenCalledTimes(1);
      });
      const callArgs = navigationService.openLookup.mock.calls[0][0];
      expect(callArgs.entityType).toBe(expectedEntityType);
      expect(callArgs.entityTypes).toEqual([expectedEntityType]);
      expect(callArgs.defaultEntityType).toBe(expectedEntityType);
      expect(callArgs.allowMultiSelect).toBe(false);
    }
  );

  it('passesDefaultViewIdWhenSpecifiedOnTarget', async () => {
    const user = userEvent.setup();
    const navigationService = createMockNavigationService();
    const customTargets: RegardingTarget[] = [
      {
        label: 'Matter',
        entityType: 'sprk_matter',
        lookupAttribute: 'sprk_regardingmatter',
        defaultViewId: 'view-guid-123',
      },
    ];

    renderWithProviders(
      <AssociateToStep
        entityTypes={customTargets}
        navigationService={navigationService}
        value={null}
        onChange={jest.fn()}
      />
    );

    await user.click(screen.getByTestId('associate-to-step-select-record-button'));

    await waitFor(() => {
      expect(navigationService.openLookup).toHaveBeenCalledWith(
        expect.objectContaining({ defaultViewId: 'view-guid-123' })
      );
    });
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Selection returns correct (entityType, recordId, recordName) triple
// ─────────────────────────────────────────────────────────────────────────────

describe('AssociateToStep — selection returns correct triple', () => {
  it('returnsTripleOnSelection_normalizesGuid', async () => {
    const user = userEvent.setup();
    const navigationService = createMockNavigationService();
    navigationService.openLookup.mockResolvedValueOnce([
      // Lookup returns GUID wrapped in braces — component must strip + lowercase
      { id: '{ABCDEF12-3456-7890-ABCD-EF1234567890}', name: 'Smith v. Jones', entityType: 'sprk_matter' },
    ]);
    const onChange = jest.fn();

    renderWithProviders(
      <AssociateToStep
        entityTypes={[...TODO_REGARDING_TARGETS]}
        navigationService={navigationService}
        value={null}
        onChange={onChange}
      />
    );

    await user.click(screen.getByTestId('associate-to-step-select-record-button'));

    await waitFor(() => expect(onChange).toHaveBeenCalledTimes(1));

    const result: AssociationResult = onChange.mock.calls[0][0];
    expect(result.entityType).toBe('sprk_matter');
    expect(result.recordId).toBe('abcdef12-3456-7890-abcd-ef1234567890'); // lowercased, no braces
    expect(result.recordName).toBe('Smith v. Jones');
  });

  it('returnsTripleForOOBContact', async () => {
    const user = userEvent.setup();
    const navigationService = createMockNavigationService();
    navigationService.openLookup.mockResolvedValueOnce([
      { id: '11111111-2222-3333-4444-555555555555', name: 'Jane Doe', entityType: 'contact' },
    ]);
    const onChange = jest.fn();

    renderWithProviders(
      <AssociateToStep
        entityTypes={[...TODO_REGARDING_TARGETS]}
        navigationService={navigationService}
        value={null}
        onChange={onChange}
      />
    );

    // Select Contact entity
    const dropdown = screen.getByTestId('associate-to-step-entity-type-dropdown');
    await user.click(dropdown);
    await user.click(screen.getByRole('option', { name: 'Contact' }));

    // Click Select Record
    await user.click(screen.getByTestId('associate-to-step-select-record-button'));

    await waitFor(() => expect(onChange).toHaveBeenCalledTimes(1));
    const result: AssociationResult = onChange.mock.calls[0][0];
    expect(result).toEqual({
      entityType: 'contact',
      recordId: '11111111-2222-3333-4444-555555555555',
      recordName: 'Jane Doe',
    });
  });

  it('doesNotInvokeOnChangeWhenLookupCancelled', async () => {
    const user = userEvent.setup();
    const navigationService = createMockNavigationService();
    navigationService.openLookup.mockResolvedValueOnce([]); // Empty = user cancelled
    const onChange = jest.fn();

    renderWithProviders(
      <AssociateToStep
        entityTypes={[...TODO_REGARDING_TARGETS]}
        navigationService={navigationService}
        value={null}
        onChange={onChange}
      />
    );

    await user.click(screen.getByTestId('associate-to-step-select-record-button'));

    // Wait briefly to ensure no async onChange was invoked
    await waitFor(() => expect(navigationService.openLookup).toHaveBeenCalled());
    expect(onChange).not.toHaveBeenCalled();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Selected record display + clear
// ─────────────────────────────────────────────────────────────────────────────

describe('AssociateToStep — selected record display', () => {
  it('displaysSelectedRecordNameAndTypeLabel', () => {
    const navigationService = createMockNavigationService();
    renderWithProviders(
      <AssociateToStep
        entityTypes={[...TODO_REGARDING_TARGETS]}
        navigationService={navigationService}
        value={{
          entityType: 'sprk_matter',
          recordId: 'abc-123',
          recordName: 'Smith v. Jones',
        }}
        onChange={jest.fn()}
      />
    );

    expect(screen.getByText('Smith v. Jones')).toBeInTheDocument();
    expect(screen.getByText('(Matter)')).toBeInTheDocument();
  });

  it('clearsSelectionWhenClearButtonClicked', async () => {
    const user = userEvent.setup();
    const navigationService = createMockNavigationService();
    const onChange = jest.fn();

    renderWithProviders(
      <AssociateToStep
        entityTypes={[...TODO_REGARDING_TARGETS]}
        navigationService={navigationService}
        value={{
          entityType: 'sprk_matter',
          recordId: 'abc-123',
          recordName: 'Smith v. Jones',
        }}
        onChange={onChange}
      />
    );

    const clearButton = screen.getByRole('button', { name: 'Clear selection' });
    await user.click(clearButton);

    expect(onChange).toHaveBeenCalledWith(null);
  });

  it('clearsSelectionWhenEntityTypeChanges', async () => {
    const user = userEvent.setup();
    const navigationService = createMockNavigationService();
    const onChange = jest.fn();

    renderWithProviders(
      <AssociateToStep
        entityTypes={[...TODO_REGARDING_TARGETS]}
        navigationService={navigationService}
        value={{
          entityType: 'sprk_matter',
          recordId: 'abc-123',
          recordName: 'Smith v. Jones',
        }}
        onChange={onChange}
      />
    );

    const dropdown = screen.getByTestId('associate-to-step-entity-type-dropdown');
    await user.click(dropdown);
    await user.click(screen.getByRole('option', { name: 'Project' }));

    // Changing entity type with existing selection clears the selection
    expect(onChange).toHaveBeenCalledWith(null);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Disabled state
// ─────────────────────────────────────────────────────────────────────────────

describe('AssociateToStep — disabled state', () => {
  it('disablesAllInteractionWhenDisabledPropTrue', () => {
    const navigationService = createMockNavigationService();
    renderWithProviders(
      <AssociateToStep
        entityTypes={[...TODO_REGARDING_TARGETS]}
        navigationService={navigationService}
        value={null}
        onChange={jest.fn()}
        disabled
      />
    );

    expect(screen.getByTestId('associate-to-step-select-record-button')).toBeDisabled();
    expect(screen.getByTestId('associate-to-step-entity-type-dropdown')).toBeDisabled();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Error handling
// ─────────────────────────────────────────────────────────────────────────────

describe('AssociateToStep — error handling', () => {
  it('rendersErrorMessageWhenLookupFails', async () => {
    // Suppress expected console.error from caught lookup failure
    const errSpy = jest.spyOn(console, 'error').mockImplementation(() => undefined);

    const user = userEvent.setup();
    const navigationService = createMockNavigationService();
    navigationService.openLookup.mockRejectedValueOnce(new Error('Network down'));

    renderWithProviders(
      <AssociateToStep
        entityTypes={[...TODO_REGARDING_TARGETS]}
        navigationService={navigationService}
        value={null}
        onChange={jest.fn()}
      />
    );

    await user.click(screen.getByTestId('associate-to-step-select-record-button'));

    await waitFor(() => {
      expect(screen.getByText('Network down')).toBeInTheDocument();
    });

    errSpy.mockRestore();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Keyboard accessibility (NFR-10)
// ─────────────────────────────────────────────────────────────────────────────

describe('AssociateToStep — keyboard accessibility (NFR-10)', () => {
  it('selectRecordButtonIsKeyboardActivatable', async () => {
    const user = userEvent.setup();
    const navigationService = createMockNavigationService();

    renderWithProviders(
      <AssociateToStep
        entityTypes={[...TODO_REGARDING_TARGETS]}
        navigationService={navigationService}
        value={null}
        onChange={jest.fn()}
      />
    );

    const button = screen.getByTestId('associate-to-step-select-record-button');
    button.focus();
    expect(button).toHaveFocus();

    // Enter activates the focused button
    fireEvent.keyDown(button, { key: 'Enter' });
    await user.keyboard('{Enter}');

    await waitFor(() => expect(navigationService.openLookup).toHaveBeenCalled());
  });

  it('dropdownIsFocusableViaTab', () => {
    const navigationService = createMockNavigationService();
    renderWithProviders(
      <AssociateToStep
        entityTypes={[...TODO_REGARDING_TARGETS]}
        navigationService={navigationService}
        value={null}
        onChange={jest.fn()}
      />
    );

    const dropdown = screen.getByTestId('associate-to-step-entity-type-dropdown');
    // Fluent UI v9 Dropdown renders a tabbable element
    expect(dropdown.tabIndex).toBeGreaterThanOrEqual(0);
  });
});
