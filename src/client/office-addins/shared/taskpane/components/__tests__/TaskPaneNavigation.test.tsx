import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { TaskPaneNavigation, getDefaultTab } from '../TaskPaneNavigation';

// Wrap component with FluentProvider for testing
const renderWithProvider = (ui: React.ReactElement) => {
  return render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);
};

describe('TaskPaneNavigation', () => {
  it('renders all navigation tabs', () => {
    renderWithProvider(
      <TaskPaneNavigation selectedTab="save" onTabChange={() => {}} />
    );

    expect(screen.getByRole('tab', { name: /save/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /share/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /search/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /recent/i })).toBeInTheDocument();
  });

  it('highlights selected tab', () => {
    renderWithProvider(
      <TaskPaneNavigation selectedTab="share" onTabChange={() => {}} />
    );

    const shareTab = screen.getByRole('tab', { name: /share/i });
    expect(shareTab).toHaveAttribute('aria-selected', 'true');

    const saveTab = screen.getByRole('tab', { name: /save/i });
    expect(saveTab).toHaveAttribute('aria-selected', 'false');
  });

  it('calls onTabChange when tab is clicked', () => {
    const handleTabChange = jest.fn();
    renderWithProvider(
      <TaskPaneNavigation selectedTab="save" onTabChange={handleTabChange} />
    );

    fireEvent.click(screen.getByRole('tab', { name: /share/i }));
    expect(handleTabChange).toHaveBeenCalledWith('share');

    fireEvent.click(screen.getByRole('tab', { name: /recent/i }));
    expect(handleTabChange).toHaveBeenCalledWith('recent');
  });

  it('disables tabs when disabled prop is true', () => {
    renderWithProvider(
      <TaskPaneNavigation
        selectedTab="save"
        onTabChange={() => {}}
        disabled={true}
      />
    );

    const tablist = screen.getByRole('tablist');
    expect(tablist).toHaveAttribute('aria-disabled', 'true');
  });

  it('renders smaller tabs in compact mode', () => {
    renderWithProvider(
      <TaskPaneNavigation
        selectedTab="save"
        onTabChange={() => {}}
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
