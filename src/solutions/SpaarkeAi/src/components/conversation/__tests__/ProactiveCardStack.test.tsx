/**
 * ProactiveCardStack.test.tsx — spaarkeai-assistant-enhancements-r3 task 041 (FR-14).
 *
 * ASSISTANT-UI-ELEMENT-CRITERIA.md §5: "collapse a *stack* of proactive
 * cards behind a single disclosure header ... the header is a toggle (no
 * hover), the cards drop down." Proves:
 *   (a) 0 slots present → renders nothing.
 *   (b) 1 slot present → renders it directly, unwrapped (no outer header).
 *   (c) 2+ slots present → collapses behind ONE outer disclosure header
 *       showing the count; expanding reveals the full stack; collapsing
 *       hides it again.
 *   (d) null/undefined-node slots are filtered from the count (a slot that
 *       isn't currently "offered" doesn't count toward the stack).
 */
import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { ProactiveCardStack, type ProactiveCardSlot } from '../ProactiveCardStack';

function renderStack(slots: ReadonlyArray<ProactiveCardSlot>) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ProactiveCardStack slots={slots} />
    </FluentProvider>
  );
}

describe('ProactiveCardStack (task 041, FR-14)', () => {
  it('renders nothing when 0 slots are present', () => {
    const { container } = renderStack([
      { key: 'a', node: null },
      { key: 'b', node: undefined },
    ]);
    expect(container.querySelector('[data-testid="proactive-card-stack"]')).toBeNull();
    expect(container.textContent).toBe('');
  });

  it('renders a SINGLE present slot directly — no outer disclosure header', () => {
    renderStack([
      { key: 'a', node: null },
      { key: 'b', node: <div data-testid="card-b">Card B</div> },
    ]);
    expect(screen.getByTestId('card-b')).toBeInTheDocument();
    expect(screen.queryByTestId('proactive-card-stack-toggle')).not.toBeInTheDocument();
  });

  it('collapses 2+ present slots behind ONE outer disclosure header', () => {
    renderStack([
      { key: 'a', node: <div data-testid="card-a">Card A</div> },
      { key: 'b', node: <div data-testid="card-b">Card B</div> },
    ]);
    const toggle = screen.getByTestId('proactive-card-stack-toggle');
    expect(toggle).toBeInTheDocument();
    expect(toggle).toHaveTextContent('You have 2 pending actions');
    // Expanded by default — the stack is visible.
    expect(screen.getByTestId('card-a')).toBeInTheDocument();
    expect(screen.getByTestId('card-b')).toBeInTheDocument();
  });

  it('collapses 3 present slots behind the SAME single header (count reflects present slots only)', () => {
    renderStack([
      { key: 'a', node: <div data-testid="card-a">Card A</div> },
      { key: 'b', node: <div data-testid="card-b">Card B</div> },
      { key: 'c', node: <div data-testid="card-c">Card C</div> },
      { key: 'd', node: null },
    ]);
    expect(screen.getByTestId('proactive-card-stack-toggle')).toHaveTextContent('You have 3 pending actions');
    // Exactly ONE header — not one per card.
    expect(screen.getAllByTestId('proactive-card-stack-toggle')).toHaveLength(1);
  });

  it('the toggle collapses and re-expands the stack (aria-expanded reflects state)', () => {
    renderStack([
      { key: 'a', node: <div data-testid="card-a">Card A</div> },
      { key: 'b', node: <div data-testid="card-b">Card B</div> },
    ]);
    const toggle = screen.getByTestId('proactive-card-stack-toggle');
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByTestId('card-a')).toBeInTheDocument();

    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByTestId('card-a')).not.toBeInTheDocument();
    expect(screen.queryByTestId('card-b')).not.toBeInTheDocument();

    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByTestId('card-a')).toBeInTheDocument();
    expect(screen.getByTestId('card-b')).toBeInTheDocument();
  });

  it('the header label contains no code/raw key — a plain human-readable count sentence', () => {
    renderStack([
      { key: 'a', node: <div>Card A</div> },
      { key: 'b', node: <div>Card B</div> },
    ]);
    const toggle = screen.getByTestId('proactive-card-stack-toggle');
    expect(toggle.textContent).toBe('You have 2 pending actions');
    // No slot key, tool name, or bracket/underscore-shaped identifier leaks into the label.
    expect(toggle.textContent).not.toMatch(/[_[\]{}]/);
  });
});
