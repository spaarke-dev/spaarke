/**
 * BrowseModal.test.tsx — `PreviewModal` + `SprkModal`'s `nav` prop (spec
 * FR-09; design §6.4). Verifies the "N of M" counter + prev/next bounds via
 * the shell's OWN nav group (the single title/counter source — no forked nav
 * chrome), and the `onBeforeNavigate` guard seam a consumer uses to wire a
 * cross-frame dirty-check / discard-confirm (e.g. `RecordNavigationModalShell`)
 * WITHOUT nesting that shell's header inside `SprkModal`.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { renderWithProviders } from '../../../../__mocks__/pcfMocks';
import { BrowseModal } from '../BrowseModal';

const noop = () => {};

describe('BrowseModal (PreviewModal + SprkModal nav — FR-09)', () => {
  it('shows the "N of M" counter via the shell nav group; prev disabled at index 0', () => {
    const onNavigate = jest.fn();
    renderWithProviders(
      <BrowseModal open onClose={noop} title="Rec" nav={{ index: 0, total: 3, onNavigate }} />,
    );
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText('1 of 3')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: /previous record/i })).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: /next record/i })).not.toBeDisabled();
  });

  it('disables next at the last index', () => {
    const onNavigate = jest.fn();
    renderWithProviders(
      <BrowseModal open onClose={noop} title="Rec" nav={{ index: 2, total: 3, onNavigate }} />,
    );
    expect(screen.getByText('3 of 3')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /next record/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /previous record/i })).not.toBeDisabled();
  });

  it('clicking next fires onNavigate("next") when no guard is supplied', async () => {
    const onNavigate = jest.fn();
    renderWithProviders(
      <BrowseModal open onClose={noop} title="Rec" nav={{ index: 0, total: 3, onNavigate }} />,
    );
    fireEvent.click(screen.getByRole('button', { name: /next record/i }));
    await waitFor(() => expect(onNavigate).toHaveBeenCalledWith('next'));
  });

  it('clicking prev fires onNavigate("prev") when no guard is supplied', async () => {
    const onNavigate = jest.fn();
    renderWithProviders(
      <BrowseModal open onClose={noop} title="Rec" nav={{ index: 1, total: 3, onNavigate }} />,
    );
    fireEvent.click(screen.getByRole('button', { name: /previous record/i }));
    await waitFor(() => expect(onNavigate).toHaveBeenCalledWith('prev'));
  });

  it('onBeforeNavigate returning false BLOCKS the onNavigate call (guard works)', async () => {
    const onNavigate = jest.fn();
    const onBeforeNavigate = jest.fn().mockReturnValue(false);
    renderWithProviders(
      <BrowseModal
        open
        onClose={noop}
        title="Rec"
        nav={{ index: 0, total: 3, onNavigate }}
        onBeforeNavigate={onBeforeNavigate}
      />,
    );
    fireEvent.click(screen.getByRole('button', { name: /next record/i }));
    await waitFor(() => expect(onBeforeNavigate).toHaveBeenCalledWith('next'));
    expect(onNavigate).not.toHaveBeenCalled();
  });

  it('onBeforeNavigate resolving true (async) ALLOWS the onNavigate call', async () => {
    const onNavigate = jest.fn();
    const onBeforeNavigate = jest.fn().mockResolvedValue(true);
    renderWithProviders(
      <BrowseModal
        open
        onClose={noop}
        title="Rec"
        nav={{ index: 0, total: 3, onNavigate }}
        onBeforeNavigate={onBeforeNavigate}
      />,
    );
    fireEvent.click(screen.getByRole('button', { name: /next record/i }));
    await waitFor(() => expect(onNavigate).toHaveBeenCalledWith('next'));
  });

  it('onBeforeNavigate returning true (sync) ALLOWS the onNavigate call', async () => {
    const onNavigate = jest.fn();
    const onBeforeNavigate = jest.fn().mockReturnValue(true);
    renderWithProviders(
      <BrowseModal
        open
        onClose={noop}
        title="Rec"
        nav={{ index: 1, total: 3, onNavigate }}
        onBeforeNavigate={onBeforeNavigate}
      />,
    );
    fireEvent.click(screen.getByRole('button', { name: /previous record/i }));
    await waitFor(() => expect(onNavigate).toHaveBeenCalledWith('prev'));
  });

  it('does not render a forked nav chrome — exactly one "N of M" counter (single header source, design §6.4)', () => {
    const onNavigate = jest.fn();
    renderWithProviders(
      <BrowseModal open onClose={noop} title="Rec" nav={{ index: 0, total: 3, onNavigate }} />,
    );
    expect(screen.getAllByText('1 of 3')).toHaveLength(1);
    expect(screen.getAllByRole('group', { name: /record navigation/i })).toHaveLength(1);
    // RecordNavigationModalShell's own discard-confirmation dialog never
    // mounts — proof the shell is NOT nested inside SprkModal.
    expect(screen.queryByText('Discard unsaved changes?')).toBeNull();
  });

  it('renders the shared PreviewModal grid (stage+meta) beneath the nav header', () => {
    renderWithProviders(
      <BrowseModal
        open
        onClose={noop}
        title="Rec"
        nav={{ index: 0, total: 3, onNavigate: noop }}
        metadata={[{ label: 'Author', value: 'J. Doe' }]}
      >
        <span>browsed stage</span>
      </BrowseModal>,
    );
    expect(screen.getByTestId('preview-stage')).toHaveTextContent('browsed stage');
    expect(screen.getByText('Author')).toBeInTheDocument();
    expect(screen.getByText('J. Doe')).toBeInTheDocument();
  });

  it('clicking the footer Close invokes onClose', () => {
    const onClose = jest.fn();
    const onNavigate = jest.fn();
    renderWithProviders(
      <BrowseModal
        open
        onClose={onClose}
        title="Rec"
        nav={{ index: 0, total: 3, onNavigate }}
      />,
    );
    const footerClose = screen
      .getAllByRole('button', { name: /^close$/i })
      .find((b) => b.textContent === 'Close') as HTMLElement;
    fireEvent.click(footerClose);
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
