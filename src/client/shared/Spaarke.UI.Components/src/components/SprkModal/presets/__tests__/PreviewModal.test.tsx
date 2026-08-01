/**
 * PreviewModal.test.tsx — thin `SprkModal` preset for rich document/file
 * preview (spec FR-09). Verifies fixed lg/landscape/unpadded sizing, the
 * `1fr 320px` stage+meta grid, metadata rendering, the placeholder stage
 * fallback, and a single primary Close in the footer.
 */
import * as React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme } from '@fluentui/react-components';
import { renderWithProviders } from '../../../../__mocks__/pcfMocks';
import { PreviewModal } from '../PreviewModal';

const noop = () => {};

describe('PreviewModal (thin SprkModal preset — FR-09)', () => {
  it('renders lg/landscape/unpadded with a 1fr 320px stage+meta grid', () => {
    renderWithProviders(
      <PreviewModal
        open
        onClose={noop}
        title="Contract.pdf"
        metadata={[{ label: 'Size', value: '2.1 MB' }]}
      >
        <span>stage content</span>
      </PreviewModal>,
    );

    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText('Contract.pdf')).toBeInTheDocument();

    // size="lg" at scale 1 (sizes.ts: cap 1280 / 94vw width, 85vh capped at 880px height).
    expect(dialog).toHaveStyle({ width: 'min(1280px, 94vw)', height: 'min(85vh, 880px)' });

    // layout="landscape" is forwarded to the body wrapper's data-layout attribute.
    const grid = within(dialog).getByTestId('preview-grid');
    const bodyDiv = grid.parentElement as HTMLElement;
    expect(bodyDiv).toHaveAttribute('data-layout', 'landscape');

    // padded=false — the body wrapper carries no padding declaration.
    expect(window.getComputedStyle(bodyDiv).padding).toBe('');

    // 1fr 320px stage+meta grid.
    const cs = window.getComputedStyle(grid);
    expect(cs.display).toBe('grid');
    expect(cs.gridTemplateColumns).toBe('1fr 320px');

    // Stage renders caller content; meta renders the metadata row.
    expect(within(dialog).getByTestId('preview-stage')).toHaveTextContent('stage content');
    expect(within(dialog).getByText('Size')).toBeInTheDocument();
    expect(within(dialog).getByText('2.1 MB')).toBeInTheDocument();
  });

  it('renders a single primary Close in the footer (distinct from the window-controls ×)', () => {
    renderWithProviders(<PreviewModal open onClose={noop} title="Doc" />);
    const dialog = screen.getByRole('dialog');
    // Both the icon-only × (aria-label "Close") and the footer's text "Close"
    // button match name /^close$/i — disambiguate by textContent: the ×
    // button is icon-only (no text node), the footer button renders "Close".
    const closeButtons = within(dialog).getAllByRole('button', { name: /^close$/i });
    expect(closeButtons).toHaveLength(2);
    const footerClose = closeButtons.filter((b) => b.textContent === 'Close');
    expect(footerClose).toHaveLength(1);
  });

  it('falls back to a placeholder stage when no children are supplied', () => {
    renderWithProviders(<PreviewModal open onClose={noop} title="Empty" />);
    expect(screen.getByTestId('preview-stage')).toHaveTextContent('Preview unavailable');
  });

  it('clicking the footer Close invokes onClose', () => {
    const onClose = jest.fn();
    renderWithProviders(<PreviewModal open onClose={onClose} title="Doc" />);
    const footerClose = screen
      .getAllByRole('button', { name: /^close$/i })
      .find((b) => b.textContent === 'Close') as HTMLElement;
    fireEvent.click(footerClose);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('renders under a dark theme without hardcoded color (ADR-021 parity smoke)', () => {
    render(
      <FluentProvider theme={webDarkTheme}>
        <PreviewModal
          open
          onClose={noop}
          title="Dark"
          metadata={[{ label: 'Type', value: 'PDF' }]}
        />
      </FluentProvider>,
    );
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByText('Type')).toBeInTheDocument();
    expect(screen.getByText('PDF')).toBeInTheDocument();
  });
});
