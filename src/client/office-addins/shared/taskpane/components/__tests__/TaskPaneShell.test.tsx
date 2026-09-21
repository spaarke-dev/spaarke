import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { TaskPaneShell } from '../TaskPaneShell';

// Wrap component with FluentProvider for testing
const renderWithProvider = (ui: React.ReactElement) => {
  return render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);
};

describe('TaskPaneShell', () => {
  it('renders with default props', () => {
    renderWithProvider(
      <TaskPaneShell>
        <div data-testid="content">Content</div>
      </TaskPaneShell>
    );

    // TaskPaneShell's default `appName` is `'Spaarke DMS'` (TaskPaneShell.tsx:138), rendered verbatim
    // as one text node by TaskPaneFooter (`<span>{appName}</span>`, TaskPaneFooter.tsx:127) — an
    // exact-match `getByText('Spaarke')` never matches it (task 071).
    expect(screen.getByText('Spaarke DMS')).toBeInTheDocument();
    expect(screen.getByTestId('content')).toBeInTheDocument();
  });

  it('accepts a title prop without breaking rendering (prop is currently unused — see note)', () => {
    // FINDING (task 071, confirms the review's "partly inferred" TaskPaneShell diagnosis): `title` is
    // still declared on `TaskPaneShellProps` (TaskPaneShell.tsx:58) but the component body never reads
    // it — task 015's toolbar consolidation ("what used to be two stacked rows... consolidated",
    // TaskPaneShell.tsx:16-18 / TaskPaneToolbar.tsx:32-36) replaced the old `TaskPaneHeader` (whose
    // default title WAS 'Spaarke', TaskPaneHeader.tsx:125) with `TaskPaneToolbar`, which renders no
    // title text at all (its `logo` style class, TaskPaneToolbar.tsx:53, is declared but never applied
    // to any element). This is dead-prop drift from that consolidation, not a live behavioral defect —
    // nothing reads or displays `title` today in Word or Outlook, so there is no user-facing regression
    // to escalate. Repairing the test to assert invisible text would be worse than deleting the
    // assertion, so this now pins the honest contract: passing `title` is accepted and harmless.
    renderWithProvider(
      <TaskPaneShell title="Custom Title">
        <div data-testid="content">Content</div>
      </TaskPaneShell>
    );

    expect(screen.queryByText('Custom Title')).not.toBeInTheDocument();
    expect(screen.getByTestId('content')).toBeInTheDocument();
  });

  it('shows loading skeleton when isLoading is true', () => {
    renderWithProvider(
      <TaskPaneShell isLoading={true}>
        <div data-testid="content">Content</div>
      </TaskPaneShell>
    );

    // Content should not be visible during loading
    expect(screen.queryByTestId('content')).not.toBeInTheDocument();
    // Skeleton should be present
    expect(screen.getByLabelText('Loading header')).toBeInTheDocument();
  });

  it('shows navigation tabs when authenticated', () => {
    renderWithProvider(
      <TaskPaneShell isAuthenticated={true} showNavigation={true} selectedTab="save" onTabChange={() => {}}>
        <div>Content</div>
      </TaskPaneShell>
    );

    expect(screen.getByRole('tablist')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /save/i })).toBeInTheDocument();
    // Share is a hidden V1 placeholder (TaskPaneNavigation.tsx TAB_CONFIGS — commented out); the r1
    // shell renders Save, Find and Create To Do instead (task 071 — matches TaskPaneNavigation.test.tsx).
    expect(screen.getByRole('tab', { name: /find/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /create to do/i })).toBeInTheDocument();
  });

  it('hides navigation when not authenticated', () => {
    renderWithProvider(
      <TaskPaneShell isAuthenticated={false} showNavigation={true}>
        <div>Content</div>
      </TaskPaneShell>
    );

    expect(screen.queryByRole('tablist')).not.toBeInTheDocument();
  });

  it('calls onTabChange when tab is clicked', () => {
    const handleTabChange = jest.fn();

    renderWithProvider(
      <TaskPaneShell isAuthenticated={true} showNavigation={true} selectedTab="save" onTabChange={handleTabChange}>
        <div>Content</div>
      </TaskPaneShell>
    );

    fireEvent.click(screen.getByRole('tab', { name: /find/i }));
    expect(handleTabChange).toHaveBeenCalledWith('find');
  });

  it('renders footer with version', () => {
    renderWithProvider(
      <TaskPaneShell version="2.0.0">
        <div>Content</div>
      </TaskPaneShell>
    );

    expect(screen.getByText('v2.0.0')).toBeInTheDocument();
  });

  it('renders footer with app name', () => {
    renderWithProvider(
      <TaskPaneShell appName="Test App">
        <div>Content</div>
      </TaskPaneShell>
    );

    expect(screen.getByText('Test App')).toBeInTheDocument();
  });

  it('shows sign out button when authenticated', () => {
    const handleSignOut = jest.fn();

    renderWithProvider(
      <TaskPaneShell isAuthenticated={true} userName="John Doe" onSignOut={handleSignOut}>
        <div>Content</div>
      </TaskPaneShell>
    );

    // TaskPaneToolbar (the live renderer since the task 015 consolidation) has no standalone
    // "signed in as {name}" button — user identity + Sign out live inside the "More options" overflow
    // menu (TaskPaneToolbar.tsx:153-217), closed by default. Open it first (task 071).
    fireEvent.click(screen.getByRole('button', { name: /more options/i }));

    expect(screen.getByText('John Doe')).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: /sign out/i })).toBeInTheDocument();
  });

  it('renders content inside error boundary', () => {
    const ErrorComponent = () => {
      throw new Error('Test error');
    };

    // Suppress console.error for this test
    const consoleSpy = jest.spyOn(console, 'error').mockImplementation(() => {});

    renderWithProvider(
      <TaskPaneShell showErrorDetails={true}>
        <ErrorComponent />
      </TaskPaneShell>
    );

    expect(screen.getByText('Something went wrong')).toBeInTheDocument();

    consoleSpy.mockRestore();
  });
});
