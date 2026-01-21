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

    expect(screen.getByText('Spaarke')).toBeInTheDocument();
    expect(screen.getByTestId('content')).toBeInTheDocument();
  });

  it('renders with custom title', () => {
    renderWithProvider(
      <TaskPaneShell title="Custom Title">
        <div>Content</div>
      </TaskPaneShell>
    );

    expect(screen.getByText('Custom Title')).toBeInTheDocument();
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
      <TaskPaneShell
        isAuthenticated={true}
        showNavigation={true}
        selectedTab="save"
        onTabChange={() => {}}
      >
        <div>Content</div>
      </TaskPaneShell>
    );

    expect(screen.getByRole('tablist')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /save/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /share/i })).toBeInTheDocument();
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
      <TaskPaneShell
        isAuthenticated={true}
        showNavigation={true}
        selectedTab="save"
        onTabChange={handleTabChange}
      >
        <div>Content</div>
      </TaskPaneShell>
    );

    fireEvent.click(screen.getByRole('tab', { name: /share/i }));
    expect(handleTabChange).toHaveBeenCalledWith('share');
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
      <TaskPaneShell
        isAuthenticated={true}
        userName="John Doe"
        onSignOut={handleSignOut}
      >
        <div>Content</div>
      </TaskPaneShell>
    );

    // User button should be present
    const userButton = screen.getByLabelText(/signed in as john doe/i);
    expect(userButton).toBeInTheDocument();
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
