/**
 * AppInsightsService (#1537): initialises from the environment's runtime
 * connection string, accepts a connection string (not only a key), and keeps
 * telemetry best-effort — a failed lookup leaves the surface working with
 * telemetry off.
 */

const sdkConfigs: Array<Record<string, unknown>> = [];
const sdkTrackEvent = jest.fn();
const sdkTrackException = jest.fn();

jest.mock('@microsoft/applicationinsights-web', () => ({
  SeverityLevel: { Error: 3 },
  ApplicationInsights: jest.fn().mockImplementation((args: { config: Record<string, unknown> }) => {
    sdkConfigs.push(args.config);
    return {
      loadAppInsights: jest.fn(),
      trackEvent: sdkTrackEvent,
      trackException: sdkTrackException,
      flush: jest.fn(),
    };
  }),
}));

const CS =
  'InstrumentationKey=00000000-0000-4000-8000-000000000127;IngestionEndpoint=https://example.in.applicationinsights.azure.com/';

async function freshService() {
  jest.resetModules();
  return (await import('../AppInsightsService')).AppInsightsService;
}

describe('AppInsightsService', () => {
  let warn: jest.SpyInstance;

  beforeEach(() => {
    sdkConfigs.length = 0;
    sdkTrackEvent.mockClear();
    sdkTrackException.mockClear();
    warn = jest.spyOn(console, 'warn').mockImplementation(() => {});
  });

  afterEach(() => warn.mockRestore());

  it('initializeFromRuntime uses the provider’s connection string (as connectionString, not instrumentationKey)', async () => {
    const service = await freshService();

    await expect(service.initializeFromRuntime(async () => CS)).resolves.toBe(true);

    expect(service.isInitialized).toBe(true);
    expect(sdkConfigs).toHaveLength(1);
    expect(sdkConfigs[0].connectionString).toBe(CS);
    expect(sdkConfigs[0].instrumentationKey).toBeUndefined();
  });

  it('initialize() still accepts a bare instrumentation key', async () => {
    const service = await freshService();

    service.initialize('00000000-0000-4000-8000-000000000127');

    expect(sdkConfigs[0].instrumentationKey).toBe('00000000-0000-4000-8000-000000000127');
    expect(sdkConfigs[0].connectionString).toBeUndefined();
  });

  it.each([
    ['rejects', () => Promise.reject(new Error('Failed to fetch'))],
    ['returns ""', async () => ''],
    ['returns null', async () => null],
  ])('a provider that %s leaves telemetry off without throwing; tracking stays a safe no-op', async (_l, provider) => {
    const service = await freshService();

    const pending = service.initializeFromRuntime(provider);
    service.trackEvent('card_rendered'); // made while the lookup is in flight
    await expect(pending).resolves.toBe(false);

    expect(service.isInitialized).toBe(false);
    expect(sdkConfigs).toHaveLength(0);
    expect(() => service.trackEvent('after')).not.toThrow();
    expect(() => service.trackException(new Error('boom'))).not.toThrow();
    expect(sdkTrackEvent).not.toHaveBeenCalled();
  });

  it('holds events made while the lookup is in flight and sends them once it succeeds', async () => {
    const service = await freshService();
    let resolve!: (cs: string) => void;
    const pending = service.initializeFromRuntime(() => new Promise<string>(r => (resolve = r)));

    service.trackEvent('card_rendered', { visualType: 1 });
    service.trackException(new Error('early'), { scope: 'AppErrorBoundary' });
    expect(sdkTrackEvent).not.toHaveBeenCalled();

    resolve(CS);
    await pending;

    expect(sdkTrackEvent).toHaveBeenCalledWith({ name: 'card_rendered' }, { visualType: 1 });
    expect(sdkTrackException).toHaveBeenCalledTimes(1);
  });

  it('caps the held calls', async () => {
    const service = await freshService();
    let resolve!: (cs: string) => void;
    const pending = service.initializeFromRuntime(() => new Promise<string>(r => (resolve = r)));

    for (let i = 0; i < 80; i++) service.trackEvent(`e${i}`);
    resolve(CS);
    await pending;

    expect(sdkTrackEvent).toHaveBeenCalledTimes(50);
  });

  it('concurrent callers share one lookup; later calls are no-ops once on', async () => {
    const service = await freshService();
    const provider = jest.fn(async () => CS);

    const [a, b] = await Promise.all([
      service.initializeFromRuntime(provider),
      service.initializeFromRuntime(provider),
    ]);
    const c = await service.initializeFromRuntime(provider);

    expect([a, b, c]).toEqual([true, true, true]);
    expect(provider).toHaveBeenCalledTimes(1);
    expect(sdkConfigs).toHaveLength(1);
  });

  it('a later call retries after a failed lookup', async () => {
    const service = await freshService();

    await expect(service.initializeFromRuntime(async () => '')).resolves.toBe(false);
    await expect(service.initializeFromRuntime(async () => CS)).resolves.toBe(true);
  });
});
