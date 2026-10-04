/**
 * `WordAdapter.writeDocumentStamp()` — spaarkeai-word-add-in-r1 task 089 (UAT-9): after a save, the document OPEN in
 * Word is marked with the id it was saved as, so its next save resolves it instead of colliding with its own record.
 *
 * The Office.js data store is simulated IN MEMORY (one array of parts, shared by `getByNamespaceAsync`, `addAsync`
 * and each part's `getXmlAsync` / `deleteAsync`), so the write and the existing reader (task 051) act on the SAME
 * store — the round trip in the last block is what AC1's "a second save resolves it" rests on.
 *
 * The namespace / element literals are typed independently (not imported from the adapter), as task 051's reader
 * suite does: a test that imported the same constant would still pass if the constant drifted.
 */
import { WordAdapter } from '../WordAdapter';
import { OutlookAdapter } from '../OutlookAdapter';
import { applyStampPrecedence } from '../../taskpane/services/documentIdentityService';

const NS = 'urn:spaarke:office:document-identity:1';
const SAVED_ID = '3fa85f64-5717-4562-b3fc-2c963f66afa6';
const OTHER_ID = '11111111-1111-1111-1111-111111111111';

const stampXml = (id: string, declaration = '') =>
  `${declaration}<documentIdentity xmlns="${NS}"><documentId>${id}</documentId></documentIdentity>`;

interface StoredPart {
  xml: string;
  getXmlAsync: jest.Mock;
  deleteAsync: jest.Mock;
}

/** The namespace a part is found under — the root's default xmlns, exactly what Office keys `namespaceUri` on. */
function namespaceOf(xml: string): string | null {
  const doc = new DOMParser().parseFromString(xml, 'application/xml');
  return doc.documentElement?.namespaceURI ?? null;
}

interface Store {
  parts: StoredPart[];
  addAsync: jest.Mock;
  getByNamespaceAsync: jest.Mock;
  idsInNamespace: () => string[];
}

function installStore(initialXml: string[] = [], options: { failAdd?: boolean } = {}): Store {
  const succeeded = <T>(value: T) => ({ status: Office.AsyncResultStatus.Succeeded, value, error: null });
  const store: Store = {
    parts: [],
    addAsync: jest.fn(),
    getByNamespaceAsync: jest.fn(),
    idsInNamespace: () =>
      store.parts
        .filter(p => namespaceOf(p.xml) === NS)
        .map(p => /<documentId>([^<]*)<\/documentId>/.exec(p.xml)?.[1] ?? '?'),
  };

  const makePart = (xml: string): StoredPart => {
    const part: StoredPart = {
      xml,
      getXmlAsync: jest.fn((callback: (r: unknown) => void) => callback(succeeded(part.xml))),
      deleteAsync: jest.fn((callback: (r: unknown) => void) => {
        store.parts = store.parts.filter(p => p !== part);
        callback(succeeded(undefined));
      }),
    };
    return part;
  };

  store.parts = initialXml.map(makePart);
  store.getByNamespaceAsync.mockImplementation((namespace: string, callback: (r: unknown) => void) =>
    callback(succeeded(store.parts.filter(p => namespaceOf(p.xml) === namespace)))
  );
  store.addAsync.mockImplementation((xml: string, callback: (r: unknown) => void) => {
    if (options.failAdd) {
      callback({ status: Office.AsyncResultStatus.Failed, value: undefined, error: { message: 'add refused' } });
      return;
    }
    store.parts.push(makePart(xml));
    callback(succeeded(undefined));
  });

  (global.Office as unknown as Record<string, unknown>).context = {
    ...global.Office.context,
    document: { customXmlParts: { getByNamespaceAsync: store.getByNamespaceAsync, addAsync: store.addAsync } },
  };
  return store;
}

function setCustomXmlPartsSupported(supported: boolean): void {
  (global.Office.context.requirements.isSetSupported as jest.Mock).mockImplementation((set: string) =>
    set === 'CustomXmlParts' ? supported : true
  );
}

describe('WordAdapter.writeDocumentStamp() (task 089, UAT-9)', () => {
  let adapter: WordAdapter;

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
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  it('an UNSTAMPED document ends with exactly one part in the namespace, carrying the saved id', async () => {
    const store = installStore([
      '<b:Sources xmlns:b="http://schemas.openxmlformats.org/officeDocument/2006/bibliography"/>',
    ]);

    await expect(adapter.writeDocumentStamp(SAVED_ID)).resolves.toBe('written');

    expect(store.idsInNamespace()).toEqual([SAVED_ID]);
    expect(store.addAsync).toHaveBeenCalledTimes(1);
    const [writtenXml] = store.addAsync.mock.calls[0]!;
    // The explicit DEFAULT xmlns on the root is load-bearing (019 condition 2): without it Office cannot find the part.
    expect(writtenXml).toBe(stampXml(SAVED_ID));
    // Foreign custom XML (a bibliography) is never touched.
    expect(store.parts).toHaveLength(2);
  });

  it('a document ALREADY carrying the same id is not written to at all — no addAsync, no deleteAsync', async () => {
    // The server's own shape, as a document opened from Spaarke carries it.
    const store = installStore([stampXml(SAVED_ID, '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\r\n')]);

    await expect(adapter.writeDocumentStamp(SAVED_ID)).resolves.toBe('unchanged');

    expect(store.addAsync).not.toHaveBeenCalled();
    expect(store.parts[0]!.deleteAsync).not.toHaveBeenCalled();
    expect(store.idsInNamespace()).toEqual([SAVED_ID]);
  });

  it('a document carrying a DIFFERENT id ends with only the new id (old part deleted, new one added)', async () => {
    const store = installStore([stampXml(OTHER_ID)]);
    const oldPart = store.parts[0]!;

    await expect(adapter.writeDocumentStamp(SAVED_ID)).resolves.toBe('written');

    expect(oldPart.deleteAsync).toHaveBeenCalledTimes(1);
    expect(store.addAsync).toHaveBeenCalledTimes(1);
    expect(store.idsInNamespace()).toEqual([SAVED_ID]);
  });

  it('keeps one part when the same id appears twice, and removes a stray different or unreadable one alongside it', async () => {
    const store = installStore([
      stampXml(SAVED_ID),
      stampXml(OTHER_ID),
      stampXml(SAVED_ID),
      `<documentIdentity xmlns="${NS}"><documentId>not-a-guid</documentId></documentIdentity>`,
    ]);

    await expect(adapter.writeDocumentStamp(SAVED_ID)).resolves.toBe('written');

    expect(store.addAsync).not.toHaveBeenCalled(); // the kept part already carries the id
    expect(store.idsInNamespace()).toEqual([SAVED_ID]);
  });

  it('with CustomXmlParts unsupported, nothing is attempted and the call is refused as unsupported', async () => {
    const store = installStore();
    setCustomXmlPartsSupported(false);

    await expect(adapter.writeDocumentStamp(SAVED_ID)).rejects.toMatchObject({ code: 'CAPABILITY_NOT_SUPPORTED' });
    expect(store.getByNamespaceAsync).not.toHaveBeenCalled();
    expect(store.addAsync).not.toHaveBeenCalled();
    expect(adapter.getCapabilities().canWriteDocumentStamp).toBe(false);
  });

  it('reports canWriteDocumentStamp from the runtime CustomXmlParts check, not a hardcoded value', () => {
    setCustomXmlPartsSupported(true);
    expect(adapter.getCapabilities().canWriteDocumentStamp).toBe(true);
  });

  it('refuses an id that is not a canonical bare-lowercase GUID before touching the document', async () => {
    const store = installStore();
    await expect(adapter.writeDocumentStamp('{3FA85F64-5717-4562-B3FC-2C963F66AFA6}')).rejects.toMatchObject({
      code: 'UNKNOWN_ERROR',
    });
    expect(store.getByNamespaceAsync).not.toHaveBeenCalled();
  });

  it('a host that refuses the write rejects with a typed error carrying the host reason (the caller logs it; never silent)', async () => {
    installStore([], { failAdd: true });
    await expect(adapter.writeDocumentStamp(SAVED_ID)).rejects.toMatchObject({
      code: 'UNKNOWN_ERROR',
      message: expect.stringContaining('add refused'),
    });
  });

  it('AC1 round trip: after the write, the reader finds the id and the identity precedence resolves the document (a version target)', async () => {
    installStore();

    await adapter.writeDocumentStamp(SAVED_ID);
    const stampId = await adapter.readDocumentStamp();

    expect(stampId).toBe(SAVED_ID);
    // A local file (no cloud URL) — the next save resolves to the saved document instead of creating again.
    expect(applyStampPrecedence({ kind: 'new', reason: 'not_cloud_document' }, stampId)).toMatchObject({
      kind: 'resolved',
      documentId: SAVED_ID,
    });
  });
});

describe('OutlookAdapter — no open document, no stamp write (task 089, NFR-10)', () => {
  it('reports canWriteDocumentStamp false and refuses writeDocumentStamp as unsupported', async () => {
    const adapter = new OutlookAdapter();
    expect(adapter.getCapabilities().canWriteDocumentStamp).toBe(false);
    await expect(adapter.writeDocumentStamp(SAVED_ID)).rejects.toMatchObject({ code: 'CAPABILITY_NOT_SUPPORTED' });
  });
});
