/**
 * Reply/forward/draft pre-fill contract for the Actions PCF (task 044).
 * Protects the user-visible behavior: Reply goes to the sender with "Re:",
 * Forward carries the body with "Fwd:", Draft keeps the record's recipients.
 */

import { deriveComposerFields, splitRecipients } from '../CommunicationActions/composerPrefill';

const record = {
  from: 'sender@contoso.com',
  to: 'a@x.com; b@y.com',
  subject: 'Quarterly review',
  body: '<p>Original body</p>',
};

describe('deriveComposerFields', () => {
  it('reply → addresses the sender and prefixes "Re:"', () => {
    const f = deriveComposerFields('reply', record);
    expect(f.initialTo).toEqual(['sender@contoso.com']);
    expect(f.initialSubject).toBe('Re: Quarterly review');
  });

  it('forward → prefixes "Fwd:" and carries the original body, no recipients', () => {
    const f = deriveComposerFields('forward', record);
    expect(f.initialSubject).toBe('Fwd: Quarterly review');
    expect(f.initialBody).toBe('<p>Original body</p>');
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
