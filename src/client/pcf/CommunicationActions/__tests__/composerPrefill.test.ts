/**
 * Reply/forward/draft pre-fill contract for the Actions PCF (task 044).
 * Protects the user-visible behavior: Reply goes to the sender with "Re:",
 * Forward carries the body with "Fwd:", Draft keeps the record's recipients.
 */

import { deriveComposerFields, splitRecipients } from '@spaarke/communication-components/logic/actions';

const record = {
  from: 'sender@contoso.com',
  to: 'a@x.com; b@y.com',
  cc: '',
  subject: 'Quarterly review',
  body: '<p>Original body</p>',
  sent: 'Jul 24, 2026 9:00 AM',
};

describe('deriveComposerFields', () => {
  it('reply → addresses the sender, prefixes "Re:", and quotes the original below (owner UAT 2026-07-24)', () => {
    const f = deriveComposerFields('reply', record);
    expect(f.initialTo).toEqual(['sender@contoso.com']);
    expect(f.initialSubject).toBe('Re: Quarterly review');
    // Original message loaded like a normal thread: header + quoted original, below a reply gap.
    expect(f.initialBody).toContain('<p>Original body</p>');
    expect(f.initialBody).toContain('From:');
    expect(f.initialBody).toContain('<blockquote>');
    expect(f.initialBody?.startsWith('<p></p><p></p>')).toBe(true);
  });

  it('forward → prefixes "Fwd:", quotes the original body with a header, no recipients', () => {
    const f = deriveComposerFields('forward', record);
    expect(f.initialSubject).toBe('Fwd: Quarterly review');
    expect(f.initialBody).toContain('<p>Original body</p>');
    expect(f.initialBody).toContain('From:');
    expect(f.initialBody).toContain('Subject:');
    expect(f.initialTo).toBeUndefined();
  });

  it('draft → carries the record recipients + subject + body verbatim', () => {
    const f = deriveComposerFields('draft', record);
    expect(f.initialTo).toEqual(['a@x.com', 'b@y.com']);
    expect(f.initialSubject).toBe('Quarterly review');
    expect(f.initialBody).toBe('<p>Original body</p>');
  });

  it('handles a null record without throwing (compose new)', () => {
    const f = deriveComposerFields('compose', null);
    expect(f.initialTo).toBeUndefined();
    expect(f.initialSubject).toBeUndefined();
  });

  it('reply with no subject omits the Re: prefix (no bare "Re: ")', () => {
    const f = deriveComposerFields('reply', { ...record, subject: '' });
    expect(f.initialSubject).toBeUndefined();
  });
});

describe('splitRecipients', () => {
  it('splits on ";" and "," and trims, dropping empties', () => {
    expect(splitRecipients('a@x.com; b@y.com , c@z.com;')).toEqual(['a@x.com', 'b@y.com', 'c@z.com']);
    expect(splitRecipients('')).toEqual([]);
    expect(splitRecipients(null)).toEqual([]);
  });
});
