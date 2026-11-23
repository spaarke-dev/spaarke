/**
 * swapHandover.test.tsx - a SprkModal that REPLACES another (a "loading" modal swapped for the "ready" one) still returns
 * focus to the control that opened the first (task 202 follow-up; dev finding 2026-11-23: Ontology admin > Rules > New rule
 * > Cancel left focus on <body>).
 *
 * The second modal opens while focus sits on the first one's Close button, so that button is what it captures; by close
 * the first modal is gone. Each test asserts where focus ENDS, after the restore's timer and bounded retry have run.
 */
import * as React from 'react';
import { render, screen, waitFor, act, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SprkModal } from '../SprkModal';
import { ConfirmModal } from '../presets/ConfirmModal';

const Provider: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <FluentProvider theme={webLightTheme}>{children}</FluentProvider>
);

const settle = (ms = 400) => act(() => new Promise<void>(resolve => setTimeout(resolve, ms)));
const active = () => document.activeElement;

function openWithClick(button: HTMLElement): void {
  act(() => {
    button.focus();
    button.click();
  });
}

/** The "ready" modal is a different component type from the loading one, so React remounts it (as RuleWizardBody does). */
const ReadyModal: React.FC<{ onClose: () => void }> = ({ onClose }) => (
  <SprkModal
    open
    onClose={onClose}
    title="Ready"
    footerStart={
      <button type="button" onClick={onClose}>
        Ready Cancel
      </button>
    }
  >
    <p>ready body</p>
  </SprkModal>
);

const FinalModal: React.FC<{ onClose: () => void }> = ({ onClose }) => (
  <SprkModal open onClose={onClose} title="Final">
    <p>final body</p>
  </SprkModal>
);

/** Renders loading modal A until a promise resolves, then a different modal B in its place (then, optionally, a third). */
const Gate: React.FC<{ onClose: () => void; finalStage?: boolean }> = ({ onClose, finalStage = false }) => {
  const [stage, setStage] = React.useState(0); // 0 loading, 1 ready, 2 final
  React.useEffect(() => {
    let live = true;
    void Promise.resolve().then(() => {
      if (live) setStage(1);
    });
    if (finalStage) {
      void new Promise<void>(resolve => setTimeout(resolve, 30)).then(() => {
        if (live) setStage(2);
      });
    }
    return () => {
      live = false;
    };
  }, [finalStage]);
  if (stage === 0) {
    return (
      <SprkModal open onClose={onClose} title="Loading" dismiss="explicit">
        <p>loading</p>
      </SprkModal>
    );
  }
  return stage === 1 ? <ReadyModal onClose={onClose} /> : <FinalModal onClose={onClose} />;
};

const SwapHost: React.FC<{ finalStage?: boolean }> = ({ finalStage }) => {
  const [open, setOpen] = React.useState(false);
  return (
    <Provider>
      <button type="button" onClick={() => setOpen(true)}>
        New rule
      </button>
      <button type="button">Bystander</button>
      {open && <Gate onClose={() => setOpen(false)} finalStage={finalStage} />}
    </Provider>
  );
};

describe('SprkModal swap handover (task 202 follow-up)', () => {
  it('(a) a loading modal replaced by a different modal: closing the second returns focus to the opener of the first', async () => {
    render(<SwapHost />);
    const opener = screen.getByRole('button', { name: 'New rule' });
    openWithClick(opener);
    await screen.findByRole('dialog', { name: 'Ready' });
    await waitFor(() => expect(screen.getByRole('dialog', { name: 'Ready' }).contains(active())).toBe(true));

    act(() => screen.getByRole('button', { name: 'Ready Cancel' }).click());

    await waitFor(() => expect(active()).toBe(opener));
    await settle();
    expect(active()).toBe(opener);
  });

  it('(a2) a swap of a swap (loading -> ready -> final) still reaches the original opener', async () => {
    render(<SwapHost finalStage />);
    const opener = screen.getByRole('button', { name: 'New rule' });
    openWithClick(opener);
    await screen.findByRole('dialog', { name: 'Final' });
    await waitFor(() => expect(screen.getByRole('dialog', { name: 'Final' }).contains(active())).toBe(true));

    act(() => screen.getByRole('button', { name: /close/i }).click());

    await waitFor(() => expect(active()).toBe(opener));
  });

  it('(a3) closing the swapped-in modal through its header close (x) returns focus too', async () => {
    render(<SwapHost />);
    const opener = screen.getByRole('button', { name: 'New rule' });
    openWithClick(opener);
    await screen.findByRole('dialog', { name: 'Ready' });
    act(() => screen.getByRole('button', { name: /close/i }).click());
    await waitFor(() => expect(active()).toBe(opener));
  });

  it('a ConfirmModal over the swapped-in wizard: Keep editing still returns to the wizard button; Discard returns to the page opener', async () => {
    const Host: React.FC = () => {
      const [open, setOpen] = React.useState(false);
      const [asked, setAsked] = React.useState(false);
      return (
        <Provider>
          <button type="button" onClick={() => setOpen(true)}>
            New rule
          </button>
          {open && <Gate onClose={() => setAsked(true)} />}
          <ConfirmModal
            open={asked}
            onClose={() => setAsked(false)}
            onConfirm={() => {
              setAsked(false);
              setOpen(false);
            }}
            title="Discard?"
            message="Discard what you entered?"
            confirmLabel="Discard"
            cancelLabel="Keep editing"
          />
        </Provider>
      );
    };
    render(<Host />);
    const opener = screen.getByRole('button', { name: 'New rule' });
    openWithClick(opener);
    await screen.findByRole('dialog', { name: 'Ready' });
    const readyCancel = screen.getByRole('button', { name: 'Ready Cancel' });
    openWithClick(readyCancel);
    await screen.findByRole('alertdialog', { name: 'Discard?' });

    act(() => screen.getByRole('button', { name: 'Keep editing', hidden: true }).click());
    await waitFor(() => expect(active()).toBe(readyCancel)); // the inner button is still connected: it wins

    openWithClick(readyCancel);
    await screen.findByRole('alertdialog', { name: 'Discard?' });
    act(() => screen.getByRole('button', { name: 'Discard', hidden: true }).click());
    await waitFor(() => expect(active()).toBe(opener));
    await settle();
    expect(active()).toBe(opener);
  });

  it('the opener was removed AND the first modal is gone: no throw, focus is not forced anywhere', async () => {
    const Host: React.FC = () => {
      const [open, setOpen] = React.useState(false);
      const [showOpener, setShowOpener] = React.useState(true);
      return (
        <Provider>
          {showOpener && (
            <button type="button" onClick={() => setOpen(true)}>
              New rule
            </button>
          )}
          <button type="button">Bystander</button>
          {open && (
            <Gate
              onClose={() => {
                setShowOpener(false);
                setOpen(false);
              }}
            />
          )}
        </Provider>
      );
    };
    const errors = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    render(<Host />);
    openWithClick(screen.getByRole('button', { name: 'New rule' }));
    await screen.findByRole('dialog', { name: 'Ready' });
    act(() => screen.getByRole('button', { name: /close/i }).click());
    await settle();
    expect(screen.queryByRole('button', { name: 'New rule' })).toBeNull();
    expect(active()).not.toBe(screen.getByRole('button', { name: 'Bystander' }));
    expect(errors).not.toHaveBeenCalled();
    errors.mockRestore();
  });

  it('the host moved focus to another element as the swapped-in modal closed: that wins, it is not pulled to the opener', async () => {
    const Host: React.FC = () => {
      const [open, setOpen] = React.useState(false);
      return (
        <Provider>
          <button type="button" onClick={() => setOpen(true)}>
            New rule
          </button>
          <button type="button" id="host-chose">
            Host chose
          </button>
          {open && (
            <Gate
              onClose={() => {
                (document.getElementById('host-chose') as HTMLElement).focus();
                setOpen(false);
              }}
            />
          )}
        </Provider>
      );
    };
    render(<Host />);
    openWithClick(screen.getByRole('button', { name: 'New rule' }));
    await screen.findByRole('dialog', { name: 'Ready' });
    act(() => screen.getByRole('button', { name: 'Ready Cancel' }).click());
    await settle();
    expect(active()).toBe(document.getElementById('host-chose'));
  });

  it('an inherited opener is not used while the modal it sits behind is still open (nested, not a swap)', async () => {
    // Inner opens from a button inside Outer; that button is then removed and Inner closes. Outer is still open, so focus
    // must not jump to the page button behind it.
    const Host: React.FC = () => {
      const [outer, setOuter] = React.useState(false);
      const [inner, setInner] = React.useState(false);
      const [showBtn, setShowBtn] = React.useState(true);
      return (
        <Provider>
          <button type="button" onClick={() => setOuter(true)}>
            Page button
          </button>
          <SprkModal open={outer} onClose={() => setOuter(false)} title="Outer" nonBlocking>
            {showBtn && (
              <button type="button" onClick={() => setInner(true)}>
                Open inner
              </button>
            )}
          </SprkModal>
          <SprkModal
            open={inner}
            nonBlocking
            onClose={() => {
              setShowBtn(false);
              setInner(false);
            }}
            title="Inner"
          >
            <p>inner</p>
          </SprkModal>
        </Provider>
      );
    };
    render(<Host />);
    openWithClick(screen.getByRole('button', { name: 'Page button' }));
    await screen.findByRole('dialog', { name: 'Outer' });
    openWithClick(screen.getByRole('button', { name: 'Open inner', hidden: true }));
    const innerDialog = await screen.findByRole('dialog', { name: 'Inner', hidden: true });
    act(() => within(innerDialog).getByRole('button', { name: /close/i, hidden: true }).click());
    await settle();
    expect(active()).not.toBe(screen.getByRole('button', { name: 'Page button', hidden: true }));
  });
});
