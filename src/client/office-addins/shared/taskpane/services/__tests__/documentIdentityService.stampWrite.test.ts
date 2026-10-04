/**
 * `writeIdentityStampAfterSave` — the one call the pane and the ribbon make after a successful save
 * (spaarkeai-word-add-in-r1 task 089, UAT-9). Pins: capability-gated (NFR-10 — never `hostType`), canonical id
 * (ADR-044), and NON-FATAL (owner 2026-10-03: the save already succeeded, so a refused write is logged, never thrown).
 */
import { writeIdentityStampAfterSave } from '../documentIdentityService';
import type { IHostAdapter } from '@shared/adapters/IHostAdapter';

type StampAdapter = Pick<IHostAdapter, 'getCapabilities' | 'writeDocumentStamp'>;

function adapterWith(
  canWriteDocumentStamp: boolean,
  write: jest.Mock = jest.fn().mockResolvedValue('written')
): {
  adapter: StampAdapter;
  write: jest.Mock;
} {
  const adapter = {
    getCapabilities: () => ({ canWriteDocumentStamp }) as ReturnType<IHostAdapter['getCapabilities']>,
    writeDocumentStamp: write,
  };
  return { adapter, write };
}

describe('writeIdentityStampAfterSave (task 089)', () => {
  let warn: jest.SpyInstance;

  beforeEach(() => {
    warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  });

  afterEach(() => {
    warn.mockRestore();
  });

  it('writes the CANONICAL id (braces stripped, lowercased) when the host can write the stamp', async () => {
    const { adapter, write } = adapterWith(true);

    await expect(writeIdentityStampAfterSave(adapter, '{3FA85F64-5717-4562-B3FC-2C963F66AFA6}')).resolves.toBe(
      'written'
    );
    expect(write).toHaveBeenCalledWith('3fa85f64-5717-4562-b3fc-2c963f66afa6');
  });

  it("passes the adapter's 'unchanged' through (a document already carrying the id is not written to)", async () => {
    const { adapter } = adapterWith(true, jest.fn().mockResolvedValue('unchanged'));
    await expect(writeIdentityStampAfterSave(adapter, '3fa85f64-5717-4562-b3fc-2c963f66afa6')).resolves.toBe(
      'unchanged'
    );
  });

  it('attempts NOTHING when the capability is false (Outlook, or Word without CustomXmlParts)', async () => {
    const { adapter, write } = adapterWith(false);

    await expect(writeIdentityStampAfterSave(adapter, '3fa85f64-5717-4562-b3fc-2c963f66afa6')).resolves.toBe('skipped');
    expect(write).not.toHaveBeenCalled();
  });

  it('attempts nothing without an id', async () => {
    const { adapter, write } = adapterWith(true);
    await expect(writeIdentityStampAfterSave(adapter, '')).resolves.toBe('skipped');
    await expect(writeIdentityStampAfterSave(adapter, null)).resolves.toBe('skipped');
    expect(write).not.toHaveBeenCalled();
  });

  it('a refused write is logged and reported as failed — never thrown, so the save still reports success', async () => {
    const { adapter } = adapterWith(
      true,
      jest.fn().mockRejectedValue({ code: 'UNKNOWN_ERROR', message: 'add refused' })
    );

    await expect(writeIdentityStampAfterSave(adapter, '3fa85f64-5717-4562-b3fc-2c963f66afa6')).resolves.toBe('failed');
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('identity mark could not be written'), expect.anything());
  });
});
