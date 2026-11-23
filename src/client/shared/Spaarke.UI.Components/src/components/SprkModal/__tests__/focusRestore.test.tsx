/**
 * focusRestore.test.tsx - a SprkModal that closes returns keyboard focus to the element that opened it (task 202).
 *
 * Dev finding 2026-11-22 (Console, 1440x900): every close of a SprkModal left `document.activeElement` on <body>
 * (Worklist > Decision wizard > Cancel > "Discard unrecorded choices?" > Keep deciding; Ontology admin > New rule >
 * Cancel). Fluent's Dialog restores focus only to a `DialogTrigger`; hosts open a SprkModal from state.
 *
 * Every test asserts where focus ENDS, after the restore's timer and bounded retry have run.
 */
import * as React from 'react';
import { render, screen, waitFor, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SprkModal } from '../SprkModal';
import { ConfirmModal } from '../presets/ConfirmModal';

const Provider: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <FluentProvider theme={webLightTheme}>{children}</FluentProvider>
);

/** Lets the restore's setTimeout(0) and its 50 ms retries all run. */
const settle = (ms = 400) => act(() => new Promise<void>(resolve => setTimeout(resolve, ms)));

const active = () => document.activeElement;

/** The opener button gets real focus first, as a click does. */
function openWithClick(button: HTMLElement): void {
  act(() => {
    button.focus();
    button.click();
  });
}

// --- Hosts -----------------------------------------------------------------------------------------------------

/** (a) a page button opens a SprkModal; the footer Cancel closes it. */
const SimpleHost: React.FC<{ onCloseHook?: () => void; extra?: React.ReactNode }> = ({ onCloseHook, extra }) => {
  const [open, setOpen] = React.useState(false);
  return (
    <Provider>
      <button type="button" onClick={() => setOpen(true)}>
        Open it
      </button>
      {extra}
      <SprkModal
        open={open}
        onClose={() => {
          onCloseHook?.();
          setOpen(false);
        }}
        title="Simple"
        footerStart={
          <button
            type="button"
            onClick={() => {
              onCloseHook?.();
              setOpen(false);
            }}
          >
            Cancel
          </button>
        }
      >
        <p>body</p>
      </SprkModal>
    </Provider>
  );
};

/** (b) a wizard SprkModal whose Cancel opens an always-mounted ConfirmModal (RuleWizard shape). */
const AlwaysMountedConfirmHost: React.FC = () => {
  const [wizard, setWizard] = React.useState(false);
  const [asked, setAsked] = React.useState(false);
  return (
    <Provider>
      <button type="button" onClick={() => setWizard(true)}>
        New rule
      </button>
      <SprkModal
        open={wizard}
        onClose={() => setAsked(true)}
        title="Wizard"
        footerStart={
          <button type="button" onClick={() => setAsked(true)}>
            Wizard Cancel
          </button>
        }
      >
        <p>wizard body</p>
      </SprkModal>
      <ConfirmModal
        open={asked}
        onClose={() => setAsked(false)}
        onConfirm={() => {
          setAsked(false);
          setWizard(false);
        }}
        title="Discard?"
        message="Discard what you entered?"
        confirmLabel="Discard"
        cancelLabel="Keep editing"
      />
    </Provider>
  );
};

/** (c) the same, with the ConfirmModal mounted only while asked (DecisionWizard shape: unmounts while open). */
const ConditionalConfirmHost: React.FC = () => {
  const [wizard, setWizard] = React.useState(false);
  const [asked, setAsked] = React.useState(false);
  return (
    <Provider>
      <button type="button" onClick={() => setWizard(true)}>
        Open wizard
      </button>
      <SprkModal
        open={wizard}
        onClose={() => setAsked(true)}
        title="Wizard"
        footerStart={
          <button type="button" onClick={() => setAsked(true)}>
            Wizard Cancel
          </button>
        }
      >
        <p>wizard body</p>
      </SprkModal>
      {asked && (
        <ConfirmModal
          open
          onClose={() => setAsked(false)}
          onConfirm={() => {
            setAsked(false);
            setWizard(false);
          }}
          title="Discard unrecorded choices?"
          message="Discard?"
          confirmLabel="Discard and close"
          cancelLabel="Keep deciding"
        />
      )}
    </Provider>
  );
};

describe('SprkModal returns focus to its opener when it closes (task 202)', () => {
  it('(a) a button opens a modal, Cancel closes it: focus is back on the button', async () => {
    render(<SimpleHost />);
    const opener = screen.getByRole('button', { name: 'Open it' });
    openWithClick(opener);
    await screen.findByRole('dialog');
    // Fluent moved focus into the modal (first-element focus) after we captured the opener.
    await waitFor(() => expect(screen.getByRole('dialog').contains(active())).toBe(true));

    act(() => screen.getByRole('button', { name: 'Cancel' }).click());

    await waitFor(() => expect(active()).toBe(opener));
  });

  it('(a2) the close button (x) returns focus too', async () => {
    render(<SimpleHost />);
    const opener = screen.getByRole('button', { name: 'Open it' });
    openWithClick(opener);
    await screen.findByRole('dialog');
    act(() => screen.getByRole('button', { name: /close/i }).click());
    await waitFor(() => expect(active()).toBe(opener));
  });

  it('(b) a ConfirmModal over a wizard: Keep editing returns focus to the wizard button that opened it', async () => {
    render(<AlwaysMountedConfirmHost />);
    openWithClick(screen.getByRole('button', { name: 'New rule' }));
    await screen.findByRole('dialog', { name: 'Wizard' });
    const wizardCancel = screen.getByRole('button', { name: 'Wizard Cancel' });
    openWithClick(wizardCancel);
    await screen.findByRole('alertdialog', { name: 'Discard?' });
    // While the confirm is open the wizard is hidden from assistive technology by Fluent's modalizer.
    await waitFor(() => expect(wizardCancel.closest('[aria-hidden="true"]')).not.toBeNull());
    // `hidden: true`: tabster's aria-hidden bookkeeping in jsdom can mark the confirm itself hidden; we only need the button.
    const keep = await screen.findByRole('button', { name: 'Keep editing', hidden: true });

    act(() => keep.click());

    // The restore must land AFTER the wizard is interactive again: the button is focusable and not aria-hidden.
    await waitFor(() => expect(active()).toBe(wizardCancel));
    await waitFor(() => expect(wizardCancel.closest('[aria-hidden="true"]')).toBeNull());
    expect(screen.getByRole('dialog', { name: 'Wizard' })).toBeInTheDocument();
  });

  it('(b2) Discard closes both modals: focus ends on the page button that opened the wizard', async () => {
    render(<AlwaysMountedConfirmHost />);
    const newRule = screen.getByRole('button', { name: 'New rule' });
    openWithClick(newRule);
    await screen.findByRole('dialog', { name: 'Wizard' });
    openWithClick(screen.getByRole('button', { name: 'Wizard Cancel' }));
    await screen.findByRole('alertdialog', { name: 'Discard?' });

    act(() => screen.getByRole('button', { name: 'Discard' }).click());

    await waitFor(() => expect(active()).toBe(newRule));
    await settle(); // the confirm's own (dead) restore must not pull focus away afterwards
    expect(active()).toBe(newRule);
  });

  it('(c) a conditionally mounted ConfirmModal (unmounted while open): Keep deciding returns focus to Wizard Cancel', async () => {
    render(<ConditionalConfirmHost />);
    openWithClick(screen.getByRole('button', { name: 'Open wizard' }));
    await screen.findByRole('dialog', { name: 'Wizard' });
    const wizardCancel = screen.getByRole('button', { name: 'Wizard Cancel' });
    openWithClick(wizardCancel);
    await screen.findByRole('alertdialog', { name: 'Discard unrecorded choices?' });

    act(() => screen.getByRole('button', { name: 'Keep deciding' }).click());

    await waitFor(() => expect(screen.queryByRole('alertdialog', { name: 'Discard unrecorded choices?' })).toBeNull());
    await waitFor(() => expect(active()).toBe(wizardCancel));
  });

  it('(c2) Discard and close (confirm unmounts and the wizard closes together): focus ends on the page opener', async () => {
    render(<ConditionalConfirmHost />);
    const opener = screen.getByRole('button', { name: 'Open wizard' });
    openWithClick(opener);
    await screen.findByRole('dialog', { name: 'Wizard' });
    openWithClick(screen.getByRole('button', { name: 'Wizard Cancel' }));
    await screen.findByRole('alertdialog', { name: 'Discard unrecorded choices?' });

    act(() => screen.getByRole('button', { name: 'Discard and close' }).click());

    await waitFor(() => expect(active()).toBe(opener));
    await settle();
    expect(active()).toBe(opener);
  });

  it('(d) ordering: the restore lands after the parent surface is interactive again (a host that lifts inert in an effect)', async () => {
    // jsdom does not refuse focus() inside [inert]; a browser does. Model that, so a restore that ran too early
    // (synchronously in the commit that closes the confirm) fails here, as it would on a real page.
    const realFocus = HTMLElement.prototype.focus;
    const focusSpy = jest.spyOn(HTMLElement.prototype, 'focus').mockImplementation(function (
      this: HTMLElement,
      ...args
    ) {
      if (this.closest('[inert]')) return;
      realFocus.apply(this, args);
    });
    const Host: React.FC = () => {
      const [asked, setAsked] = React.useState(false);
      React.useEffect(() => {
        // Passive effect: runs after every layout effect of the commit that closes the confirm.
        const wizard = document.querySelector<HTMLElement>('[aria-label="Wizard surface"]');
        if (!wizard) return;
        if (asked) wizard.setAttribute('inert', '');
        else wizard.removeAttribute('inert');
      }, [asked]);
      return (
        <Provider>
          <SprkModal
            open
            onClose={() => undefined}
            title="Wizard"
            footerStart={
              <button type="button" onClick={() => setAsked(true)}>
                Wizard Cancel
              </button>
            }
          >
            <div aria-label="Wizard surface" role="group">
              <button type="button" onClick={() => setAsked(true)}>
                Inside
              </button>
            </div>
          </SprkModal>
          <ConfirmModal
            open={asked}
            onClose={() => setAsked(false)}
            onConfirm={() => setAsked(false)}
            title="Sure?"
            message="m"
            cancelLabel="Keep"
          />
        </Provider>
      );
    };
    try {
      render(<Host />);
      await screen.findByRole('dialog', { name: 'Wizard', hidden: true });
      const inside = screen.getByRole('button', { name: 'Inside', hidden: true });
      act(() => {
        inside.focus();
        inside.click();
      });
      const keep = await screen.findByRole('button', { name: 'Keep', hidden: true });
      expect(inside.closest('[inert]')).not.toBeNull();

      act(() => keep.click());

      await waitFor(() => expect(inside.closest('[inert]')).toBeNull());
      await waitFor(() => expect(document.activeElement).toBe(inside));
    } finally {
      focusSpy.mockRestore();
    }
  });

  it('captures the opener before Fluent moves focus: content that takes focus itself does not become the target', async () => {
    const Autofocus: React.FC = () => {
      const ref = React.useRef<HTMLInputElement>(null);
      React.useLayoutEffect(() => ref.current?.focus(), []);
      return <input ref={ref} aria-label="autofocus field" />;
    };
    const Host: React.FC = () => {
      const [open, setOpen] = React.useState(false);
      return (
        <Provider>
          <button type="button" onClick={() => setOpen(true)}>
            Open it
          </button>
          <SprkModal open={open} onClose={() => setOpen(false)} title="T">
            <Autofocus />
          </SprkModal>
        </Provider>
      );
    };
    render(<Host />);
    const opener = screen.getByRole('button', { name: 'Open it' });
    openWithClick(opener);
    await screen.findByLabelText('autofocus field');
    // Both the content's own layout-effect focus and Fluent's first-element focus ran: focus is inside the modal.
    await waitFor(() => expect(screen.getByRole('dialog').contains(active())).toBe(true));

    act(() => screen.getByRole('button', { name: /close/i }).click());

    await waitFor(() => expect(active()).toBe(opener));
  });

  it("restores in the opener's own document (an iframe host), not the global one", async () => {
    const frame = document.createElement('iframe');
    document.body.appendChild(frame);
    let unmountHost: (() => void) | undefined;
    try {
      const frameDoc = frame.contentDocument as Document;
      const mount = frameDoc.createElement('div');
      frameDoc.body.appendChild(mount);
      const Host: React.FC = () => {
        const [open, setOpen] = React.useState(false);
        return (
          <FluentProvider theme={webLightTheme} targetDocument={frameDoc}>
            <button type="button" onClick={() => setOpen(true)}>
              Frame opener
            </button>
            <SprkModal open={open} onClose={() => setOpen(false)} title="In frame">
              <p>body</p>
            </SprkModal>
          </FluentProvider>
        );
      };
      unmountHost = render(<Host />, { container: mount }).unmount;
      const opener = frameDoc.querySelector('button') as HTMLButtonElement;
      act(() => {
        opener.focus();
        opener.click();
      });
      await waitFor(() => expect(frameDoc.querySelector('[role="dialog"]')).not.toBeNull());
      const close = Array.from(frameDoc.querySelectorAll('button')).find(b =>
        /close/i.test(b.getAttribute('aria-label') ?? '')
      );
      act(() => (close as HTMLButtonElement).click());
      await waitFor(() => expect(frameDoc.activeElement).toBe(opener));
    } finally {
      unmountHost?.(); // before the frame goes, so React unmounts into a live document
      frame.remove();
    }
  });
});

describe('SprkModal focus restore: negative cases (task 202)', () => {
  it('the opener was removed before close: no throw, focus is not forced anywhere', async () => {
    const Host: React.FC = () => {
      const [open, setOpen] = React.useState(false);
      const [showOpener, setShowOpener] = React.useState(true);
      return (
        <Provider>
          {showOpener && (
            <button type="button" onClick={() => setOpen(true)}>
              Open it
            </button>
          )}
          <button type="button">Bystander</button>
          <SprkModal
            open={open}
            onClose={() => {
              setShowOpener(false);
              setOpen(false);
            }}
            title="T"
          >
            <p>body</p>
          </SprkModal>
        </Provider>
      );
    };
    const errors = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    render(<Host />);
    openWithClick(screen.getByRole('button', { name: 'Open it' }));
    await screen.findByRole('dialog');
    act(() => screen.getByRole('button', { name: /close/i }).click());
    await settle();
    expect(screen.queryByRole('button', { name: 'Open it' })).toBeNull();
    expect(active()).not.toBe(screen.getByRole('button', { name: 'Bystander' }));
    expect(errors).not.toHaveBeenCalled();
    errors.mockRestore();
  });

  it('the opener is disabled when the modal closes: it is not focused', async () => {
    const Host: React.FC = () => {
      const [open, setOpen] = React.useState(false);
      const [busy, setBusy] = React.useState(false);
      return (
        <Provider>
          <button type="button" disabled={busy} onClick={() => setOpen(true)}>
            Open it
          </button>
          <SprkModal
            open={open}
            onClose={() => {
              setBusy(true);
              setOpen(false);
            }}
            title="T"
          >
            <p>body</p>
          </SprkModal>
        </Provider>
      );
    };
    render(<Host />);
    const opener = screen.getByRole('button', { name: 'Open it' });
    openWithClick(opener);
    await screen.findByRole('dialog');
    act(() => screen.getByRole('button', { name: /close/i }).click());
    await settle();
    expect(opener).toBeDisabled();
    expect(active()).not.toBe(opener);
  });

  it('the opener is inside an [inert] subtree: it is not focused', async () => {
    const Host: React.FC = () => {
      const [open, setOpen] = React.useState(false);
      return (
        <Provider>
          <div data-testid="region">
            <button type="button" onClick={() => setOpen(true)}>
              Open it
            </button>
          </div>
          <SprkModal
            open={open}
            onClose={() => {
              screen.getByTestId('region').setAttribute('inert', '');
              setOpen(false);
            }}
            title="T"
          >
            <p>body</p>
          </SprkModal>
        </Provider>
      );
    };
    render(<Host />);
    const opener = screen.getByRole('button', { name: 'Open it' });
    openWithClick(opener);
    await screen.findByRole('dialog');
    act(() => screen.getByRole('button', { name: /close/i }).click());
    await settle();
    expect(active()).not.toBe(opener);
  });

  it('the host moved focus to another element outside the modal as it closed: that wins, on the first attempt too', async () => {
    render(
      <SimpleHost
        onCloseHook={() => (document.getElementById('host-chose') as HTMLElement).focus()}
        extra={
          <button id="host-chose" type="button">
            Host chose
          </button>
        }
      />
    );
    openWithClick(screen.getByRole('button', { name: 'Open it' }));
    await screen.findByRole('dialog');

    act(() => screen.getByRole('button', { name: 'Cancel' }).click());
    await settle();

    expect(active()).toBe(document.getElementById('host-chose'));
  });

  it('focus on another open dialog (a parent wizard the host focused on purpose) is kept, not pulled to the opener', async () => {
    // The opener is a page button; while the inner modal closes the host puts focus inside the OUTER dialog.
    const Host: React.FC = () => {
      const [outer, setOuter] = React.useState(false);
      const [inner, setInner] = React.useState(false);
      return (
        <Provider>
          <button type="button" onClick={() => setInner(true)}>
            Open inner
          </button>
          <SprkModal open={outer} onClose={() => setOuter(false)} title="Outer" nonBlocking>
            <button type="button" id="outer-field">
              Outer field
            </button>
          </SprkModal>
          <SprkModal
            open={inner}
            nonBlocking
            onClose={() => {
              (document.getElementById('outer-field') as HTMLElement).focus();
              setInner(false);
            }}
            title="Inner"
          >
            <p>inner body</p>
          </SprkModal>
          <button type="button" onClick={() => setOuter(true)}>
            Open outer
          </button>
        </Provider>
      );
    };
    render(<Host />);
    act(() => screen.getByRole('button', { name: 'Open outer' }).click());
    await screen.findByRole('dialog', { name: 'Outer' });
    openWithClick(screen.getByRole('button', { name: 'Open inner' }));
    await screen.findByRole('dialog', { name: 'Inner' });
    act(() =>
      screen
        .getByRole('dialog', { name: 'Inner' })
        .querySelector<HTMLButtonElement>('button[aria-label*="lose" i]')!
        .click()
    );
    await settle();
    expect(active()).toBe(document.getElementById('outer-field'));
  });

  it('a hidden toggle does not restore focus (the modal stays open)', async () => {
    const Host: React.FC<{ hidden: boolean }> = ({ hidden }) => (
      <Provider>
        <button type="button" id="opener">
          Open it
        </button>
        <SprkModal open onClose={() => undefined} title="T" hidden={hidden}>
          <button type="button">In modal</button>
        </SprkModal>
      </Provider>
    );
    const opener = (() => {
      const b = document.createElement('button');
      document.body.appendChild(b);
      b.focus();
      return b;
    })();
    const { rerender } = render(<Host hidden={false} />);
    await screen.findByRole('dialog');
    await waitFor(() => expect(screen.getByRole('dialog').contains(active())).toBe(true));
    rerender(<Host hidden />);
    await settle();
    expect(active()).not.toBe(opener);
    rerender(<Host hidden={false} />);
    await settle();
    expect(active()).not.toBe(opener);
    opener.remove();
  });

  it('yieldToSidePane on and off does not restore focus (task 198 behaviour kept)', async () => {
    const opener = document.createElement('button');
    document.body.appendChild(opener);
    opener.focus();
    const Host: React.FC<{ yielding: boolean }> = ({ yielding }) => (
      <Provider>
        <SprkModal open onClose={() => undefined} title="T" yieldToSidePane={yielding}>
          <button type="button">In modal</button>
        </SprkModal>
      </Provider>
    );
    const { rerender } = render(<Host yielding={false} />);
    await screen.findByRole('dialog');
    rerender(<Host yielding />);
    await settle();
    rerender(<Host yielding={false} />);
    await settle();
    expect(active()).not.toBe(opener);
    opener.remove();
  });

  it('closing and reopening quickly leaves the new modal in charge: the pending restore does not pull focus out', async () => {
    const Host: React.FC = () => {
      const [open, setOpen] = React.useState(false);
      return (
        <Provider>
          <button type="button" onClick={() => setOpen(o => !o)}>
            Toggle
          </button>
          <SprkModal open={open} onClose={() => setOpen(false)} title="T">
            <button type="button">In modal</button>
          </SprkModal>
        </Provider>
      );
    };
    render(<Host />);
    const toggle = screen.getByRole('button', { name: 'Toggle' });
    openWithClick(toggle);
    await screen.findByRole('dialog');
    act(() => {
      toggle.click(); // close
    });
    act(() => {
      toggle.focus();
      toggle.click(); // reopen before the restore timer fires
    });
    await screen.findByRole('dialog', { hidden: true });
    await settle();
    expect(screen.getByRole('dialog', { hidden: true }).contains(active())).toBe(true);
  });

  it('a pending restore is cancelled by a reopen: with focus on <body> at the restore tick, the old opener is not focused', async () => {
    // Discriminating form of the test above. There, focus is inside the reopened modal at the tick, so even a stale
    // (uncancelled) restore sees "focus is kept" and does nothing. Here focus is on <body> when the 0 ms tick runs
    // (the state a stale restore acts on), so only the cancel at reopen keeps the old opener from being focused.
    const Host: React.FC = () => {
      const [open, setOpen] = React.useState(false);
      return (
        <Provider>
          <button type="button" onClick={() => setOpen(o => !o)}>
            Toggle
          </button>
          <SprkModal open={open} onClose={() => setOpen(false)} title="T">
            <button type="button">In modal</button>
          </SprkModal>
        </Provider>
      );
    };
    render(<Host />);
    const toggle = screen.getByRole('button', { name: 'Toggle' });
    openWithClick(toggle);
    await screen.findByRole('dialog');
    act(() => {
      toggle.click(); // close: schedules the restore to `toggle` (0 ms timer)
    });
    act(() => {
      toggle.focus();
      toggle.click(); // reopen before the timer fires: must cancel the pending restore
    });
    // Synchronously, before the timer: nobody has focus.
    (document.activeElement as HTMLElement | null)?.blur();
    expect(active()).toBe(document.body);
    await settle();
    expect(active()).not.toBe(toggle);
    expect(active()).toBe(document.body);
  });
});
