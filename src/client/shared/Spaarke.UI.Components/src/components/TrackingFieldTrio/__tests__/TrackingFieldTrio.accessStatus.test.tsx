/**
 * TrackingFieldTrio — the access-status indicator (unified-access-control-r2 task 153, owner round 83 item 11).
 *
 * Covers: the fail-closed parse of task 064's per-record read (another record's answer, an unknown or missing signal,
 * a non-object body → unknown, never "does not apply"); the indicator rule (any unknown → neutral "Access status
 * unavailable", both doesNotApply → nothing, otherwise red Secure / No Access / both); click gating (clickable only
 * with onOpenGrantModal AND canGrantAccess === true; No Access, or both, opens at 'noAccess', Secure only opens at the
 * top; the unavailable state is never clickable); consumers that do not pass `accessStatus` are unchanged; no text
 * says "not secure" / "not restricted"; dark theme renders with tokens and no console errors.
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { TrackingFieldTrio } from '../TrackingFieldTrio';
import { throwingAuthenticatedFetch } from '../../../__tests__/helpers/authenticatedFetchDouble';
import type { ITrackingFieldTrioProps, IAccessPermissionOption } from '../types';
import {
  parseAccessStatusResponse,
  readAccessStatus,
  resolveAccessIndicator,
  ACCESS_STATUS_UNAVAILABLE,
  ACCESS_STATUS_TIMEOUT_MS,
  INDICATOR_SHOWS_SECURE,
  type ITrackingAccessStatus,
} from '../accessStatus';

const RECORD_ID = '6f1c2d3e-4a5b-4c6d-8e7f-90a1b2c3d4e5';

const OPTIONS: IAccessPermissionOption[] = [
  { value: 100000000, label: 'Standard' },
  { value: 100000001, label: 'Limited' },
  { value: 100000002, label: 'Restricted' },
];

function makeProps(overrides?: Partial<ITrackingFieldTrioProps>): ITrackingFieldTrioProps {
  return {
    monitor: false,
    highPriority: false,
    accessPermission: 100000000,
    accessPermissionOptions: OPTIONS,
    monitorLabel: 'Monitor',
    highPriorityLabel: 'High Priority',
    accessPermissionLabel: 'Access Permission',
    onMonitorChange: jest.fn(),
    onHighPriorityChange: jest.fn(),
    onAccessPermissionChange: jest.fn(),
    ...overrides,
  };
}

const renderTrio = (props: ITrackingFieldTrioProps, theme = webLightTheme) =>
  render(
    <FluentProvider theme={theme}>
      <TrackingFieldTrio {...props} />
    </FluentProvider>
  );

const status = (secure: ITrackingAccessStatus['secure'], noAccess: ITrackingAccessStatus['noAccess']) => ({
  secure,
  noAccess,
});

describe('parseAccessStatusResponse (task 153, fail closed)', () => {
  it('reads both signals of an answer about this record, comparing the id canonically', () => {
    expect(
      parseAccessStatusResponse(
        { recordType: 'sprk_matter', recordId: RECORD_ID, secure: 'applies', noAccess: 'doesNotApply' },
        `{${RECORD_ID.toUpperCase()}}`
      )
    ).toEqual({ secure: 'applies', noAccess: 'doesNotApply' });
  });

  it.each([
    ['another record', { recordId: 'ffffffff-0000-0000-0000-000000000000', secure: 'applies', noAccess: 'applies' }],
    ['no record id', { secure: 'applies', noAccess: 'applies' }],
    ['a non-string record id', { recordId: 42, secure: 'applies', noAccess: 'applies' }],
    ['null', null],
    ['a string', 'ok'],
    ['an array', []],
  ])('is unavailable for an answer naming %s', (_label, body) => {
    expect(parseAccessStatusResponse(body, RECORD_ID)).toEqual(ACCESS_STATUS_UNAVAILABLE);
  });

  it('reads a missing or unrecognised signal as unknown, never as doesNotApply', () => {
    expect(parseAccessStatusResponse({ recordId: RECORD_ID, secure: 'doesNotApply' }, RECORD_ID)).toEqual({
      secure: 'doesNotApply',
      noAccess: 'unknown',
    });
    expect(parseAccessStatusResponse({ recordId: RECORD_ID, secure: 'no', noAccess: false }, RECORD_ID)).toEqual({
      secure: 'unknown',
      noAccess: 'unknown',
    });
    expect(
      parseAccessStatusResponse({ recordId: RECORD_ID, secure: 'unknown', noAccess: 'doesNotApply' }, RECORD_ID)
    ).toEqual({ secure: 'unknown', noAccess: 'doesNotApply' });
  });

  it('is unavailable when the host has no record id to compare with', () => {
    expect(parseAccessStatusResponse({ recordId: RECORD_ID, secure: 'applies', noAccess: 'applies' }, '')).toEqual(
      ACCESS_STATUS_UNAVAILABLE
    );
  });
});

describe('readAccessStatus (task 153, the PCF host read — evaluateGrantGate rules)', () => {
  const res = (body: unknown, status = 200) =>
    ({
      ok: status >= 200 && status < 300,
      status,
      json: async () => {
        if (body instanceof Error) throw body;
        return body;
      },
    }) as unknown as Response;

  beforeEach(() => {
    jest.spyOn(console, 'info').mockImplementation(() => {});
    jest.spyOn(console, 'warn').mockImplementation(() => {});
  });
  afterEach(() => jest.restoreAllMocks());

  it("asks 064's route for this record with a canonical id, as a GET, and parses the two signals", async () => {
    const fetchFn = jest.fn(async () => res({ recordId: RECORD_ID, secure: 'applies', noAccess: 'doesNotApply' }));
    const status = await readAccessStatus(fetchFn, 'workassignment', `{${RECORD_ID.toUpperCase()}}`);
    expect(fetchFn).toHaveBeenCalledWith(
      `/api/v1/records/sprk_workassignment/${RECORD_ID}/no-access`,
      expect.objectContaining({ method: 'GET' })
    );
    expect(status).toEqual({ secure: 'applies', noAccess: 'doesNotApply' });
  });

  it.each([
    ['the uniform 404', async () => res({ title: 'Not Found', status: 404 }, 404)],
    ['a 403', async () => res({ status: 403 }, 403)],
    ['a 500', async () => res({ status: 500 }, 500)],
    ['an unparseable body', async () => res(new Error('bad json'))],
    [
      'an answer about another record',
      async () => res({ recordId: 'ffffffff-0000-0000-0000-000000000000', secure: 'applies', noAccess: 'applies' }),
    ],
    [
      'a thrown call (auth not initialised)',
      async () => {
        throw new Error('not initialized');
      },
    ],
  ])('%s → unavailable, never doesNotApply', async (_label, impl) => {
    // Production shape: the PCF injects `@spaarke/auth`'s authenticatedFetch, which THROWS for a non-2xx.
    const status = await readAccessStatus(
      throwingAuthenticatedFetch(impl) as unknown as (u: string) => Promise<Response>,
      'matter',
      RECORD_ID
    );
    expect(status).toEqual(ACCESS_STATUS_UNAVAILABLE);
  });

  it('control: a fetch that resolves no response at all (no token) → unavailable', async () => {
    const status = await readAccessStatus(
      jest.fn(async () => undefined) as unknown as (u: string) => Promise<Response>,
      'matter',
      RECORD_ID
    );
    expect(status).toEqual(ACCESS_STATUS_UNAVAILABLE);
  });

  it.each([404, 403, 500])(
    'control: a fetch that RETURNS the %s response → unavailable, never doesNotApply',
    async code => {
      const status = await readAccessStatus(
        jest.fn(async () => res({ status: code }, code)) as unknown as (u: string) => Promise<Response>,
        'matter',
        RECORD_ID
      );
      expect(status).toEqual(ACCESS_STATUS_UNAVAILABLE);
    }
  );
});

describe('readAccessStatus — a hung request (hardening)', () => {
  beforeEach(() => jest.spyOn(console, 'warn').mockImplementation(() => {}));
  afterEach(() => jest.restoreAllMocks());

  it('is unavailable after the timeout, and aborts the request', async () => {
    let signal: AbortSignal | undefined;
    const fetchFn = jest.fn((_url: string, init?: RequestInit) => {
      signal = init?.signal ?? undefined;
      return new Promise<Response>(() => {});
    });
    const status = await readAccessStatus(fetchFn, 'matter', RECORD_ID, 20);
    expect(status).toEqual(ACCESS_STATUS_UNAVAILABLE);
    expect(signal?.aborted).toBe(true);
  });

  it('defaults to a bounded wait', () => {
    expect(ACCESS_STATUS_TIMEOUT_MS).toBeGreaterThan(0);
    expect(ACCESS_STATUS_TIMEOUT_MS).toBeLessThanOrEqual(30000);
  });
});

describe('resolveAccessIndicator (task 153)', () => {
  // The Secure path, through the explicit parameter (owner round 85 switched the default off: change point b).
  it.each([
    [status('doesNotApply', 'doesNotApply'), { kind: 'none' }],
    [status('applies', 'doesNotApply'), { kind: 'restricted', secure: true, noAccess: false }],
    [status('doesNotApply', 'applies'), { kind: 'restricted', secure: false, noAccess: true }],
    [status('applies', 'applies'), { kind: 'restricted', secure: true, noAccess: true }],
    // Any unknown is unavailable — even beside an applying signal (criterion 7: only the unavailable notice).
    [status('applies', 'unknown'), { kind: 'unavailable' }],
    [status('unknown', 'applies'), { kind: 'unavailable' }],
    [status('unknown', 'doesNotApply'), { kind: 'unavailable' }],
    [status('doesNotApply', 'unknown'), { kind: 'unavailable' }],
    [ACCESS_STATUS_UNAVAILABLE, { kind: 'unavailable' }],
  ])('with the Secure signal shown: %j → %j', (input, expected) => {
    expect(resolveAccessIndicator(input as ITrackingAccessStatus, true)).toEqual(expected);
  });

  // The default (owner round 85, change point b): only No Access; a secure-only record draws nothing (the pill says
  // Secure). An unknown Secure signal is still "unavailable".
  it.each([
    [status('applies', 'doesNotApply'), { kind: 'none' }],
    [status('applies', 'applies'), { kind: 'restricted', secure: false, noAccess: true }],
    [status('doesNotApply', 'applies'), { kind: 'restricted', secure: false, noAccess: true }],
    [status('unknown', 'doesNotApply'), { kind: 'unavailable' }],
    [status('doesNotApply', 'doesNotApply'), { kind: 'none' }],
    [status('applies', 'unknown'), { kind: 'unavailable' }],
  ])('by default (Secure not shown): %j → %j', (input, expected) => {
    expect(resolveAccessIndicator(input as ITrackingAccessStatus)).toEqual(expected);
    expect(resolveAccessIndicator(input as ITrackingAccessStatus, false)).toEqual(expected);
  });

  it('does not show the Secure signal (owner round 85, change point b flipped)', () => {
    expect(INDICATOR_SHOWS_SECURE).toBe(false);
  });
});

describe('TrackingFieldTrio — access-status indicator (task 153)', () => {
  const indicator = () => screen.queryByTestId('tracking-access-indicator');

  it('draws nothing when accessStatus is omitted (existing consumers unchanged)', () => {
    renderTrio(makeProps({ onOpenGrantModal: jest.fn(), canGrantAccess: true }));
    expect(indicator()).toBeNull();
  });

  it('draws nothing when neither signal applies, and never says "not secure" or "not restricted"', () => {
    const { container } = renderTrio(makeProps({ accessStatus: status('doesNotApply', 'doesNotApply') }));
    expect(indicator()).toBeNull();
    expect(container.textContent).not.toMatch(/not secure|not restricted|unrestricted/i);
  });

  it.each([
    [status('doesNotApply', 'applies'), 'No Access'],
    // Owner round 85: the indicator shows only No Access; "Secure" is the pill's job.
    [status('applies', 'applies'), 'No Access'],
  ])('shows %j as "%s"', (s, label) => {
    renderTrio(makeProps({ accessStatus: s }));
    expect(indicator()).toHaveTextContent(label);
    expect(indicator()).not.toHaveTextContent('Secure');
  });

  it('draws nothing for a secure-only record (the red pill already says Secure)', () => {
    renderTrio(
      makeProps({ onOpenGrantModal: jest.fn(), canGrantAccess: true, accessStatus: status('applies', 'doesNotApply') })
    );
    expect(indicator()).toBeNull();
  });

  it('shows "Access status unavailable" (neutral) when a signal is unknown — present, never absent', () => {
    renderTrio(makeProps({ accessStatus: status('applies', 'unknown') }));
    expect(indicator()).toHaveTextContent('Access status unavailable');
    expect(screen.queryByText('Secure')).not.toBeInTheDocument();
  });

  it('renders the indicator in the header row even with no title and no governance icons', () => {
    renderTrio(makeProps({ accessStatus: status('doesNotApply', 'applies') }));
    expect(indicator()).not.toBeNull();
  });

  describe('click gating', () => {
    it('No Access with canGrantAccess === true opens Manage Access at the No Access List', () => {
      const onOpenGrantModal = jest.fn();
      renderTrio(
        makeProps({ onOpenGrantModal, canGrantAccess: true, accessStatus: status('doesNotApply', 'applies') })
      );
      fireEvent.click(screen.getByRole('button', { name: 'No Access' }));
      expect(onOpenGrantModal).toHaveBeenCalledTimes(1);
      expect(onOpenGrantModal).toHaveBeenCalledWith('noAccess');
    });

    it('Secure AND No Access shows "No Access" and targets the No Access List', () => {
      const onOpenGrantModal = jest.fn();
      renderTrio(makeProps({ onOpenGrantModal, canGrantAccess: true, accessStatus: status('applies', 'applies') }));
      fireEvent.click(screen.getByRole('button', { name: 'No Access' }));
      expect(onOpenGrantModal).toHaveBeenCalledWith('noAccess');
    });

    it('Secure only offers no click at all (nothing is drawn; Manage Access stays on the person icon)', () => {
      const onOpenGrantModal = jest.fn();
      renderTrio(
        makeProps({ onOpenGrantModal, canGrantAccess: true, accessStatus: status('applies', 'doesNotApply') })
      );
      expect(screen.queryByRole('button', { name: /Secure/ })).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Grant access' })).toBeInTheDocument();
      expect(onOpenGrantModal).not.toHaveBeenCalled();
    });

    it.each([
      ['canGrantAccess false', { canGrantAccess: false }],
      ['canGrantAccess omitted', {}],
    ])('is not a button and has no handler when %s', (_label, gate) => {
      const onOpenGrantModal = jest.fn();
      renderTrio(makeProps({ onOpenGrantModal, ...gate, accessStatus: status('applies', 'applies') }));
      const el = indicator() as HTMLElement;
      expect(el.tagName).toBe('SPAN');
      expect(el).toHaveTextContent('No Access');
      expect(screen.queryByRole('button', { name: 'No Access' })).not.toBeInTheDocument();
      fireEvent.click(el);
      expect(onOpenGrantModal).not.toHaveBeenCalled();
    });

    it('is not clickable without onOpenGrantModal, even when canGrantAccess is true', () => {
      renderTrio(makeProps({ canGrantAccess: true, accessStatus: status('doesNotApply', 'applies') }));
      expect((indicator() as HTMLElement).tagName).toBe('SPAN');
    });

    it('the unavailable state is never clickable', () => {
      const onOpenGrantModal = jest.fn();
      renderTrio(makeProps({ onOpenGrantModal, canGrantAccess: true, accessStatus: ACCESS_STATUS_UNAVAILABLE }));
      const el = indicator() as HTMLElement;
      expect(el.tagName).toBe('SPAN');
      fireEvent.click(el);
      expect(onOpenGrantModal).not.toHaveBeenCalled();
    });

    it('the non-clickable indicator is focusable and its tooltip says the caller cannot manage access', () => {
      renderTrio(makeProps({ onOpenGrantModal: jest.fn(), accessStatus: status('doesNotApply', 'applies') }));
      const el = indicator() as HTMLElement;
      expect(el).toHaveAttribute('tabindex', '0');
      fireEvent.focus(el);
      expect(screen.getByText(/You cannot manage access on this record\./)).toBeInTheDocument();
    });
  });

  it('a clickable red indicator stays red on hover and press (verifier F4-2)', () => {
    renderTrio(
      makeProps({ onOpenGrantModal: jest.fn(), canGrantAccess: true, accessStatus: status('doesNotApply', 'applies') })
    );
    const button = screen.getByRole('button', { name: 'No Access' });
    const classes = Array.from(button.classList);
    const rules: string[] = [];
    for (const sheet of Array.from(document.styleSheets)) {
      for (const rule of Array.from((sheet as CSSStyleSheet).cssRules)) rules.push(rule.cssText);
    }
    // A rule on one of the button's own classes, for exactly this pseudo-class, setting the property to the token.
    const ownRule = (pseudo: string, property: string, token: string) =>
      rules.some(r => {
        const selector = r.slice(0, r.indexOf('{'));
        const body = r.slice(r.indexOf('{'));
        return (
          classes.some(c => selector.trim() === `.${c}${pseudo}`) &&
          new RegExp(`(^|[{;\\s])${property}:\\s*var\\(--${token}\\)`).test(body)
        );
      });
    expect(ownRule(':hover', 'background-color', 'colorPaletteRedBackground3')).toBe(true);
    expect(ownRule(':hover:active', 'background-color', 'colorPaletteRedBackground3')).toBe(true);
    expect(ownRule(':hover', 'color', 'colorNeutralForeground1')).toBe(true);
    expect(ownRule(':hover:active', 'color', 'colorNeutralForeground1')).toBe(true);
  });

  it('the person icon still opens Manage Access at the top, without passing the click event as a section', () => {
    const onOpenGrantModal = jest.fn();
    renderTrio(makeProps({ onOpenGrantModal, canGrantAccess: true, accessStatus: status('doesNotApply', 'applies') }));
    fireEvent.click(screen.getByRole('button', { name: 'Grant access' }));
    expect(onOpenGrantModal.mock.calls[0]).toEqual([]);
  });

  it('renders every state under webDarkTheme with zero console errors or warnings (ADR-021)', () => {
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
    for (const s of [status('applies', 'applies'), ACCESS_STATUS_UNAVAILABLE, status('doesNotApply', 'applies')]) {
      const view = renderTrio(
        makeProps({ onOpenGrantModal: jest.fn(), canGrantAccess: true, accessStatus: s }),
        webDarkTheme
      );
      expect(screen.getByTestId('tracking-access-indicator')).toBeInTheDocument();
      view.unmount();
    }
    expect(errorSpy).not.toHaveBeenCalled();
    expect(warnSpy).not.toHaveBeenCalled();
    errorSpy.mockRestore();
    warnSpy.mockRestore();
  });
});
