/**
 * ModalWindowControls.test.tsx — the shared standard modal window controls
 * (owner UAT 2026-07-31 item 4). Verifies each affordance renders only when its
 * handler is wired, that the maximize icon/label flips on `isMaximized`, and that
 * clicks call back.
 */
import * as React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { ModalWindowControls } from '../ModalWindowControls';

describe('ModalWindowControls (item 4 — standard modal chrome)', () => {
  it('renders both maximize and close when both handlers are wired, and calls back on click', () => {
    const onToggleMaximize = jest.fn();
    const onClose = jest.fn();
    renderWithProviders(<ModalWindowControls onToggleMaximize={onToggleMaximize} onClose={onClose} />);

    const maximize = screen.getByRole('button', { name: /maximize dialog/i });
    const close = screen.getByRole('button', { name: /^close$/i });
    expect(maximize).toBeInTheDocument();
    expect(close).toBeInTheDocument();

    fireEvent.click(maximize);
    fireEvent.click(close);
    expect(onToggleMaximize).toHaveBeenCalledTimes(1);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('shows the restore label when maximized', () => {
    renderWithProviders(<ModalWindowControls isMaximized onToggleMaximize={jest.fn()} onClose={jest.fn()} />);
    expect(screen.getByRole('button', { name: /restore dialog size/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /maximize dialog/i })).toBeNull();
  });

  it('renders only the close button when maximize is not wired', () => {
    renderWithProviders(<ModalWindowControls onClose={jest.fn()} />);
    expect(screen.queryByRole('button', { name: /maximize dialog/i })).toBeNull();
    expect(screen.getByRole('button', { name: /^close$/i })).toBeInTheDocument();
  });

  it('renders nothing when neither handler is wired', () => {
    const { container } = renderWithProviders(<ModalWindowControls />);
    expect(container.querySelector('button')).toBeNull();
  });
});
