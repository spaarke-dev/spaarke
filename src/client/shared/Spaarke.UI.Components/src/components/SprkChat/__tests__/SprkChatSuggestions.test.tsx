/**
 * SprkChatSuggestions Component Tests
 *
 * Tests the TYPED two-kind chip family (spaarkeai-assistant-enhancements-r4 task
 * 021b — grounded, capability-backed suggestions; design delta §5a):
 * - one chip family, two variants driven by the structural `kind`
 * - capability/action chips render the "acts" arrow; question chips do not
 * - onSelect receives the full typed item (so the parent can route by kind)
 * - selection, keyboard navigation, truncation, visibility, accessibility
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 */

import * as React from 'react';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { SprkChatSuggestions } from '../SprkChatSuggestions';
import type { ISprkChatFollowup } from '../types';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';

// ── Typed follow-up fixtures ────────────────────────────────────────────────
const cap = (label: string, targetBindingId = 'binding-1'): ISprkChatFollowup => ({
  kind: 'capability',
  label,
  targetBindingId,
  actionId: null,
});
const question = (label: string): ISprkChatFollowup => ({
  kind: 'question',
  label,
  targetBindingId: null,
  actionId: null,
});
const action = (label: string, actionId: string): ISprkChatFollowup => ({
  kind: 'action',
  label,
  targetBindingId: null,
  actionId,
});

describe('SprkChatSuggestions', () => {
  let mockOnSelect: jest.Mock;

  beforeEach(() => {
    mockOnSelect = jest.fn();
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  it('Render_WithSuggestions_ShowsChips', () => {
    const suggestions = [cap('Prioritize my tasks'), question('What are the risks?'), action('Upload File', 'upload')];

    renderWithProviders(<SprkChatSuggestions suggestions={suggestions} onSelect={mockOnSelect} visible={true} />);

    expect(screen.getByTestId('suggestion-chip-0')).toBeInTheDocument();
    expect(screen.getByTestId('suggestion-chip-1')).toBeInTheDocument();
    expect(screen.getByTestId('suggestion-chip-2')).toBeInTheDocument();
    expect(screen.getByText('Prioritize my tasks')).toBeInTheDocument();
    expect(screen.getByText('What are the risks?')).toBeInTheDocument();
    expect(screen.getByText('Upload File')).toBeInTheDocument();
  });

  it('Render_EmptySuggestions_ShowsNothing', () => {
    renderWithProviders(<SprkChatSuggestions suggestions={[]} onSelect={mockOnSelect} visible={true} />);
    expect(screen.queryByTestId('sprkchat-suggestions')).not.toBeInTheDocument();
  });

  it('Render_MaxThreeSuggestions_TruncatesExtra', () => {
    const suggestions = [cap('First'), question('Second?'), cap('Third'), question('Fourth?'), cap('Fifth')];

    renderWithProviders(<SprkChatSuggestions suggestions={suggestions} onSelect={mockOnSelect} visible={true} />);

    expect(screen.getByTestId('suggestion-chip-0')).toBeInTheDocument();
    expect(screen.getByTestId('suggestion-chip-1')).toBeInTheDocument();
    expect(screen.getByTestId('suggestion-chip-2')).toBeInTheDocument();
    expect(screen.queryByTestId('suggestion-chip-3')).not.toBeInTheDocument();
    expect(screen.queryByTestId('suggestion-chip-4')).not.toBeInTheDocument();
    expect(screen.queryByText('Fourth?')).not.toBeInTheDocument();
    expect(screen.queryByText('Fifth')).not.toBeInTheDocument();
  });

  // ─────────────────────────────────────────────────────────────────────
  // Variant rendering — arrow encodes the promise (design delta §5a)
  // ─────────────────────────────────────────────────────────────────────

  it('Render_CapabilityChip_ShowsActsArrow', () => {
    renderWithProviders(
      <SprkChatSuggestions suggestions={[cap('Prioritize my tasks')]} onSelect={mockOnSelect} visible={true} />
    );

    const chip = screen.getByTestId('suggestion-chip-0');
    expect(chip).toHaveAttribute('data-suggestion-kind', 'capability');
    // The "acts" arrow (→) is present for capability chips.
    expect(chip.textContent).toContain('→');
  });

  it('Render_ActionChip_ShowsActsArrow', () => {
    renderWithProviders(
      <SprkChatSuggestions suggestions={[action('Upload File', 'upload')]} onSelect={mockOnSelect} visible={true} />
    );

    const chip = screen.getByTestId('suggestion-chip-0');
    expect(chip).toHaveAttribute('data-suggestion-kind', 'action');
    expect(chip.textContent).toContain('→');
  });

  it('Render_QuestionChip_HasNoArrow', () => {
    renderWithProviders(
      <SprkChatSuggestions suggestions={[question('What are the risks?')]} onSelect={mockOnSelect} visible={true} />
    );

    const chip = screen.getByTestId('suggestion-chip-0');
    expect(chip).toHaveAttribute('data-suggestion-kind', 'question');
    // No "acts" arrow on a question chip — it asks, it does not act.
    expect(chip.textContent).not.toContain('→');
  });

  // ─────────────────────────────────────────────────────────────────────
  // Selection — onSelect receives the full typed item
  // ─────────────────────────────────────────────────────────────────────

  it('Click_CapabilityChip_CallsOnSelectWithItem', async () => {
    const user = userEvent.setup();
    const capItem = cap('Prioritize my tasks', 'binding-42');
    const suggestions = [capItem, question('What are the risks?')];

    renderWithProviders(<SprkChatSuggestions suggestions={suggestions} onSelect={mockOnSelect} visible={true} />);

    await user.click(screen.getByTestId('suggestion-chip-0'));

    expect(mockOnSelect).toHaveBeenCalledTimes(1);
    expect(mockOnSelect).toHaveBeenCalledWith(capItem);
  });

  it('Click_QuestionChip_CallsOnSelectWithItem', async () => {
    const user = userEvent.setup();
    const qItem = question('What are the risks?');
    const suggestions = [cap('Prioritize my tasks'), qItem];

    renderWithProviders(<SprkChatSuggestions suggestions={suggestions} onSelect={mockOnSelect} visible={true} />);

    await user.click(screen.getByTestId('suggestion-chip-1'));

    expect(mockOnSelect).toHaveBeenCalledTimes(1);
    expect(mockOnSelect).toHaveBeenCalledWith(qItem);
  });

  it('Render_LongSuggestion_TruncatesText', () => {
    const longText = 'This is a very long suggestion that exceeds the fifty char limit easily';
    const suggestions = [question(longText)];

    renderWithProviders(<SprkChatSuggestions suggestions={suggestions} onSelect={mockOnSelect} visible={true} />);

    const truncated = longText.slice(0, 49).trimEnd() + '…';
    expect(screen.getByText(truncated)).toBeInTheDocument();
    expect(screen.queryByText(longText)).not.toBeInTheDocument();
  });

  it('Visible_False_HidesComponent', () => {
    renderWithProviders(
      <SprkChatSuggestions suggestions={[question('A suggestion?')]} onSelect={mockOnSelect} visible={false} />
    );

    const container = screen.getByTestId('sprkchat-suggestions');
    expect(container).toBeInTheDocument();
    expect(container.className).toBeTruthy();
  });

  it('Keyboard_ArrowRight_MovesFocusToNextChip', async () => {
    const user = userEvent.setup();
    const suggestions = [cap('First'), question('Second?'), cap('Third')];

    renderWithProviders(<SprkChatSuggestions suggestions={suggestions} onSelect={mockOnSelect} visible={true} />);

    const chip0 = screen.getByTestId('suggestion-chip-0');
    const chip1 = screen.getByTestId('suggestion-chip-1');

    chip0.focus();
    expect(document.activeElement).toBe(chip0);

    await user.keyboard('{ArrowRight}');
    expect(document.activeElement).toBe(chip1);
  });

  it('Keyboard_EnterOnChip_CallsOnSelect', async () => {
    const user = userEvent.setup();
    const capItem = cap('Prioritize my tasks');

    renderWithProviders(<SprkChatSuggestions suggestions={[capItem]} onSelect={mockOnSelect} visible={true} />);

    const chip = screen.getByTestId('suggestion-chip-0');
    chip.focus();
    await user.keyboard('{Enter}');

    expect(mockOnSelect).toHaveBeenCalledWith(capItem);
  });

  it('Accessibility_ContainerHasGroupRole', () => {
    renderWithProviders(
      <SprkChatSuggestions suggestions={[question('A suggestion?')]} onSelect={mockOnSelect} visible={true} />
    );

    const group = screen.getByRole('group');
    expect(group).toBeInTheDocument();
    expect(group).toHaveAttribute('aria-label', 'Follow-up suggestions');
  });

  it('Accessibility_ChipsHaveLabels', () => {
    const longText = 'This is a very long suggestion that exceeds the fifty char limit easily';
    const suggestions = [question(longText), question('Short one?')];

    renderWithProviders(<SprkChatSuggestions suggestions={suggestions} onSelect={mockOnSelect} visible={true} />);

    const truncatedChip = screen.getByTestId('suggestion-chip-0');
    expect(truncatedChip).toHaveAttribute('aria-label', longText);

    const shortChip = screen.getByTestId('suggestion-chip-1');
    expect(shortChip).not.toHaveAttribute('aria-label');
  });
});
