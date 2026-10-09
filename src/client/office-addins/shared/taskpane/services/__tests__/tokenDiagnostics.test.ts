/**
 * Task 113 (provisioning request R1): the dev-only Diagnostics view reads named claims off the backend access token —
 * tid, oid, acct, idp, iss, aud — for the guest sign-in test. Display only; the token itself never leaves the module.
 */
import { decodeJwtPayload, describeTokenClaims, diagnosticsAsText } from '../tokenDiagnostics';

const b64url = (value: string): string =>
  Buffer.from(value, 'utf8').toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');

const jwt = (payload: Record<string, unknown>): string =>
  `${b64url(JSON.stringify({ alg: 'RS256', typ: 'JWT' }))}.${b64url(JSON.stringify(payload))}.signature`;

const SPAARKE_TENANT = 'a221a95e-6abc-4434-aecc-e48338a1b2f2';

describe('decodeJwtPayload', () => {
  it('decodes a base64url payload, including non-ASCII text', () => {
    expect(decodeJwtPayload(jwt({ tid: SPAARKE_TENANT, name: 'Zoë Ångström' }))).toEqual({
      tid: SPAARKE_TENANT,
      name: 'Zoë Ångström',
    });
  });

  it.each([[null], [undefined], [''], ['not-a-token'], ['a.b'], ['a.!!!.c'], [`x.${b64url('[1,2]')}.y`]])(
    'returns null for %p',
    value => {
      expect(decodeJwtPayload(value as string | null | undefined)).toBeNull();
    }
  );
});

describe('describeTokenClaims', () => {
  it('a guest token: tenant, object id, acct 1 — guest, the home idp, issuer and audience', () => {
    const rows = describeTokenClaims(
      jwt({
        tid: SPAARKE_TENANT,
        oid: 'oid-guest',
        acct: 1,
        idp: 'https://sts.windows.net/home-tenant/',
        iss: `https://login.microsoftonline.com/${SPAARKE_TENANT}/v2.0`,
        aud: 'api://1e40baad-e065-4aea-a8d4-4b7ab273458c',
      })
    );

    expect(rows.map(r => r.value)).toEqual([
      SPAARKE_TENANT,
      'oid-guest',
      '1 — guest',
      'https://sts.windows.net/home-tenant/',
      `https://login.microsoftonline.com/${SPAARKE_TENANT}/v2.0`,
      'api://1e40baad-e065-4aea-a8d4-4b7ab273458c',
    ]);
    expect(rows.map(r => r.label.split(' ')[0])).toEqual(['tid', 'oid', 'acct', 'idp', 'iss', 'aud']);
  });

  it('a member token: acct 0 — member, and no idp says "not a guest sign-in"', () => {
    const rows = describeTokenClaims(jwt({ tid: SPAARKE_TENANT, oid: 'oid-member', acct: 0 }));
    expect(rows[2]?.value).toBe('0 — member');
    expect(rows[3]?.value).toBe('(not present) — not a guest sign-in');
    expect(rows[4]?.value).toBe('(not present)');
  });

  it('an undecodable token gives every row "(not present)" rather than throwing', () => {
    const rows = describeTokenClaims('garbage');
    expect(rows).toHaveLength(6);
    expect(rows.every(r => r.value.startsWith('(not present)'))).toBe(true);
  });

  it('the copied text has the rows and never the token', () => {
    const token = jwt({ tid: SPAARKE_TENANT, oid: 'o', acct: 1 });
    const text = diagnosticsAsText(describeTokenClaims(token));
    expect(text).toContain(`tid (tenant that issued the token): ${SPAARKE_TENANT}`);
    expect(text).toContain('acct (account status): 1 — guest');
    expect(text).not.toContain(token.split('.')[1] as string);
  });
});
