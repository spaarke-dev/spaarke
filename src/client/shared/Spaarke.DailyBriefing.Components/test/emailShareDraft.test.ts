/**
 * emailShareDraft.test.ts — r5 email-share #3 (2026-07-09).
 *
 * Locks the deterministic composition of the "Email Item" draft: subject + body +
 * deep link are built ONLY from the item's structured fields (name, kindLabel,
 * description, entityType, entityId) — never from narrative/display text. This is
 * the client-side analogue of the TL;DR deterministic-link rule (task 016's
 * deterministicRenderer test) applied to the shared-item email.
 */

import { buildItemEmailDraft, buildRecordDeepLink } from '../src/components/DailyBriefingApp';
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
