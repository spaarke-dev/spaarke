/**
 * SprkModal.test.tsx — the canonical modal shell (spec FR-01/03/04/05/07/08).
 * Verifies the shell renders ALL chrome from content+intent, the dismiss modes,
 * maximize→full, browse nav, a11y (aria-modal), and the transform-robust portal.
 */
import * as React from 'react';
import { render, fireEvent, screen, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme } from '@fluentui/react-components';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { SprkModal } from '../SprkModal';

const noop = () => {};

describe('SprkModal (base shell — FR-01/03/04/05/07/08)', () => {
  it('renders full chrome from content + intent (title, body, footer slots, window controls)', () => {
    renderWithProviders(
      <SprkModal
        open
        onClose={noop}
        title="Matter Details"
        size="md"
        footerStart={<button>Cancel</button>}
        footer={<button>Save</button>}
      >
        <div>Body content here</div>
      </SprkModal>,
    );
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText('Matter Details')).toBeInTheDocument();
    expect(within(dialog).getByText('Body content here')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: /^cancel$/i })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: /^save$/i })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: /maximize dialog/i })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: /^close$/i })).toBeInTheDocument();
    expect(dialog).toHaveAttribute('aria-modal', 'true');
  });

  it('renders nothing when closed', () => {
    renderWithProviders(
      <SprkModal open={false} onClose={noop} title="Hidden">
        <div>secret</div>
      </SprkModal>,
    );
    expect(screen.queryByText('secret')).toBeNull();
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('maximize toggles the surface to full and back (label flips)', () => {
    renderWithProviders(
      <SprkModal open onClose={noop} title="Sizeable">
        <div>x</div>
      </SprkModal>,
    );
    fireEvent.click(screen.getByRole('button', { name: /maximize dialog/i }));
    expect(screen.getByRole('button', { name: /restore dialog size/i })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /restore dialog size/i }));
    expect(screen.getByRole('button', { name: /maximize dialog/i })).toBeInTheDocument();
  });

  it('omits the maximize control when maximizable=false', () => {
    renderWithProviders(
      <SprkModal open onClose={noop} title="No max" maximizable={false}>
        <div>x</div>
      </SprkModal>,
    );
    expect(screen.queryByRole('button', { name: /maximize dialog/i })).toBeNull();
    expect(screen.getByRole('button', { name: /^close$/i })).toBeInTheDocument();
  });

  it('renders no footer when neither footer nor footerStart is supplied', () => {
    renderWithProviders(
      <SprkModal open onClose={noop} title="No footer">
        <div>only body</div>
      </SprkModal>,
    );
    expect(screen.queryByRole('button', { name: /^save$/i })).toBeNull();
    expect(screen.getByText('only body')).toBeInTheDocument();
  });

  it('close button invokes onClose', () => {
    const onClose = jest.fn();
    renderWithProviders(
      <SprkModal open onClose={onClose} title="Closable">
        <div>x</div>
      </SprkModal>,
    );
    fireEvent.click(screen.getByRole('button', { name: /^close$/i }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('renders the browse nav counter and disables prev/next at bounds', () => {
    const onNavigate = jest.fn();
    renderWithProviders(
      <SprkModal open onClose={noop} title="Rec" nav={{ index: 0, total: 3, onNavigate }}>
        <div>x</div>
      </SprkModal>,
    );
    expect(screen.getByText('1 of 3')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /previous record/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /next record/i })).not.toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: /next record/i }));
    expect(onNavigate).toHaveBeenCalledWith('next');
  });

  it("dismiss='alert' uses the alert role (no light dismiss); 'light' uses the dialog role", () => {
    const { unmount } = renderWithProviders(
      <SprkModal open onClose={noop} title="Alert" dismiss="alert">
        <div>x</div>
      </SprkModal>,
    );
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    unmount();
    renderWithProviders(
      <SprkModal open onClose={noop} title="Light" dismiss="light">
        <div>x</div>
      </SprkModal>,
    );
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('portal escapes a CSS-transformed ancestor (transform-robust centering)', () => {
    const { container } = renderWithProviders(
      <div style={{ transform: 'scale(0.9)' }} data-testid="xf">
        <SprkModal open onClose={noop} title="Centered">
          <div>x</div>
        </SprkModal>
      </div>,
    );
    const transformed = container.querySelector('[data-testid="xf"]') as HTMLElement;
    const dialog = screen.getByRole('dialog');
    // The Fluent portal mounts the surface OUTSIDE the transformed subtree, so the
    // transform cannot offset it — the invariant the whole project rests on.
    expect(transformed).not.toContainElement(dialog);
  });

  it('renders under a dark theme (light/dark parity smoke)', () => {
    render(
      <FluentProvider theme={webDarkTheme}>
        <SprkModal open onClose={noop} title="Dark">
          <div>dark body</div>
        </SprkModal>
      </FluentProvider>,
    );
    expect(screen.getByText('dark body')).toBeInTheDocument();
  });
});
