/**
 * OpenFullFormButton.test.tsx (email-communication-solution-r5 task 036, FR-15)
 */
import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { OpenFullFormButton } from '../OpenFullFormButton';

function renderWithProvider(ui: React.ReactElement, theme = webLightTheme) {
  return render(<FluentProvider theme={theme}>{ui}</FluentProvider>);
}

describe('OpenFullFormButton (FR-15)', () => {
  it('invokes onOpenFullForm with the communication id when clicked', () => {
    const onOpenFullForm = jest.fn();
    renderWithProvider(<OpenFullFormButton communicationId="comm-1" onOpenFullForm={onOpenFullForm} />);

    fireEvent.click(screen.getByRole('button', { name: 'Open full form' }));

    expect(onOpenFullForm).toHaveBeenCalledWith('comm-1');
  });

  it('respects disabled', () => {
    const onOpenFullForm = jest.fn();
    renderWithProvider(<OpenFullFormButton communicationId="comm-1" onOpenFullForm={onOpenFullForm} disabled />);

    expect(screen.getByRole('button', { name: 'Open full form' })).toBeDisabled();
  });

  it('renders correctly under a dark FluentProvider theme (ADR-021) with no console errors', () => {
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    renderWithProvider(<OpenFullFormButton communicationId="comm-1" onOpenFullForm={jest.fn()} />, webDarkTheme);

    expect(screen.getByRole('button', { name: 'Open full form' })).toBeInTheDocument();
    expect(errorSpy).not.toHaveBeenCalled();
    errorSpy.mockRestore();
  });
});
