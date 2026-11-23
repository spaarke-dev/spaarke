/**
 * RuleWizard focus return (task 202 follow-up; dev finding 2026-11-23): RuleWizard shows a "loading" SprkModal until its
 * catalog arrives and then a different modal (WizardShell). Cancel, with nothing typed, left focus on <body> instead of the
 * "New rule" button that opened it.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { RuleWizard } from '../RuleWizard';
import { makeAuthoringService, catalog, failure } from './fixtures';

jest.setTimeout(120000);

const WAIT = { timeout: 20000 };

const Host: React.FC = () => {
  const [open, setOpen] = React.useState(false);
  const [service] = React.useState(() => makeAuthoringService());
  return (
    <FluentProvider theme={webLightTheme}>
      <button type="button" onClick={() => setOpen(true)}>
        New rule
      </button>
      {open && <RuleWizard service={service} mode={{ kind: 'new' }} onClose={() => setOpen(false)} />}
    </FluentProvider>
  );
};

function openNewRule(): HTMLElement {
  const opener = screen.getByRole('button', { name: 'New rule' });
  act(() => {
    opener.focus();
    opener.click();
  });
  return opener;
}

describe('RuleWizard returns focus to the control that opened it', () => {
  it('Cancel with nothing entered: focus is back on "New rule"', async () => {
    render(<Host />);
    const opener = openNewRule();
    await screen.findByTestId('rule-wizard-basics', undefined, WAIT);
    await waitFor(() =>
      expect(screen.getByRole('dialog', { hidden: true }).contains(document.activeElement)).toBe(true)
    );

    fireEvent.click(screen.getByRole('button', { name: 'Cancel', hidden: true }));

    await waitFor(() => expect(document.activeElement).toBe(opener), WAIT);
  });

  it('Cancel, then Discard on the confirm: focus is back on "New rule"', async () => {
    render(<Host />);
    const opener = openNewRule();
    await screen.findByTestId('rule-wizard-basics', undefined, WAIT);
    fireEvent.change(screen.getByTestId('rule-wizard-name'), { target: { value: 'Half done' } });

    fireEvent.click(screen.getByRole('button', { name: 'Cancel', hidden: true }));
    const check = await screen.findByRole('alertdialog', undefined, WAIT);
    fireEvent.click(within(check).getByRole('button', { name: 'Discard', hidden: true }));

    await waitFor(() => expect(document.activeElement).toBe(opener), WAIT);
  });
});

describe('RuleWizard returns focus after a catalog retry', () => {
  it('catalog fails, Try again (focused), then Cancel: the button stays connected during the retry and focus ends on "New rule"', async () => {
    let release: () => void = () => undefined;
    const second = new Promise<{ ok: true; data: typeof catalog }>(resolve => {
      release = () => resolve({ ok: true, data: catalog });
    });
    const getCatalog = jest
      .fn()
      .mockResolvedValueOnce(failure({ status: 403, message: 'nope' }))
      .mockReturnValueOnce(second);

    const RetryHost: React.FC = () => {
      const [open, setOpen] = React.useState(false);
      const [service] = React.useState(() => makeAuthoringService({ getCatalog } as never));
      return (
        <FluentProvider theme={webLightTheme}>
          <button type="button" onClick={() => setOpen(true)}>
            New rule
          </button>
          {open && <RuleWizard service={service} mode={{ kind: 'new' }} onClose={() => setOpen(false)} />}
        </FluentProvider>
      );
    };

    render(<RetryHost />);
    const opener = openNewRule();
    await screen.findByTestId('rule-wizard-catalog-error', undefined, WAIT);
    const retry = screen.getByTestId('rule-wizard-catalog-retry');
    act(() => {
      retry.focus();
      retry.click();
    });

    // The retry is loading: the focused button is still in the document, still focused, and no longer actionable.
    await screen.findByTestId('rule-wizard-loading', undefined, WAIT);
    expect(retry.isConnected).toBe(true);
    expect(document.activeElement).toBe(retry);
    expect(retry).toHaveAttribute('aria-disabled', 'true');

    await act(async () => {
      release();
      await second;
    });
    await screen.findByTestId('rule-wizard-basics', undefined, WAIT);
    fireEvent.click(screen.getByRole('button', { name: 'Cancel', hidden: true }));

    await waitFor(() => expect(document.activeElement).toBe(opener), WAIT);
  });

  it('the first load shows no "Try again" button', async () => {
    render(<Host />);
    openNewRule();
    expect(screen.queryByTestId('rule-wizard-catalog-retry')).toBeNull();
    await screen.findByTestId('rule-wizard-basics', undefined, WAIT);
  });
});
