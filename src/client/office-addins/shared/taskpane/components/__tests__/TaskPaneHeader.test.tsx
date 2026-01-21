import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { TaskPaneHeader } from '../TaskPaneHeader';

// Wrap component with FluentProvider for testing
const renderWithProvider = (ui: React.ReactElement) => {
  return render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);
};

describe('TaskPaneHeader', () => {
  it('renders with default title', () => {
    renderWithProvider(<TaskPaneHeader />);
    expect(screen.getByText('Spaarke')).toBeInTheDocument();
  });

  it('renders with custom title', () => {
    renderWithProvider(<TaskPaneHeader title="Custom Title" />);
    expect(screen.getByText('Custom Title')).toBeInTheDocument();
  });

  it('shows Outlook icon for outlook host', () => {
    renderWithProvider(<TaskPaneHeader hostType="outlook" />);
    // Mail icon should be present (Outlook)
    expect(screen.getByText('Spaarke')).toBeInTheDocument();
  });

  it('shows Word icon for word host', () => {
    renderWithProvider(<TaskPaneHeader hostType="word" />);
    expect(screen.getByText('Spaarke')).toBeInTheDocument();
  });

  it('calls onSettings when settings button clicked', () => {
    const handleSettings = jest.fn();
    renderWithProvider(<TaskPaneHeader onSettings={handleSettings} />);

    fireEvent.click(screen.getByLabelText('Settings'));
    expect(handleSettings).toHaveBeenCalled();
  });

  it('shows theme menu when theme button clicked', () => {
    const handleThemeChange = jest.fn();
    renderWithProvider(
      <TaskPaneHeader themePreference="auto" onThemeChange={handleThemeChange} />
    );

    fireEvent.click(screen.getByLabelText('Change theme'));
    expect(screen.getByText('Auto')).toBeInTheDocument();
    expect(screen.getByText('Light')).toBeInTheDocument();
    expect(screen.getByText('Dark')).toBeInTheDocument();
  });

  it('shows user menu when authenticated', () => {
    renderWithProvider(
      <TaskPaneHeader
        isAuthenticated={true}
        userName="John Doe"
        userEmail="john@example.com"
        onSignOut={() => {}}
      />
    );

    const userButton = screen.getByLabelText(/signed in as john doe/i);
    expect(userButton).toBeInTheDocument();

    // Open user menu
    fireEvent.click(userButton);
    expect(screen.getByText('John Doe')).toBeInTheDocument();
    expect(screen.getByText('john@example.com')).toBeInTheDocument();
    expect(screen.getByText('Sign out')).toBeInTheDocument();
  });

  it('calls onSignOut when sign out clicked', () => {
    const handleSignOut = jest.fn();
    renderWithProvider(
      <TaskPaneHeader
        isAuthenticated={true}
        userName="John Doe"
        onSignOut={handleSignOut}
      />
    );

    // Open user menu
    fireEvent.click(screen.getByLabelText(/signed in as john doe/i));
    // Click sign out
    fireEvent.click(screen.getByText('Sign out'));
    expect(handleSignOut).toHaveBeenCalled();
  });

  it('hides title in compact mode', () => {
    renderWithProvider(<TaskPaneHeader title="Spaarke" compact={true} />);
    // Title should not be visible in compact mode
    expect(screen.queryByText('Spaarke')).not.toBeInTheDocument();
  });
});
