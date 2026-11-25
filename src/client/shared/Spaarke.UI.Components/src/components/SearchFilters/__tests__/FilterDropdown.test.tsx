/**
 * FilterDropdown — shared by the SemanticSearch code page and PCF (#3093).
 *
 * The behaviour the former copies disagreed on: the PCF copy treated "no options" as "loading", so an
 * option set that loaded empty showed a spinner forever. Loading is now the host's explicit
 * `isLoading` (the code page's rule).
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { FilterDropdown, formatFilterSelection } from '../FilterDropdown';
import type { FilterDropdownProps } from '../FilterDropdown';

const options = [
  { value: '1', label: 'Contract', count: 4 },
  { value: '2', label: 'Invoice' },
  { value: '3', label: 'Email' },
];

const renderDropdown = (props: Partial<FilterDropdownProps> = {}) => {
  const onChange = jest.fn();
  render(
    <FluentProvider theme={webLightTheme}>
      <FilterDropdown label="Document Type" options={options} selectedValues={[]} onChange={onChange} {...props} />
    </FluentProvider>
  );
  return onChange;
};

describe('FilterDropdown', () => {
  it('shows a spinner while loading with no options yet', () => {
    renderDropdown({ options: [], isLoading: true });
    expect(screen.getByText('Loading...')).toBeInTheDocument();
    expect(screen.queryByRole('combobox')).toBeNull();
  });

  it('shows an (empty) dropdown, not a spinner, when the options loaded empty', () => {
    renderDropdown({ options: [] });
    expect(screen.queryByText('Loading...')).toBeNull();
    expect(screen.getByRole('combobox')).toBeInTheDocument();
  });

  it('emits the new selection, with result counts in the option text', () => {
    const onChange = renderDropdown();
    fireEvent.click(screen.getByRole('combobox'));
    // A multi-select Fluent Option is a menuitemcheckbox.
    fireEvent.click(screen.getByRole('menuitemcheckbox', { name: 'Contract (4)' }));
    expect(onChange).toHaveBeenCalledWith(['1']);
  });

  it('summarises the selection: All, the label, or "{N} selected"', () => {
    expect(formatFilterSelection([], options)).toBe('All');
    expect(formatFilterSelection(['2'], options)).toBe('Invoice');
    expect(formatFilterSelection(['9'], options)).toBe('9');
    expect(formatFilterSelection(['1', '3'], options)).toBe('2 selected');
  });

  it('disables the dropdown when disabled', () => {
    renderDropdown({ disabled: true });
    expect(screen.getByRole('combobox')).toBeDisabled();
  });
});
