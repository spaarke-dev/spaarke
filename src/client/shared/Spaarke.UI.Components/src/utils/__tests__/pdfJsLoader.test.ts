/**
 * pdfJsLoader tests (ebilling-r1 task 108) — lazy load, worker default, host-preset worker kept, failed load not cached.
 */

const workerOptions: { workerSrc?: string } = {};
let importShouldFail = false;
let factoryCalls = 0;

jest.mock(
  'pdfjs-dist',
  () => {
    factoryCalls += 1;
    if (importShouldFail) throw new Error('chunk load failed');
    return {
      __esModule: true,
      version: '9.9.9',
      getDocument: jest.fn(),
      GlobalWorkerOptions: workerOptions,
    };
  },
  { virtual: true }
);

describe('pdfJsLoader', () => {
  beforeEach(() => {
    jest.resetModules();
    workerOptions.workerSrc = '';
    importShouldFail = false;
    factoryCalls = 0;
  });

  it('does not import pdfjs-dist until loadPdfJs is called (NFR-12)', async () => {
    await import('../pdfJsLoader');
    expect(factoryCalls).toBe(0);
  });

  it('sets a versioned CDN worker source when none is preset', async () => {
    const { loadPdfJs } = await import('../pdfJsLoader');
    await loadPdfJs();
    expect(workerOptions.workerSrc).toBe('https://cdn.jsdelivr.net/npm/pdfjs-dist@9.9.9/build/pdf.worker.min.mjs');
  });

  it('keeps a worker source the host preset', async () => {
    workerOptions.workerSrc = '/webresources/pdf.worker.min.mjs';
    const { loadPdfJs } = await import('../pdfJsLoader');
    await loadPdfJs();
    expect(workerOptions.workerSrc).toBe('/webresources/pdf.worker.min.mjs');
  });

  it('loads the library once', async () => {
    const { loadPdfJs } = await import('../pdfJsLoader');
    const a = await loadPdfJs();
    const b = await loadPdfJs();
    expect(a).toBe(b);
    expect(factoryCalls).toBe(1);
  });

  it('does not cache a failed load, so a later call retries', async () => {
    importShouldFail = true;
    const { loadPdfJs } = await import('../pdfJsLoader');
    await expect(loadPdfJs()).rejects.toThrow('chunk load failed');

    importShouldFail = false;
    await expect(loadPdfJs()).resolves.toBeDefined();
    expect(factoryCalls).toBe(2);
  });
});
