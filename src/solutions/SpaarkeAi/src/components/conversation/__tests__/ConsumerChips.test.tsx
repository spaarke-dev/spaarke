/**
 * ConsumerChips tests — ai-architecture-redesign-r1 task 023 / FR-P1-04.
 *
 * Covers:
 *   - chips render with labels + carry binding_id (data attribute)
 *   - click surfaces the FULL chip (binding_id is the routing datum)
 *   - empty-attachments Click precondition: requiresAttachments chip is
 *     DISABLED at zero attachments (no dispatch possible), enabled otherwise
 *   - empty chip list renders nothing
 *   - strip-level disabled prop disables all chips
 *
 * ADR-021 note: token-driven styling is asserted structurally here (no inline
 * hardcoded colors); the visual dark-mode check runs in the browser at gate 027.
 */

import '@testing-library/jest-dom';
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import type { ConsumerChip } from '@spaarke/ui-components';

import { ConsumerChips } from '../ConsumerChips';

function renderChips(
  props: Partial<React.ComponentProps<typeof ConsumerChips>> = {}
): { onChipClick: jest.Mock } {
  const onChipClick = jest.fn();
  const chips: ConsumerChip[] = props.chips
    ? [...props.chips]
    : [
        { bindingId: 'binding-summarize', label: 'Summarize all?', requiresAttachments: true },
        { bindingId: 'binding-create-matter', label: 'Create matter' },
      ];
  render(
    <FluentProvider theme={webLightTheme}>
      <ConsumerChips
        chips={chips}
        attachmentCount={props.attachmentCount ?? 1}
        disabled={props.disabled}
        onChipClick={props.onChipClick ?? onChipClick}
      />
    </FluentProvider>
  );
  return { onChipClick };
}

describe('ConsumerChips', () => {
  it('renders one chip per transition, carrying binding_id', () => {
    renderChips();
    const strip = screen.getByTestId('consumer-chips');
    expect(strip).toBeInTheDocument();

    const summarize = screen.getByTestId('consumer-chip-binding-summarize');
    expect(summarize).toHaveTextContent('Summarize all?');
    expect(summarize).toHaveAttribute('data-binding-id', 'binding-summarize');

    const createMatter = screen.getByTestId('consumer-chip-binding-create-matter');
    expect(createMatter).toHaveTextContent('Create matter');
    expect(createMatter).toHaveAttribute('data-binding-id', 'binding-create-matter');
  });

  it('click delivers the full chip (the host dispatches by chip.bindingId)', () => {
    const { onChipClick } = renderChips({ attachmentCount: 2 });
    fireEvent.click(screen.getByTestId('consumer-chip-binding-summarize'));
    expect(onChipClick).toHaveBeenCalledTimes(1);
    expect(onChipClick.mock.calls[0][0]).toMatchObject({
      bindingId: 'binding-summarize',
      label: 'Summarize all?',
      requiresAttachments: true,
    });
  });

  it('empty-attachments precondition: requiresAttachments chip disabled at zero attachments', () => {
    const { onChipClick } = renderChips({ attachmentCount: 0 });

    const gated = screen.getByTestId('consumer-chip-binding-summarize');
    expect(gated).toBeDisabled();
    fireEvent.click(gated);
    expect(onChipClick).not.toHaveBeenCalled();

    // Non-gated chip stays clickable.
    const open = screen.getByTestId('consumer-chip-binding-create-matter');
    expect(open).toBeEnabled();
    fireEvent.click(open);
    expect(onChipClick).toHaveBeenCalledTimes(1);
    expect(onChipClick.mock.calls[0][0].bindingId).toBe('binding-create-matter');
  });

  it('requiresAttachments chip is enabled when attachments are present', () => {
    renderChips({ attachmentCount: 3 });
    expect(screen.getByTestId('consumer-chip-binding-summarize')).toBeEnabled();
  });

  it('renders nothing for an empty chip list', () => {
    renderChips({ chips: [] });
    expect(screen.queryByTestId('consumer-chips')).not.toBeInTheDocument();
  });

  it('strip-level disabled prop disables every chip', () => {
    renderChips({ disabled: true, attachmentCount: 5 });
    expect(screen.getByTestId('consumer-chip-binding-summarize')).toBeDisabled();
    expect(screen.getByTestId('consumer-chip-binding-create-matter')).toBeDisabled();
  });

  it('does not use hardcoded inline colors (ADR-021 — tokens only)', () => {
    renderChips();
    const strip = screen.getByTestId('consumer-chips');
    // Structural check: no element in the strip carries an inline style with
    // a literal color value; styling flows through Fluent v9 makeStyles tokens.
    const withInlineColor = Array.from(strip.querySelectorAll<HTMLElement>('*')).filter(
      (el) => /#|rgb\(/.test(el.getAttribute('style') ?? '')
    );
    expect(withInlineColor).toHaveLength(0);
  });
});
