/**
 * FR-02 (task 011) — Wizard rowHeight UI integration tests
 *
 * Covers the wizard UI half of FR-02 (spaarke-dataset-grid-framework-r2). Task
 * 010 added `rowHeight?: string` to the framework's `LayoutJsonRow` type; this
 * task exposes that field to makers via a per-row Fluent v9 Dropdown with 5
 * presets + "Custom…" in the Arrange step. These tests verify the acceptance
 * criteria from the POML:
 *
 *   (a) Dropdown shows 5 presets + "Custom…"
 *   (b) Selecting "80vh (large)" produces JSON with `"rowHeight": "80vh"`
 *       in the corresponding row
 *   (c) Selecting "Auto (default)" OMITS `rowHeight` from the JSON entirely
 *       (back-compat invariant — omitted-field default matches R1 behavior)
 *   (d) "Custom…" reveals an Input that accepts arbitrary CSS length and
 *       emits the exact value in the JSON
 *   (e) Dark mode preserves readability — all Fluent v9 semantic tokens used;
 *       no hardcoded colors (verified structurally by rendering under both
 *       webLightTheme and webDarkTheme without console errors)
 *
 * Test harness: jest + @testing-library/react, matching TemplateStep.test.tsx.
 *
 * NOTE — deferred test-runner setup: WorkspaceLayoutWizard does NOT yet have
 * a jest config, test script in package.json, or @types/jest / testing-lib
 * devDependencies (as of task 011 / 2026-07-02). This file is scaffolded to
 * follow the same shape as `src/steps/__tests__/TemplateStep.test.tsx` so it
 * runs unchanged once the runner is wired. Task 011 chose "create test file
 * for future setup" over "block on runner install" per the parent task's
 * "if a test framework isn't set up in the wizard package, note it in report
 * + create the test file for future setup" instruction.
 *
 * @see App.tsx — buildSectionsJson + parseSectionsJson (wire point)
 * @see steps/ArrangeStep.tsx — RowHeightControl (component under test)
 * @see docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md § FR-02
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
import type { SectionCatalogItem, SlotAssignments } from '../steps';
import type { LayoutTemplateId } from '@spaarke/ui-components';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

/** Minimal section catalog to hand to ArrangeStep — one section per slot. */
const FIXTURE_SECTIONS: SectionCatalogItem[] = [
  {
    id: 'documents',
    label: 'Documents',
    description: 'Document library',
    category: 'data',
    icon: (() => <span />) as unknown as SectionCatalogItem['icon'],
  },
];

/** Sensible template default — single-column has one row for clarity. */
const SINGLE_COLUMN_TEMPLATE_ID: LayoutTemplateId = 'single-column';

/**
 * Render ArrangeStep inside a FluentProvider, returning `onRowHeightsChange`
 * spy so tests can inspect the emitted row-height Map. The tests DO NOT
 * exercise `buildSectionsJson` directly — that lives in App.tsx and is
 * exercised by the higher-level dropdown → map → JSON flow. Instead, the
 * tests assert the Map shape the wizard would hand to buildSectionsJson.
 */
function renderArrangeStep(overrides?: {
  rowHeights?: Map<string, string>;
  theme?: typeof webLightTheme;
}): {
  onRowHeightsChange: jest.Mock;
} {
  const onRowHeightsChange = jest.fn();
  const assignments: SlotAssignments = new Map([['row-1:0', 'documents']]);

  render(
    <FluentProvider theme={overrides?.theme ?? webLightTheme}>
      <ArrangeStep
        templateId={SINGLE_COLUMN_TEMPLATE_ID}
        selectedSections={FIXTURE_SECTIONS}
        sectionAssignments={assignments}
        workspaceName="Test Workspace"
        isDefault={false}
        pinToStart={false}
        rowHeights={overrides?.rowHeights ?? new Map()}
        sectionInstances={new Map()}
        onAssignmentsChange={jest.fn()}
        onNameChange={jest.fn()}
        onDefaultChange={jest.fn()}
        onPinToStartChange={jest.fn()}
        onRowHeightsChange={onRowHeightsChange}
        onSectionInstancesChange={jest.fn()}
        authenticatedFetch={jest.fn().mockResolvedValue({
          ok: true,
          json: async () => [],
        })}
      />
    </FluentProvider>,
  );

  return { onRowHeightsChange };
}

// ---------------------------------------------------------------------------
// (a) Dropdown renders 5 presets + Custom
// ---------------------------------------------------------------------------

describe('ArrangeStep rowHeight dropdown — FR-02 UI (task 011)', () => {
  it('renderInitial_RowHeightDropdownVisible_ShowsFivePresetsPlusCustom', () => {
    renderArrangeStep();

    // Open the dropdown to reveal options.
    const dropdown = screen.getByTestId(/^row-height-dropdown-/);
    fireEvent.click(dropdown);

    // Fluent v9 Dropdown renders options as role="option" once opened.
    // Expected: Auto (default), 40vh, 60vh, 80vh, 100vh, Custom… = 6 total.
    expect(screen.getByText('Auto (default)')).toBeInTheDocument();
    expect(screen.getByText('40vh (small)')).toBeInTheDocument();
    expect(screen.getByText('60vh (medium)')).toBeInTheDocument();
    expect(screen.getByText('80vh (large)')).toBeInTheDocument();
    expect(screen.getByText('100vh (full-viewport)')).toBeInTheDocument();
    expect(screen.getByText('Custom…')).toBeInTheDocument();
  });

  it('renderInitial_NoRowHeightSet_DropdownDefaultsToAuto', () => {
    renderArrangeStep({ rowHeights: new Map() });

    const dropdown = screen.getByTestId(/^row-height-dropdown-/);
    // Fluent v9 Dropdown displays its `value` prop as the button text.
    expect(dropdown).toHaveTextContent('Auto (default)');
  });

  // -------------------------------------------------------------------------
  // (b) 80vh preset → emits Map with `rowHeight: "80vh"` for the row
  // -------------------------------------------------------------------------

  it('select80vhPreset_EmitsRowHeightsMapWithEightyVh', () => {
    const { onRowHeightsChange } = renderArrangeStep();

    const dropdown = screen.getByTestId(/^row-height-dropdown-/);
    fireEvent.click(dropdown);

    const option = screen.getByText('80vh (large)');
    fireEvent.click(option);

    expect(onRowHeightsChange).toHaveBeenCalled();
    const emittedMap = onRowHeightsChange.mock.calls[onRowHeightsChange.mock.calls.length - 1][0] as Map<string, string>;
    // The template row's ID (from LAYOUT_TEMPLATES for single-column) should
    // now have '80vh' as its rowHeight — this map is what App.tsx feeds into
    // buildSectionsJson, which emits `"rowHeight": "80vh"` in the JSON row.
    const values = Array.from(emittedMap.values());
    expect(values).toContain('80vh');
  });

  // -------------------------------------------------------------------------
  // (c) Auto (default) → EMPTY / removes entry → JSON omits rowHeight
  //
  // buildSectionsJson (App.tsx) only emits `rowHeight` when the map contains
  // a non-empty value for the row.id — so clearing the map value = JSON omits
  // the field. This is the back-compat invariant.
  // -------------------------------------------------------------------------

  it('selectAutoDefault_FromCustomValue_ClearsRowHeightMapEntry', () => {
    // Start with a stored custom value; selecting Auto (default) MUST remove it.
    const initial = new Map<string, string>([['row-1', '80vh']]);
    const { onRowHeightsChange } = renderArrangeStep({ rowHeights: initial });

    const dropdown = screen.getByTestId(/^row-height-dropdown-/);
    fireEvent.click(dropdown);

    const option = screen.getByText('Auto (default)');
    fireEvent.click(option);

    expect(onRowHeightsChange).toHaveBeenCalled();
    const emittedMap = onRowHeightsChange.mock.calls[onRowHeightsChange.mock.calls.length - 1][0] as Map<string, string>;
    // Selecting Auto (default) MUST remove the row's entry from the map so
    // buildSectionsJson omits the field entirely (back-compat).
    expect(emittedMap.has('row-1')).toBe(false);
  });

  // -------------------------------------------------------------------------
  // (d) Custom… reveals input + input value flows through
  // -------------------------------------------------------------------------

  it('selectCustomOption_RevealsCustomInputForArbitraryCssLength', () => {
    renderArrangeStep();

    const dropdown = screen.getByTestId(/^row-height-dropdown-/);
    fireEvent.click(dropdown);

    const customOption = screen.getByText('Custom…');
    fireEvent.click(customOption);

    // After selecting Custom…, the Input appears.
    const customInput = screen.getByTestId(/^row-height-custom-input-/);
    expect(customInput).toBeInTheDocument();
  });

  it('customInputTyped640Px_EmitsRowHeightsMapWith640Px', () => {
    // Start with an already-Custom state so the input is visible.
    const initial = new Map<string, string>([['row-1', '500px']]);
    const { onRowHeightsChange } = renderArrangeStep({ rowHeights: initial });

    const customInput = screen.getByTestId(/^row-height-custom-input-/);
    fireEvent.change(customInput, { target: { value: '640px' } });

    expect(onRowHeightsChange).toHaveBeenCalled();
    const emittedMap = onRowHeightsChange.mock.calls[onRowHeightsChange.mock.calls.length - 1][0] as Map<string, string>;
    const values = Array.from(emittedMap.values());
    expect(values).toContain('640px');
  });

  // -------------------------------------------------------------------------
  // (e) Dark mode structural test — no console errors under dark theme
  //
  // Full pixel-level dark-mode verification requires a browser (task's Step
  // 9.7 UI-test section covers that path). This test verifies the JSX
  // renders under dark theme without runtime errors — a proxy for "all
  // colors use semantic tokens" because hardcoded colors would render but
  // wouldn't adapt (visual review at Step 9.7 is the final gate).
  // -------------------------------------------------------------------------

  it('renderUnderDarkTheme_NoConsoleErrors_DropdownVisible', () => {
    const consoleErrorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    try {
      renderArrangeStep({ theme: webDarkTheme });
      // Dropdown must still render + be interactable in dark mode.
      expect(screen.getByTestId(/^row-height-dropdown-/)).toBeInTheDocument();
      expect(consoleErrorSpy).not.toHaveBeenCalled();
    } finally {
      consoleErrorSpy.mockRestore();
    }
  });

  // -------------------------------------------------------------------------
  // (f) Help tooltip is rendered
  // -------------------------------------------------------------------------

  it('renderInitial_HelpTooltipTriggerVisible_HasAccessibleLabel', () => {
    renderArrangeStep();
    const helpTrigger = screen.getByTestId(/^row-height-help-/);
    expect(helpTrigger).toBeInTheDocument();
    expect(helpTrigger).toHaveAttribute('aria-label', 'Row height help');
  });
});

// ---------------------------------------------------------------------------
// buildSectionsJson round-trip — verifies the wired output matches the schema
//
// These tests DUPLICATE the logic path exercised in ArrangeStep but at the
// JSON boundary — proving that a row-heights Map with '80vh' produces JSON
// with `"rowHeight": "80vh"`, and that an empty map produces JSON without the
// field. `buildSectionsJson` currently lives inside App.tsx (module-scoped);
// once App.tsx exports it (or the wizard is refactored to lift it into a
// helpers/ module), these tests wire directly. For task 011 we scaffold the
// test names so the JSON-boundary coverage exists once App.tsx exposes the
// helper.
// ---------------------------------------------------------------------------

describe.skip('buildSectionsJson — row height propagation (requires App.tsx export)', () => {
  it('buildSectionsJson_MapContainsEightyVh_EmitsRowHeightInJson', () => {
    // Placeholder — to be wired once buildSectionsJson is exported.
    // Expected shape:
    //   const json = buildSectionsJson('single-column', assignments, 'my', new Map([['row-1', '80vh']]));
    //   expect(JSON.parse(json).rows[0].rowHeight).toBe('80vh');
  });

  it('buildSectionsJson_EmptyMap_OmitsRowHeightField', () => {
    // Placeholder — to be wired once buildSectionsJson is exported.
    // Expected shape:
    //   const json = buildSectionsJson('single-column', assignments, 'my', new Map());
    //   expect(JSON.parse(json).rows[0]).not.toHaveProperty('rowHeight');
  });

  it('buildSectionsJson_MapContains640Px_EmitsExactCustomValueInJson', () => {
    // Placeholder — to be wired once buildSectionsJson is exported.
    // Expected shape:
    //   const json = buildSectionsJson('single-column', assignments, 'my', new Map([['row-1', '640px']]));
    //   expect(JSON.parse(json).rows[0].rowHeight).toBe('640px');
  });
});

// Silence eslint unused-import warnings while the test framework isn't wired.
void within;
