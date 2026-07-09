/**
 * emailShareDraft.test.ts — r5 email-share #3 (2026-07-09).
 *
 * Locks the deterministic composition of the "Email Item" draft: subject + body +
 * deep link are built ONLY from the item's structured fields (name, kindLabel,
 * description, entityType, entityId) — never from narrative/display text. This is
 * the client-side analogue of the TL;DR deterministic-link rule (task 016's
 * deterministicRenderer test) applied to the shared-item email.
 */

import { buildItemEmailDraft, buildRecordDeepLink, buildEmailActivityRecord } from '../src/components/DailyBriefingApp';
import type { HighPriorityItemResult } from '../src/services/briefingService';

const CLIENT_URL = 'https://contoso.crm.dynamics.com';

function makeItem(overrides: Partial<HighPriorityItemResult> = {}): HighPriorityItemResult {
  return {
    entityType: 'sprk_matter',
    entityId: '11112222-3333-4444-5555-666677778888',
    name: 'Acme v. Beta',
    highPriority: true,
    monitor: false,
    kindLabel: 'Matter',
    description: 'Discovery deadline approaching.',
    action: 'Overdue',
    reason: 'HighPriority',
    ...overrides,
  };
}

describe('buildRecordDeepLink', () => {
  it('builds the link from entityType + entityId (structured identity), not display text', () => {
    const link = buildRecordDeepLink(CLIENT_URL, 'sprk_matter', '{ABC-123}');
    expect(link).toBe('https://contoso.crm.dynamics.com/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=ABC-123');
  });

  it('returns empty string when identity parts are missing (link omitted, never guessed)', () => {
    expect(buildRecordDeepLink(CLIENT_URL, '', 'id')).toBe('');
    expect(buildRecordDeepLink(CLIENT_URL, 'sprk_matter', '')).toBe('');
  });

  it('returns empty string when clientUrl is unknown (never emit a dead relative link)', () => {
    // getClientUrl() can throw / be absent → clientUrl is ''. A relative
    // /main.aspx link is broken in a mail client, so the link must be omitted.
    expect(buildRecordDeepLink('', 'sprk_matter', 'abc')).toBe('');
  });
});

describe('buildEmailActivityRecord', () => {
  const payload = { to: { id: 'to-user-999' }, subject: 'Matter: Acme', body: 'see link' };

  it('maps subject/body and builds From (mask 1) + To (mask 2) systemuser parties', () => {
    const record = buildEmailActivityRecord('from-user-111', payload);
    expect(record.subject).toBe('Matter: Acme');
    expect(record.description).toBe('see link');
    expect(record.email_activity_parties).toEqual([
      { 'partyid_systemuser@odata.bind': '/systemusers(from-user-111)', participationtypemask: 1 },
      { 'partyid_systemuser@odata.bind': '/systemusers(to-user-999)', participationtypemask: 2 },
    ]);
  });

  it('omits the From party when the caller systemuserid is unknown (Dataverse defaults it)', () => {
    const record = buildEmailActivityRecord('', payload);
    expect(record.email_activity_parties).toEqual([
      { 'partyid_systemuser@odata.bind': '/systemusers(to-user-999)', participationtypemask: 2 },
    ]);
  });
});

describe('buildItemEmailDraft', () => {
  it('composes subject from kindLabel + name', () => {
    const { subject } = buildItemEmailDraft(makeItem(), CLIENT_URL);
    expect(subject).toBe('Matter: Acme v. Beta');
  });

  it('embeds a deep link derived from the item entityType + entityId', () => {
    const { body } = buildItemEmailDraft(makeItem(), CLIENT_URL);
    expect(body).toContain(
      'Open the record: https://contoso.crm.dynamics.com/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=11112222-3333-4444-5555-666677778888'
    );
  });

  it('includes the structured description and the name in the body', () => {
    const { body } = buildItemEmailDraft(makeItem(), CLIENT_URL);
    expect(body).toContain('Acme v. Beta');
    expect(body).toContain('Discovery deadline approaching.');
  });

  it('never leaks narrative-only fields (action / reason) into the shared body', () => {
    const { subject, body } = buildItemEmailDraft(makeItem({ action: 'Overdue', reason: 'Both' }), CLIENT_URL);
    // action/reason are widget-render concerns, not shareable content.
    expect(body).not.toContain('Overdue');
    expect(body).not.toContain('Both');
    expect(subject).not.toContain('Overdue');
  });

  it('omits the description block and link cleanly when those fields are empty', () => {
    const { subject, body } = buildItemEmailDraft(makeItem({ description: '', entityId: '' }), CLIENT_URL);
    expect(subject).toBe('Matter: Acme v. Beta');
    expect(body).not.toContain('Open the record:');
    expect(body).not.toContain('Discovery deadline');
    expect(body).toContain('Acme v. Beta');
  });

  it('falls back to a safe subject/body when the item has no name', () => {
    const { subject, body } = buildItemEmailDraft(makeItem({ name: '', kindLabel: '' }), CLIENT_URL);
    expect(subject).toBe('Record');
    expect(body).toContain('Record');
  });
});
