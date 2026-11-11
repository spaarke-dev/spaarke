/**
 * closeNotUnmount.test.tsx: why a SprkModal must be closed with `open={false}`, never unmounted while open (#2658).
 *
 * Fluent's focus manager (tabster) hides everything outside an open modal Dialog with `aria-hidden`, on a ~250 ms timer
 * (`Modalizer.hiddenUpdate`). Closing the Dialog (`open={false}`) exposes the page again. Unmounting it while it is still
 * open does NOT: the page stays hidden from assistive technology until reload. `{cond && <XModal open />}`, closed by
 * clearing `cond`, is exactly that unmount. The ArchTest `ModalUnmountedWhileOpenGuardTests` bans the shape.
 *
 * If the second test starts failing, a tabster/Fluent upgrade fixed the leak: the guard can then be retired.
 */
import * as React from 'react';
import { render, screen, waitFor, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SprkModal } from '../SprkModal';

interface HostProps {
  mounted: boolean;
  open: boolean;
  modalKey?: string;
}

function Host({ mounted, open, modalKey }: HostProps): React.JSX.Element {
  return (
    <FluentProvider theme={webLightTheme}>
      <main aria-label="Page">
        <button type="button">Page action</button>
      </main>
      {mounted && (
        <SprkModal key={modalKey} open={open} onClose={() => undefined} title="Probe">
          <div>modal body</div>
        </SprkModal>
      )}
    </FluentProvider>
  );
}

const page = () => screen.queryByRole('main', { name: 'Page' });

/** Hold the modal open until tabster has hidden the page, as a person always does. */
async function waitForPageHidden(): Promise<void> {
  await waitFor(() => expect(page()).not.toBeInTheDocument());
}

describe('SprkModal: closing exposes the page again; unmounting while open does not (#2658)', () => {
  it('a modal closed with open={false} exposes the page again', async () => {
    const { rerender } = render(<Host mounted open />);
    await screen.findByRole('dialog');
    await waitForPageHidden();

    rerender(<Host mounted open={false} />);

    expect(await screen.findByRole('main', { name: 'Page' })).toBeInTheDocument();
  });

  it('a modal unmounted while open leaves the page hidden (the leak the guard prevents)', async () => {
    const { rerender } = render(<Host mounted open />);
    await screen.findByRole('dialog');
    await waitForPageHidden();

    rerender(<Host mounted={false} open />);
    // Well past tabster's ~250 ms update: nothing will expose the page any more.
    await act(() => new Promise(resolve => setTimeout(resolve, 800)));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(page()).not.toBeInTheDocument();
  });

  it('changing the key of an OPEN modal is an unmount while open: the page stays hidden even after the new one closes', async () => {
    // So a site must never swap an open modal for another by key. It keeps the key and changes props, or closes first.
    const { rerender } = render(<Host mounted open modalKey="a" />);
    await screen.findByRole('dialog');
    await waitForPageHidden();

    rerender(<Host mounted open modalKey="b" />);
    await screen.findByRole('dialog');
    await act(() => new Promise(resolve => setTimeout(resolve, 400)));
    rerender(<Host mounted open={false} modalKey="b" />);
    await act(() => new Promise(resolve => setTimeout(resolve, 800)));

    expect(page()).not.toBeInTheDocument();
  });

  it('a new key after the modal has closed is safe: open, close, reopen fresh, close exposes the page', async () => {
    // The pattern every fixed site uses: the key changes only on an opening, and only while the modal is closed.
    const { rerender } = render(<Host mounted open modalKey="1" />);
    await screen.findByRole('dialog');
    await waitForPageHidden();
    rerender(<Host mounted open={false} modalKey="1" />);
    await screen.findByRole('main', { name: 'Page' });

    rerender(<Host mounted open modalKey="2" />);
    await screen.findByRole('dialog');
    await waitForPageHidden();
    rerender(<Host mounted open={false} modalKey="2" />);

    expect(await screen.findByRole('main', { name: 'Page' })).toBeInTheDocument();
  });
});
