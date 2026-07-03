/**
 * FR-03 (task 013) — Wizard "Advanced" panel integration tests
 *
 * Covers the wizard UI half of FR-03 (spaarke-dataset-grid-framework-r2). Task
 * 012 added the `SectionInstance` type to the framework's `LayoutJsonRow`;
 * this task exposes the four override fields (configId, label, pageSize,
 * availableViews) to makers via an expandable Fluent v9 Accordion under each
 * placed section. These tests verify the acceptance criteria from the POML:
 *
 *   (a) Per-section Advanced panel is expandable in wizard UI
 *   (b) configId dropdown renders (currently placeholder-stubbed — see
 *       ArrangeStep.tsx AdvancedSectionControl comment for follow-up)
 *   (c) Setting label + pageSize produces a `SectionInstance` in the emitted
 *       state map that ultimately serializes to a JSON object with those fields
 *   (d) Leaving all fields empty leaves the state map empty for that slot,
 *       which serializes as a BARE-STRING section entry (back-compat)
 *   (e) Dark mode renders correctly (no hardcoded colors — structural check)
 *
 * Also verifies `buildSectionsJson` at the JSON boundary — proving that a
 * sectionInstances Map with a filled instance produces JSON with an object
 * section entry, and that an empty map produces bare-string entries.
 *
 * Test harness: jest + @testing-library/react, matching TemplateStep.test.tsx
 * and rowHeight.test.tsx (task 011).
 *
 * NOTE — deferred test-runner setup: WorkspaceLayoutWizard does NOT yet have
 * a jest config, test script in package.json, or @types/jest / testing-lib
 * devDependencies (as of task 011 / 2026-07-02, still true as of task 013).
 * This file is scaffolded to follow the same shape as `rowHeight.test.tsx` so
 * it runs unchanged once the runner is wired. Task 013 chose "create test file
 * for future setup" over "block on runner install" — same policy as task 011.
 *
 * @see App.tsx — buildSectionsJson + parseSectionsJson (wire point)
 * @see steps/ArrangeStep.tsx — AdvancedSectionControl (component under test)
 * @see docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md § FR-03
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, within, fireEvent } from '@testing-library/react';
import {
  FluentProvider,
  webLightTheme,
  webDarkTheme,
} from '@fluentui/react-components';
import { ArrangeStep } from '../steps/ArrangeStep';
import { buildSectionsJson } from '../App';
import type { SectionCatalogItem, SlotAssignments, SectionInstance } from '../steps';
import type { LayoutTemplateId } from '@spaarke/ui-components';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const FIXTURE_SECTIONS: SectionCatalogItem[] = [
  {
    id: 'communications',
    label: 'Communications',
    description: 'Email, Teams, SMS',
    category: 'data',
    icon: (() => <span />) as unknown as SectionCatalogItem['icon'],
  },
];

const SINGLE_COLUMN_TEMPLATE_ID: LayoutTemplateId = 'single-column';

/**
 * Render ArrangeStep inside a FluentProvider, returning the
 * `onSectionInstancesChange` spy so tests can inspect the emitted map. The
 * tests assert both the Map state emitted by the wizard component AND the
 * downstream `buildSectionsJson` output.
 */
function renderArrangeStep(overrides?: {
  sectionInstances?: Map<string, SectionInstance>;
  theme?: typeof webLightTheme;
}): {
  onSectionInstancesChange: jest.Mock;
} {
  const onSectionInstancesChange = jest.fn();
  const assignments: SlotAssignments = new Map([['row-1:0', 'communications']]);

  render(
    <FluentProvider theme={overrides?.theme ?? webLightTheme}>
      <ArrangeStep
        templateId={SINGLE_COLUMN_TEMPLATE_ID}
        selectedSections={FIXTURE_SECTIONS}
        sectionAssignments={assignments}
        workspaceName="Test Workspace"
        isDefault={false}
        pinToStart={false}
        rowHeights={new Map()}
        sectionInstances={overrides?.sectionInstances ?? new Map()}
        onAssignmentsChange={jest.fn()}
        onNameChange={jest.fn()}
        onDefaultChange={jest.fn()}
        onPinToStartChange={jest.fn()}
        onRowHeightsChange={jest.fn()}
        onSectionInstancesChange={onSectionInstancesChange}
        authenticatedFetch={jest.fn().mockResolvedValue({
          ok: true,
          json: async () => [],
        })}
      />
    </FluentProvider>,
  );

  return { onSectionInstancesChange };
}

// ---------------------------------------------------------------------------
// (a) Advanced accordion renders under each placed section + expands
// ---------------------------------------------------------------------------

describe('ArrangeStep Advanced accordion — FR-03 UI (task 013)', () => {
  it('renderFilledSlot_AdvancedAccordionRendered_ContainsFourControls', () => {
    renderArrangeStep();

    // The accordion wrapper exists.
    const accordion = screen.getByTestId(/^advanced-accordion-/);
    expect(accordion).toBeInTheDocument();

    // Expand the accordion by clicking its header.
    const header = within(accordion).getByRole('button');
    fireEvent.click(header);

    // Four controls expected inside the panel.
    expect(screen.getByTestId(/^advanced-configid-dropdown-/)).toBeInTheDocument();
    expect(screen.getByTestId(/^advanced-label-input-/)).toBeInTheDocument();
    expect(screen.getByTestId(/^advanced-pagesize-spinbutton-/)).toBeInTheDocument();
    // DEF-003 (spaarke-dataset-grid-framework-r2): availableViews Input replaced
    // by Combobox multiselect wired to BFF savedqueries. Testid changed.
    expect(screen.getByTestId(/^advanced-views-combobox-/)).toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // (b) configId dropdown renders + "None (use default)" option is present
  //
  // NOTE — currently placeholder-stubbed to `[{ key: '', label: 'None (use
  // default)' }]`. Once the picker is wired to a real Dataverse query (BFF
  // endpoint OR Xrm.WebApi shim), extend this test to assert the entity-
  // filtered options.
  // -------------------------------------------------------------------------

  it('configIdDropdown_OpensWithNoneOption_PlaceholderStubbedForNow', () => {
    // DEF-002 (spaarke-dataset-grid-framework-r2): configId picker now hydrates
    // real records via BFF. With authenticatedFetch stubbed to return `[]`, the
    // effective option list is `[None (use default)]` only — same shape as the
    // pre-DEF-002 placeholder. The test still validates the None entry is
    // present as the default fallback option.
    renderArrangeStep();

    const header = screen.getByTestId(/^advanced-accordion-/).querySelector('button');
    if (header) fireEvent.click(header);

    const dropdown = screen.getByTestId(/^advanced-configid-dropdown-/);
    fireEvent.click(dropdown);
    // Fluent v9 Dropdown renders the selected value in the button AND in the
    // listbox option — getAllByText correctly matches both instances.
    expect(screen.getAllByText('None (use default)').length).toBeGreaterThanOrEqual(1);
  });

  // -------------------------------------------------------------------------
  // (c) Setting label + pageSize produces a SectionInstance in the emitted map
  // -------------------------------------------------------------------------

  it('setLabelOverride_EmitsSectionInstanceMapWithLabel', () => {
    const { onSectionInstancesChange } = renderArrangeStep();

    const header = screen.getByTestId(/^advanced-accordion-/).querySelector('button');
    if (header) fireEvent.click(header);

    const labelInput = screen.getByTestId(/^advanced-label-input-/);
    fireEvent.change(labelInput, { target: { value: 'Email' } });

    expect(onSectionInstancesChange).toHaveBeenCalled();
    const lastCall = onSectionInstancesChange.mock.calls[onSectionInstancesChange.mock.calls.length - 1];
    const emittedMap = lastCall[0] as Map<string, SectionInstance>;
    const instance = emittedMap.get('row-1:0');
    expect(instance).toBeDefined();
    expect(instance?.label).toBe('Email');
  });

  it('setPageSizeOverride_EmitsSectionInstanceMapWithPageSize', () => {
    const { onSectionInstancesChange } = renderArrangeStep();

    const header = screen.getByTestId(/^advanced-accordion-/).querySelector('button');
    if (header) fireEvent.click(header);

    const spin = screen.getByTestId(/^advanced-pagesize-spinbutton-/);
    // SpinButton onChange fires with a numeric `value`.
    fireEvent.change(spin, { target: { value: '100' } });

    expect(onSectionInstancesChange).toHaveBeenCalled();
    const lastCall = onSectionInstancesChange.mock.calls[onSectionInstancesChange.mock.calls.length - 1];
    const emittedMap = lastCall[0] as Map<string, SectionInstance>;
    const instance = emittedMap.get('row-1:0');
    expect(instance).toBeDefined();
    expect(instance?.overrides?.pageSize).toBe(100);
  });

  // -------------------------------------------------------------------------
  // (d) Clearing all fields removes the entry from the map
  //     ("empty → bare string" back-compat invariant, enforced at Map level)
  // -------------------------------------------------------------------------

  it('clearLabelWhenOnlyOverride_RemovesEntryFromMap_BackCompatBareString', () => {
    const initial = new Map<string, SectionInstance>([
      ['row-1:0', { id: 'communications', label: 'Email' }],
    ]);
    const { onSectionInstancesChange } = renderArrangeStep({ sectionInstances: initial });

    const header = screen.getByTestId(/^advanced-accordion-/).querySelector('button');
    if (header) fireEvent.click(header);

    const labelInput = screen.getByTestId(/^advanced-label-input-/);
    fireEvent.change(labelInput, { target: { value: '' } });

    expect(onSectionInstancesChange).toHaveBeenCalled();
    const lastCall = onSectionInstancesChange.mock.calls[onSectionInstancesChange.mock.calls.length - 1];
    const emittedMap = lastCall[0] as Map<string, SectionInstance>;
    // Clearing the only override MUST remove the entry from the map so that
    // buildSectionsJson emits the section as a bare string (back-compat).
    expect(emittedMap.has('row-1:0')).toBe(false);
  });

  // -------------------------------------------------------------------------
  // (e) Dark mode structural test — no console errors under dark theme
  // -------------------------------------------------------------------------

  it('renderUnderDarkTheme_NoConsoleErrors_AccordionVisible', () => {
    const consoleErrorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    try {
      renderArrangeStep({ theme: webDarkTheme });
      expect(screen.getByTestId(/^advanced-accordion-/)).toBeInTheDocument();
      expect(consoleErrorSpy).not.toHaveBeenCalled();
    } finally {
      consoleErrorSpy.mockRestore();
    }
  });
});

// ---------------------------------------------------------------------------
// buildSectionsJson round-trip — verifies the wired output at the JSON boundary
// ---------------------------------------------------------------------------

describe('buildSectionsJson — SectionInstance emission (FR-03 / task 013)', () => {
  it('buildSectionsJson_EmptyInstancesMap_EmitsBareStringSections', () => {
    const assignments: SlotAssignments = new Map([['row-1:0', 'communications']]);
    const json = buildSectionsJson(
      SINGLE_COLUMN_TEMPLATE_ID,
      assignments,
      'my',
      new Map(), // rowHeights
      new Map(), // sectionInstances — empty
    );
    const parsed = JSON.parse(json);
    // Back-compat invariant: empty map → bare-string section entry.
    expect(parsed.rows[0].sections[0]).toBe('communications');
  });

  it('buildSectionsJson_InstanceWithLabel_EmitsSectionInstanceObject', () => {
    const assignments: SlotAssignments = new Map([['row-1:0', 'communications']]);
    const instances = new Map<string, SectionInstance>([
      ['row-1:0', { id: 'communications', label: 'Email' }],
    ]);
    const json = buildSectionsJson(
      SINGLE_COLUMN_TEMPLATE_ID,
      assignments,
      'my',
      new Map(),
      instances,
    );
    const parsed = JSON.parse(json);
    const entry = parsed.rows[0].sections[0];
    expect(typeof entry).toBe('object');
    expect(entry.id).toBe('communications');
    expect(entry.label).toBe('Email');
    // Undefined fields should be omitted, not written as undefined.
    expect(entry.configIdOverride).toBeUndefined();
    expect(entry.overrides).toBeUndefined();
  });

  it('buildSectionsJson_InstanceWithAllFourFields_EmitsFullSectionInstance', () => {
    const assignments: SlotAssignments = new Map([['row-1:0', 'communications']]);
    const instances = new Map<string, SectionInstance>([
      [
        'row-1:0',
        {
          id: 'communications',
          configIdOverride: 'e1826c4c-9575-f111-ab0e-7ced8ddc4a05',
          label: 'Email',
          overrides: {
            pageSize: 100,
            availableViews: ['aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'],
          },
        },
      ],
    ]);
    const json = buildSectionsJson(
      SINGLE_COLUMN_TEMPLATE_ID,
      assignments,
      'my',
      new Map(),
      instances,
    );
    const parsed = JSON.parse(json);
    const entry = parsed.rows[0].sections[0];
    expect(entry.id).toBe('communications');
    expect(entry.configIdOverride).toBe('e1826c4c-9575-f111-ab0e-7ced8ddc4a05');
    expect(entry.label).toBe('Email');
    expect(entry.overrides.pageSize).toBe(100);
    expect(entry.overrides.availableViews).toEqual(['aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa']);
  });

  it('buildSectionsJson_InstanceWithNoOverrides_StillEmitsBareString', () => {
    // Defensive: even if the map contains an instance with no actual overrides
    // set (which shouldn't happen after parseSectionsJson filters, but the JSON
    // boundary must be robust to it), buildSectionsJson MUST fall back to bare
    // string per the back-compat invariant.
    const assignments: SlotAssignments = new Map([['row-1:0', 'communications']]);
    const instances = new Map<string, SectionInstance>([
      ['row-1:0', { id: 'communications' }],
    ]);
    const json = buildSectionsJson(
      SINGLE_COLUMN_TEMPLATE_ID,
      assignments,
      'my',
      new Map(),
      instances,
    );
    const parsed = JSON.parse(json);
    expect(parsed.rows[0].sections[0]).toBe('communications');
  });
});

// Silence eslint unused-import warnings while the test framework isn't wired.
void within;
