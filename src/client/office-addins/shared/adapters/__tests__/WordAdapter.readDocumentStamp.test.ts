/**
 * Unit tests for `WordAdapter.readDocumentStamp()` — spaarkeai-word-add-in-r1 task 051 (FR-02 client
 * half: read the custom XML stamp task 014 writes server-side).
 *
 * A NEW test file (ADR-038: new tests in new files). `WordAdapter.test.ts` is GATED and must stay
 * green — this file is scoped narrowly to the stamp reader and the `checkRequirementSet` extension,
 * so it never touches that file's mocks or assertions.
 *
 * The namespace/root/element strings below are an INDEPENDENTLY-typed literal (not imported from the
 * adapter under test) — matching the exact contract task 014 wrote
 * (`projects/spaarkeai-word-add-in-r1/notes/014-xml-part-stamp-decisions.md` §13.3:
 * `OfficeDocumentStamp.StampNamespace` / `.StampRootElement` / `.StampIdElement`). Using a literal
 * here, rather than an imported constant, is what actually PINS the cross-language client/server
 * string contract: if `WordAdapter.ts`'s mirror of those three strings ever drifted, a test that
 * imported the SAME (now-wrong) constant would still pass.
 */
import { WordAdapter } from '../WordAdapter';

const STAMP_NAMESPACE = 'urn:spaarke:office:document-identity:1';
const STAMP_ROOT_ELEMENT = 'documentIdentity';
const STAMP_ID_ELEMENT = 'documentId';

const VALID_ID = '3fa85f64-5717-4562-b3fc-2c963f66afa6';
const OTHER_VALID_ID = '11111111-1111-1111-1111-111111111111';

function stampXml(id: string): string {
  return (
    '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\r\n' +
    `<${STAMP_ROOT_ELEMENT} xmlns="${STAMP_NAMESPACE}"><${STAMP_ID_ELEMENT}>${id}</${STAMP_ID_ELEMENT}></${STAMP_ROOT_ELEMENT}>`
  );
}

/** A foreign custom XML part in a DIFFERENT namespace — e.g. Word's own bibliography data store. */
const FOREIGN_XML =
  '<?xml version="1.0"?><b:Sources xmlns:b="http://schemas.openxmlformats.org/officeDocument/2006/bibliography"/>';

interface MockPart {
  getXmlAsync: jest.Mock;
}

function mockPart(xml: string, options: { fail?: boolean } = {}): MockPart {
  return {
    getXmlAsync: jest.fn((callback: (result: unknown) => void) => {
      if (options.fail) {
        callback({
          status: Office.AsyncResultStatus.Failed,
          error: { message: 'Mocked getXmlAsync failure.' },
          value: undefined,
        });
        return;
      }
      callback({ status: Office.AsyncResultStatus.Succeeded, value: xml, error: null });
    }),
  };
}

/**
 * Wire `Office.context.document.customXmlParts.getByNamespaceAsync` to return the given parts for a
 * namespace, and record every namespace it was actually called with (so a test can assert the reader
 * is NEVER called — task 051 AC5). Local to this file, not the shared `office-js.ts` mock: a narrow,
 * single-capability helper kept out of a file several OTHER gated suites also depend on.
 *
 * Like `setupWordCompressedFile` (`shared/__mocks__/office-js.ts`), this replaces
 * `global.Office.context.document` — it preserves whatever `requirements` the caller already set by
 * spreading the CURRENT `global.Office.context` first, so call `isSetSupported` overrides before this.
 */
function setupCustomXmlParts(partsByNamespace: Record<string, MockPart[]>): { requestedNamespaces: string[] } {
  const handle = { requestedNamespaces: [] as string[] };
  const getByNamespaceAsync = jest.fn((namespace: string, callback: (result: unknown) => void) => {
    handle.requestedNamespaces.push(namespace);
    const parts = partsByNamespace[namespace] ?? [];
    callback({ status: Office.AsyncResultStatus.Succeeded, value: parts, error: null });
  });

  (global.Office as unknown as Record<string, unknown>).context = {
    ...global.Office.context,
    document: { customXmlParts: { getByNamespaceAsync } },
  };

  return handle;
}

describe('WordAdapter.readDocumentStamp() (FR-02 client half / task 051)', () => {
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

  describe('the happy path', () => {
    it('reads a single valid stamp and returns the id', async () => {
      setupCustomXmlParts({ [STAMP_NAMESPACE]: [mockPart(stampXml(VALID_ID))] });

      await expect(adapter.readDocumentStamp()).resolves.toBe(VALID_ID);
    });

    it('requests the EXACT namespace task 014 writes to', async () => {
      const handle = setupCustomXmlParts({});

      await adapter.readDocumentStamp();

      expect(handle.requestedNamespaces).toEqual([STAMP_NAMESPACE]);
    });

    it('normalizes a braced, uppercase id to bare lowercase (shape validation — the caller still runs this through the canonical cleanGuid per ADR-044)', async () => {
      setupCustomXmlParts({
        [STAMP_NAMESPACE]: [mockPart(stampXml('{3FA85F64-5717-4562-B3FC-2C963F66AFA6}'))],
      });

      await expect(adapter.readDocumentStamp()).resolves.toBe(VALID_ID);
    });

    it('two parts that AGREE (redundant, not disagreeing) resolve to the shared id', async () => {
      setupCustomXmlParts({
        [STAMP_NAMESPACE]: [mockPart(stampXml(VALID_ID)), mockPart(stampXml(VALID_ID))],
      });

      await expect(adapter.readDocumentStamp()).resolves.toBe(VALID_ID);
    });
  });

  describe('the null path — every failure mode is a normal, recoverable state (019 condition 3), never a throw', () => {
    it('no matching part at all (absent stamp)', async () => {
      setupCustomXmlParts({});

      await expect(adapter.readDocumentStamp()).resolves.toBeNull();
    });

    it('a foreign custom XML part in a different namespace (a bibliography, Google Docs data, SharePoint columns) is ignored', async () => {
      setupCustomXmlParts({ [STAMP_NAMESPACE]: [mockPart(FOREIGN_XML)] });

      await expect(adapter.readDocumentStamp()).resolves.toBeNull();
    });

    it('malformed XML (unparseable)', async () => {
      setupCustomXmlParts({ [STAMP_NAMESPACE]: [mockPart('<documentIdentity><documentId>not closed')] });

      await expect(adapter.readDocumentStamp()).resolves.toBeNull();
    });

    it('well-formed XML whose documentId text is not GUID-shaped', async () => {
      setupCustomXmlParts({
        [STAMP_NAMESPACE]: [
          mockPart(
            `<${STAMP_ROOT_ELEMENT} xmlns="${STAMP_NAMESPACE}"><${STAMP_ID_ELEMENT}>not-a-guid</${STAMP_ID_ELEMENT}></${STAMP_ROOT_ELEMENT}>`
          ),
        ],
      });

      await expect(adapter.readDocumentStamp()).resolves.toBeNull();
    });

    it('the empty GUID (00000000-...) is rejected, not treated as a valid id', async () => {
      setupCustomXmlParts({ [STAMP_NAMESPACE]: [mockPart(stampXml('00000000-0000-0000-0000-000000000000'))] });

      await expect(adapter.readDocumentStamp()).resolves.toBeNull();
    });

    it('TWO parts carrying DISAGREEING ids — "no answer is better than the wrong one" (mirrors the server TryReadStamp)', async () => {
      setupCustomXmlParts({
        [STAMP_NAMESPACE]: [mockPart(stampXml(VALID_ID)), mockPart(stampXml(OTHER_VALID_ID))],
      });

      await expect(adapter.readDocumentStamp()).resolves.toBeNull();
    });

    it('getByNamespaceAsync itself reports Failed', async () => {
      (global.Office as unknown as Record<string, unknown>).context = {
        ...global.Office.context,
        document: {
          customXmlParts: {
            getByNamespaceAsync: jest.fn((_ns: string, callback: (result: unknown) => void) => {
              callback({ status: Office.AsyncResultStatus.Failed, error: { message: 'boom' }, value: undefined });
            }),
          },
        },
      };

      await expect(adapter.readDocumentStamp()).resolves.toBeNull();
    });

    it('getXmlAsync reports Failed for the one matching part', async () => {
      setupCustomXmlParts({ [STAMP_NAMESPACE]: [mockPart('', { fail: true })] });

      await expect(adapter.readDocumentStamp()).resolves.toBeNull();
    });

    it('never throws even when Office.context.document.customXmlParts is entirely absent', async () => {
      (global.Office as unknown as Record<string, unknown>).context = {
        ...global.Office.context,
        document: {}, // no customXmlParts at all
      };

      await expect(adapter.readDocumentStamp()).resolves.toBeNull();
    });
  });

  describe('capability + runtime guard (019 condition 4 — extends WordAdapter.checkRequirementSet rather than adding a second helper, root CLAUDE.md §11)', () => {
    it('canReadDocumentStamp is true when CustomXmlParts is supported', () => {
      global.Office.context.requirements.isSetSupported = jest.fn().mockReturnValue(true);

      expect(adapter.getCapabilities().canReadDocumentStamp).toBe(true);
    });

    it('canReadDocumentStamp is false when CustomXmlParts is NOT supported, without affecting WordApi-gated flags', () => {
      global.Office.context.requirements.isSetSupported = jest.fn((set: string) => set !== 'CustomXmlParts');

      const capabilities = adapter.getCapabilities();

      expect(capabilities.canReadDocumentStamp).toBe(false);
      expect(capabilities.canGetDocumentContent).toBe(true);
      expect(capabilities.canGetDocumentUrl).toBe(true);
      expect(capabilities.canInsertLink).toBe(true);
    });

    it('checks the requirement set with the BARE one-argument call — CustomXmlParts is unversioned (019 §6 condition 4)', () => {
      const isSetSupported = jest.fn().mockReturnValue(true);
      global.Office.context.requirements.isSetSupported = isSetSupported;

      adapter.getCapabilities();

      const customXmlCalls = isSetSupported.mock.calls.filter(call => call[0] === 'CustomXmlParts');
      expect(customXmlCalls).toEqual([['CustomXmlParts']]);
    });

    it('AC5: the reader is NEVER called when the requirement set is unsupported — getByNamespaceAsync is not invoked, and saving is unaffected (resolves null, does not throw)', async () => {
      global.Office.context.requirements.isSetSupported = jest.fn().mockReturnValue(false);
      const handle = setupCustomXmlParts({ [STAMP_NAMESPACE]: [mockPart(stampXml(VALID_ID))] });

      await expect(adapter.readDocumentStamp()).resolves.toBeNull();
      expect(handle.requestedNamespaces).toEqual([]);
    });
  });

  describe('error handling', () => {
    it('throws NOT_INITIALIZED when called before initialize(), matching getDocumentUrl()', async () => {
      const uninitializedAdapter = new WordAdapter();

      await expect(uninitializedAdapter.readDocumentStamp()).rejects.toMatchObject({
        code: 'NOT_INITIALIZED',
      });
    });
  });
});
