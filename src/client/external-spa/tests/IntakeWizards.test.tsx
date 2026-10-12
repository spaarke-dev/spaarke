/**
 * R3 task 052 (FR-05) - the Legal Front Door intake wizards (NDA Assessment, Policy Question) and their entry points.
 *
 * What runs for real: `QuickStartPane` (cards + More Services), the intake registry and visibility rule, both wizards on
 * the real shared `WizardShell`/`SprkModal`, the intake client (path, body, ProblemDetails parsing) and the widget
 * registry. What is substituted: the network seam (`bffApiCall`, answering the submit route and throwing `ApiError`
 * exactly as bff-client builds it - the raw ProblemDetails body is the message) and the record opener (`useOpenRecord`).
 */
import * as React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const { bffApiCall, openRecord } = vi.hoisted(() => ({ bffApiCall: vi.fn(), openRecord: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall, bffApiBlob: vi.fn() };
});

vi.mock('../src/components/detail/RecordOpener', () => ({ useOpenRecord: () => openRecord }));

import { ApiError } from '../src/types';
import { SignInRequiredError } from '../src/auth/msal-auth';
import type { MeEntitlementsResponse } from '../src/api/me-client';
import { QuickStartPane } from '../src/components/shell/QuickStartPane';
import { INTAKE_WIZARDS, getIntakeWizard, isIntakeTypeAvailable } from '../src/intake/intakeRegistry';
import { INTAKE_REQUEST_TYPE } from '../src/api/intake-client';
import { addDaysIso, todayIso } from '../src/intake/intakeValidation';
import {
  WIDGET_DEFINITIONS,
  defaultWidgetIdsFor,
  getWidgetDefinition,
  isEntitled,
  isEntitledToWidgetId,
  libraryWidgetsFor,
} from '../src/registry/widgetRegistry';

// ---------------------------------------------------------------------------
// Fixtures + helpers
// ---------------------------------------------------------------------------

const CREATED_ID = '00000000-0000-4000-8000-0000000c0001';
const SUBMIT_PATH = '/api/v1/external/servicerequests';

const NDA = INTAKE_REQUEST_TYPE.ndaAssessment;
const INVENTION = INTAKE_REQUEST_TYPE.inventionSubmission;
const POLICY = INTAKE_REQUEST_TYPE.policyQuestion;
const TRADEMARK = INTAKE_REQUEST_TYPE.trademarkSearch;

const workforce = (over: Partial<MeEntitlementsResponse> = {}): MeEntitlementsResponse => ({
  displayName: '',
  email: '',
  plane: 'workforce',
  entitlements: ['legal-front-door', 'policy-library'],
  ...over,
});

/** A CIAM partner. With the partner switch ON the server adds legal-front-door; OFF it does not. */
const partner = (switchOn: boolean, over: Partial<MeEntitlementsResponse> = {}): MeEntitlementsResponse => ({
  displayName: '',
  email: '',
  plane: 'ciam',
  entitlements: switchOn ? ['assigned-work', 'legal-front-door'] : ['assigned-work'],
  ...over,
});

const problem = (status: number, extra: Record<string, unknown> = {}): string =>
  JSON.stringify({ status, title: 'refused', ...extra });

const onOpenWidget = vi.fn();

function renderPane(me: MeEntitlementsResponse = workforce()) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <QuickStartPane me={me} onOpenWidget={onOpenWidget} />
    </FluentProvider>
  );
}

/** The card in the Quick Start row (the first match; the More Services modal renders a second copy when open). */
const card = (name: RegExp): HTMLElement => screen.getAllByRole('button', { name })[0];
const hasCard = (name: RegExp): boolean => screen.queryAllByRole('button', { name }).length > 0;

const change = (label: RegExp | string, value: string): void => {
  fireEvent.change(screen.getByLabelText(label), { target: { value } });
};
const radio = (name: string): void => {
  fireEvent.click(screen.getByRole('radio', { name }));
};
const next = (): void => {
  fireEvent.click(screen.getByRole('button', { name: 'Next' }));
};
const nextButton = (): HTMLButtonElement => screen.getByRole('button', { name: 'Next' }) as HTMLButtonElement;
const submitButton = (name: string): HTMLButtonElement => screen.getByRole('button', { name }) as HTMLButtonElement;

const FUTURE = addDaysIso(todayIso(), 30);

/** NDA step 1 (review path) and step 2, leaving the wizard on the timing step. */
function fillNdaToTiming(action: 'review' | 'issue' = 'review'): void {
  radio(action === 'review' ? 'Review the other party’s NDA (they sent their own draft)' : 'Issue our standard NDA');
  change(/Who is the other party/, 'Contoso Ltd');
  change(/What is your relationship with them/, 'vendor');
  next();
  change(/What is the NDA for/, 'Evaluate their analytics platform for the Q3 pilot');
  radio('Both sides');
  if (action === 'issue') {
    change(/Signer name/, 'Pat Doe');
    change(/Signer email/, 'pat@contoso.example');
    radio('2 years');
    change(/Governing law/, 'Delaware, USA');
  }
  next();
}

function fillPolicyToTiming(): void {
  change(/What is it about/, 'gifts');
  change(/What do you need to know/, 'Can I accept a conference ticket worth about $400 from a supplier?');
  radio('I’m planning to do something');
  next();
}

const lastSubmit = (): { path: string; init: RequestInit; body: any } => {
  const call = bffApiCall.mock.calls.filter(c => c[0] === SUBMIT_PATH).at(-1);
  if (!call) throw new Error('no submit call');
  return { path: call[0], init: call[1], body: JSON.parse((call[1] as RequestInit).body as string) };
};

beforeEach(() => {
  bffApiCall.mockReset();
  openRecord.mockReset();
  onOpenWidget.mockReset();
  bffApiCall.mockImplementation(async (path: string) => {
    if (path === SUBMIT_PATH) return { sprk_servicerequestid: CREATED_ID };
    throw new Error(`unexpected call ${path}`);
  });
});

// ---------------------------------------------------------------------------
// Entry points open the wizards
// ---------------------------------------------------------------------------

describe('entry points open the intake wizards', () => {
  it('the NDA Assessment card opens the NDA wizard (not a widget tab)', () => {
    renderPane();
    fireEvent.click(card(/Start an NDA assessment/));
    expect(screen.getByRole('dialog', { name: 'NDA Assessment' })).toBeTruthy();
    expect(onOpenWidget).not.toHaveBeenCalled();
  });

  it('the Policy Question card opens the Policy Question wizard (not the Policy Library tab)', () => {
    renderPane();
    fireEvent.click(card(/Submit a policy question/));
    expect(screen.getByRole('dialog', { name: 'Policy Question' })).toBeTruthy();
    expect(onOpenWidget).not.toHaveBeenCalled();
  });

  it('the More Services entries open the same wizards', () => {
    renderPane();
    fireEvent.click(screen.getByRole('button', { name: /Browse all legal services/ }));
    const services = screen.getByRole('dialog', { name: 'Legal services' });
    fireEvent.click(within(services).getByRole('button', { name: /Start an NDA assessment/ }));
    expect(screen.getByRole('dialog', { name: 'NDA Assessment' })).toBeTruthy();
    expect(screen.queryByRole('dialog', { name: 'Legal services' })).toBeNull();

    cleanup();
    renderPane();
    fireEvent.click(screen.getByRole('button', { name: /Browse all legal services/ }));
    fireEvent.click(
      within(screen.getByRole('dialog', { name: 'Legal services' })).getByRole('button', {
        name: /Submit a policy question/,
      })
    );
    expect(screen.getByRole('dialog', { name: 'Policy Question' })).toBeTruthy();
  });

  it('a card with neither wizard nor widget (Trademark) is "coming soon" in More Services and opens nothing', () => {
    renderPane();
    fireEvent.click(screen.getByRole('button', { name: /Browse all legal services/ }));
    const services = screen.getByRole('dialog', { name: 'Legal services' });
    expect(within(services).getByText('Coming soon')).toBeTruthy();
    expect(onOpenWidget).not.toHaveBeenCalled();
  });

  it('the Invention card still opens its widget tab (task 053 replaces it with a wizard)', () => {
    renderPane();
    fireEvent.click(card(/Submit an invention disclosure/));
    expect(onOpenWidget).toHaveBeenCalledWith('inventions');
  });

  it('the registry maps each request type to one wizard; task 053 only has to append two entries', () => {
    expect(INTAKE_WIZARDS.map(w => w.requestType)).toEqual([NDA, POLICY]);
    expect(getIntakeWizard(NDA)?.label).toBe('NDA Assessment');
    expect(getIntakeWizard(INVENTION)).toBeUndefined();
    expect(getIntakeWizard(TRADEMARK)).toBeUndefined();
  });
});

// ---------------------------------------------------------------------------
// Visibility is driven by /me/entitlements, not the plane (review 011 K3)
// ---------------------------------------------------------------------------

describe('entry-point visibility follows /me/entitlements, not the plane (review 011 K3)', () => {
  it('partner switch ON: a CIAM partner holding legal-front-door sees the NDA, Policy and Trademark entry points', () => {
    renderPane(partner(true, { intakeRequestTypes: [NDA, POLICY, TRADEMARK] }));
    expect(hasCard(/Start an NDA assessment/)).toBe(true);
    expect(hasCard(/Submit a policy question/)).toBe(true);
    expect(hasCard(/clearance search/)).toBe(true);
    expect(hasCard(/Submit an invention disclosure/)).toBe(false);
    fireEvent.click(card(/Start an NDA assessment/));
    expect(screen.getByRole('dialog', { name: 'NDA Assessment' })).toBeTruthy();
  });

  it('partner switch OFF: a CIAM partner without legal-front-door sees no intake entry point at all', () => {
    renderPane(partner(false, { intakeRequestTypes: [] }));
    expect(hasCard(/Start an NDA assessment/)).toBe(false);
    expect(hasCard(/Submit a policy question/)).toBe(false);
    expect(hasCard(/clearance search/)).toBe(false);
    expect(hasCard(/Submit an invention disclosure/)).toBe(false);
  });

  it('partner switch OFF even if a (stale) intakeRequestTypes lists types: no legal-front-door means hidden', () => {
    renderPane(partner(false, { intakeRequestTypes: [NDA, POLICY] }));
    expect(hasCard(/Start an NDA assessment/)).toBe(false);
  });

  it('intakeRequestTypes narrows what is shown: a type the server does not list gets no card', () => {
    renderPane(workforce({ intakeRequestTypes: [NDA] }));
    expect(hasCard(/Start an NDA assessment/)).toBe(true);
    expect(hasCard(/Submit a policy question/)).toBe(false);
    expect(hasCard(/Submit an invention disclosure/)).toBe(false);
  });

  it('intakeRequestTypes [] means none, not "all" (an entitled caller the server reports no types for)', () => {
    renderPane(workforce({ intakeRequestTypes: [] }));
    expect(hasCard(/Start an NDA assessment/)).toBe(false);
    expect(hasCard(/Submit a policy question/)).toBe(false);
  });

  it('partner invention switch: the Invention card follows the server list for a partner', () => {
    renderPane(partner(true, { intakeRequestTypes: [NDA, INVENTION, POLICY, TRADEMARK] }));
    expect(hasCard(/Submit an invention disclosure/)).toBe(true);
  });

  it('intakeRequestTypes undefined (older server / rollout fallback): the documented fallback, not none, not all', () => {
    // workforce + legal-front-door: NDA, Policy, Trademark and Invention
    expect(isIntakeTypeAvailable(workforce(), NDA)).toBe(true);
    expect(isIntakeTypeAvailable(workforce(), POLICY)).toBe(true);
    expect(isIntakeTypeAvailable(workforce(), TRADEMARK)).toBe(true);
    expect(isIntakeTypeAvailable(workforce(), INVENTION)).toBe(true);
    // CIAM + legal-front-door (switch on): NDA, Policy, Trademark, but Invention only for workforce
    expect(isIntakeTypeAvailable(partner(true), NDA)).toBe(true);
    expect(isIntakeTypeAvailable(partner(true), POLICY)).toBe(true);
    expect(isIntakeTypeAvailable(partner(true), TRADEMARK)).toBe(true);
    expect(isIntakeTypeAvailable(partner(true), INVENTION)).toBe(false);
    // no legal-front-door: nothing
    expect(isIntakeTypeAvailable(partner(false), NDA)).toBe(false);
    expect(isIntakeTypeAvailable(workforce({ entitlements: ['policy-library'] }), POLICY)).toBe(false);
    // an unknown type is never offered by the fallback
    expect(isIntakeTypeAvailable(workforce(), 100000099)).toBe(false);

    renderPane(partner(true));
    expect(hasCard(/Start an NDA assessment/)).toBe(true);
    expect(hasCard(/Submit an invention disclosure/)).toBe(false);
  });

  it('the Policy Question card is gated by legal-front-door (the submit route), not by policy-library', () => {
    expect(isIntakeTypeAvailable(workforce({ entitlements: ['legal-front-door'] }), POLICY)).toBe(true);
    expect(isIntakeTypeAvailable(workforce({ entitlements: ['policy-library'] }), POLICY)).toBe(false);
  });

  it('widgets: Inventions in the library follows the Invention flag, like its Quick Start card (review 052 F3)', () => {
    const ids = (me: MeEntitlementsResponse): string[] => libraryWidgetsFor(me).map(w => w.id);
    // Invention flag OFF: the server lists the other three types only - no Invention widget, for a partner or workforce
    expect(ids(partner(true, { intakeRequestTypes: [NDA, POLICY, TRADEMARK] }))).not.toContain('inventions');
    expect(ids(workforce({ intakeRequestTypes: [NDA, POLICY, TRADEMARK] }))).not.toContain('inventions');
    expect(isEntitledToWidgetId(partner(true, { intakeRequestTypes: [NDA, POLICY, TRADEMARK] }), 'inventions')).toBe(
      false
    );
    // Invention flag ON
    expect(ids(partner(true, { intakeRequestTypes: [NDA, INVENTION, POLICY, TRADEMARK] }))).toContain('inventions');
    // [] means none
    expect(ids(workforce({ intakeRequestTypes: [] }))).not.toContain('inventions');
    // intakeRequestTypes undefined: today's rule - a partner does not get it, a workforce caller does
    expect(ids(partner(true))).not.toContain('inventions');
    expect(ids(workforce())).toContain('inventions');
    // no legal-front-door: never, whatever the list says
    expect(ids(partner(false, { intakeRequestTypes: [INVENTION] }))).not.toContain('inventions');
    // the gate is production `isEntitled`, and an un-gated widget is unaffected by intakeRequestTypes
    const gate = getWidgetDefinition('inventions')!;
    expect(gate.requiredIntakeType).toBe(INVENTION);
    expect(isEntitled(workforce({ intakeRequestTypes: [] }), { planes: ['workforce'] })).toBe(true);
    expect(ids(workforce({ intakeRequestTypes: [] }))).toContain('service-requests');
  });

  it('widgets: there is no "nda" placeholder widget (the wizard replaces it); a stale "nda" tab id is refused (review 052 F4)', () => {
    expect(WIDGET_DEFINITIONS.map(d => d.id)).not.toContain('nda');
    expect(getWidgetDefinition('nda')).toBeUndefined();
    expect(isEntitledToWidgetId(workforce(), 'nda')).toBe(false);
    expect(libraryWidgetsFor(workforce()).map(w => w.id)).not.toContain('nda');
    expect(defaultWidgetIdsFor(workforce())).not.toContain('nda');
  });

  it('widgets: the Service Requests tab follows the entitlement in BOTH switch states', () => {
    // switch ON: the partner holds legal-front-door -> the tab is allowed, in the library and as a default
    expect(isEntitledToWidgetId(partner(true), 'service-requests')).toBe(true);
    expect(libraryWidgetsFor(partner(true)).map(w => w.id)).toContain('service-requests');
    expect(defaultWidgetIdsFor(partner(true))).toContain('service-requests');
    // switch OFF: no legal-front-door -> not routable, not listed, not a default
    expect(isEntitledToWidgetId(partner(false), 'service-requests')).toBe(false);
    expect(libraryWidgetsFor(partner(false)).map(w => w.id)).not.toContain('service-requests');
    expect(defaultWidgetIdsFor(partner(false))).not.toContain('service-requests');
    // workforce unchanged
    expect(defaultWidgetIdsFor(workforce())).toContain('service-requests');
    // the outside-counsel defaults are untouched in both states
    expect(defaultWidgetIdsFor(partner(false))).toEqual([
      'projects',
      'matters',
      'work-assignments',
      'documents',
      'invoices',
    ]);
  });
});

// ---------------------------------------------------------------------------
// NDA wizard
// ---------------------------------------------------------------------------

describe('NDA Assessment wizard', () => {
  beforeEach(() => {
    renderPane();
    fireEvent.click(card(/Start an NDA assessment/));
  });

  it('required fields block Next, with field-level messages once a field is left', () => {
    expect(nextButton().disabled).toBe(true);
    const name = screen.getByLabelText(/Who is the other party/);
    fireEvent.blur(name);
    expect(screen.getByText('Enter the other party’s name.')).toBeTruthy();
    fireEvent.change(name, { target: { value: 'C' } });
    expect(screen.getByText(/at least 2 characters/)).toBeTruthy();
    expect(nextButton().disabled).toBe(true);

    fireEvent.change(name, { target: { value: 'Contoso Ltd' } });
    fireEvent.change(screen.getByLabelText(/What is your relationship/), { target: { value: 'vendor' } });
    expect(nextButton().disabled).toBe(true); // ndaAction still missing
    radio('Review the other party’s NDA (they sent their own draft)');
    expect(nextButton().disabled).toBe(false);
  });

  it('the "other" relationship asks for a description before Next', () => {
    radio('Issue our standard NDA');
    change(/Who is the other party/, 'Contoso Ltd');
    change(/What is your relationship/, 'other');
    expect(screen.getByLabelText(/Describe the relationship/)).toBeTruthy();
    expect(nextButton().disabled).toBe(true);
    change(/Describe the relationship/, 'Joint venture');
    expect(nextButton().disabled).toBe(false);
  });

  it('review path: tells the requester to add the draft under Documents; asks no signer fields', () => {
    fillNdaToTiming('review');
    // we are on the timing step; go back to read the terms step banner
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(screen.getByText(/open the request and add the other party/i)).toBeTruthy();
    expect(screen.queryByLabelText(/Signer email/)).toBeNull();
  });

  it('issue path: asks signer name, signer email (format), term and governing law, and requires neededBy', () => {
    radio('Issue our standard NDA');
    change(/Who is the other party/, 'Contoso Ltd');
    change(/What is your relationship/, 'vendor');
    next();
    change(/What is the NDA for/, 'Evaluate their analytics platform for the Q3 pilot');
    radio('Both sides');
    expect(screen.queryByText(/open the request and add the other party/i)).toBeNull();
    expect(nextButton().disabled).toBe(true); // signer, term, law outstanding
    change(/Signer name/, 'Pat Doe');
    const email = screen.getByLabelText(/Signer email/);
    fireEvent.change(email, { target: { value: 'not-an-email' } });
    fireEvent.blur(email);
    expect(screen.getByText(/valid email address/)).toBeTruthy();
    fireEvent.change(email, { target: { value: 'pat@contoso.example' } });
    radio('2 years');
    change(/Governing law/, 'Delaware, USA');
    expect(nextButton().disabled).toBe(false);
    next();

    // timing: the issue path needs a date
    expect(submitButton('Submit request').disabled).toBe(true);
    const date = screen.getByLabelText(/When do you need it signed/);
    fireEvent.blur(date);
    expect(screen.getByText('Choose a date.')).toBeTruthy();
    fireEvent.change(date, { target: { value: FUTURE } });
    expect(submitButton('Submit request').disabled).toBe(false);
  });

  it('review path: neededBy is optional', () => {
    fillNdaToTiming('review');
    expect(submitButton('Submit request').disabled).toBe(false);
  });

  it('submits the NDA type, a title of the counterparty only, and the answers envelope; shows the success screen', async () => {
    fillNdaToTiming('review');
    change(/When do you need it signed/, FUTURE);
    change(/Anything else Legal should know/, 'They asked for a 2-year term.');
    fireEvent.click(submitButton('Submit request'));

    await screen.findByText('NDA request sent to Legal');
    const { path, init, body } = lastSubmit();
    expect(path).toBe(SUBMIT_PATH);
    expect((init as RequestInit).method).toBe('POST');
    expect(body).toEqual({
      requestType: 100000000,
      title: 'NDA – Contoso Ltd',
      answers: {
        schemaVersion: 1,
        ndaAction: 'review',
        counterpartyName: 'Contoso Ltd',
        counterpartyType: 'vendor',
        purpose: 'Evaluate their analytics platform for the Q3 pilot',
        disclosureDirection: 'mutual',
        neededBy: FUTURE,
        notes: 'They asked for a 2-year term.',
      },
    });
    // success text: review path says what to do next
    expect(screen.getByText(/add the other party.s draft under/)).toBeTruthy();
    expect(screen.getByText(/share confidential information with Contoso Ltd until the NDA is signed/)).toBeTruthy();
  });

  it('the issue submission carries the signer, term and governing law', async () => {
    fillNdaToTiming('issue');
    change(/When do you need it signed/, FUTURE);
    fireEvent.click(submitButton('Submit request'));
    await screen.findByText('NDA request sent to Legal');
    expect(lastSubmit().body.answers).toEqual({
      schemaVersion: 1,
      ndaAction: 'issue',
      counterpartyName: 'Contoso Ltd',
      counterpartyType: 'vendor',
      purpose: 'Evaluate their analytics platform for the Q3 pilot',
      disclosureDirection: 'mutual',
      counterpartySignerName: 'Pat Doe',
      counterpartySignerEmail: 'pat@contoso.example',
      term: 'twoYears',
      governingLaw: 'Delaware, USA',
      neededBy: FUTURE,
    });
    expect(screen.queryByText(/add the other party.s draft/)).toBeNull();
    expect(screen.getByText(/issue our standard NDA to Pat Doe/)).toBeTruthy();
  });

  it('the success screen links to the request and to the Service Requests tab', async () => {
    fillNdaToTiming('review');
    fireEvent.click(submitButton('Submit request'));
    await screen.findByText('NDA request sent to Legal');

    fireEvent.click(screen.getByRole('button', { name: 'Service Requests' }));
    expect(onOpenWidget).toHaveBeenCalledWith('service-requests');
    expect(screen.queryByRole('dialog', { name: 'NDA Assessment' })).toBeNull();
  });

  it('"Open request" opens the new request in the detail view', async () => {
    fillNdaToTiming('review');
    fireEvent.click(submitButton('Submit request'));
    await screen.findByText('NDA request sent to Legal');

    fireEvent.click(screen.getByRole('button', { name: 'Open request' }));
    expect(openRecord).toHaveBeenCalledWith({ entityName: 'sprk_servicerequest', recordId: CREATED_ID });
    expect(screen.queryByRole('dialog', { name: 'NDA Assessment' })).toBeNull();
  });

  it('a 403 shows the server message and keeps every entered answer; a retry then succeeds', async () => {
    bffApiCall.mockRejectedValueOnce(
      new ApiError(
        403,
        problem(403, {
          detail: 'You are not entitled to submit Legal Front Door requests.',
          reasonCode: 'sdap.external.intake.module_not_entitled',
        })
      )
    );
    fillNdaToTiming('review');
    fireEvent.click(submitButton('Submit request'));

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain('You are not entitled to submit Legal Front Door requests.');
    // still on the last step, with the entered answers on the summary, and able to submit again
    expect(screen.queryByText('NDA request sent to Legal')).toBeNull();
    expect(screen.getByRole('dialog', { name: 'NDA Assessment' })).toBeTruthy();
    expect(screen.getByText('Contoso Ltd')).toBeTruthy();
    expect(screen.getByText('Evaluate their analytics platform for the Q3 pilot')).toBeTruthy();
    expect(submitButton('Submit request').disabled).toBe(false);
    // earlier steps kept their values too
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect((screen.getByLabelText(/Who is the other party/) as HTMLInputElement).value).toBe('Contoso Ltd');
    next();
    next();

    fireEvent.click(submitButton('Submit request'));
    await screen.findByText('NDA request sent to Legal');
    expect(bffApiCall.mock.calls.filter(c => c[0] === SUBMIT_PATH)).toHaveLength(2);
  });

  it('a 403 type refusal shows a fixed message (no server wording needed)', async () => {
    bffApiCall.mockRejectedValueOnce(
      new ApiError(
        403,
        problem(403, {
          reasonCode: 'sdap.external.intake.invention_not_available',
          errorCode: 'sdap.external.intake.invention_not_available',
        })
      )
    );
    fillNdaToTiming('review');
    fireEvent.click(submitButton('Submit request'));
    expect((await screen.findByRole('alert')).textContent).toContain('not available to you');
  });

  it.each([
    [400, { detail: 'title is required and may be at most 850 characters.' }, 'title is required'],
    [503, { errorCode: 'EXT_INTAKE_DATAVERSE_UNAVAILABLE' }, 'Nothing was submitted'],
    [
      500,
      {
        errorCode: 'EXT_INTAKE_OWNER_UNRESOLVED',
        detail: 'Your request could not be filed because its owning team could not be determined.',
      },
      'owning team could not be determined',
    ],
    [429, {}, 'too quickly'],
  ])('a %i keeps the wizard open with an honest message', async (status, extra, text) => {
    bffApiCall.mockRejectedValueOnce(new ApiError(status, problem(status, extra)));
    fillNdaToTiming('review');
    fireEvent.click(submitButton('Submit request'));
    expect((await screen.findByRole('alert')).textContent).toContain(text);
    expect(screen.queryByText('NDA request sent to Legal')).toBeNull();
  });

  it('a network failure shows a generic message and keeps the answers', async () => {
    bffApiCall.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    fillNdaToTiming('review');
    fireEvent.click(submitButton('Submit request'));
    expect((await screen.findByRole('alert')).textContent).toContain('could not be submitted');
    expect(screen.getByText('Contoso Ltd')).toBeTruthy();
  });

  it.each([
    ['a 401 after the forced-refresh retry', () => new ApiError(401, problem(401))],
    [
      'a sign-in that could not be started',
      () => new SignInRequiredError({ errorCode: 'user_cancelled', aadsts: null }),
    ],
  ])(
    '%s says the session expired, not "could not be submitted", and keeps the answers (review 052 K3)',
    async (_n, make) => {
      bffApiCall.mockRejectedValueOnce(make());
      fillNdaToTiming('review');
      fireEvent.click(submitButton('Submit request'));
      const text = (await screen.findByRole('alert')).textContent ?? '';
      expect(text).toContain('Your session has expired. Sign in again, then submit.');
      expect(text).not.toContain('could not be submitted');
      expect(screen.queryByText('NDA request sent to Legal')).toBeNull();
      expect(screen.getByText('Contoso Ltd')).toBeTruthy();
      expect(submitButton('Submit request').disabled).toBe(false);
    }
  );

  it('Cancel closes the wizard without submitting', () => {
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog', { name: 'NDA Assessment' })).toBeNull();
    expect(bffApiCall).not.toHaveBeenCalled();
  });

  it('re-opening starts from an empty form', () => {
    change(/Who is the other party/, 'Contoso Ltd');
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    fireEvent.click(card(/Start an NDA assessment/));
    expect((screen.getByLabelText(/Who is the other party/) as HTMLInputElement).value).toBe('');
  });
});

// ---------------------------------------------------------------------------
// Policy Question wizard
// ---------------------------------------------------------------------------

describe('Policy Question wizard', () => {
  beforeEach(() => {
    renderPane();
    fireEvent.click(card(/Submit a policy question/));
  });

  it('required fields block Next, with field-level messages', () => {
    expect(nextButton().disabled).toBe(true);
    const question = screen.getByLabelText(/What do you need to know/);
    fireEvent.blur(question);
    expect(screen.getByText('Enter your question.')).toBeTruthy();
    fireEvent.change(question, { target: { value: 'Too short?' } });
    expect(screen.getByText(/at least 20 characters/)).toBeTruthy();
    const topic = screen.getByLabelText(/What is it about/);
    fireEvent.blur(topic);
    expect(screen.getByText('Choose a topic.')).toBeTruthy();
    expect(nextButton().disabled).toBe(true);
  });

  it('offers the fixed topic list by label, with "Other" asking for text', () => {
    const topic = screen.getByLabelText(/What is it about/) as HTMLSelectElement;
    const labels = Array.from(topic.options).map(o => o.textContent);
    expect(labels).toContain('Gifts, entertainment and hospitality');
    expect(labels).toContain('Anti-bribery and corruption');
    expect(labels.at(-1)).toBe('Other');
    change(/What is it about/, 'other');
    expect(screen.getByLabelText(/What topic/)).toBeTruthy();
  });

  it('the "Other" topic text warns that it appears in the (unsecured) request title (review 052 K2)', () => {
    expect(screen.queryByText(/Appears in the request title/)).toBeNull();
    change(/What is it about/, 'other');
    expect(screen.getByText('Appears in the request title; keep it generic.')).toBeTruthy();
  });

  it('shows no incident channel, link or routing (owner decision 2026-10-12)', () => {
    change(/What is it about/, 'privacy');
    radio('It has already happened');
    expect(screen.queryByText(/incident|speak-up|speak up|breach|hotline/i)).toBeNull();
    expect(screen.queryByRole('link')).toBeNull();
  });

  it('soon/urgent ask for a date; routine does not', () => {
    fillPolicyToTiming();
    expect(screen.queryByLabelText(/By when/)).toBeNull();
    radio('No rush (within about a week)');
    expect(submitButton('Submit question').disabled).toBe(false);
    radio('In the next few days');
    expect(screen.getByLabelText(/By when/)).toBeTruthy();
    expect(submitButton('Submit question').disabled).toBe(true);
    change(/By when/, FUTURE);
    expect(submitButton('Submit question').disabled).toBe(false);
  });

  it('submits the Policy type, a topic-label-only title, and the answers envelope; shows the success screen', async () => {
    change(/What is it about/, 'gifts');
    change(/What do you need to know/, 'Can I accept a conference ticket worth about $400 from a supplier?');
    change(/Background/, 'The supplier is bidding on our contract.');
    radio('I’m planning to do something');
    next();
    radio('In the next few days');
    change(/By when/, FUTURE);
    fireEvent.click(submitButton('Submit question'));

    await screen.findByText('Question sent to Legal');
    const { path, body } = lastSubmit();
    expect(path).toBe(SUBMIT_PATH);
    expect(body).toEqual({
      requestType: 100000002,
      title: 'Policy question – Gifts, entertainment and hospitality',
      answers: {
        schemaVersion: 1,
        topic: 'gifts',
        question: 'Can I accept a conference ticket worth about $400 from a supplier?',
        background: 'The supplier is bidding on our contract.',
        situation: 'planning',
        urgency: 'soon',
        neededBy: FUTURE,
      },
    });
    // the unsecured title never carries the question or background
    expect(body.title).not.toMatch(/conference|bidding/);
    expect(screen.getByText(/We.ve logged your Gifts, entertainment and hospitality question/)).toBeTruthy();
    expect(screen.queryByText(/contact Legal directly/)).toBeNull();
  });

  it('"Other" titles the request with the typed topic', async () => {
    change(/What is it about/, 'other');
    change(/What topic/, 'Lobbying');
    change(/What do you need to know/, 'May we meet a city councillor about the zoning change?');
    radio('General question');
    next();
    radio('Urgent: it’s blocking a decision now');
    change(/By when/, FUTURE);
    fireEvent.click(submitButton('Submit question'));
    await screen.findByText('Question sent to Legal');
    expect(lastSubmit().body.title).toBe('Policy question – Lobbying');
    expect(lastSubmit().body.answers).toMatchObject({ topic: 'other', topicOther: 'Lobbying', urgency: 'urgent' });
    expect(screen.getByText(/contact Legal directly/)).toBeTruthy();
  });

  it('a 403 shows an error and keeps the entered answers', async () => {
    bffApiCall.mockRejectedValueOnce(
      new ApiError(
        403,
        problem(403, {
          detail: 'Your account is not linked to a contact record, so a request cannot be filed in your name.',
        })
      )
    );
    fillPolicyToTiming();
    radio('No rush (within about a week)');
    fireEvent.click(submitButton('Submit question'));

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain('not linked to a contact record');
    expect(screen.queryByText('Question sent to Legal')).toBeNull();
    expect(screen.getByText('Can I accept a conference ticket worth about $400 from a supplier?')).toBeTruthy();
    expect(submitButton('Submit question').disabled).toBe(false);
  });

  it('the success screen opens the Service Requests tab', async () => {
    fillPolicyToTiming();
    radio('No rush (within about a week)');
    fireEvent.click(submitButton('Submit question'));
    await screen.findByText('Question sent to Legal');
    fireEvent.click(screen.getByRole('button', { name: 'Service Requests' }));
    expect(onOpenWidget).toHaveBeenCalledWith('service-requests');
  });
});

// ---------------------------------------------------------------------------
// /me contract
// ---------------------------------------------------------------------------

describe('/me/entitlements intakeRequestTypes', () => {
  it('is passed through from the server as sent', async () => {
    const { fetchMeEntitlements } = await import('../src/api/me-client');
    bffApiCall.mockResolvedValueOnce({
      displayName: 'A',
      email: 'a@x.test',
      plane: 'ciam',
      entitlements: ['assigned-work', 'legal-front-door'],
      intakeRequestTypes: [NDA, POLICY, TRADEMARK],
    });
    const me = await fetchMeEntitlements(false);
    expect(me.intakeRequestTypes).toEqual([NDA, POLICY, TRADEMARK]);
  });

  it('stays undefined when the server omits it, and on the 5xx rollout fallback (neither is "none" nor "all")', async () => {
    const { fetchMeEntitlements } = await import('../src/api/me-client');
    bffApiCall.mockResolvedValueOnce({
      displayName: 'A',
      email: 'a@x.test',
      plane: 'workforce',
      entitlements: ['legal-front-door'],
    });
    expect((await fetchMeEntitlements(true)).intakeRequestTypes).toBeUndefined();

    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    bffApiCall.mockRejectedValueOnce(new ApiError(503, 'down'));
    const fallback = await fetchMeEntitlements(true);
    warn.mockRestore();
    expect(fallback.entitlements).toContain('legal-front-door');
    expect(fallback.intakeRequestTypes).toBeUndefined();
    // ... and the entry points then use the documented fallback rule
    expect(isIntakeTypeAvailable(fallback, NDA)).toBe(true);
    expect(isIntakeTypeAvailable(fallback, INVENTION)).toBe(true); // workforce plane
  });
});

// ---------------------------------------------------------------------------
// The new request appears in the Service Requests tab (its read path is the existing grid; this task wires the way in)
// ---------------------------------------------------------------------------

describe('the submitted request reaches the Service Requests tab', () => {
  it('after a submit, "Service Requests" asks the shell to open that tab, which is entitled for the submitter', async () => {
    const me = workforce();
    renderPane(me);
    fireEvent.click(card(/Start an NDA assessment/));
    fillNdaToTiming('review');
    fireEvent.click(submitButton('Submit request'));
    await screen.findByText('NDA request sent to Legal');
    fireEvent.click(screen.getByRole('button', { name: 'Service Requests' }));
    await waitFor(() => expect(onOpenWidget).toHaveBeenCalledWith('service-requests'));
    expect(isEntitledToWidgetId(me, 'service-requests')).toBe(true);
  });
});
