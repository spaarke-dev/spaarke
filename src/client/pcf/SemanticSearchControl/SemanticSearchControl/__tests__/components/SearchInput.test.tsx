/**
 * Unit tests for SearchInput component
 *
 * @see SearchInput.tsx for implementation
 */
import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SearchInput } from '../../components/SearchInput';

// Wrapper for Fluent Provider
const renderWithProvider = (ui: React.ReactElement) => {
  return render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);
};

describe('SearchInput', () => {
  const defaultProps = {
    value: '',
    placeholder: 'Search documents...',
    disabled: false,
    onValueChange: jest.fn(),
    onSearch: jest.fn(),
    onAddDocument: jest.fn(),
  };

  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('should render input with placeholder', () => {
    renderWithProvider(<SearchInput {...defaultProps} />);

    expect(screen.getByPlaceholderText('Search documents...')).toBeInTheDocument();
  });

  it('should render search button', () => {
    renderWithProvider(<SearchInput {...defaultProps} />);

    // The component now also renders an info-popover button ("Search info"), so the
    // search button must be addressed by its accessible name, not by bare role.
    expect(screen.getByRole('button', { name: 'Search' })).toBeInTheDocument();
  });

  it('should render the info popover trigger separately from the search button', () => {
    renderWithProvider(<SearchInput {...defaultProps} />);

    expect(screen.getAllByRole('button')).toHaveLength(2);
    expect(screen.getByRole('button', { name: 'Search info' })).toBeInTheDocument();
  });

  it('should display value in input', () => {
    renderWithProvider(<SearchInput {...defaultProps} value="test query" />);

    expect(screen.getByDisplayValue('test query')).toBeInTheDocument();
  });

  it('should call onValueChange when input changes', () => {
    const onValueChange = jest.fn();
    renderWithProvider(<SearchInput {...defaultProps} onValueChange={onValueChange} />);

    fireEvent.change(screen.getByRole('textbox'), {
      target: { value: 'new query' },
    });

    expect(onValueChange).toHaveBeenCalledWith('new query');
  });

  it('should call onSearch when search button clicked', () => {
    const onSearch = jest.fn();
    renderWithProvider(<SearchInput {...defaultProps} value="test" onSearch={onSearch} />);

    fireEvent.click(screen.getByRole('button', { name: 'Search' }));

    expect(onSearch).toHaveBeenCalledTimes(1);
  });

  it('should call onSearch when Enter key pressed', () => {
    const onSearch = jest.fn();
    renderWithProvider(<SearchInput {...defaultProps} value="test" onSearch={onSearch} />);

    fireEvent.keyDown(screen.getByRole('textbox'), { key: 'Enter' });

    expect(onSearch).toHaveBeenCalledTimes(1);
  });

  it('should not call onSearch for other keys', () => {
    const onSearch = jest.fn();
    renderWithProvider(<SearchInput {...defaultProps} value="test" onSearch={onSearch} />);

    fireEvent.keyDown(screen.getByRole('textbox'), { key: 'Tab' });
    fireEvent.keyDown(screen.getByRole('textbox'), { key: 'a' });

    expect(onSearch).not.toHaveBeenCalled();
  });

  it('should disable input and button when disabled', () => {
    renderWithProvider(<SearchInput {...defaultProps} disabled={true} />);

    expect(screen.getByRole('textbox')).toBeDisabled();
    // While disabled the search button is relabelled "Searching..." and is disabled;
    // the info trigger stays usable.
    expect(screen.getByRole('button', { name: 'Searching...' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Search info' })).toBeEnabled();
  });

  it('should not call onSearch when disabled and Enter pressed', () => {
    const onSearch = jest.fn();
    renderWithProvider(<SearchInput {...defaultProps} value="test" disabled={true} onSearch={onSearch} />);

    fireEvent.keyDown(screen.getByRole('textbox'), { key: 'Enter' });

    expect(onSearch).not.toHaveBeenCalled();
  });

  it('should have a search icon inside the input (not on the button)', () => {
    renderWithProvider(<SearchInput {...defaultProps} />);

    // Search20Regular is the Input's `contentBefore`; the Search button is text-only
    // while idle (a spinner replaces it only when disabled).
    const input = screen.getByRole('textbox');
    expect(input.parentElement?.querySelector('svg')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Search' }).querySelector('svg')).not.toBeInTheDocument();
  });
});
