/**
 * Unit tests for HostAdapterFactory.
 *
 * Task 010 / FR-04 activated this factory. Before that it was dead infrastructure:
 * `registerAdapter()` had ZERO call sites, so the registry was permanently empty and `create()`
 * always threw INVALID_HOST while both task panes bypassed it with a direct `new`. Both panes now
 * register their adapter and call `createAndInitialize()`, so the factory is on the product's
 * bootstrap path and needs real coverage.
 */

import { OutlookAdapter } from '../OutlookAdapter';
import { WordAdapter } from '../WordAdapter';
import type { IHostAdapter } from '../IHostAdapter';

type FactoryModule = typeof import('../HostAdapterFactory');

/** Load a FRESH copy of the module so each test starts from an empty registry. */
function freshFactory(): FactoryModule {
  let mod!: FactoryModule;
  jest.isolateModules(() => {
    // jest.isolateModules is synchronous, so a static import cannot give each test a fresh registry.
    // require() is the documented idiom for module-state isolation here.
    // eslint-disable-next-line @typescript-eslint/no-var-requires
    mod = require('../HostAdapterFactory') as FactoryModule;
  });
  return mod;
}

function setHost(host: Office.HostType | string | undefined): void {
  (global.Office as unknown as Record<string, unknown>).context = {
    ...global.Office.context,
    host,
    requirements: { isSetSupported: jest.fn().mockReturnValue(true) },
    mailbox: { item: null },
  };
}

function setOnReady(host: Office.HostType | string): void {
  global.Office.onReady = jest.fn().mockImplementation((cb: (info: { host: Office.HostType }) => void) => {
    if (cb) cb({ host: host as Office.HostType });
    return Promise.resolve({ host: host as Office.HostType });
  });
}

describe('HostAdapterFactory', () => {
  // setHost() replaces global.Office.context wholesale. jest's clearMocks/restoreMocks do NOT restore a
  // replaced global object, so without this the next test added here would inherit whatever the previous
  // one left behind. (adr-check W-7, 2026-09-09.)
  const originalContext = global.Office.context;
  const originalOnReady = global.Office.onReady;
  afterEach(() => {
    (global.Office as unknown as Record<string, unknown>).context = originalContext;
    global.Office.onReady = originalOnReady;
  });

  describe('registry', () => {
    it('starts empty — this is the state that made create() always throw before task 010', () => {
      const { HostAdapterFactory } = freshFactory();

      expect(HostAdapterFactory.getRegisteredHosts()).toEqual([]);
      expect(HostAdapterFactory.hasAdapter('word')).toBe(false);
      expect(HostAdapterFactory.hasAdapter('outlook')).toBe(false);
    });

    // NOTE: there were once two more tests here asserting that registerAdapter('word'|'outlook', X)
    // makes getRegisteredHosts()/hasAdapter() report that host. They were REMOVED as the TypeScript
    // isomorph of ADR-038 §4's banned DI-registration test — they assert that a registration call
    // registered, which a two-line Map.set already guarantees, and they break en masse on any
    // registry refactor. Registration is transitively proven by the create() and createAndInitialize()
    // tests below, which cannot pass unless it worked. (adr-check W-1, resolved Path C, 2026-09-09.)
  });

  describe('detectHostType', () => {
    it('maps Office.context.host === Word to "word"', () => {
      const { HostAdapterFactory } = freshFactory();
      setHost(Office.HostType.Word);

      expect(HostAdapterFactory.detectHostType()).toBe('word');
    });

    it('maps Office.context.host === Outlook to "outlook"', () => {
      const { HostAdapterFactory } = freshFactory();
      setHost(Office.HostType.Outlook);

      expect(HostAdapterFactory.detectHostType()).toBe('outlook');
    });

    it('throws INVALID_HOST for a supported-by-Office but unsupported-by-us host', () => {
      const { HostAdapterFactory } = freshFactory();
      setHost(Office.HostType.Excel);

      expect(() => HostAdapterFactory.detectHostType()).toThrow(
        expect.objectContaining({ code: 'INVALID_HOST' })
      );
    });

    it('throws INVALID_HOST when Office.context.host is undefined (plan.md R-4 failure shape)', () => {
      // Documents what happens if the never-before-exercised `Office.context.host` read comes back
      // empty in a real host: a typed, renderable INVALID_HOST — NOT a silent bad adapter.
      const { HostAdapterFactory } = freshFactory();
      setHost(undefined);

      expect(() => HostAdapterFactory.detectHostType()).toThrow(
        expect.objectContaining({ code: 'INVALID_HOST' })
      );
    });
  });

  describe('create', () => {
    it('throws INVALID_HOST when no adapter is registered for the detected host', () => {
      const { HostAdapterFactory } = freshFactory();
      setHost(Office.HostType.Word);

      expect(() => HostAdapterFactory.create()).toThrow(
        expect.objectContaining({ code: 'INVALID_HOST' })
      );
    });

    it('returns the registered class, NOT initialized', () => {
      const { HostAdapterFactory } = freshFactory();
      setHost(Office.HostType.Word);
      HostAdapterFactory.registerAdapter('word', WordAdapter);

      const adapter = HostAdapterFactory.create();

      expect(adapter).toBeInstanceOf(WordAdapter);
      expect(adapter.isInitialized()).toBe(false);
    });
  });

  describe('createAndInitialize — the call both task panes now make', () => {
    it('Word: auto-detects, constructs WordAdapter, and initializes it', async () => {
      const { HostAdapterFactory } = freshFactory();
      setHost(Office.HostType.Word);
      setOnReady(Office.HostType.Word);
      HostAdapterFactory.registerAdapter('word', WordAdapter);

      const adapter = await HostAdapterFactory.createAndInitialize();

      expect(adapter).toBeInstanceOf(WordAdapter);
      expect(adapter.getHostType()).toBe('word');
      expect(adapter.isInitialized()).toBe(true);
    });

    it('Outlook: auto-detects, constructs OutlookAdapter, and initializes it', async () => {
      const { HostAdapterFactory } = freshFactory();
      setHost(Office.HostType.Outlook);
      setOnReady(Office.HostType.Outlook);
      HostAdapterFactory.registerAdapter('outlook', OutlookAdapter);

      const adapter = await HostAdapterFactory.createAndInitialize();

      expect(adapter).toBeInstanceOf(OutlookAdapter);
      expect(adapter.getHostType()).toBe('outlook');
      expect(adapter.isInitialized()).toBe(true);
    });

    it('Outlook: factory bootstrap is observably EQUIVALENT to the pre-task-010 direct construction', async () => {
      // The Outlook pane previously did `new OutlookAdapter(); await adapter.initialize();`.
      // It now does `registerAdapter(...); await createAndInitialize();`. Same class, same
      // initialized state, same host type, same capability surface — this is the regression guard
      // for escalation trigger 1.
      const { HostAdapterFactory } = freshFactory();
      setHost(Office.HostType.Outlook);
      setOnReady(Office.HostType.Outlook);

      const legacy: IHostAdapter = new OutlookAdapter();
      await legacy.initialize();

      HostAdapterFactory.registerAdapter('outlook', OutlookAdapter);
      const viaFactory = await HostAdapterFactory.createAndInitialize();

      expect(viaFactory.constructor).toBe(legacy.constructor);
      expect(viaFactory.getHostType()).toBe(legacy.getHostType());
      expect(viaFactory.getItemType()).toBe(legacy.getItemType());
      expect(viaFactory.isInitialized()).toBe(legacy.isInitialized());
      expect(viaFactory.getCapabilities()).toEqual(legacy.getCapabilities());
    });

    it('propagates INVALID_HOST rather than swallowing it when the host is unsupported', async () => {
      const { HostAdapterFactory } = freshFactory();
      setHost(Office.HostType.PowerPoint);
      await expect(HostAdapterFactory.createAndInitialize()).rejects.toMatchObject({
        code: 'INVALID_HOST',
      });
    });
  });

  describe('isHostAdapterError', () => {
    it('recognizes a HostAdapterError and rejects a plain Error', () => {
      const { isHostAdapterError } = freshFactory();

      expect(isHostAdapterError({ code: 'INVALID_HOST', message: 'x' })).toBe(true);
      expect(isHostAdapterError(new Error('x'))).toBe(false);
      expect(isHostAdapterError(null)).toBe(false);
    });
  });
});
