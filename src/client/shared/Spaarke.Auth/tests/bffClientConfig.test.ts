/**
 * bffClientConfig (#1537) — the one cached client path to the BFF's anonymous
 * `/api/config/client`, now carrying the browser App Insights connection string
 * as well as the tenant (#1453).
 */

const TENANT = 'b221a95e-6abc-4434-aecc-e48338a1b2f2';
const CS =
  'InstrumentationKey=00000000-0000-4000-8000-000000000127;IngestionEndpoint=https://example.in.applicationinsights.azure.com/';
const BFF = 'https://bff.example.com';
const LS_KEY = '__spaarke_bff_tenant__';

type Reply = { status: number; body?: unknown } | 'throw';

function installFetch(reply: Reply): jest.Mock {
  const fetchMock = jest.fn(async (url: string) => {
    if (!url.endsWith('/api/config/client')) throw new Error(`unexpected fetch ${url}`);
    if (reply === 'throw') throw new TypeError('Failed to fetch');
    return {
      ok: reply.status >= 200 && reply.status < 300,
      status: reply.status,
      json: async () => reply.body,
    };
  });
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).fetch = fetchMock;
  return fetchMock;
}

async function load() {
  return import('../src/bffClientConfig');
}

describe('bffClientConfig', () => {
  let warn: jest.SpyInstance;

  beforeEach(() => {
    jest.resetModules();
    localStorage.clear();
    warn = jest.spyOn(console, 'warn').mockImplementation(() => {});
  });

  afterEach(() => {
    warn.mockRestore();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    delete (globalThis as any).fetch;
  });

  it('returns the connection string, sharing ONE request between telemetry and tenant callers', async () => {
    const fetchMock = installFetch({ status: 200, body: { tenantId: TENANT, appInsightsConnectionString: CS } });
    const { getTelemetryConnectionString, fetchBffClientTenant, clearBffClientConfigRequests } = await load();

    const [cs, tenant] = await Promise.all([getTelemetryConnectionString(`${BFF}/api`), fetchBffClientTenant(BFF)]);
    clearBffClientConfigRequests(); // a new page: only the persisted answer remains
    const again = await getTelemetryConnectionString(BFF);

    expect([cs, tenant, again]).toEqual([CS, TENANT, CS]);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('returns "" when the BFF has no connection string configured, and caches that answer', async () => {
    const fetchMock = installFetch({ status: 200, body: { tenantId: TENANT, appInsightsConnectionString: null } });
    const { getTelemetryConnectionString, clearBffClientConfigRequests } = await load();

    expect(await getTelemetryConnectionString(BFF)).toBe('');
    clearBffClientConfigRequests();
    expect(await getTelemetryConnectionString(BFF)).toBe('');
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('ignores a value that is not an App Insights connection string', async () => {
    installFetch({ status: 200, body: { tenantId: TENANT, appInsightsConnectionString: '@Microsoft.KeyVault(x)' } });
    const { getTelemetryConnectionString } = await load();

    expect(await getTelemetryConnectionString(BFF)).toBe('');
  });

  it.each<[string, Reply]>([
    ['network error', 'throw'],
    ['503', { status: 503, body: {} }],
    ['no usable tenant', { status: 200, body: { tenantId: 'organizations', appInsightsConnectionString: CS } }],
  ])('a failed lookup (%s) resolves to "" (telemetry off) and never throws', async (_label, reply) => {
    installFetch(reply);
    const { getTelemetryConnectionString, fetchBffClientTenant } = await load();

    await expect(getTelemetryConnectionString(BFF)).resolves.toBe('');
    await expect(fetchBffClientTenant(BFF)).resolves.toBe('');
    expect(localStorage.getItem(LS_KEY)).toBeNull();
  });

  it('makes no request without a usable BFF URL', async () => {
    const fetchMock = installFetch({ status: 200, body: { tenantId: TENANT } });
    const { getTelemetryConnectionString } = await load();

    expect(await getTelemetryConnectionString(undefined)).toBe('');
    expect(await getTelemetryConnectionString('not-a-url')).toBe('');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('a tenant persisted before #1537 still answers tenant lookups; telemetry fetches once and upgrades it', async () => {
    localStorage.setItem(LS_KEY, JSON.stringify({ bffBaseUrl: BFF, tenantId: TENANT, _ts: Date.now() }));
    const fetchMock = installFetch({ status: 200, body: { tenantId: TENANT, appInsightsConnectionString: CS } });
    const { getTelemetryConnectionString, fetchBffClientTenant, clearBffClientConfigRequests } = await load();

    expect(await fetchBffClientTenant(BFF)).toBe(TENANT);
    expect(fetchMock).not.toHaveBeenCalled();

    expect(await getTelemetryConnectionString(BFF)).toBe(CS);
    clearBffClientConfigRequests();
    expect(await getTelemetryConnectionString(BFF)).toBe(CS);
    expect(await fetchBffClientTenant(BFF)).toBe(TENANT);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('an answer without a connection string is re-checked after an hour (BFF deployed or configured later)', async () => {
    const fetchMock = installFetch({ status: 200, body: { tenantId: TENANT } }); // a BFF without the field
    const { getTelemetryConnectionString, fetchBffClientTenant, clearBffClientConfigRequests } = await load();
    const now = Date.now();
    const clock = jest.spyOn(Date, 'now').mockReturnValue(now);
    try {
      expect(await getTelemetryConnectionString(BFF)).toBe('');

      clearBffClientConfigRequests();
      clock.mockReturnValue(now + 59 * 60_000);
      expect(await getTelemetryConnectionString(BFF)).toBe('');
      expect(fetchMock).toHaveBeenCalledTimes(1);

      // The BFF now has a connection string; an hour later the next page picks it up.
      installFetch({ status: 200, body: { tenantId: TENANT, appInsightsConnectionString: CS } });
      clearBffClientConfigRequests();
      clock.mockReturnValue(now + 61 * 60_000);
      expect(await fetchBffClientTenant(BFF)).toBe(TENANT); // the tenant is still served from the cache
      expect(await getTelemetryConnectionString(BFF)).toBe(CS);

      // A connection string, once found, is kept for the full day.
      const fetchAfter = installFetch({ status: 503, body: {} });
      clearBffClientConfigRequests();
      clock.mockReturnValue(now + 61 * 60_000 + 23 * 60 * 60_000);
      expect(await getTelemetryConnectionString(BFF)).toBe(CS);
      expect(fetchAfter).not.toHaveBeenCalled();
    } finally {
      clock.mockRestore();
    }
  });

  it('does not use an answer persisted for a different BFF host', async () => {
    localStorage.setItem(
      LS_KEY,
      JSON.stringify({
        bffBaseUrl: 'https://other.example.com',
        tenantId: TENANT,
        appInsightsConnectionString: CS,
        _ts: Date.now(),
      })
    );
    const fetchMock = installFetch({ status: 200, body: { tenantId: TENANT, appInsightsConnectionString: null } });
    const { getTelemetryConnectionString } = await load();

    expect(await getTelemetryConnectionString(BFF)).toBe('');
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});
