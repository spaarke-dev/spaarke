import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { TaskPaneNavigation, getDefaultTab } from '../TaskPaneNavigation';

// Wrap component with FluentProvider for testing
const renderWithProvider = (ui: React.ReactElement) => {
  return render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);
};

describe('TaskPaneNavigation', () => {
  it('renders the enabled navigation tabs (Save + Create To Do for Outlook)', () => {
    renderWithProvider(<TaskPaneNavigation selectedTab="save" onTabChange={() => {}} hostType="outlook" />);

    expect(screen.getByRole('tab', { name: /save/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /create to do/i })).toBeInTheDocument();
    // Share/Search/Recent are disabled ("V1") — not rendered.
    expect(screen.queryByRole('tab', { name: /share/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /recent/i })).not.toBeInTheDocument();
  });

  it('renders only the Save tab for Word', () => {
    renderWithProvider(<TaskPaneNavigation selectedTab="save" onTabChange={() => {}} hostType="word" />);

    expect(screen.getByRole('tab', { name: /save/i })).toBeInTheDocument();
    // Create To Do is Outlook-only (a To Do is created from an email).
    expect(screen.queryByRole('tab', { name: /create to do/i })).not.toBeInTheDocument();
  });

  it('highlights selected tab', () => {
    renderWithProvider(<TaskPaneNavigation selectedTab="createTodo" onTabChange={() => {}} hostType="outlook" />);

    const createTodoTab = screen.getByRole('tab', { name: /create to do/i });
    expect(createTodoTab).toHaveAttribute('aria-selected', 'true');

    const saveTab = screen.getByRole('tab', { name: /save/i });
    expect(saveTab).toHaveAttribute('aria-selected', 'false');
  });

  it('calls onTabChange when tab is clicked', () => {
    const handleTabChange = jest.fn();
    renderWithProvider(<TaskPaneNavigation selectedTab="save" onTabChange={handleTabChange} hostType="outlook" />);

    fireEvent.click(screen.getByRole('tab', { name: /create to do/i }));
    expect(handleTabChange).toHaveBeenCalledWith('createTodo');
  });

  it('disables tabs when disabled prop is true', () => {
    renderWithProvider(<TaskPaneNavigation selectedTab="save" onTabChange={() => {}} disabled={true} />);

    const tablist = screen.getByRole('tablist');
    expect(tablist).toHaveAttribute('aria-disabled', 'true');
  });

  it('renders smaller tabs in compact mode', () => {
    renderWithProvider(<TaskPaneNavigation selectedTab="save" onTabChange={() => {}} compact={true} />);

    // In compact mode, tab text should not be visible (icon only)
    // The tab should still exist but with just the icon
    expect(screen.getByRole('tab', { name: /save/i })).toBeInTheDocument();
  });

  it('returns correct default tab for each host type', () => {
    expect(getDefaultTab('outlook')).toBe('save');
    expect(getDefaultTab('word')).toBe('save');
  });
});
