/**
 * R4 task 048 (FR-32, #2466): the phone layout at a viewport of 480px or less.
 *
 * jsdom has no layout and does not evaluate media queries, so the CSS contract is proven the way
 * RecordTemplateModal.test.tsx proves the 1000px stack: an element carries exactly the atomic classes a phone rule
 * compiles to (Griffel class names hash the property, value AND media query, so the class proves the 480px breakpoint).
 * The overflow decision is JS, so `window.matchMedia` is stubbed to answer the phone query either way.
 *
 * What runs for real: `RecordTemplateModal` (with the shared `RecordCommandBar` and `SprkModal`), `PortalWorkspaceShell`,
 * `TabStrip`, `phoneLayout`. Substituted: the caller's `/me` read (`getExternalUserContext`), and the record read (a
 * `seed` row with a `readRecord` stand-in). The 375px browser check (no horizontal scroll, every control 44px or more) is
 * recorded in notes/task-048-notes.md and repeated on dev by task 058.
 */
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { FluentProvider, makeStyles, tokens, webLightTheme, buttonClassNames } from '@fluentui/react-components';
import { DocumentRegular } from '@fluentui/react-icons';

const h = vi.hoisted(() => ({ getExternalUserContext: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, getExternalUserContext: h.getExternalUserContext };
});

import {
  PHONE_MAX_WIDTH_PX,
  PHONE_MEDIA,
  PHONE_MEDIA_QUERY,
  PHONE_TOUCH_TARGET_CSS,
  TOUCH_TARGET,
  useIsPhoneWidth,
} from '../src/components/phoneLayout';
import { PHONE_OVERFLOW_ACTIONS, RECORD_TEMPLATES } from '../src/components/record/recordTemplates';
import { RECORD_SECTION_LABELS, type RecordSectionId } from '../src/components/record/recordTemplate.types';
import { RecordTemplateModal } from '../src/components/record/RecordTemplateModal';
import { PortalWorkspaceShell } from '../src/components/shell/PortalWorkspaceShell';
import { TabStrip } from '../src/components/shell/TabStrip';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Stubs `window.matchMedia` so the phone query answers `phone`; other queries answer false. */
function stubViewport(phone: boolean): { setPhone: (next: boolean) => void } {
  const listeners = new Set<() => void>();
  let matches = phone;
  vi.stubGlobal(
    'matchMedia',
    vi.fn((query: string) => ({
      get matches() {
        return query === PHONE_MEDIA_QUERY ? matches : false;
      },
      media: query,
      onchange: null,
      addEventListener: (_: string, cb: () => void) => listeners.add(cb),
      removeEventListener: (_: string, cb: () => void) => listeners.delete(cb),
      addListener: () => undefined,
      removeListener: () => undefined,
      dispatchEvent: () => false,
    }))
  );
  return {
    setPhone: next => {
      matches = next;
      listeners.forEach(cb => cb());
    },
  };
}

/** The atomic classes a style object compiles to (the RecordTemplateModal.test.tsx probe). */
function atomicClassesOf(style: Parameters<typeof makeStyles>[0][string]): string[] {
  const useProbe = makeStyles({ probe: style });
  let classes = '';
  const Probe: React.FC = () => {
    classes = useProbe().probe;
    return null;
  };
  const { unmount } = render(<Probe />);
  unmount();
  return classes.split(/\s+/).filter(c => c && !c.startsWith('___'));
}

function expectPhoneRule(el: Element, rule: Parameters<typeof makeStyles>[0][string]): void {
  const classes = atomicClassesOf({ [PHONE_MEDIA]: rule });
  expect(classes.length).toBeGreaterThan(0);
  for (const c of classes) expect(el.classList.contains(c)).toBe(true);
}

const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;

const standInSection = (id: RecordSectionId): React.ReactNode => (
  <section aria-label={RECORD_SECTION_LABELS[id]} data-testid={`record-section-${id}`} />
);

/** A document record (toolbar: Refresh, Download, Copy link). Copy link always shows; Download needs a file host. */
async function openDocument(): Promise<HTMLElement> {
  const seed = {
    sprk_documentid: guid(301),
    sprk_documentname: 'Engagement letter.pdf',
  };
  render(
    <FluentProvider theme={webLightTheme}>
      <RecordTemplateModal
        open
        onClose={() => undefined}
        config={RECORD_TEMPLATES.sprk_document}
        recordIds={[guid(301)]}
        index={0}
        onIndexChange={() => undefined}
        seed={seed}
        readRecord={async () => seed}
        renderSection={standInSection}
      />
    </FluentProvider>
  );
  const dialog = await screen.findByRole('dialog');
  await within(dialog).findByTestId('record-command-bar');
  return dialog;
}

beforeEach(() => {
  h.getExternalUserContext.mockReset();
  h.getExternalUserContext.mockResolvedValue({ contactId: guid(7), email: 'me@example.test', projects: [] });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

// ---------------------------------------------------------------------------
// The breakpoint
// ---------------------------------------------------------------------------

describe('phone breakpoint (FR-32)', () => {
  it('is one 480px constant, used by both the media key and the matchMedia query', () => {
    expect(PHONE_MAX_WIDTH_PX).toBe(480);
    expect(PHONE_MEDIA_QUERY).toBe('(max-width: 480px)');
    expect(PHONE_MEDIA).toBe('@media (max-width: 480px)');
    expect(TOUCH_TARGET).toBe('44px');
  });

  it('the touch-target rule applies only inside the phone query and covers buttons, menu items, tabs and inputs', () => {
    expect(PHONE_TOUCH_TARGET_CSS.trim().startsWith('@media (max-width: 480px) {')).toBe(true);
    for (const cls of [
      'fui-Button',
      'fui-ToggleButton',
      'fui-MenuItem',
      'fui-Tab',
      'fui-Input',
      'fui-Select__select',
    ]) {
      expect(PHONE_TOUCH_TARGET_CSS).toContain(`.${cls}.${cls}`);
    }
    expect(PHONE_TOUCH_TARGET_CSS).toContain(`.${buttonClassNames.root}`);
    expect(PHONE_TOUCH_TARGET_CSS).toContain('min-height: 44px');
    expect(PHONE_TOUCH_TARGET_CSS).toContain('min-width: 44px');
  });

  it('useIsPhoneWidth follows the viewport', () => {
    const viewport = stubViewport(false);
    let seen: boolean[] = [];
    const Probe: React.FC = () => {
      seen = [...seen, useIsPhoneWidth()];
      return null;
    };
    render(<Probe />);
    expect(seen.at(-1)).toBe(false);
    act(() => viewport.setPhone(true));
    expect(seen.at(-1)).toBe(true);
    act(() => viewport.setPhone(false));
    expect(seen.at(-1)).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// The record toolbar: secondary actions in the shared bar's overflow
// ---------------------------------------------------------------------------

describe('record toolbar at phone width (FR-32)', () => {
  it('lists only secondary actions for the overflow, never Refresh or a type primary', () => {
    expect([...PHONE_OVERFLOW_ACTIONS].sort()).toEqual(['changeStatus', 'copyLink', 'delete', 'history']);
    for (const primary of ['refresh', 'download', 'submitInvoice', 'edit'] as const) {
      expect(PHONE_OVERFLOW_ACTIONS.has(primary)).toBe(false);
    }
  });

  it('at 480px or less, Copy link moves into "More actions" with tooltip = aria-label; Refresh stays inline', async () => {
    stubViewport(true);
    const dialog = await openDocument();
    const bar = within(dialog).getByTestId('record-command-bar');
    expect(within(bar).getByRole('button', { name: 'Refresh' })).toBeTruthy();
    expect(within(bar).queryByRole('button', { name: 'Copy link' })).toBeNull();

    fireEvent.click(within(bar).getByRole('button', { name: 'More actions' }));
    const item = await screen.findByTestId('record-command-bar-item-copyLink');
    expect(item.getAttribute('aria-label')).toBe('Copy link');
    expect(item.textContent).toContain('Copy link');
  });

  it('negative: wider than 480px, every action stays inline and there is no "More actions"', async () => {
    stubViewport(false);
    const dialog = await openDocument();
    const bar = within(dialog).getByTestId('record-command-bar');
    expect(within(bar).getByRole('button', { name: 'Refresh' })).toBeTruthy();
    expect(within(bar).getByRole('button', { name: 'Copy link' })).toBeTruthy();
    expect(within(bar).queryByRole('button', { name: 'More actions' })).toBeNull();
  });

  it('the record content and header facts carry the phone rules (one column, tighter padding)', async () => {
    stubViewport(true);
    const dialog = await openDocument();
    // The scrolling content element is the columns' grandparent (content > ready > columns).
    const scroller = within(dialog).getByTestId('record-template-columns').parentElement!.parentElement!;
    expectPhoneRule(scroller, { paddingBlock: tokens.spacingVerticalM, paddingInline: tokens.spacingHorizontalS });
    const facts = within(dialog).getByTestId('record-template-header').querySelector('dl')!;
    expectPhoneRule(facts, { flexDirection: 'column', width: '100%' });
  });
});

// ---------------------------------------------------------------------------
// The shell and the tab strip
// ---------------------------------------------------------------------------

function renderShell(assistantCollapsed: boolean): ReturnType<typeof render> {
  return render(
    <FluentProvider theme={webLightTheme}>
      <PortalWorkspaceShell
        widgetTabs={[{ id: 'docs', title: 'My Documents', icon: DocumentRegular }]}
        activeTabId="docs"
        onSelectTab={() => undefined}
        onCloseTab={() => undefined}
        onAddWidget={() => undefined}
        assistant={<p>assistant body</p>}
        renderWidget={id => <p>widget {id}</p>}
        assistantCollapsed={assistantCollapsed}
        onToggleAssistant={() => undefined}
        assistantDock="right"
        onMoveAssistant={() => undefined}
      />
    </FluentProvider>
  );
}

describe('workspace shell at phone width (FR-32)', () => {
  it('stacks into one column; an open assistant takes the body and hides "Move assistant"', () => {
    const { container } = renderShell(false);
    const main = container.querySelector('main')!;
    expectPhoneRule(main.parentElement!, { flexDirection: 'column' });
    expectPhoneRule(main, { display: 'none' });
    const aside = container.querySelector('aside')!;
    expectPhoneRule(aside, { width: '100%', flexGrow: 1, flexShrink: 1 });
    expectPhoneRule(within(aside).getByRole('button', { name: 'Move assistant to the left' }), { display: 'none' });
  });

  it('negative: with the assistant collapsed, the workspace is not hidden', () => {
    const { container } = renderShell(true);
    const main = container.querySelector('main')!;
    const hide = atomicClassesOf({ [PHONE_MEDIA]: { display: 'none' } });
    expect(hide.some(c => main.classList.contains(c))).toBe(false);
  });

  it('tab strip: tabs are 44px high and the close control is an inline 44px target', () => {
    render(
      <FluentProvider theme={webLightTheme}>
        <TabStrip
          tabs={[{ id: 'docs', title: 'My Documents', icon: DocumentRegular, closable: true }]}
          activeId="docs"
          onSelect={() => undefined}
          onClose={() => undefined}
        />
      </FluentProvider>
    );
    const tab = screen.getByRole('tab', { name: /My Documents/ });
    expectPhoneRule(tab, { minHeight: TOUCH_TARGET, boxSizing: 'border-box', maxWidth: '100%' });
    const close = screen.getByRole('button', { name: 'Close My Documents tab' });
    expectPhoneRule(close, { position: 'static', width: TOUCH_TARGET, height: TOUCH_TARGET });
  });
});
