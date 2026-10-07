import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { TaskPaneNavigation, getDefaultTab, getAvailableTabs } from '../TaskPaneNavigation';
import { TaskPaneToolbar } from '../TaskPaneToolbar';

// Wrap component with FluentProvider for testing
const renderWithProvider = (ui: React.ReactElement) => {
  return render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);
};

describe('TaskPaneNavigation', () => {
  it('renders the enabled navigation tabs (Save + To Do for Outlook)', () => {
    renderWithProvider(
      <TaskPaneNavigation
        selectedTab="save"
        onTabChange={() => {
          /* no-op */
        }}
        hostType="outlook"
      />
    );

    expect(screen.getByRole('tab', { name: /save/i })).toBeInTheDocument();
    // Task 091 (UAT-2): the tab's accessible/visible label is "To Do", not "Create To Do".
    expect(screen.getByRole('tab', { name: /^to do$/i })).toBeInTheDocument();
    // Share/Search/Recent are disabled ("V1") — not rendered.
    expect(screen.queryByRole('tab', { name: /share/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /recent/i })).not.toBeInTheDocument();
  });

  it('renders Save, Find and To Do tabs for Word', () => {
    renderWithProvider(
      <TaskPaneNavigation
        selectedTab="save"
        onTabChange={() => {
          /* no-op */
        }}
        hostType="word"
      />
    );

    expect(screen.getByRole('tab', { name: /save/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /find/i })).toBeInTheDocument();
    // Create To Do is a shared capability (task 049 / FR-14 / FR-19) — no longer Outlook-only.
    expect(screen.getByRole('tab', { name: /^to do$/i })).toBeInTheDocument();
    // Share/Recent are disabled ("V1") — not rendered on Word either.
    expect(screen.queryByRole('tab', { name: /share/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /recent/i })).not.toBeInTheDocument();
  });

  it('highlights selected tab', () => {
    renderWithProvider(
      <TaskPaneNavigation
        selectedTab="createTodo"
        onTabChange={() => {
          /* no-op */
        }}
        hostType="outlook"
      />
    );

    const createTodoTab = screen.getByRole('tab', { name: /^to do$/i });
    expect(createTodoTab).toHaveAttribute('aria-selected', 'true');

    const saveTab = screen.getByRole('tab', { name: /save/i });
    expect(saveTab).toHaveAttribute('aria-selected', 'false');
  });

  it('calls onTabChange when tab is clicked', () => {
    const handleTabChange = jest.fn();
    renderWithProvider(<TaskPaneNavigation selectedTab="save" onTabChange={handleTabChange} hostType="outlook" />);

    fireEvent.click(screen.getByRole('tab', { name: /^to do$/i }));
    expect(handleTabChange).toHaveBeenCalledWith('createTodo');
  });

  it('disables tabs when disabled prop is true', () => {
    renderWithProvider(
      <TaskPaneNavigation
        selectedTab="save"
        onTabChange={() => {
          /* no-op */
        }}
        disabled={true}
      />
    );

    // Fluent v9's TabList spreads `disabled` onto each rendered `<button role="tab">` (a real HTML
    // `disabled=""` attribute), not onto the `role="tablist"` container as `aria-disabled` — verified
    // by inspecting the rendered DOM (task 071).
    expect(screen.getByRole('tab', { name: /save/i })).toBeDisabled();
  });

  it('renders smaller tabs in compact mode', () => {
    renderWithProvider(
      <TaskPaneNavigation
        selectedTab="save"
        onTabChange={() => {
          /* no-op */
        }}
        compact={true}
      />
    );

    // In compact mode, tab text should not be visible (icon only)
    // The tab should still exist but with just the icon
    expect(screen.getByRole('tab', { name: /save/i })).toBeInTheDocument();
  });

  it('returns correct default tab for each host type', () => {
    expect(getDefaultTab('outlook')).toBe('save');
    expect(getDefaultTab('word')).toBe('save');
  });
});

// Task 096 (owner UAT round 4 item 6: "Add the send email to the main add-in bar next to 'Find'"): the Email
// tab follows Find, and is gated on the `canEmailFromPane` CAPABILITY (Word true, Outlook false) — NFR-10.
// Task 106 (owner UAT round 8): its label is "Send".
describe('Email tab (task 096) — capability-gated, after Find', () => {
  const labels = (tabs: { label: string }[]) => tabs.map(t => t.label);

  it('Word (canEmailFromPane true): Save · To Do · Find · Send, in that order', () => {
    expect(labels(getAvailableTabs('word', { canEmailFromPane: true }))).toEqual(['Save', 'To Do', 'Find', 'Send']);
  });

  it('Outlook (canEmailFromPane false): tabs unchanged — no Email tab', () => {
    expect(labels(getAvailableTabs('outlook', { canEmailFromPane: false }))).toEqual(['Save', 'To Do', 'Find']);
  });

  it('the gate is the capability, not the host: a host without the capability never gets the tab', () => {
    expect(labels(getAvailableTabs('word', { canEmailFromPane: false }))).not.toContain('Send');
    expect(labels(getAvailableTabs('outlook', { canEmailFromPane: true }))).toContain('Send');
  });

  it('fails closed: no capabilities supplied → no Email tab', () => {
    expect(labels(getAvailableTabs('word'))).toEqual(['Save', 'To Do', 'Find']);
  });

  it('the live toolbar renders the Email tab after Find when the host reports the capability', () => {
    renderWithProvider(
      <TaskPaneToolbar hostType="word" capabilities={{ canEmailFromPane: true }} isAuthenticated selectedTab="save" />
    );

    // Fluent's Tab renders a hidden width-reserving copy of its label, so read the order from the DOM position
    // of each tab's accessible name rather than `textContent`.
    const tabs = screen.getAllByRole('tab');
    expect(tabs).toHaveLength(4);
    const find = screen.getByRole('tab', { name: 'Find' });
    const email = screen.getByRole('tab', { name: 'Send' });
    expect(tabs.indexOf(email)).toBe(3);
    expect(tabs.indexOf(find)).toBe(2);
  });

  it('the live toolbar renders no Email tab for Outlook', () => {
    renderWithProvider(
      <TaskPaneToolbar
        hostType="outlook"
        capabilities={{ canEmailFromPane: false }}
        isAuthenticated
        selectedTab="save"
      />
    );

    expect(screen.queryByRole('tab', { name: /send/i })).not.toBeInTheDocument();
  });
});

// Task 106 (owner UAT round 8: "the 'send email should be in the tool bar next to Find"): Outlook's Send Email
// is an ACTION button in the toolbar, directly after the last tab — not a tab, and absent unless supplied.
describe('Send action (task 106) — Outlook toolbar, after Find', () => {
  const outlookToolbar = (props: Partial<React.ComponentProps<typeof TaskPaneToolbar>> = {}) =>
    renderWithProvider(
      <TaskPaneToolbar
        hostType="outlook"
        capabilities={{ canEmailFromPane: false }}
        isAuthenticated
        selectedTab="save"
        {...props}
      />
    );

  it('renders a Send button directly after the Find tab and calls the handler', () => {
    const onSendEmail = jest.fn();
    outlookToolbar({ onSendEmail });

    const send = screen.getByRole('button', { name: 'Send' });
    const find = screen.getByRole('tab', { name: 'Find' });
    // Find precedes Send in document order, and Send is not a tab.
    expect(find.compareDocumentPosition(send) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(screen.queryByRole('tab', { name: 'Send' })).not.toBeInTheDocument();

    fireEvent.click(send);
    expect(onSendEmail).toHaveBeenCalledTimes(1);
  });

  it('is absent (not disabled) when no handler is supplied — nothing to link yet', () => {
    outlookToolbar();
    expect(screen.queryByRole('button', { name: 'Send' })).not.toBeInTheDocument();
  });

  it('is disabled while the compose window is opening', () => {
    const onSendEmail = jest.fn();
    outlookToolbar({ onSendEmail, isSendingEmail: true });
    const send = screen.getByRole('button', { name: 'Send' });
    expect(send).toBeDisabled();
    fireEvent.click(send);
    expect(onSendEmail).not.toHaveBeenCalled();
  });
});
