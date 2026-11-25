/**
 * WizardShell.finishing.test.tsx — while `onFinish` runs the header cannot move or close the wizard
 * (ebilling-r1 ISS-016, #2755). The shell used to disable only its footer buttons, so a user could step
 * to another record (the save's result then showed under the new title) or close the wizard (an error
 * from a multi-call save was never seen).
 *
 * While finishing: ‹ › are disabled and do nothing; ×, Escape (dismiss 'light'), backdrop and Cancel call
 * no `onClose`. When the save ends (resolve or reject) they all work again. Not finishing: unchanged.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { WizardShell } from '../WizardShell';
import type { IWizardShellProps, IWizardStepConfig } from '../wizardShellTypes';

const oneStep: IWizardStepConfig[] = [
  { id: 'a', label: 'Step a', renderContent: () => <div>content-a</div>, canAdvance: () => true },
];

interface Deferred {
  promise: Promise<undefined>;
  resolve: () => void;
  reject: (e: Error) => void;
}
function deferred(): Deferred {
  let resolve!: () => void;
  let reject!: (e: Error) => void;
  const promise = new Promise<undefined>((res, rej) => {
    resolve = () => res(undefined);
    reject = rej;
  });
  return { promise, resolve, reject };
}

function setup(overrides: Partial<IWizardShellProps> = {}) {
  const onClose = jest.fn();
  const onNavigate = jest.fn();
  const onBeforeNavigate = jest.fn(() => true);
  const d = deferred();
  const onFinish = jest.fn(() => d.promise);
  const utils = renderWithProviders(
    <WizardShell
      open
      title="Decide"
      steps={oneStep}
      onClose={onClose}
      onFinish={onFinish}
      stayOpenOnFinish
      dismiss="light"
      nav={{ index: 1, total: 5, onNavigate, onBeforeNavigate }}
      {...overrides}
    />
  );
  return { ...utils, onClose, onNavigate, onBeforeNavigate, onFinish, d };
}

const surface = () => screen.getByRole('dialog');
const byName = (name: string) => screen.getByRole('button', { name });
const startFinish = async () => {
  fireEvent.click(byName('Finish'));
  await waitFor(() => expect(byName('Previous record')).toBeDisabled());
};

describe('WizardShell — header is held while onFinish runs', () => {
  it('disables ‹ › and ignores clicks on them', async () => {
    const { onNavigate, onBeforeNavigate } = setup();
    expect(byName('Previous record')).toBeEnabled();
    await startFinish();
    expect(byName('Next record')).toBeDisabled();
    fireEvent.click(byName('Previous record'));
    fireEvent.click(byName('Next record'));
    await act(async () => {});
    expect(onBeforeNavigate).not.toHaveBeenCalled();
    expect(onNavigate).not.toHaveBeenCalled();
  });

  it('× , Escape and Cancel call no onClose', async () => {
    const { onClose } = setup();
    await startFinish();
    fireEvent.click(byName('Close'));
    fireEvent.keyDown(surface(), { key: 'Escape', code: 'Escape' });
    fireEvent.click(byName('Cancel'));
    await act(async () => {});
    expect(onClose).not.toHaveBeenCalled();
  });

  it('backdrop click calls no onClose', async () => {
    const { onClose } = setup();
    await startFinish();
    const backdrop = document.querySelector('.fui-DialogSurface__backdrop') as HTMLElement | null;
    if (backdrop) fireEvent.click(backdrop);
    await act(async () => {});
    expect(onClose).not.toHaveBeenCalled();
  });

  it('nav and close work again after onFinish resolves', async () => {
    const { onClose, onNavigate, onBeforeNavigate, d } = setup();
    await startFinish();
    await act(async () => d.resolve());
    await waitFor(() => expect(byName('Next record')).toBeEnabled());
    fireEvent.click(byName('Next record'));
    await waitFor(() => expect(onNavigate).toHaveBeenCalledWith('next'));
    expect(onBeforeNavigate).toHaveBeenCalledWith('next');
    fireEvent.click(byName('Close'));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('nav and close work again after onFinish rejects, and the error is shown', async () => {
    const { onClose, onNavigate, d } = setup();
    await startFinish();
    await act(async () => d.reject(new Error('second call refused')));
    expect(await screen.findByText(/second call refused/)).toBeInTheDocument();
    expect(byName('Previous record')).toBeEnabled();
    fireEvent.click(byName('Previous record'));
    await waitFor(() => expect(onNavigate).toHaveBeenCalledWith('prev'));
    fireEvent.keyDown(surface(), { key: 'Escape', code: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(1);
    fireEvent.click(byName('Cancel'));
    expect(onClose).toHaveBeenCalledTimes(2);
  });

  it("a successful finish still closes the wizard itself (the shell's own onClose call is not blocked)", async () => {
    const { onClose, d } = setup({ stayOpenOnFinish: false });
    await startFinish();
    await act(async () => d.resolve());
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
  });

  it('keeps a consumer-supplied nav.disabled after the save ends', async () => {
    const { d } = setup({ nav: { index: 1, total: 5, onNavigate: jest.fn(), disabled: true } });
    expect(byName('Next record')).toBeDisabled();
    fireEvent.click(byName('Finish'));
    await act(async () => d.resolve());
    await waitFor(() => expect(within(surface()).getByRole('button', { name: 'Finish' })).toBeEnabled());
    expect(byName('Next record')).toBeDisabled();
  });
});

describe('WizardShell — not finishing: header unchanged (must-not-fire control)', () => {
  it('‹ › navigate, and × / Escape / Cancel close', async () => {
    const { onClose, onNavigate, onBeforeNavigate } = setup();
    fireEvent.click(byName('Next record'));
    await waitFor(() => expect(onNavigate).toHaveBeenCalledWith('next'));
    expect(onBeforeNavigate).toHaveBeenCalledTimes(1);
    fireEvent.click(byName('Close'));
    fireEvent.keyDown(surface(), { key: 'Escape', code: 'Escape' });
    fireEvent.click(byName('Cancel'));
    expect(onClose).toHaveBeenCalledTimes(3);
  });
});

describe('WizardShell — reset, re-entry, errors and unmount while finishing', () => {
  const finishProps = (
    onFinish: IWizardShellProps['onFinish'],
    onClose: jest.Mock,
    extra: Partial<IWizardShellProps> = {}
  ) =>
    ({
      open: true,
      title: 'Decide',
      steps: oneStep,
      onClose,
      onFinish,
      stayOpenOnFinish: true,
      dismiss: 'light',
      ...extra,
    }) as IWizardShellProps;

  it('a reset (resetKey) mid-save releases the header; the stale run changes nothing, and a newer run is not released by it', async () => {
    const onClose = jest.fn();
    const onNavigate = jest.fn();
    const nav = { index: 1, total: 5, onNavigate };
    const first = deferred();
    const second = deferred();
    const onFinish = jest.fn().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);
    const { rerender } = renderWithProviders(
      <WizardShell {...finishProps(onFinish, onClose, { nav, resetKey: 'a' })} />
    );
    await startFinish();

    // Moved to another item while the save runs (the host re-keys the shell).
    rerender(<WizardShell {...finishProps(onFinish, onClose, { nav, resetKey: 'b' })} />);
    await waitFor(() => expect(byName('Next record')).toBeEnabled());
    fireEvent.click(byName('Next record'));
    await waitFor(() => expect(onNavigate).toHaveBeenCalledWith('next'));
    fireEvent.click(byName('Close'));
    expect(onClose).toHaveBeenCalledTimes(1);

    // The new item starts its own save; the OLD save then fails: nothing shows, the new save stays held.
    fireEvent.click(byName('Finish'));
    await waitFor(() => expect(byName('Previous record')).toBeDisabled());
    expect(onFinish).toHaveBeenCalledTimes(2);
    await act(async () => first.reject(new Error('stale failure')));
    expect(screen.queryByText(/stale failure/)).toBeNull();
    expect(byName('Previous record')).toBeDisabled();
    fireEvent.click(byName('Close'));
    expect(onClose).toHaveBeenCalledTimes(1);

    // The new save ends: the header is released.
    await act(async () => second.resolve());
    await waitFor(() => expect(byName('Previous record')).toBeEnabled());
  });

  it('a stale successful run shows no success screen and does not close the wizard on the new item', async () => {
    const onClose = jest.fn();
    const first = deferred();
    const onFinish = jest.fn(() => first.promise);
    const { rerender } = renderWithProviders(
      <WizardShell {...finishProps(onFinish, onClose, { stayOpenOnFinish: false, resetKey: 'a' })} />
    );
    fireEvent.click(byName('Finish'));
    await waitFor(() => expect(byName('Processing…')).toBeDisabled());
    rerender(<WizardShell {...finishProps(onFinish, onClose, { stayOpenOnFinish: false, resetKey: 'b' })} />);
    await act(async () => first.resolve());
    expect(onClose).not.toHaveBeenCalled();
    expect(byName('Finish')).toBeEnabled();
  });

  it('a second Finish while one save runs starts no second onFinish', async () => {
    const onClose = jest.fn();
    const d = deferred();
    const onFinish = jest.fn(() => d.promise);
    renderWithProviders(<WizardShell {...finishProps(onFinish, onClose)} />);
    const finish = byName('Finish');
    fireEvent.click(finish);
    fireEvent.click(finish);
    await act(async () => {});
    expect(onFinish).toHaveBeenCalledTimes(1);
    await act(async () => d.resolve());
  });

  it('a synchronous throw from onFinish shows the error and releases the guard', async () => {
    const onClose = jest.fn();
    const nav = { index: 1, total: 5, onNavigate: jest.fn() };
    const onFinish = jest.fn(() => {
      throw new Error('threw at once');
    });
    renderWithProviders(<WizardShell {...finishProps(onFinish, onClose, { nav })} />);
    fireEvent.click(byName('Finish'));
    expect(await screen.findByText(/threw at once/)).toBeInTheDocument();
    expect(byName('Next record')).toBeEnabled();
    fireEvent.click(byName('Close'));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('a footer override context.close is held while finishing and works after', async () => {
    const onClose = jest.fn();
    const d = deferred();
    const onFinish = jest.fn(() => d.promise);
    const footer: IWizardShellProps['footer'] = ctx => ({
      start: <button onClick={ctx.close}>Ctx close</button>,
    });
    renderWithProviders(<WizardShell {...finishProps(onFinish, onClose, { footer })} />);
    fireEvent.click(byName('Finish'));
    await waitFor(() => expect(byName('Processing…')).toBeDisabled());
    fireEvent.click(byName('Ctx close'));
    expect(onClose).not.toHaveBeenCalled();
    await act(async () => d.resolve());
    await waitFor(() => expect(byName('Finish')).toBeEnabled());
    fireEvent.click(byName('Ctx close'));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('unmounting mid-save leaves no warning, closes nothing and shows nothing', async () => {
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    try {
      const onClose = jest.fn();
      const d = deferred();
      const onFinish = jest.fn(() => d.promise);
      const { unmount } = renderWithProviders(
        <WizardShell {...finishProps(onFinish, onClose, { stayOpenOnFinish: false })} />
      );
      fireEvent.click(byName('Finish'));
      await waitFor(() => expect(byName('Processing…')).toBeDisabled());
      unmount();
      await act(async () => d.resolve());
      expect(onClose).not.toHaveBeenCalled();
      expect(errorSpy).not.toHaveBeenCalled();
    } finally {
      errorSpy.mockRestore();
    }
  });

  it('footerLeftExtra is disabled while finishing, enabled before and after', async () => {
    const onClose = jest.fn();
    const d = deferred();
    const onFinish = jest.fn(() => d.promise);
    renderWithProviders(
      <WizardShell {...finishProps(onFinish, onClose, { footerLeftExtra: <button>Delete</button> })} />
    );
    expect(byName('Delete')).toBeEnabled();
    fireEvent.click(byName('Finish'));
    await waitFor(() => expect(byName('Delete')).toBeDisabled());
    await act(async () => d.resolve());
    await waitFor(() => expect(byName('Delete')).toBeEnabled());
  });
});

describe('WizardShell — embedded mode', () => {
  it('× is a no-op while finishing and works after', async () => {
    const { onClose, d } = setup({ embedded: true, nav: undefined });
    fireEvent.click(byName('Close'));
    expect(onClose).toHaveBeenCalledTimes(1);
    fireEvent.click(byName('Finish'));
    await waitFor(() => expect(byName('Processing…')).toBeDisabled());
    fireEvent.click(byName('Close'));
    expect(onClose).toHaveBeenCalledTimes(1);
    await act(async () => d.resolve());
    await waitFor(() => expect(byName('Finish')).toBeEnabled());
    fireEvent.click(byName('Close'));
    expect(onClose).toHaveBeenCalledTimes(2);
  });
});
