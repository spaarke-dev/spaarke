/**
 * `WordAdapter.registerDocumentChangeHandler()` — spaarkeai-word-add-in-r1 task 094 (owner, 2026-10-04:
 * "Re-enable on document edits"). Registers `Word.Document.onParagraphAdded` / `onParagraphChanged` /
 * `onParagraphDeleted` (WordApi 1.6, verified GA on Microsoft Learn 2026-10-04 — see notes/094), behind
 * ONE wrapped callback, and returns a promise-resolved unsubscribe function that removes all three.
 *
 * Also pins the "own write never triggers detection" rule (AC3): `writeDocumentStamp` (task 089) sets an
 * internal suppression flag for the duration of its custom-XML-part write, and the registered handler
 * checks it before invoking the caller's `onChange` — simulated here by firing the stored handler
 * SYNCHRONOUSLY from inside the mocked `addAsync`/`deleteAsync` calls, i.e. mid-write.
 *
 * Registration uses `Word.run(callback)` (the single-argument form); removal does NOT go back through
 * `Word.run` at all (Word.run's typings have no context-accepting overload, unlike Excel/Visio's) —
 * it calls `handler.remove()` then `context.sync()` directly on the SAME `RequestContext` the handlers
 * were added under, which is the Office.js rule the Microsoft Learn sample's `Word.run(context, ...)`
 * form exists to satisfy, achieved here by a more direct route.
 */
import { WordAdapter } from '../WordAdapter';
import { OutlookAdapter } from '../OutlookAdapter';

type Handler = (args: unknown) => Promise<void>;

interface FakeEventHandlers {
  add: jest.Mock;
  handlers: Handler[];
}

function makeEventHandlers(context: unknown): FakeEventHandlers {
  const fake: FakeEventHandlers = { handlers: [], add: jest.fn() };
  fake.add.mockImplementation((handler: Handler) => {
    fake.handlers.push(handler);
    const result = {
      context,
      remove: jest.fn(() => {
        fake.handlers = fake.handlers.filter(h => h !== handler);
      }),
    };
    return result;
  });
  return fake;
}

interface StoredPart {
  xml: string;
  getXmlAsync: jest.Mock;
  deleteAsync: jest.Mock;
}

/** Minimal in-memory customXmlParts store (same shape as WordAdapter.writeDocumentStamp.test.ts). */
function installCustomXmlStore(onWrite?: () => void): {
  addAsync: jest.Mock;
  getByNamespaceAsync: jest.Mock;
  parts: StoredPart[];
} {
  const succeeded = <T>(value: T) => ({ status: Office.AsyncResultStatus.Succeeded, value, error: null });
  const store = { parts: [] as StoredPart[], addAsync: jest.fn(), getByNamespaceAsync: jest.fn() };
  store.getByNamespaceAsync.mockImplementation((_ns: string, callback: (r: unknown) => void) => {
    onWrite?.();
    callback(succeeded(store.parts));
  });
  store.addAsync.mockImplementation((xml: string, callback: (r: unknown) => void) => {
    onWrite?.();
    const part: StoredPart = {
      xml,
      getXmlAsync: jest.fn((cb: (r: unknown) => void) => cb(succeeded(xml))),
      deleteAsync: jest.fn((cb: (r: unknown) => void) => cb(succeeded(undefined))),
    };
    store.parts.push(part);
    callback(succeeded(undefined));
  });
  return store;
}

describe('WordAdapter.registerDocumentChangeHandler() (task 094)', () => {
  let adapter: WordAdapter;
  let documentEvents: {
    onParagraphAdded: FakeEventHandlers;
    onParagraphChanged: FakeEventHandlers;
    onParagraphDeleted: FakeEventHandlers;
  };
  let wordContext: Record<string, unknown>;
  let customXmlStore: ReturnType<typeof installCustomXmlStore>;

  beforeEach(async () => {
    adapter = new WordAdapter();

    (global.Office as unknown as Record<string, unknown>).context = {
      ...global.Office.context,
      document: {},
      requirements: { isSetSupported: jest.fn().mockReturnValue(true) },
    };
    global.Office.onReady = jest.fn().mockImplementation((callback: (info: { host: Office.HostType }) => void) => {
      callback({ host: Office.HostType.Word });
      return Promise.resolve({ host: Office.HostType.Word });
    });
    await adapter.initialize();

    customXmlStore = installCustomXmlStore();
    wordContext = {
      document: {
        onParagraphAdded: undefined,
        onParagraphChanged: undefined,
        onParagraphDeleted: undefined,
        customXmlParts: { getByNamespaceAsync: customXmlStore.getByNamespaceAsync, addAsync: customXmlStore.addAsync },
      },
      sync: jest.fn().mockResolvedValue(undefined),
    };
    documentEvents = {
      onParagraphAdded: makeEventHandlers(wordContext),
      onParagraphChanged: makeEventHandlers(wordContext),
      onParagraphDeleted: makeEventHandlers(wordContext),
    };
    (wordContext.document as Record<string, unknown>).onParagraphAdded = documentEvents.onParagraphAdded;
    (wordContext.document as Record<string, unknown>).onParagraphChanged = documentEvents.onParagraphChanged;
    (wordContext.document as Record<string, unknown>).onParagraphDeleted = documentEvents.onParagraphDeleted;
    // Office.context.document also needs customXmlParts directly (WordAdapter reads it off
    // `Office.context.document`, not off the Word.run RequestContext, for writeDocumentStamp/readDocumentStamp).
    (global.Office.context as unknown as { document: unknown }).document = {
      customXmlParts: { getByNamespaceAsync: customXmlStore.getByNamespaceAsync, addAsync: customXmlStore.addAsync },
    };

    (global as unknown as Record<string, unknown>).Word = {
      run: jest.fn(async (callback: (context: unknown) => Promise<unknown>) => callback(wordContext)),
    };
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  it('registers all three paragraph events behind one wrapped handler', async () => {
    await adapter.registerDocumentChangeHandler(jest.fn());

    expect(documentEvents.onParagraphAdded.add).toHaveBeenCalledTimes(1);
    expect(documentEvents.onParagraphChanged.add).toHaveBeenCalledTimes(1);
    expect(documentEvents.onParagraphDeleted.add).toHaveBeenCalledTimes(1);
  });

  it('invokes onChange when a registered paragraph event fires', async () => {
    const onChange = jest.fn();
    await adapter.registerDocumentChangeHandler(onChange);

    await documentEvents.onParagraphChanged.handlers[0]!({});

    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it('the unsubscribe function removes all three handlers', async () => {
    const onChange = jest.fn();
    const unsubscribe = await adapter.registerDocumentChangeHandler(onChange);

    unsubscribe();
    await Promise.resolve(); // the removal's Word.run is a fire-and-forget promise

    expect(documentEvents.onParagraphAdded.handlers).toHaveLength(0);
    expect(documentEvents.onParagraphChanged.handlers).toHaveLength(0);
    expect(documentEvents.onParagraphDeleted.handlers).toHaveLength(0);
  });

  it('is refused as unsupported when WordApi 1.6 is not available, without registering anything', async () => {
    (global.Office.context.requirements.isSetSupported as jest.Mock).mockImplementation(
      (set: string, version?: string) => !(set === 'WordApi' && version === '1.6')
    );

    await expect(adapter.registerDocumentChangeHandler(jest.fn())).rejects.toMatchObject({
      code: 'CAPABILITY_NOT_SUPPORTED',
    });
    expect(documentEvents.onParagraphAdded.add).not.toHaveBeenCalled();
  });

  // ── AC3: the pane's own identity-mark write never fires the re-enable trigger ──────────────────────
  it('a change event fired mid-writeDocumentStamp is suppressed — onChange is not invoked', async () => {
    const onChange = jest.fn();
    await adapter.registerDocumentChangeHandler(onChange);
    const fireDuringWrite = (): void => {
      documentEvents.onParagraphChanged.handlers.forEach(h => void h({}));
    };
    // Make the customXmlParts write itself fire a (hypothetical) change event synchronously, mid-call —
    // the worst case the suppression flag must cover.
    customXmlStore.addAsync.mockImplementationOnce((xml: string, callback: (r: unknown) => void) => {
      fireDuringWrite();
      const part: StoredPart = {
        xml,
        getXmlAsync: jest.fn((cb: (r: unknown) => void) =>
          cb({ status: Office.AsyncResultStatus.Succeeded, value: xml, error: null })
        ),
        deleteAsync: jest.fn((cb: (r: unknown) => void) =>
          cb({ status: Office.AsyncResultStatus.Succeeded, value: undefined, error: null })
        ),
      };
      customXmlStore.parts.push(part);
      callback({ status: Office.AsyncResultStatus.Succeeded, value: undefined, error: null });
    });

    await adapter.writeDocumentStamp('3fa85f64-5717-4562-b3fc-2c963f66afa6');

    expect(onChange).not.toHaveBeenCalled();
  });

  it('a change event fired AFTER writeDocumentStamp resolves is NOT suppressed', async () => {
    const onChange = jest.fn();
    await adapter.registerDocumentChangeHandler(onChange);

    await adapter.writeDocumentStamp('3fa85f64-5717-4562-b3fc-2c963f66afa6');
    await documentEvents.onParagraphChanged.handlers[0]!({});

    expect(onChange).toHaveBeenCalledTimes(1);
  });
});

describe('OutlookAdapter — no open document, no change detection (task 094, NFR-10)', () => {
  it('reports canDetectDocumentChanges false and refuses registerDocumentChangeHandler as unsupported', async () => {
    const adapter = new OutlookAdapter();
    expect(adapter.getCapabilities().canDetectDocumentChanges).toBe(false);
    await expect(adapter.registerDocumentChangeHandler(jest.fn())).rejects.toMatchObject({
      code: 'CAPABILITY_NOT_SUPPORTED',
    });
  });
});
