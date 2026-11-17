/**
 * ebilling-r1 task 130e-2: what the rule detail shows for the firm description, read from its server-rendered markup (the suite runs in a
 * node environment, as the other page tests do; the shared `@spaarke/ui-components` is replaced by two probes so no `dist` build is needed).
 * The values a typed edit sends are tested on the pure functions in `firmDescription.test.ts`.
 */

import * as React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { proposal } from './testKit';
import type { ProposalView } from '../services/ruleSetApi';
import type { FirmRefusal } from '../services/firmDescription';

jest.mock(
  '@spaarke/ui-components',
  () => ({
    __esModule: true,
    thinScrollbarStyle: {},
    StatusBadge: (props: { label: string }) => React.createElement('span', null, props.label),
  }),
  { virtual: true }
);

import { ProposalDetailPane } from '../components/ProposalDetailPane';

function render(p: ProposalView, extra: { canWrite?: boolean; firmRefusals?: readonly FirmRefusal[]; ruleSetName?: string } = {}): string {
  return renderToStaticMarkup(
    React.createElement(ProposalDetailPane, {
      proposal: p,
      canWrite: extra.canWrite ?? true,
      editable: true,
      busy: false,
      ruleSetName: extra.ruleSetName ?? 'Acme guidelines',
      firmRefusals: extra.firmRefusals,
      onAccept: jest.fn(),
      onReject: jest.fn(),
      onSave: jest.fn(async () => undefined),
    })
  );
}

const field = (html: string): string => /<textarea[^>]*data-testid="edit-firm-description"[^>]*>[\s\S]*?<\/textarea>/.exec(html)?.[0] ?? '';
// The element's text: up to the first closing span (up to the closing of the wrapper, for the error, whose messages are nested spans).
const textOf = (html: string, testId: string): string => {
  const end = testId === 'firm-description-error' ? '</span></span>' : '</span>';
  const start = html.indexOf(`data-testid="${testId}"`);
  if (start < 0) return '';
  const from = html.indexOf('>', start) + 1;
  return html.slice(from, html.indexOf(end, from)).replace(/<[^>]+>/g, '').replace(/<!-- -->/g, '');
};

describe('the Firm description field in the rule detail', () => {
  it('is labelled "Firm description" and shows the stored text', () => {
    const html = render(proposal({ firmDescription: 'Hours above the daily limit were billed.' }));

    expect(html).toContain('Firm description');
    expect(field(html)).toContain('Hours above the daily limit were billed.');
    expect(field(html)).not.toContain('disabled');
    expect(field(html)).not.toContain('readOnly');
  });

  it('is empty when the rule has none (a null from the server)', () => {
    const html = render(proposal({ firmDescription: null }));

    expect(field(html)).toBeTruthy();
    expect(field(html)).not.toMatch(/>[^<]+<\/textarea>/);
    expect(html).toMatch(/data-testid="firm-description-count"[^>]*>0 \/ 200/);
  });

  it('counts "n / 200" in Unicode scalars and sets no maxLength (ISS-027: no silent truncation)', () => {
    const html = render(proposal({ firmDescription: '\u{1F600}'.repeat(150) }));

    expect(textOf(html, 'firm-description-count').replace(/\s/g, '')).toBe('150/200');
    expect(html.toLowerCase()).not.toContain('maxlength');
  });

  it('over the limit shows the length error under the field, not a truncated text', () => {
    const html = render(proposal({ firmDescription: 'a'.repeat(201) }));

    expect(field(html)).toContain('a'.repeat(201));
    expect(textOf(html, 'firm-description-error')).toContain('longer than 200 characters');
    expect(textOf(html, 'firm-description-count').replace(/\s/g, '')).toBe('201/200');
  });

  it('a stored text that names the rule set is flagged under the field (the rule set\'s name is passed in)', () => {
    const html = render(proposal({ firmDescription: 'Per the Acme guidelines this is refused.' }));

    expect(textOf(html, 'firm-description-error')).toContain('cannot name the rule set or the rule');
  });

  it('must-not-fire: a clean stored text shows no field error', () => {
    const html = render(proposal({ firmDescription: 'Hours above the daily limit were billed.' }));

    expect(html).not.toContain('firm-description-error');
  });

  it('carries the visible notice: firms see it, no names, no internal notes, look-alike letters not caught', () => {
    const notice = textOf(render(proposal()), 'firm-description-notice');

    expect(notice).toContain('Firms see this text when this rule rejects their invoice');
    expect(notice).toContain('Do not name the rule set or the rule');
    expect(notice).toContain('internal notes');
    expect(notice).toContain('letters from other alphabets that look the same');
  });

  it('is shown read-only to a reader without the billing administrator right', () => {
    const html = render(proposal({ firmDescription: 'Stored.' }), { canWrite: false });

    expect(field(html)).toContain('Stored.');
    expect(field(html)).toContain('disabled');
  });
});

describe('a Duplicate rule', () => {
  const duplicate = () => proposal({ ruleType: 'Duplicate', ruleBody: { type: 'Duplicate' }, firmDescription: null });

  it('shows the field read-only with the reason, and no counter or "firms see this" notice', () => {
    const html = render(duplicate());

    expect(field(html)).toContain('readOnly');
    expect(textOf(html, 'firm-description-duplicate')).toContain('A Duplicate rule has no firm description');
    expect(textOf(html, 'firm-description-duplicate')).toContain('standard duplicate wording');
    expect(html).not.toContain('firm-description-count');
    expect(html).not.toContain('firm-description-notice');
  });

  it('must-not-fire: any other rule type is editable and has no Duplicate reason', () => {
    const html = render(proposal({ ruleType: 'DailyHoursCap' }));

    expect(field(html)).not.toContain('readOnly');
    expect(html).not.toContain('firm-description-duplicate');
  });

  it('shows no field error even if the stored text were there (nothing is sent for it)', () => {
    const html = render({ ...duplicate(), firmDescription: 'Leftover' });

    expect(field(html)).toContain('Leftover');
    expect(html).not.toContain('firm-description-error');
  });

  it('with a stored text: shows it read-only with a Remove action and says it is not used (fix round 1 F3)', () => {
    const html = render({ ...duplicate(), firmDescription: 'Leftover' });

    expect(field(html)).toContain('Leftover');
    expect(field(html)).toContain('readOnly');
    expect(html).toContain('data-testid="firm-description-remove"');
    expect(textOf(html, 'firm-description-duplicate')).toContain('The text stored on this rule is not used');
    expect(html).not.toContain('firm-description-removing');
  });

  it('must-not-fire: no Remove with no stored text, or for a reader who cannot write', () => {
    expect(render(duplicate())).not.toContain('firm-description-remove');
    expect(render({ ...duplicate(), firmDescription: '   ' })).not.toContain('firm-description-remove');
    expect(render({ ...duplicate(), firmDescription: 'Leftover' }, { canWrite: false })).not.toContain('firm-description-remove');
  });
});

describe('the warning for a stored text a save would leave off (fix round 1 F6)', () => {
  const inForce = (text: string | null) => proposal({ inForce: true, state: 'Accepted', firmDescription: text });

  it('shows by the field when the rule is in force and its stored text fails the checks', () => {
    const html = render(inForce('Per the Acme guidelines, no.'));

    expect(textOf(html, 'firm-description-carry-warning')).toContain('This text no longer passes the checks');
    expect(textOf(html, 'firm-description-carry-warning')).toContain('Saving creates a new draft without it');
  });

  it('must-not-fire: a clean text, or a draft with a failing text, shows no warning', () => {
    expect(render(inForce('Clean wording.'))).not.toContain('firm-description-carry-warning');
    expect(render(inForce(null))).not.toContain('firm-description-carry-warning');
    expect(render(proposal({ inForce: false, firmDescription: 'Per the Acme guidelines, no.' }))).not.toContain('firm-description-carry-warning');
  });
});

describe('a server refusal (422) under the field', () => {
  const refusal = (reason: string, message: string): FirmRefusal => ({ reason, message });

  it('shows one message per reason code', () => {
    const html = render(proposal({ firmDescription: 'Fine text.' }), {
      firmRefusals: [
        refusal('firm_description_braces', 'The firm description cannot contain { or }.'),
        refusal('firm_description_markup', 'The firm description cannot contain < or >.'),
      ],
    });

    const error = textOf(html, 'firm-description-error');
    expect(error).toContain('cannot contain { or }');
    expect(error).toContain('cannot contain &lt; or &gt;');
  });

  it('does not repeat a message the client check already shows', () => {
    const msg = 'The firm description is longer than 200 characters.';
    const html = render(proposal({ firmDescription: 'a'.repeat(201) }), { firmRefusals: [refusal('firm_description_too_long', msg)] });

    expect(html.split(msg).length - 1).toBe(1);
  });

  it('must-not-fire: no refusal, no message', () => {
    expect(render(proposal({ firmDescription: 'Fine text.' }), { firmRefusals: [] })).not.toContain('firm-description-error');
  });
});
