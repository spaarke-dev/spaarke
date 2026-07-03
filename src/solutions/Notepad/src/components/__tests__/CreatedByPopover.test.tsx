/**
 * Unit tests for CreatedByPopover.
 *
 * Approach
 * ────────
 * Notepad's devDeps do NOT include `@testing-library/react`, so we render the
 * component using a minimalist harness built from `react-dom/client` + `act`.
 * This mirrors the pattern used by `useSprkMemoRepository.test.ts`.
 *
 * Coverage: FR-18 acceptance criteria (icon trigger, popover surface content,
 * locale-aware date formatting, fallbacks for null/invalid values, disabled).
 *
 * Interaction is exercised via `dispatchEvent` on real DOM nodes wrapped in
 * `act` — no `@testing-library/user-event` needed.
 */

/* eslint-disable @typescript-eslint/no-explicit-any */

// React 18 concurrent-mode test environment flag — must be set BEFORE React
// is imported so its internal `isConcurrentActEnvironment` check picks it up.
(globalThis as any).IS_REACT_ACT_ENVIRONMENT = true;

import * as React from 'react';
import { createRoot, type Root } from 'react-dom/client';
// React 18.3+: `act` moved from `react-dom/test-utils` to the `react` package.
import { act } from 'react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { CreatedByPopover } from '../CreatedByPopover';

// -- Test harness --------------------------------------------------------------

interface Harness {
  container: HTMLElement;
  root: Root;
  cleanup: () => void;
}

function mount(ui: React.ReactElement): Harness {
  const container = document.createElement('div');
  document.body.appendChild(container);
  const root = createRoot(container);
  act(() => {
    root.render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);
  });
  const cleanup = () => {
    act(() => {
      root.unmount();
    });
    container.remove();
  };
  return { container, root, cleanup };
}

function click(el: Element): void {
  act(() => {
    el.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
  });
}

function pressKey(el: Element, key: string): void {
  act(() => {
    el.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
  });
}

function getTrigger(container: HTMLElement): HTMLButtonElement {
  const trigger = container.querySelector<HTMLButtonElement>(
    '[data-testid="created-by-trigger"]'
  );
  if (!trigger) throw new Error('Trigger button not found');
  return trigger;
}

function getSurface(): HTMLElement | null {
  // PopoverSurface is portalled to document.body — search globally.
  return document.querySelector<HTMLElement>('[data-testid="created-by-surface"]');
}

// -- Fixtures ------------------------------------------------------------------

const validUser = { id: '11111111-2222-3333-4444-555555555555', name: 'Alice Author' };
const validIso = '2026-07-02T10:00:00Z';

describe('CreatedByPopover', () => {
  let harness: Harness | null = null;

  afterEach(() => {
    if (harness) {
      harness.cleanup();
      harness = null;
    }
    // Belt-and-suspenders — remove any orphaned popover surfaces.
    document
      .querySelectorAll('[data-testid="created-by-surface"]')
      .forEach((n) => n.remove());
  });

  // ────────────────────────────────────────────────────────────────────────────
  // Test 1: Renders icon trigger button (not popover) by default
  // ────────────────────────────────────────────────────────────────────────────

  it('renders icon trigger button and NOT the popover surface on initial mount', () => {
    harness = mount(<CreatedByPopover createdBy={validUser} createdOn={validIso} />);

    const trigger = getTrigger(harness.container);
    // Node is attached to the document (avoid relying on jest-dom matchers).
    expect(document.body.contains(trigger)).toBe(true);
    expect(trigger.tagName).toBe('BUTTON');

    // Popover surface is closed → not in DOM
    expect(getSurface()).toBeNull();
  });

  // ────────────────────────────────────────────────────────────────────────────
  // Test 2: Click trigger → popover opens
  // ────────────────────────────────────────────────────────────────────────────

  it('opens the popover surface when the trigger is clicked', () => {
    harness = mount(<CreatedByPopover createdBy={validUser} createdOn={validIso} />);

    click(getTrigger(harness.container));

    const surface = getSurface();
    expect(surface).not.toBeNull();
    expect(surface!.textContent).toContain('Created by Alice Author');
  });

  // ────────────────────────────────────────────────────────────────────────────
  // Test 3: Escape key closes the popover
  // ────────────────────────────────────────────────────────────────────────────

  it('closes the popover when Escape is pressed', () => {
    harness = mount(<CreatedByPopover createdBy={validUser} createdOn={validIso} />);

    click(getTrigger(harness.container));
    expect(getSurface()).not.toBeNull();

    const surface = getSurface()!;
    pressKey(surface, 'Escape');

    expect(getSurface()).toBeNull();
  });

  // ────────────────────────────────────────────────────────────────────────────
  // Test 4: Renders createdBy name when provided
  // ────────────────────────────────────────────────────────────────────────────

  it('renders "Created by {name}" when createdBy is provided', () => {
    harness = mount(<CreatedByPopover createdBy={validUser} createdOn={validIso} />);

    click(getTrigger(harness.container));

    expect(getSurface()!.textContent).toContain('Created by Alice Author');
  });

  // ────────────────────────────────────────────────────────────────────────────
  // Test 5: Fallback "Unknown user" when createdBy is null
  // ────────────────────────────────────────────────────────────────────────────

  it('renders "Unknown user" fallback when createdBy is null', () => {
    harness = mount(<CreatedByPopover createdBy={null} createdOn={validIso} />);

    click(getTrigger(harness.container));

    expect(getSurface()!.textContent).toContain('Created by Unknown user');
  });

  it('renders "Unknown user" fallback when createdBy.name is an empty string', () => {
    harness = mount(
      <CreatedByPopover createdBy={{ id: 'x', name: '' }} createdOn={validIso} />
    );

    click(getTrigger(harness.container));

    expect(getSurface()!.textContent).toContain('Created by Unknown user');
  });

  // ────────────────────────────────────────────────────────────────────────────
  // Test 6: Formatted date shown for a valid ISO string
  // ────────────────────────────────────────────────────────────────────────────

  it('renders a locale-formatted date when createdOn is a valid ISO string', () => {
    harness = mount(<CreatedByPopover createdBy={validUser} createdOn={validIso} />);

    click(getTrigger(harness.container));

    const expected = new Intl.DateTimeFormat(undefined, {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(new Date(validIso));

    const text = getSurface()!.textContent!;
    expect(text).toContain('on ');
    expect(text).toContain(expected);
    // Also assert the surface does not include an unknown-date fallback
    expect(text).not.toContain('unknown date');
  });

  // ────────────────────────────────────────────────────────────────────────────
  // Test 7: "on unknown date" when createdOn is null
  // ────────────────────────────────────────────────────────────────────────────

  it('renders "on unknown date" when createdOn is null', () => {
    harness = mount(<CreatedByPopover createdBy={validUser} createdOn={null} />);

    click(getTrigger(harness.container));

    expect(getSurface()!.textContent).toContain('on unknown date');
  });

  // ────────────────────────────────────────────────────────────────────────────
  // Test 8: "on unknown date" when createdOn is an invalid string
  // ────────────────────────────────────────────────────────────────────────────

  it('renders "on unknown date" when createdOn is an invalid ISO string', () => {
    harness = mount(<CreatedByPopover createdBy={validUser} createdOn={'not-a-date'} />);

    click(getTrigger(harness.container));

    expect(getSurface()!.textContent).toContain('on unknown date');
  });

  // ────────────────────────────────────────────────────────────────────────────
  // Test 9: disabled=true disables the trigger button
  // ────────────────────────────────────────────────────────────────────────────

  it('disables the trigger button when disabled=true', () => {
    harness = mount(
      <CreatedByPopover createdBy={validUser} createdOn={validIso} disabled />
    );

    const trigger = getTrigger(harness.container);
    // Use the DOM `disabled` property (avoid relying on jest-dom matchers).
    expect(trigger.disabled).toBe(true);
  });
});
