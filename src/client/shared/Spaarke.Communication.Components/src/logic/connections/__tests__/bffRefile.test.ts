/**
 * bffRefile.test.ts — the BFF base URL is read when a re-file is SENT, not when it is built.
 *
 * Owner UAT 2026-10-11: the Reconciliation code page built its refile in a `useMemo` on the first render, by calling its
 * runtime-config getter. The config is set later, by the auth bootstrap, so the getter threw ("Runtime config not
 * initialized") and the page never loaded. A host now passes the getter itself.
 *
 * The real `bffRefile` and `updateChildRecordViaBff` run; only the authenticated fetch is a stub (ADR-038).
 */
import { bffRefile } from '../ConnectionsWriteHandler';

const GUID = '11111111-1111-1111-1111-111111111111';

function okFetch() {
  return jest.fn().mockResolvedValue({ ok: true, status: 204, json: async () => ({}) });
}

describe('bffRefile — base URL resolution', () => {
  it('does not read a getter while the refile is being built', () => {
    const getBaseUrl = jest.fn(() => {
      throw new Error('[CommunicationReconciliation] Runtime config not initialized.');
    });

    expect(() => bffRefile(okFetch(), getBaseUrl)).not.toThrow();
    expect(getBaseUrl).not.toHaveBeenCalled();
  });

  it('reads the getter when the write is sent, so a config set after the first render is used', async () => {
    let configured: string | null = null;
    const getBaseUrl = jest.fn(() => {
      if (!configured) throw new Error('Runtime config not initialized.');
      return configured;
    });
    const fetch = okFetch();
    const refile = bffRefile(fetch, getBaseUrl);

    configured = 'https://bff.example';
    await refile('sprk_communication', GUID, { sprk_regardingmatter: null });

    expect(getBaseUrl).toHaveBeenCalledTimes(1);
    expect(fetch.mock.calls[0][0]).toMatch(/^https:\/\/bff\.example\/api\/communications\//);
    expect(fetch.mock.calls[0][1]).toMatchObject({ method: 'PATCH' });
  });

  it('still takes a plain string base URL', async () => {
    const fetch = okFetch();

    await bffRefile(fetch, 'https://bff.example')('sprk_communication', GUID, {});

    expect(fetch.mock.calls[0][0]).toMatch(/^https:\/\/bff\.example\/api\/communications\//);
  });
});
