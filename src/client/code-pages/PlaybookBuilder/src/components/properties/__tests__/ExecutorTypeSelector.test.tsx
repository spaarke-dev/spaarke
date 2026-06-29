/**
 * ExecutorTypeSelector tests — R7 Wave 8 task 089a (FR-22 + FR-24).
 *
 * Verifies the new Action-tab dropdown renders the 33-entry executor catalog,
 * groups options into 6 tiers (AI / Compute / Mutations / Control / Delivery /
 * Capability), surfaces selected-value display text with the tier prefix, and
 * propagates onChange with the numeric Choice value when the maker picks an
 * option.
 *
 * Test scope (ADR-038 KEEP path — render + interaction, not React Flow
 * internals):
 *   - 33 options render across 6 OptionGroup labels (FR-22 tier grouping).
 *   - Each option shows "NN  Label" (tier prefix + label).
 *   - Selected value renders with tier prefix in the closed dropdown.
 *   - Empty state renders the placeholder when no value supplied.
 *   - onChange fires with the numeric Choice value on selection.
 *   - disabled prop disables the combobox.
 *
 * The Fluent v9 Dropdown popover surfaces options in the DOM once opened
 * (verified upstream by the canonical ViewSelector test in
 * `Spaarke.UI.Components/src/components/DatasetGrid/__tests__/ViewSelector.test.tsx`).
 * We intentionally do NOT mock `executorMetadata.ts` — the source-of-truth
 * 33-entry catalog is the contract under test.
 *
 * @see R7 spec FR-22 (33-executor catalog + tier grouping)
 * @see R7 spec FR-24 (Action tab Executor Type dropdown)
 * @see src/components/properties/ExecutorTypeSelector.tsx (component under test)
 * @see src/config/executorMetadata.ts (source-of-truth catalog)
 */
import * as React from 'react';
import { screen, fireEvent, within } from '@testing-library/react';
import { ExecutorTypeSelector } from '../ExecutorTypeSelector';
import {
  EXECUTOR_METADATA,
  EXECUTOR_METADATA_COUNT,
  TIER_LABEL,
  TIER_ORDER,
} from '../../../config/executorMetadata';
import { renderWithProviders, renderWithTheme, webDarkTheme } from './testUtils';

describe('ExecutorTypeSelector (R7 Wave 8 task 089a / FR-22 + FR-24)', () => {
  it('renders the Executor Type label', () => {
    renderWithProviders(<ExecutorTypeSelector onChange={jest.fn()} />);
    expect(screen.getByText('Executor Type')).toBeInTheDocument();
  });

  it('renders a combobox in the closed state', () => {
    renderWithProviders(<ExecutorTypeSelector onChange={jest.fn()} />);
    expect(screen.getByRole('combobox')).toBeInTheDocument();
  });

  it('renders placeholder text when no value supplied', () => {
    // Fluent v9 Dropdown renders the placeholder as the combobox button's
    // visible text (NOT as a native `placeholder` attribute). Assert on the
    // text inside the combobox surface.
    renderWithProviders(<ExecutorTypeSelector onChange={jest.fn()} />);
    const combobox = screen.getByRole('combobox');
    expect(combobox).toHaveTextContent(/Select an executor type/i);
  });

  it('sources its option set from the 33-entry catalog (sanity check)', () => {
    // Build-time sanity — guards against catalog drift breaking the contract.
    expect(EXECUTOR_METADATA_COUNT).toBe(33);
    expect(EXECUTOR_METADATA).toHaveLength(33);
  });

  it('opens the popover and renders all 33 options when the combobox is clicked (FR-22)', () => {
    renderWithProviders(<ExecutorTypeSelector onChange={jest.fn()} />);
    fireEvent.click(screen.getByRole('combobox'));

    // Fluent v9 Dropdown renders each Option with role="option" once opened.
    const options = screen.getAllByRole('option');
    expect(options).toHaveLength(33);
  });

  it('groups options into the 6 expected tier labels (FR-22)', () => {
    renderWithProviders(<ExecutorTypeSelector onChange={jest.fn()} />);
    fireEvent.click(screen.getByRole('combobox'));

    // Each tier surface is rendered by Fluent OptionGroup. The visible
    // label text matches TIER_LABEL (e.g., "AI (0–9)", "Compute (10–19)"…).
    for (const tier of TIER_ORDER) {
      expect(screen.getByText(TIER_LABEL[tier])).toBeInTheDocument();
    }
    // Total tier label count exactly 6 — guards against silent tier additions.
    expect(TIER_ORDER).toHaveLength(6);
  });

  it('renders each option with its tier-prefix + label format (e.g., "01  AI Completion")', () => {
    renderWithProviders(<ExecutorTypeSelector onChange={jest.fn()} />);
    fireEvent.click(screen.getByRole('combobox'));

    // Spot-check three representative entries spanning multiple tiers.
    // AI=01 → "01  AI Completion"; Mutations=21 → "21  Send Email";
    // Control=30 → "30  Condition". Two-space separator is intentional
    // (matches `${tierPrefix}  ${label}` in the component).
    expect(screen.getAllByText(/01\s+AI Completion/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/21\s+Send Email/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/30\s+Condition/).length).toBeGreaterThan(0);
  });

  it('renders the description line beneath each option label', () => {
    renderWithProviders(<ExecutorTypeSelector onChange={jest.fn()} />);
    fireEvent.click(screen.getByRole('combobox'));

    // Verify one specific description from the catalog appears in the popover.
    // "AI Completion" → "Raw LLM completion with a prompt template + structured-output schema."
    expect(
      screen.getByText(/Raw LLM completion with a prompt template/i)
    ).toBeInTheDocument();
  });

  it('displays the tier-prefixed label for the currently selected value', () => {
    // value=30 → Condition (Control tier). Display string is "30  Condition".
    renderWithProviders(<ExecutorTypeSelector value={30} onChange={jest.fn()} />);
    const combobox = screen.getByRole('combobox') as HTMLInputElement;
    expect(combobox.value).toMatch(/30\s+Condition/);
  });

  it('emits onChange with the numeric Choice value when an option is clicked (FR-24)', () => {
    const onChange = jest.fn();
    renderWithProviders(<ExecutorTypeSelector onChange={onChange} />);
    fireEvent.click(screen.getByRole('combobox'));

    // Find the "30  Condition" option and click it.
    const options = screen.getAllByRole('option');
    const conditionOption = options.find(opt =>
      within(opt).queryByText(/30\s+Condition/)
    );
    expect(conditionOption).toBeDefined();
    fireEvent.click(conditionOption!);

    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith(30);
  });

  it('emits onChange with the correct numeric value for a 3-digit Capability-tier option (e.g., value=141)', () => {
    const onChange = jest.fn();
    renderWithProviders(<ExecutorTypeSelector onChange={onChange} />);
    fireEvent.click(screen.getByRole('combobox'));

    // 141 → Entity Name Validator (Capability tier).
    const options = screen.getAllByRole('option');
    const target = options.find(opt =>
      within(opt).queryByText(/141\s+Entity Name Validator/)
    );
    expect(target).toBeDefined();
    fireEvent.click(target!);

    expect(onChange).toHaveBeenCalledWith(141);
  });

  it('does NOT call onChange when disabled', () => {
    const onChange = jest.fn();
    renderWithProviders(<ExecutorTypeSelector onChange={onChange} disabled />);

    const combobox = screen.getByRole('combobox') as HTMLInputElement;
    expect(combobox).toBeDisabled();
    // Click attempt — when disabled, the popover should not open and no option
    // is selectable. (We don't assert popover state; the disabled attribute is
    // the contract.)
    fireEvent.click(combobox);
    expect(onChange).not.toHaveBeenCalled();
  });

  it('renders 6 tier labels matching TIER_ORDER (no extras, no drops)', () => {
    renderWithProviders(<ExecutorTypeSelector onChange={jest.fn()} />);
    fireEvent.click(screen.getByRole('combobox'));

    // Defensive: each expected tier label appears exactly once in the popover.
    for (const tier of TIER_ORDER) {
      const matches = screen.getAllByText(TIER_LABEL[tier]);
      expect(matches.length).toBe(1);
    }
  });

  it('renders cleanly under dark theme (ADR-021 semantic-token parity)', () => {
    renderWithTheme(
      <ExecutorTypeSelector value={1} onChange={jest.fn()} />,
      webDarkTheme
    );
    // Same label + combobox surface; Fluent v9 token-driven styles flip automatically.
    expect(screen.getByText('Executor Type')).toBeInTheDocument();
    const combobox = screen.getByRole('combobox') as HTMLInputElement;
    expect(combobox.value).toMatch(/01\s+AI Completion/);
  });
});
