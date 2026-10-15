/**
 * R3 tasks 052/053 (FR-05) - the Legal Front Door intake wizards (NDA Assessment, Policy Question, Invention Submission,
 * Trademark Search) and their entry points.
 *
 * What runs for real: `QuickStartPane` (cards + More Services), the intake registry and visibility rule, all four wizards
 * on the real shared `WizardShell`/`SprkModal`, the intake client (path, body, ProblemDetails parsing) and the widget
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

  it('the Invention Submission card opens the Invention wizard (not the Inventions widget tab) - task 053', () => {
    renderPane();
    fireEvent.click(card(/Submit an invention disclosure/));
    expect(screen.getByRole('dialog', { name: 'Invention Submission' })).toBeTruthy();
    expect(onOpenWidget).not.toHaveBeenCalled();
  });

  it('the Trademark Search card opens the Trademark wizard - task 053', () => {
    renderPane();
    fireEvent.click(card(/clearance search/));
    expect(screen.getByRole('dialog', { name: 'Trademark Search' })).toBeTruthy();
    expect(onOpenWidget).not.toHaveBeenCalled();
  });

  it('the More Services entries open the Invention and Trademark wizards, and nothing is "coming soon"', () => {
    renderPane();
    fireEvent.click(screen.getByRole('button', { name: /Browse all legal services/ }));
    const services = screen.getByRole('dialog', { name: 'Legal services' });
    expect(within(services).queryByText('Coming soon')).toBeNull();
    fireEvent.click(within(services).getByRole('button', { name: /Submit an invention disclosure/ }));
    expect(screen.getByRole('dialog', { name: 'Invention Submission' })).toBeTruthy();
    expect(screen.queryByRole('dialog', { name: 'Legal services' })).toBeNull();

    cleanup();
    renderPane();
    fireEvent.click(screen.getByRole('button', { name: /Browse all legal services/ }));
    fireEvent.click(
      within(screen.getByRole('dialog', { name: 'Legal services' })).getByRole('button', { name: /clearance search/ })
    );
    expect(screen.getByRole('dialog', { name: 'Trademark Search' })).toBeTruthy();
    expect(onOpenWidget).not.toHaveBeenCalled();
  });

  it('the registry maps each of the four request types to one wizard', () => {
    expect(INTAKE_WIZARDS.map(w => w.requestType)).toEqual([NDA, POLICY, INVENTION, TRADEMARK]);
    expect(getIntakeWizard(NDA)?.label).toBe('NDA Assessment');
    expect(getIntakeWizard(INVENTION)?.label).toBe('Invention Submission');
    expect(getIntakeWizard(TRADEMARK)?.label).toBe('Trademark Search');
    expect(getIntakeWizard(100000099)).toBeUndefined();
  });

  it('every Quick Start card now opens a wizard, none a widget tab', () => {
    for (const name of [
      /Start an NDA assessment/,
      /Submit a policy question/,
      /Submit an invention disclosure/,
      /clearance search/,
    ]) {
      renderPane();
      fireEvent.click(card(name));
      expect(screen.getAllByRole('dialog').length).toBeGreaterThan(0);
      cleanup();
    }
    expect(onOpenWidget).not.toHaveBeenCalled();
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

  it('partner with [NDA, POLICY, TRADEMARK] (invention switch off): no Invention card or More Services entry; Trademark opens its wizard', () => {
    renderPane(partner(true, { intakeRequestTypes: [NDA, POLICY, TRADEMARK] }));
    expect(hasCard(/Submit an invention disclosure/)).toBe(false);
    fireEvent.click(screen.getByRole('button', { name: /Browse all legal services/ }));
    const services = screen.getByRole('dialog', { name: 'Legal services' });
    expect(within(services).queryByRole('button', { name: /Submit an invention disclosure/ })).toBeNull();
    expect(within(services).getByRole('button', { name: /clearance search/ })).toBeTruthy();

    cleanup();
    renderPane(partner(true, { intakeRequestTypes: [NDA, POLICY, TRADEMARK] }));
    fireEvent.click(card(/clearance search/));
    expect(screen.getByRole('dialog', { name: 'Trademark Search' })).toBeTruthy();
  });

  it('the same partner with Invention listed (switch on) sees the Invention card and More Services entry, and it opens the wizard', () => {
    renderPane(partner(true, { intakeRequestTypes: [NDA, INVENTION, POLICY, TRADEMARK] }));
    fireEvent.click(screen.getByRole('button', { name: /Browse all legal services/ }));
    const services = screen.getByRole('dialog', { name: 'Legal services' });
    expect(within(services).getByRole('button', { name: /Submit an invention disclosure/ })).toBeTruthy();
    fireEvent.click(within(services).getByRole('button', { name: /Submit an invention disclosure/ }));
    expect(screen.getByRole('dialog', { name: 'Invention Submission' })).toBeTruthy();
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

  it('the sensitive-information checkboxes each have their own name and toggle independently (task 053 fix to CheckboxRow)', () => {
    radio('Review the other party’s NDA (they sent their own draft)');
    change(/Who is the other party/, 'Contoso Ltd');
    change(/What is your relationship with them/, 'vendor');
    next();
    const source = screen.getByRole('checkbox', { name: 'Source code or technical designs' }) as HTMLInputElement;
    const personal = screen.getByRole('checkbox', { name: 'Personal or customer data' }) as HTMLInputElement;
    expect(source.id).not.toBe(personal.id);
    fireEvent.click(source);
    expect(source.checked).toBe(true);
    expect(personal.checked).toBe(false);
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
// Invention Submission wizard (task 053)
// ---------------------------------------------------------------------------

const PAST = addDaysIso(todayIso(), -30);
const SOON = addDaysIso(todayIso(), 20); // inside the 60-day warning window
const LATER = addDaysIso(todayIso(), 200); // outside it

const INV_TITLE = 'Improved battery cooling method';
const INV_PROBLEM = 'Batteries overheat during fast charging in compact devices.';
const INV_DESCRIPTION =
  'A phase-change layer routes heat to the chassis so charging can stay at full rate without throttling.';

/** A radio inside the radio group whose label matches `group` (Yes/No labels repeat across the Invention wizard). */
const radioIn = (group: RegExp, option: string): void => {
  fireEvent.click(within(screen.getByRole('radiogroup', { name: group })).getByRole('radio', { name: option }));
};

/** Invention step 1, then Next. */
function fillInventionStep1(): void {
  change(/Short, non-confidential title/, INV_TITLE);
  change(/What problem does it solve/, INV_PROBLEM);
  change(/How does it work/, INV_DESCRIPTION);
  next();
}

/** Invention steps 2 and 3 on the shortest path (submitter is the inventor; nothing disclosed, shared or used), then Next. */
function fillInventionStep2and3(): void {
  radioIn(/Are you one of the inventors/, 'Yes');
  next();
  radioIn(/shared publicly/, 'No');
  radioIn(/outside the company, even under NDA/, 'No');
  radioIn(/used in a product or service/, 'No');
  radioIn(/developed with, or funded by/, 'No');
  next();
}

/** Leaves the wizard on step 4 with everything valid except the confirmation. */
function fillInventionToConfirm(): void {
  fillInventionStep1();
  fillInventionStep2and3();
}

const tickConfirm = (): void => {
  fireEvent.click(screen.getByRole('checkbox', { name: /I confirm this is accurate/ }));
};

describe('Invention Submission wizard', () => {
  beforeEach(() => {
    renderPane();
    fireEvent.click(card(/Submit an invention disclosure/));
  });

  it('step 1 requires title (5-150), problem (20+) and description (50+); knownAlternatives is optional', () => {
    expect(nextButton().disabled).toBe(true);
    const title = screen.getByLabelText(/Short, non-confidential title/);
    fireEvent.blur(title);
    expect(screen.getByText('Enter a short title.')).toBeTruthy();
    fireEvent.change(title, { target: { value: 'abc' } });
    expect(screen.getByText(/at least 5 characters/)).toBeTruthy();
    fireEvent.change(title, { target: { value: INV_TITLE } });
    change(/What problem does it solve/, 'too short');
    change(/How does it work/, 'also too short');
    expect(nextButton().disabled).toBe(true);
    change(/What problem does it solve/, INV_PROBLEM);
    change(/How does it work/, INV_DESCRIPTION);
    expect(nextButton().disabled).toBe(false); // knownAlternatives left blank
  });

  it('warns beside the title that it appears in the (unsecured) request title and must stay generic', () => {
    expect(screen.getByText(/Appears in the request title; keep it generic\./)).toBeTruthy();
  });

  it('asks no department question, anywhere', () => {
    const dialog = (): HTMLElement => screen.getByRole('dialog', { name: 'Invention Submission' });
    expect(within(dialog()).queryByText(/department/i)).toBeNull();
    fillInventionToConfirm();
    expect(within(dialog()).queryByLabelText(/department/i)).toBeNull();
    expect(within(dialog()).queryByText(/department/i)).toBeNull();
  });

  it('"not an inventor" needs at least one listed inventor with a name; an email must have an email shape', () => {
    fillInventionStep1();
    radioIn(/Are you one of the inventors/, 'No');
    expect(screen.getByText('Add at least one inventor.')).toBeTruthy();
    expect(nextButton().disabled).toBe(true);

    fireEvent.click(screen.getByRole('button', { name: 'Add inventor' }));
    expect(nextButton().disabled).toBe(true); // the row's name is empty
    const name = screen.getByLabelText(/Inventor 1 name/);
    fireEvent.blur(name);
    expect(screen.getByText('Enter the inventor’s name.')).toBeTruthy();
    fireEvent.change(name, { target: { value: 'Pat Doe' } });
    expect(nextButton().disabled).toBe(false); // email and organization are optional

    const email = screen.getByLabelText(/Inventor 1 email/);
    fireEvent.change(email, { target: { value: 'not-an-email' } });
    fireEvent.blur(email);
    expect(screen.getByText(/valid email address/)).toBeTruthy();
    expect(nextButton().disabled).toBe(true);
    fireEvent.change(email, { target: { value: 'pat@contoso.example' } });
    expect(nextButton().disabled).toBe(false);

    // removing the only row brings the list error back
    fireEvent.click(screen.getByRole('button', { name: 'Remove inventor 1' }));
    expect(screen.getByText('Add at least one inventor.')).toBeTruthy();
    expect(nextButton().disabled).toBe(true);
  });

  it('removing a row does not hand its touched state to the row that shifts up (rows are keyed by id, not position)', () => {
    fillInventionStep1();
    radioIn(/Are you one of the inventors/, 'Yes');
    const add = screen.getByRole('button', { name: 'Add inventor' });
    fireEvent.click(add);
    fireEvent.click(add);
    fireEvent.blur(screen.getByLabelText(/Inventor 1 name/)); // row 1: touched and empty, so its error shows
    expect(screen.getAllByText('Enter the inventor’s name.')).toHaveLength(1);

    fireEvent.click(screen.getByRole('button', { name: 'Remove inventor 1' }));
    // The untouched, still-empty row 2 is now "Inventor 1": it is invalid but must not inherit row 1's touched state.
    expect(screen.getByLabelText(/Inventor 1 name/)).toBeTruthy();
    expect(screen.queryByLabelText(/Inventor 2 name/)).toBeNull();
    expect(screen.queryByText('Enter the inventor’s name.')).toBeNull();
    expect(nextButton().disabled).toBe(true);
  });

  it('the co-inventor id never reaches the POST body', async () => {
    fillInventionStep1();
    radioIn(/Are you one of the inventors/, 'No');
    fireEvent.click(screen.getByRole('button', { name: 'Add inventor' }));
    change(/Inventor 1 name/, 'Pat Doe');
    next();
    radioIn(/shared publicly/, 'No');
    radioIn(/outside the company, even under NDA/, 'No');
    radioIn(/used in a product or service/, 'No');
    radioIn(/developed with, or funded by/, 'No');
    next();
    tickConfirm();
    fireEvent.click(submitButton('Submit disclosure'));
    await screen.findByText('Invention disclosure submitted');
    const { body } = lastSubmit();
    expect(body.answers.coInventors).toEqual([{ name: 'Pat Doe' }]);
    expect(JSON.stringify(body)).not.toMatch(/"id"/);
  });

  it('"I am an inventor" needs no list; the list is capped at 20 inventors', () => {
    fillInventionStep1();
    radioIn(/Are you one of the inventors/, 'Yes');
    expect(nextButton().disabled).toBe(false);
    const add = screen.getByRole('button', { name: 'Add inventor' }) as HTMLButtonElement;
    for (let i = 0; i < 20; i++) fireEvent.click(add);
    expect(screen.getByLabelText(/Inventor 20 name/)).toBeTruthy();
    expect(add.disabled).toBe(true);
  });

  it('public disclosure: asks date and method; a past date is for "already", a future one for "planned"', () => {
    fillInventionStep1();
    radioIn(/Are you one of the inventors/, 'Yes');
    next();
    expect(screen.queryByLabelText(/Date \(first or planned\)/)).toBeNull();
    radioIn(/shared publicly/, 'Yes, already');
    const date = screen.getByLabelText(/Date \(first or planned\)/);
    fireEvent.change(date, { target: { value: LATER } });
    fireEvent.blur(date);
    expect(screen.getByText(/Choose a date on or before today/)).toBeTruthy();
    fireEvent.change(date, { target: { value: PAST } });
    expect(screen.queryByText(/Choose a date on or before today/)).toBeNull();

    radioIn(/shared publicly/, 'Not yet, but planned');
    fireEvent.change(screen.getByLabelText(/Date \(first or planned\)/), { target: { value: PAST } });
    fireEvent.blur(screen.getByLabelText(/Date \(first or planned\)/));
    expect(screen.getByText(/Choose a date from today/)).toBeTruthy();
    fireEvent.change(screen.getByLabelText(/Date \(first or planned\)/), { target: { value: LATER } });
    expect(screen.queryByText(/Choose a date from today/)).toBeNull();
    // the method is still outstanding
    radioIn(/outside the company, even under NDA/, 'No');
    radioIn(/used in a product or service/, 'No');
    radioIn(/developed with, or funded by/, 'No');
    expect(nextButton().disabled).toBe(true);
    change(/How\?/, 'demo');
    expect(nextButton().disabled).toBe(false);
  });

  it('shows the patent-timing warning for a disclosure already made, and for a planned one within 60 days only', () => {
    fillInventionStep1();
    radioIn(/Are you one of the inventors/, 'Yes');
    next();
    expect(screen.queryByText(/can limit or end patent protection/)).toBeNull();
    radioIn(/shared publicly/, 'Yes, already');
    expect(screen.getByText(/can limit or end patent protection/)).toBeTruthy();
    radioIn(/shared publicly/, 'Not yet, but planned');
    expect(screen.queryByText(/can limit or end patent protection/)).toBeNull(); // no date yet
    change(/Date \(first or planned\)/, LATER);
    expect(screen.queryByText(/can limit or end patent protection/)).toBeNull();
    change(/Date \(first or planned\)/, SOON);
    expect(screen.getByText(/can limit or end patent protection/)).toBeTruthy();
    radioIn(/shared publicly/, 'No');
    expect(screen.queryByText(/can limit or end patent protection/)).toBeNull();
  });

  it('conditional follow-ups: shared externally, product use and outside involvement each ask their detail', () => {
    fillInventionStep1();
    radioIn(/Are you one of the inventors/, 'Yes');
    next();
    radioIn(/shared publicly/, 'No');
    radioIn(/outside the company, even under NDA/, 'Yes');
    radioIn(/used in a product or service/, 'Planned');
    radioIn(/developed with, or funded by/, 'Yes');
    expect(nextButton().disabled).toBe(true);
    change(/With whom, and was there an NDA/, 'Supplier X, under NDA');
    change(/Product or service name/, 'Widget 2');
    change(/Who, and under what agreement/, 'University grant');
    expect(nextButton().disabled).toBe(false);
    // "Not sure" is a complete answer and asks nothing more
    radioIn(/outside the company, even under NDA/, 'Not sure');
    expect(screen.queryByLabelText(/With whom/)).toBeNull();
    radioIn(/used in a product or service/, 'No');
    expect(screen.queryByLabelText(/Product or service name/)).toBeNull();
  });

  it('the confirmation must be ticked before Submit; the summary shows the answers as labels', () => {
    fillInventionToConfirm();
    expect(submitButton('Submit disclosure').disabled).toBe(true);
    expect(screen.getByText(INV_TITLE)).toBeTruthy();
    expect(screen.getByText('Shared outside the company')).toBeTruthy();
    tickConfirm();
    expect(submitButton('Submit disclosure').disabled).toBe(false);
    tickConfirm();
    expect(submitButton('Submit disclosure').disabled).toBe(true);
    expect(screen.getByText('Confirm the statement to submit.')).toBeTruthy();
  });

  it('submits the Invention type, a short-title-only title, and the shortest answers envelope; shows the success screen', async () => {
    fillInventionToConfirm();
    tickConfirm();
    fireEvent.click(submitButton('Submit disclosure'));

    await screen.findByText('Invention disclosure submitted');
    const { path, init, body } = lastSubmit();
    expect(path).toBe(SUBMIT_PATH);
    expect((init as RequestInit).method).toBe('POST');
    expect(body).toEqual({
      requestType: 100000001,
      title: 'Invention – Improved battery cooling method',
      answers: {
        schemaVersion: 1,
        shortTitle: INV_TITLE,
        problemSolved: INV_PROBLEM,
        description: INV_DESCRIPTION,
        submitterIsInventor: true,
        publicDisclosure: 'none',
        sharedExternally: 'no',
        productUse: 'no',
        externalInvolvement: 'no',
        accuracyConfirmed: true,
      },
    });
    // technical detail never reaches the unsecured title
    expect(body.title).not.toMatch(/phase-change|overheat/);
    expect(screen.getByText(/Legal has received/)).toBeTruthy();
    expect(screen.getByText(/add drawings, slides, lab notes or a draft paper under/)).toBeTruthy();
    expect(screen.getByText(/don.t publish, present, demo or post about this invention/)).toBeTruthy();
    expect(screen.queryByText(/will prioritise accordingly/)).toBeNull();
  });

  it('the full submission carries inventors, disclosure, sharing, product and outside involvement as codes', async () => {
    change(/Short, non-confidential title/, INV_TITLE);
    change(/What problem does it solve/, INV_PROBLEM);
    change(/How does it work/, INV_DESCRIPTION);
    change(/Similar products, methods or patents/, 'Competitor patent US 1,234,567; ours differs in the layer.');
    next();
    radioIn(/Are you one of the inventors/, 'No');
    fireEvent.click(screen.getByRole('button', { name: 'Add inventor' }));
    change(/Inventor 1 name/, 'Pat Doe');
    change(/Inventor 1 email/, 'pat@contoso.example');
    change(/Inventor 1 organization/, 'Contoso Labs');
    fireEvent.click(screen.getByRole('button', { name: 'Add inventor' }));
    change(/Inventor 2 name/, 'Sam Roe');
    change(/Roughly when did you come up with it/, PAST);
    next();
    radioIn(/shared publicly/, 'Yes, already');
    change(/Date \(first or planned\)/, PAST);
    change(/How\?/, 'publication');
    change(/Where\?/, 'Journal of Thermal Design');
    radioIn(/outside the company, even under NDA/, 'Yes');
    change(/With whom, and was there an NDA/, 'Supplier X, under NDA');
    radioIn(/used in a product or service/, 'Planned');
    change(/Product or service name/, 'Widget 2');
    change(/Release date/, FUTURE);
    radioIn(/developed with, or funded by/, 'Yes');
    change(/Who, and under what agreement/, 'University grant 77');
    next();
    change(/Anything else\?/, 'Prototype exists.');
    tickConfirm();
    fireEvent.click(submitButton('Submit disclosure'));

    await screen.findByText('Invention disclosure submitted');
    expect(lastSubmit().body.answers).toEqual({
      schemaVersion: 1,
      shortTitle: INV_TITLE,
      problemSolved: INV_PROBLEM,
      description: INV_DESCRIPTION,
      knownAlternatives: 'Competitor patent US 1,234,567; ours differs in the layer.',
      submitterIsInventor: false,
      coInventors: [
        { name: 'Pat Doe', email: 'pat@contoso.example', organization: 'Contoso Labs' },
        { name: 'Sam Roe' },
      ],
      conceptionDate: PAST,
      publicDisclosure: 'done',
      disclosureDate: PAST,
      disclosureMethod: 'publication',
      disclosureVenue: 'Journal of Thermal Design',
      sharedExternally: 'yes',
      sharedWith: 'Supplier X, under NDA',
      productUse: 'planned',
      productName: 'Widget 2',
      productDate: FUTURE,
      externalInvolvement: 'yes',
      externalInvolvementDetails: 'University grant 77',
      notes: 'Prototype exists.',
      accuracyConfirmed: true,
    });
    // the success screen echoes the disclosure date
    expect(
      screen.getByText(new RegExp(`Legal has your disclosure date of ${PAST} and will prioritise accordingly`))
    ).toBeTruthy();
  });

  it('a hidden branch leaves no stale key: switching disclosure back to "No" drops the date, method and venue', async () => {
    fillInventionStep1();
    radioIn(/Are you one of the inventors/, 'Yes');
    next();
    radioIn(/shared publicly/, 'Yes, already');
    change(/Date \(first or planned\)/, PAST);
    change(/How\?/, 'demo');
    change(/Where\?/, 'Trade show');
    radioIn(/shared publicly/, 'No');
    radioIn(/outside the company, even under NDA/, 'No');
    radioIn(/used in a product or service/, 'No');
    radioIn(/developed with, or funded by/, 'No');
    next();
    tickConfirm();
    fireEvent.click(submitButton('Submit disclosure'));
    await screen.findByText('Invention disclosure submitted');
    const answers = lastSubmit().body.answers;
    for (const key of ['disclosureDate', 'disclosureMethod', 'disclosureVenue', 'coInventors', 'conceptionDate']) {
      expect(answers).not.toHaveProperty(key);
    }
    expect(Object.keys(answers).some(k => /department|requester/i.test(k))).toBe(false);
  });

  it('the success screen links to the request and to the Service Requests tab', async () => {
    fillInventionToConfirm();
    tickConfirm();
    fireEvent.click(submitButton('Submit disclosure'));
    await screen.findByText('Invention disclosure submitted');
    fireEvent.click(screen.getByRole('button', { name: 'Open request' }));
    expect(openRecord).toHaveBeenCalledWith({ entityName: 'sprk_servicerequest', recordId: CREATED_ID });
    expect(screen.queryByRole('dialog', { name: 'Invention Submission' })).toBeNull();
  });

  it('"Service Requests" on the success screen opens that tab', async () => {
    fillInventionToConfirm();
    tickConfirm();
    fireEvent.click(submitButton('Submit disclosure'));
    await screen.findByText('Invention disclosure submitted');
    fireEvent.click(screen.getByRole('button', { name: 'Service Requests' }));
    expect(onOpenWidget).toHaveBeenCalledWith('service-requests');
  });

  it('a 403 shows the server message, keeps every answer, and a retry then succeeds', async () => {
    bffApiCall.mockRejectedValueOnce(
      new ApiError(
        403,
        problem(403, {
          reasonCode: 'sdap.external.intake.invention_not_available',
          errorCode: 'sdap.external.intake.invention_not_available',
        })
      )
    );
    fillInventionToConfirm();
    tickConfirm();
    fireEvent.click(submitButton('Submit disclosure'));
    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain('not available to you');
    expect(screen.queryByText('Invention disclosure submitted')).toBeNull();
    expect(screen.getByText(INV_TITLE)).toBeTruthy();
    expect(submitButton('Submit disclosure').disabled).toBe(false);

    fireEvent.click(submitButton('Submit disclosure'));
    await screen.findByText('Invention disclosure submitted');
    expect(bffApiCall.mock.calls.filter(c => c[0] === SUBMIT_PATH)).toHaveLength(2);
  });

  it('a 401 says the session expired, not "could not be submitted", and keeps the answers', async () => {
    bffApiCall.mockRejectedValueOnce(new ApiError(401, problem(401)));
    fillInventionToConfirm();
    tickConfirm();
    fireEvent.click(submitButton('Submit disclosure'));
    const text = (await screen.findByRole('alert')).textContent ?? '';
    expect(text).toContain('Your session has expired. Sign in again, then submit.');
    expect(text).not.toContain('could not be submitted');
    expect(screen.queryByText('Invention disclosure submitted')).toBeNull();
    expect(screen.getByText(INV_TITLE)).toBeTruthy();
    expect(submitButton('Submit disclosure').disabled).toBe(false);
  });

  it('Cancel closes the wizard without submitting; re-opening starts empty', () => {
    change(/Short, non-confidential title/, INV_TITLE);
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog', { name: 'Invention Submission' })).toBeNull();
    expect(bffApiCall).not.toHaveBeenCalled();
    fireEvent.click(card(/Submit an invention disclosure/));
    expect((screen.getByLabelText(/Short, non-confidential title/) as HTMLInputElement).value).toBe('');
  });
});

// ---------------------------------------------------------------------------
// Trademark Search wizard (task 053)
// ---------------------------------------------------------------------------

const TM_GOODS = 'A cloud analytics product for retail teams.';

const checkbox = (name: string): void => {
  fireEvent.click(screen.getByRole('checkbox', { name }));
};

/** Trademark step 1, then Next. */
function fillTrademarkStep1(form = 'Name or word only'): void {
  change(/Proposed name or mark/, 'AURORA');
  radio(form);
  next();
}

/** Trademark steps 1 and 2, leaving the wizard on step 3. */
function fillTrademarkToStatus(): void {
  fillTrademarkStep1();
  change(/What will it be used for/, TM_GOODS);
  checkbox('United States');
  next();
}

describe('Trademark Search wizard', () => {
  beforeEach(() => {
    renderPane();
    fireEvent.click(card(/clearance search/));
  });

  it('step 1 requires the mark (1-200) and the kind of mark', () => {
    expect(nextButton().disabled).toBe(true);
    const mark = screen.getByLabelText(/Proposed name or mark/);
    fireEvent.blur(mark);
    expect(screen.getByText('Enter the proposed name or mark.')).toBeTruthy();
    fireEvent.change(mark, { target: { value: 'AURORA' } });
    expect(nextButton().disabled).toBe(true); // markForm missing
    radio('Slogan or tagline');
    expect(nextButton().disabled).toBe(false);
  });

  it('warns that the mark appears in the (unsecured) request title', () => {
    expect(screen.getByText(/The mark appears in the request title/)).toBeTruthy();
  });

  it('a logo (alone or with the name) tells the requester to add the file under Documents; a word or slogan does not', () => {
    expect(screen.queryByText(/add the logo or design file under/)).toBeNull();
    radio('Logo or design only');
    expect(screen.getByText(/add the logo or design file under/)).toBeTruthy();
    radio('Name with a logo');
    expect(screen.getByText(/add the logo or design file under/)).toBeTruthy();
    radio('Name or word only');
    expect(screen.queryByText(/add the logo or design file under/)).toBeNull();
    radio('Slogan or tagline');
    expect(screen.queryByText(/add the logo or design file under/)).toBeNull();
  });

  it('step 2 requires the goods/services (10+) and at least one territory; "Other" asks for text', () => {
    fillTrademarkStep1();
    expect(nextButton().disabled).toBe(true);
    change(/What will it be used for/, 'short');
    fireEvent.blur(screen.getByLabelText(/What will it be used for/));
    expect(screen.getByText(/at least 10 characters/)).toBeTruthy();
    change(/What will it be used for/, TM_GOODS);
    expect(nextButton().disabled).toBe(true); // no territory
    checkbox('Other');
    expect(nextButton().disabled).toBe(true); // other countries missing
    change(/Other countries/, 'Brazil and Mexico');
    expect(nextButton().disabled).toBe(false);
    checkbox('Other');
    expect(screen.queryByLabelText(/Other countries/)).toBeNull();
    expect(nextButton().disabled).toBe(true); // no territory left
    checkbox('Worldwide / online only');
    expect(nextButton().disabled).toBe(false);
  });

  it('offers the fixed territory list by label, "Other" last, and no trademark-class question', () => {
    fillTrademarkStep1();
    const group = screen.getByRole('group', { name: /Where will it be used/ });
    expect(within(group).getByRole('checkbox', { name: 'United States' })).toBeTruthy();
    expect(within(group).getByRole('checkbox', { name: 'European Union' })).toBeTruthy();
    expect(within(group).getAllByRole('checkbox')).toHaveLength(10);
    expect(within(group).getAllByRole('checkbox').at(-1)).toBe(within(group).getByRole('checkbox', { name: 'Other' }));
    expect(screen.queryByLabelText(/class/i)).toBeNull(); // the hint says Legal works out the classes; no field asks
  });

  it('the checkbox group is described by its hint, and by the error message (and marked invalid) once the step is tried', () => {
    fillTrademarkStep1();
    const group = (): HTMLElement => screen.getByRole('group', { name: /Where will it be used/ });
    const describedBy = (): HTMLElement[] =>
      (group().getAttribute('aria-describedby') ?? '')
        .split(' ')
        .filter(Boolean)
        .map(id => document.getElementById(id) as HTMLElement);

    // fresh: the hint only, not invalid
    expect(describedBy().map(e => e.textContent)).toEqual([
      'Where it will be sold, offered or marketed, now and within about 2 years.',
    ]);
    expect(group().getAttribute('aria-invalid')).toBeNull();

    // tick then clear: the group is touched and empty, so the message appears and comes before the hint
    checkbox('United States');
    checkbox('United States');
    expect(describedBy().map(e => e.textContent)).toEqual([
      'Choose at least one place.',
      'Where it will be sold, offered or marketed, now and within about 2 years.',
    ]);
    expect(group().getAttribute('aria-invalid')).toBe('true');

    // the checkboxes keep their own names
    expect(within(group()).getByRole('checkbox', { name: 'United States' })).toBeTruthy();
  });

  it('step 3: current use is required; "publicly" asks since-when and where; launch and need-by dates are optional', () => {
    fillTrademarkToStatus();
    expect(submitButton('Submit request').disabled).toBe(true);
    radio('Yes, publicly');
    expect(screen.getByLabelText(/Since when/)).toBeTruthy();
    expect(screen.getByLabelText(/Where is it used/)).toBeTruthy();
    expect(submitButton('Submit request').disabled).toBe(false);
    radio('Not yet');
    expect(screen.queryByLabelText(/Since when/)).toBeNull();
    expect(submitButton('Submit request').disabled).toBe(false);
  });

  it('the result date cannot be after the launch date', () => {
    fillTrademarkToStatus();
    radio('Not yet');
    change(/Planned launch or announcement date/, FUTURE);
    const needed = screen.getByLabelText(/When do you need the result/);
    fireEvent.change(needed, { target: { value: addDaysIso(todayIso(), 60) } });
    fireEvent.blur(needed);
    expect(screen.getByText(/up to the launch date/)).toBeTruthy();
    expect(submitButton('Submit request').disabled).toBe(true);
    fireEvent.change(needed, { target: { value: addDaysIso(todayIso(), 10) } });
    expect(submitButton('Submit request').disabled).toBe(false);
  });

  it('submits the Trademark type, a mark-only title, and the answers envelope; shows the success screen', async () => {
    fillTrademarkToStatus();
    radio('Not yet');
    change(/Planned launch or announcement date/, FUTURE);
    fireEvent.click(submitButton('Submit request'));

    await screen.findByText('Trademark search requested');
    const { path, init, body } = lastSubmit();
    expect(path).toBe(SUBMIT_PATH);
    expect((init as RequestInit).method).toBe('POST');
    expect(body).toEqual({
      requestType: 100000003,
      title: 'Trademark search – AURORA',
      answers: {
        schemaVersion: 1,
        proposedMark: 'AURORA',
        markForm: 'word',
        goodsServices: TM_GOODS,
        territories: ['us'],
        currentUse: 'notYet',
        launchDate: FUTURE,
      },
    });
    expect(body.title).not.toMatch(/analytics|retail/);
    expect(screen.getByText(/Legal has received your request for/)).toBeTruthy();
    expect(screen.getByText(/A quick search that finds no conflicts is not the same as clearance/)).toBeTruthy();
    expect(screen.queryByText(/add the logo file/)).toBeNull();
  });

  it('the full submission carries meaning, role, territories (incl. other), use details and notes; a logo adds the Documents step', async () => {
    fillTrademarkStep1('Name with a logo');
    // back to step 1 is not needed: add the optional meaning there
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    change(/What does it mean/, 'Latin for dawn');
    next();
    change(/What will it be used for/, TM_GOODS);
    fireEvent.change(screen.getByLabelText(/What will it name/), { target: { value: 'product' } });
    checkbox('United States');
    checkbox('European Union');
    checkbox('Other');
    change(/Other countries/, 'Brazil');
    next();
    radio('Yes, publicly');
    change(/Since when/, PAST);
    change(/Where is it used/, 'Canada, web');
    change(/Planned launch or announcement date/, LATER);
    change(/When do you need the result/, FUTURE);
    change(/Anything else Legal should know/, 'An agency proposed the name.');
    fireEvent.click(submitButton('Submit request'));

    await screen.findByText('Trademark search requested');
    expect(lastSubmit().body.answers).toEqual({
      schemaVersion: 1,
      proposedMark: 'AURORA',
      markForm: 'wordAndLogo',
      markMeaning: 'Latin for dawn',
      goodsServices: TM_GOODS,
      markRole: 'product',
      territories: ['us', 'eu', 'other'],
      territoriesOther: 'Brazil',
      currentUse: 'public',
      firstUseDate: PAST,
      useLocations: 'Canada, web',
      launchDate: LATER,
      neededBy: FUTURE,
      notes: 'An agency proposed the name.',
    });
    expect(screen.getByText(/add the logo file under/)).toBeTruthy();
  });

  it('a hidden branch leaves no stale key: "not yet" drops since-when and where; no department or requester key', async () => {
    fillTrademarkToStatus();
    radio('Yes, publicly');
    change(/Since when/, PAST);
    change(/Where is it used/, 'Canada');
    radio('Not yet');
    fireEvent.click(submitButton('Submit request'));
    await screen.findByText('Trademark search requested');
    const answers = lastSubmit().body.answers;
    expect(answers).not.toHaveProperty('firstUseDate');
    expect(answers).not.toHaveProperty('useLocations');
    expect(Object.keys(answers).some(k => /department|requester|class/i.test(k))).toBe(false);
  });

  it('"Open request" and "Service Requests" on the success screen work', async () => {
    fillTrademarkToStatus();
    radio('Not yet');
    fireEvent.click(submitButton('Submit request'));
    await screen.findByText('Trademark search requested');
    fireEvent.click(screen.getByRole('button', { name: 'Service Requests' }));
    expect(onOpenWidget).toHaveBeenCalledWith('service-requests');
  });

  it('"Open request" opens the new request in the detail view', async () => {
    fillTrademarkToStatus();
    radio('Not yet');
    fireEvent.click(submitButton('Submit request'));
    await screen.findByText('Trademark search requested');
    fireEvent.click(screen.getByRole('button', { name: 'Open request' }));
    expect(openRecord).toHaveBeenCalledWith({ entityName: 'sprk_servicerequest', recordId: CREATED_ID });
  });

  it('a 403 shows the server message and keeps every answer; a retry then succeeds', async () => {
    bffApiCall.mockRejectedValueOnce(
      new ApiError(403, problem(403, { detail: 'Your account is not linked to a contact record.' }))
    );
    fillTrademarkToStatus();
    radio('Not yet');
    fireEvent.click(submitButton('Submit request'));
    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain('not linked to a contact record');
    expect(screen.queryByText('Trademark search requested')).toBeNull();
    expect(screen.getByText('AURORA')).toBeTruthy();
    expect(submitButton('Submit request').disabled).toBe(false);
    fireEvent.click(submitButton('Submit request'));
    await screen.findByText('Trademark search requested');
    expect(bffApiCall.mock.calls.filter(c => c[0] === SUBMIT_PATH)).toHaveLength(2);
  });

  it('a 401 says the session expired and keeps the answers', async () => {
    bffApiCall.mockRejectedValueOnce(new ApiError(401, problem(401)));
    fillTrademarkToStatus();
    radio('Not yet');
    fireEvent.click(submitButton('Submit request'));
    const text = (await screen.findByRole('alert')).textContent ?? '';
    expect(text).toContain('Your session has expired. Sign in again, then submit.');
    expect(text).not.toContain('could not be submitted');
    expect(screen.getByText('AURORA')).toBeTruthy();
    expect(submitButton('Submit request').disabled).toBe(false);
  });

  it('Cancel closes the wizard without submitting; re-opening starts empty', () => {
    change(/Proposed name or mark/, 'AURORA');
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog', { name: 'Trademark Search' })).toBeNull();
    expect(bffApiCall).not.toHaveBeenCalled();
    fireEvent.click(card(/clearance search/));
    expect((screen.getByLabelText(/Proposed name or mark/) as HTMLInputElement).value).toBe('');
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
