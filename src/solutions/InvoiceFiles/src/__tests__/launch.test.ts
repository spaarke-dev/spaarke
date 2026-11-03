import { cleanId, parseLaunch } from '../services/launch';
import { INVOICE_ID } from './testKit';

describe('parseLaunch', () => {
  it('reads the form-passed id (braced, upper case) as a bare lower-case id', () => {
    expect(parseLaunch({ id: `{${INVOICE_ID.toUpperCase()}}`, typename: 'sprk_invoice' })).toEqual({ ok: true, invoiceId: INVOICE_ID });
  });

  it('accepts an invoiceId for standalone use', () => {
    expect(parseLaunch({ invoiceId: INVOICE_ID })).toEqual({ ok: true, invoiceId: INVOICE_ID });
  });

  it('an unsaved form (no id) is a prompt, not an error and not a BFF call', () => {
    expect(parseLaunch({ typename: 'sprk_invoice' })).toMatchObject({ ok: false, kind: 'unsaved' });
    expect(parseLaunch({})).toMatchObject({ ok: false, kind: 'unsaved' });
  });

  it('an id that is not a GUID is a launch error', () => {
    expect(parseLaunch({ id: 'not-a-guid' })).toMatchObject({ ok: false, kind: 'invalid' });
    expect(parseLaunch({ id: `${INVOICE_ID},${INVOICE_ID}` })).toMatchObject({ ok: false, kind: 'invalid' });
  });

  it('a record of another table is a launch error', () => {
    expect(parseLaunch({ id: INVOICE_ID, typename: 'sprk_matter' })).toMatchObject({ ok: false, kind: 'invalid' });
  });

  it('cleanId strips braces and lower-cases', () => {
    expect(cleanId(' {ABC} ')).toBe('abc');
  });
});
