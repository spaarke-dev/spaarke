/**
 * csvExport.test — unit tests for the RFC 4180 CSV exporter (FR-DG-17).
 *
 * Covers the four acceptance criteria from task 008:
 *  1. RFC 4180 quoting — commas, double quotes, and newlines inside cell values.
 *  2. UTF-8 BOM presence (`U+FEFF`) as the first character of the blob body.
 *  3. Filename format `{entityName}-{savedQueryName}-{yyyymmdd}.csv`.
 *  4. Edge cases — empty rows, null/undefined, Lookup `{name}` projection, Date.
 *
 * @see csvExport.ts
 */

// Node's built-in Blob is needed because jsdom's Blob (in this project's jest
// preset) is a stub that lacks `.arrayBuffer()` / `.text()` / `.stream()` — so
// we override the global before importing the SUT. Node 18+ exposes a
// spec-compliant `Blob` from `node:buffer`; using it lets the production code
// (which calls `new Blob([...])`) round-trip bytes including the leading BOM
// in test, while still constructing the *same* Blob shape at runtime in the
// browser (where the platform Blob obviously already works). TextDecoder
// likewise needs the Node implementation from `node:util` because jsdom's
// preset does not expose one in the test global scope.
//
// These shims have zero impact on production behavior — they only swap the
// references jsdom installs onto `globalThis` during this test file.
// eslint-disable-next-line @typescript-eslint/no-var-requires
const { Blob: NodeBlob } = require('node:buffer');
// eslint-disable-next-line @typescript-eslint/no-var-requires
const { TextDecoder: NodeTextDecoder } = require('node:util');
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(globalThis as any).Blob = NodeBlob;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(globalThis as any).TextDecoder = NodeTextDecoder;

import { asSpreadsheetText, exportCsv, escapeCsvField, csvFilename, formatYyyymmdd, UTF8_BOM } from '../csvExport';
import type { ResolvedColumn } from '../../configResolution';

// ─────────────────────────────────────────────────────────────────────────────
// Fixtures
// ─────────────────────────────────────────────────────────────────────────────

const columns: ResolvedColumn[] = [
  {
    name: 'name',
    label: 'Name',
    width: 200,
    renderer: 'default',
    align: 'left',
    hidden: false,
    isPrimaryName: true,
  },
  {
    name: 'status',
    label: 'Status',
    width: 120,
    renderer: 'badge',
    align: 'left',
    hidden: false,
    isPrimaryName: false,
  },
  {
    name: 'amount',
    label: 'Amount',
    width: 100,
    renderer: 'currency',
    align: 'right',
    hidden: false,
    isPrimaryName: false,
  },
];

// Helper: extract the body string from the test Blob.
//
// With Node's `node:buffer.Blob` swapped in above, `.arrayBuffer()` is reliable.
// We use `TextDecoder({ ignoreBOM: true })` so the BOM byte survives — without
// that flag, TextDecoder silently strips a leading BOM and our BOM-presence
// assertion would always fail.
async function readBlobAsText(blob: Blob): Promise<string> {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const buf = await (blob as any).arrayBuffer();
  const decoder = new TextDecoder('utf-8', { ignoreBOM: true });
  return decoder.decode(buf);
}

// ─────────────────────────────────────────────────────────────────────────────
// escapeCsvField — direct unit tests
// ─────────────────────────────────────────────────────────────────────────────

describe('escapeCsvField — RFC 4180 quoting', () => {
  it('returns empty string for null / undefined', () => {
    expect(escapeCsvField(null)).toBe('');
    expect(escapeCsvField(undefined)).toBe('');
  });

  it('does not quote simple ASCII strings without delimiters', () => {
    expect(escapeCsvField('hello')).toBe('hello');
    expect(escapeCsvField('hello world')).toBe('hello world');
  });

  it('quotes fields containing a comma', () => {
    expect(escapeCsvField('a,b')).toBe('"a,b"');
  });

  it('quotes fields containing a double-quote and doubles the embedded quote', () => {
    expect(escapeCsvField('say "hi"')).toBe('"say ""hi"""');
  });

  it('quotes fields containing CR / LF / CRLF', () => {
    expect(escapeCsvField('line1\nline2')).toBe('"line1\nline2"');
    expect(escapeCsvField('line1\r\nline2')).toBe('"line1\r\nline2"');
    expect(escapeCsvField('cr\rlf')).toBe('"cr\rlf"');
  });

  it('renders numbers / booleans / bigint without quoting', () => {
    expect(escapeCsvField(42)).toBe('42');
    expect(escapeCsvField(3.14)).toBe('3.14');
    expect(escapeCsvField(true)).toBe('true');
    expect(escapeCsvField(false)).toBe('false');
    expect(escapeCsvField(BigInt(123))).toBe('123');
  });

  it('renders Date as ISO 8601', () => {
    const d = new Date('2026-06-01T12:00:00.000Z');
    expect(escapeCsvField(d)).toBe('2026-06-01T12:00:00.000Z');
  });

  it('renders Date with NaN time as empty string', () => {
    expect(escapeCsvField(new Date('not-a-date'))).toBe('');
  });

  it('projects Dataverse-style lookup objects via .name', () => {
    expect(escapeCsvField({ id: 'x', name: 'Acme Corp' })).toBe('Acme Corp');
  });

  it('projects option-set-style objects via .label', () => {
    expect(escapeCsvField({ value: 100000000, label: 'Open' })).toBe('Open');
  });

  it('quotes lookup names containing delimiters', () => {
    expect(escapeCsvField({ name: 'Smith, John' })).toBe('"Smith, John"');
  });

  it('writes an object with no name/label as an empty field, never JSON (D-34 #3045)', () => {
    expect(escapeCsvField({ foo: 'bar' })).toBe('');
    expect(escapeCsvField({ value: 1, extensionData: null })).toBe('');
  });

  it('reads an AliasedValue through its inner value, and an array as its readable items (shared rule)', () => {
    expect(escapeCsvField({ attributeLogicalName: 'name', entityLogicalName: 'account', value: 'Acme' })).toBe('Acme');
    expect(escapeCsvField({ attributeLogicalName: 'name', entityLogicalName: 'account', value: null })).toBe('');
    expect(escapeCsvField(['a', { name: 'B' }, { value: 1 }])).toBe('"a, B"');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// asSpreadsheetText — text a spreadsheet would evaluate is written as text
// ─────────────────────────────────────────────────────────────────────────────

describe('asSpreadsheetText', () => {
  it('must-fire: text starting with = + - @ tab or CR gets a leading apostrophe', () => {
    for (const lead of ['=', '+', '-', '@', '\t', '\r']) {
      expect(asSpreadsheetText(`${lead}SUM(A1:A2)`)).toBe(`'${lead}SUM(A1:A2)`);
    }
    expect(asSpreadsheetText('-1+2')).toBe("'-1+2");
    expect(asSpreadsheetText('+1e5x')).toBe("'+1e5x");
  });

  it('must-not-fire: signed amounts, exponent numbers, a lone sign and ordinary text are unchanged', () => {
    for (const text of [
      '-$1,200.00',
      '-1.200,00 €',
      '-1 200,00 €',
      '($1,200.00)',
      '+5%',
      '-12',
      '-0.5',
      '-1e-7',
      '+2.5E10',
      '-',
      '+',
      'Draft',
      'a=b',
      'Smith, Jo',
      '',
    ]) {
      expect(asSpreadsheetText(text)).toBe(text);
    }
  });
});

describe('escapeCsvField — text a spreadsheet would evaluate', () => {
  it('writes such text with a leading apostrophe, RFC 4180 quoted when needed', () => {
    expect(escapeCsvField('=1+1')).toBe("'=1+1");
    expect(escapeCsvField('@A1')).toBe("'@A1");
    expect(escapeCsvField('=A1,B1')).toBe(`"'=A1,B1"`);
    expect(escapeCsvField('=say "hi"')).toBe(`"'=say ""hi"""`);
  });

  it('applies to an object cell read through its name or label', () => {
    expect(escapeCsvField({ id: '1', name: '=Acme' })).toBe("'=Acme");
    expect(escapeCsvField({ value: 1, label: '+Open' })).toBe("'+Open");
  });

  it('must-not-fire: number and boolean values, including negative and exponent forms, are written as is', () => {
    expect(escapeCsvField(-5)).toBe('-5');
    expect(escapeCsvField(-1e-7)).toBe('-1e-7');
    expect(escapeCsvField(BigInt(-9))).toBe('-9');
    expect(escapeCsvField(true)).toBe('true');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// formatYyyymmdd
// ─────────────────────────────────────────────────────────────────────────────

describe('formatYyyymmdd', () => {
  it('pads month and day to two digits', () => {
    expect(formatYyyymmdd(new Date(2026, 0, 5))).toBe('20260105');
    expect(formatYyyymmdd(new Date(2026, 11, 31))).toBe('20261231');
  });

  it('returns 8-character string', () => {
    const out = formatYyyymmdd();
    expect(out).toMatch(/^\d{8}$/);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// csvFilename — filename format
// ─────────────────────────────────────────────────────────────────────────────

describe('csvFilename', () => {
  it('produces {entity}-{view}-{yyyymmdd}.csv', () => {
    const d = new Date(2026, 5, 1);
    expect(csvFilename('sprk_event', 'All Events', d)).toBe('sprk_event-All_Events-20260601.csv');
  });

  it('replaces unsafe filename characters with underscores', () => {
    const d = new Date(2026, 0, 1);
    expect(csvFilename('account', 'Q1/Q2 Pipeline: 2026', d)).toBe('account-Q1_Q2_Pipeline__2026-20260101.csv');
  });

  it('collapses whitespace runs into single underscores', () => {
    const d = new Date(2026, 0, 1);
    expect(csvFilename('contact', '  My   View  ', d)).toBe('contact-My_View-20260101.csv');
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// exportCsv — end-to-end blob composition
// ─────────────────────────────────────────────────────────────────────────────

describe('exportCsv — end-to-end', () => {
  it('prepends the UTF-8 BOM as the first character', async () => {
    const blob = exportCsv([], columns, 'My View', 'sprk_event');
    const text = await readBlobAsText(blob);
    expect(text.charAt(0)).toBe(UTF8_BOM);
    expect(text.charCodeAt(0)).toBe(0xfeff);
  });

  it('emits header row with column labels', async () => {
    const blob = exportCsv([], columns, 'My View', 'sprk_event');
    const text = await readBlobAsText(blob);
    // After the BOM, the first line should be the headers (no records, so no body).
    expect(text).toBe(`${UTF8_BOM}Name,Status,Amount`);
  });

  it('emits body rows with CRLF terminators per RFC 4180', async () => {
    const records = [
      { name: 'Alice', status: 'Open', amount: 100 },
      { name: 'Bob', status: 'Closed', amount: 200 },
    ];
    const blob = exportCsv(records, columns, 'My View', 'sprk_event');
    const text = await readBlobAsText(blob);
    expect(text).toBe(`${UTF8_BOM}Name,Status,Amount\r\nAlice,Open,100\r\nBob,Closed,200`);
  });

  it('quotes cells with commas, quotes, and newlines all together', async () => {
    const records = [
      {
        name: 'Smith, John "Jr."\nLine 2',
        status: 'Open',
        amount: 100,
      },
    ];
    const blob = exportCsv(records, columns, 'My View', 'sprk_event');
    const text = await readBlobAsText(blob);
    const expectedRow = '"Smith, John ""Jr.""\nLine 2",Open,100';
    expect(text).toBe(`${UTF8_BOM}Name,Status,Amount\r\n${expectedRow}`);
  });

  it('handles null and undefined cell values as empty strings', async () => {
    const records = [{ name: null, status: undefined, amount: 0 }];
    const blob = exportCsv(records, columns, 'My View', 'sprk_event');
    const text = await readBlobAsText(blob);
    // Empty strings between commas — exactly per RFC 4180 §2.4.
    expect(text).toBe(`${UTF8_BOM}Name,Status,Amount\r\n,,0`);
  });

  it('projects Dataverse lookup objects via name', async () => {
    const cols: ResolvedColumn[] = [
      {
        name: 'sprk_owner',
        label: 'Owner',
        width: 160,
        renderer: 'link',
        align: 'left',
        hidden: false,
        isPrimaryName: false,
      },
    ];
    const records = [{ sprk_owner: { id: '1', name: 'Acme, Inc.' } }, { sprk_owner: { id: '2', name: 'Plain Name' } }];
    const blob = exportCsv(records, cols, 'View', 'account');
    const text = await readBlobAsText(blob);
    // First lookup contains a comma → must be quoted; second is plain.
    expect(text).toBe(`${UTF8_BOM}Owner\r\n"Acme, Inc."\r\nPlain Name`);
  });

  it('emits BOM-only blob when columns is empty', async () => {
    const blob = exportCsv([{ x: 1 }], [], 'View', 'sprk_event');
    const text = await readBlobAsText(blob);
    expect(text).toBe(UTF8_BOM);
  });

  it('produces a Blob with text/csv;charset=utf-8 type', () => {
    const blob = exportCsv([], columns, 'View', 'sprk_event');
    expect(blob.type).toBe('text/csv;charset=utf-8');
  });

  // Task 033 (FR-20, #2447): the optional cellText hook. Absent, every case above is unchanged.
  describe('cellText (host-supplied cell text)', () => {
    const records = [
      { name: 'Alice', status: 'Open', amount: { value: 1200, extensionData: null } },
      { name: 'Bob', status: 'Closed', amount: 9 },
    ];
    // What the grid supplies for a Money column: the displayed text; nothing for any other column.
    const displayedMoney = (r: Record<string, unknown>, c: ResolvedColumn): string | undefined =>
      c.renderer === 'currency' ? (r.amount === 9 ? '$9.00' : '$1,200.00') : undefined;

    it('writes the supplied text for a cell (RFC 4180 quoted), and the default formatting where it returns undefined', async () => {
      const text = await readBlobAsText(exportCsv(records, columns, 'View', 'sprk_event', displayedMoney));
      expect(text).toBe(`${UTF8_BOM}Name,Status,Amount\r\nAlice,Open,"$1,200.00"\r\nBob,Closed,$9.00`);
    });

    it('an empty string from the hook is an empty cell, not the raw value', async () => {
      const text = await readBlobAsText(exportCsv(records, columns, 'View', 'sprk_event', () => ''));
      expect(text).toBe(`${UTF8_BOM}Name,Status,Amount\r\n,,\r\n,,`);
    });

    it('a hook that always returns undefined gives the same file as no hook', async () => {
      const without = await readBlobAsText(exportCsv(records, columns, 'View', 'sprk_event'));
      const declined = await readBlobAsText(exportCsv(records, columns, 'View', 'sprk_event', () => undefined));
      expect(declined).toBe(without);
      expect(without).toBe(`${UTF8_BOM}Name,Status,Amount\r\nAlice,Open,\r\nBob,Closed,9`);
    });

    it('text from the hook goes through the same guard: an evaluated lead gets the apostrophe, an amount does not', async () => {
      const hook = (r: Record<string, unknown>, c: ResolvedColumn): string | undefined =>
        c.name === 'status' ? '=HYPERLINK("x")' : c.renderer === 'currency' ? '-$9.00' : undefined;
      const text = await readBlobAsText(exportCsv(records.slice(1), columns, 'View', 'sprk_event', hook));
      expect(text).toBe(`${UTF8_BOM}Name,Status,Amount\r\nBob,"'=HYPERLINK(""x"")",-$9.00`);
    });
  });

  it('round-trips UTF-8 characters (non-ASCII payload renders intact)', async () => {
    const records = [{ name: 'Naïve café — résumé', status: '✓', amount: 1 }];
    const blob = exportCsv(records, columns, 'View', 'sprk_event');
    const text = await readBlobAsText(blob);
    expect(text).toContain('Naïve café — résumé');
    expect(text).toContain('✓');
  });
});
