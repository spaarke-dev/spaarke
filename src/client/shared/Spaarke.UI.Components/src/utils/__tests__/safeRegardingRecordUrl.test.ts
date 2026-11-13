import {
  buildRegardingRecordUrl,
  readRelativeRecordTarget,
  resolveRegardingPairTable,
  resolveRegardingRecordTarget,
  safeRegardingRecordUrl,
} from '../safeRegardingRecordUrl';

const ID = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';
const OTHER_ID = '11111111-2222-3333-4444-555555555555';
const REL = `/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=${ID}`;

describe('safeRegardingRecordUrl', () => {
  describe('stored value accepted', () => {
    it('opens a same-origin relative record URL in its canonical form', () => {
      expect(safeRegardingRecordUrl(REL)).toBe(REL);
    });

    it('accepts any parameter order and normalizes an upper-case or braced id', () => {
      const stored = `/main.aspx?etn=sprk_matter&id={${ID.toUpperCase()}}&pagetype=entityrecord`;
      expect(safeRegardingRecordUrl(stored)).toBe(REL);
    });

    it('drops every parameter other than the record (rebuilds the canonical form)', () => {
      expect(safeRegardingRecordUrl(`/main.aspx?appid=1&etn=sprk_matter&id=${ID}&cmdbar=false`)).toBe(REL);
    });

    it('accepts a stored value that names the supplied pair', () => {
      expect(safeRegardingRecordUrl(REL, 'SPRK_MATTER', `{${ID.toUpperCase()}}`)).toBe(REL);
    });
  });

  describe('stored value refused', () => {
    it.each([
      ['absolute with another host', `https://evil.example/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=${ID}`],
      [
        'absolute with the org host',
        `https://org.crm.dynamics.com/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=${ID}`,
      ],
      ['javascript scheme', 'javascript:alert(1)'],
      ['data scheme', 'data:text/html,<script>alert(1)</script>'],
      ['protocol-relative', `//evil.example/main.aspx?etn=sprk_matter&id=${ID}`],
      ['backslash host', `/\\evil.example/main.aspx?etn=sprk_matter&id=${ID}`],
      ['other path', `/other.aspx?etn=sprk_matter&id=${ID}`],
      ['path prefix lookalike', `/main.aspxx?etn=sprk_matter&id=${ID}`],
      ['missing etn', `/main.aspx?pagetype=entityrecord&id=${ID}`],
      ['blank etn', `/main.aspx?etn=&id=${ID}`],
      ['missing id', '/main.aspx?pagetype=entityrecord&etn=sprk_matter'],
      ['id that is not a GUID', '/main.aspx?etn=sprk_matter&id=not-a-guid'],
      ['repeated etn', `/main.aspx?etn=sprk_matter&etn=contact&id=${ID}`],
      ['repeated id', `/main.aspx?etn=sprk_matter&id=${ID}&id=${OTHER_ID}`],
      ['etn that is not a table name', `/main.aspx?etn=sprk_matter%22%3E&id=${ID}`],
      ['fragment', `/main.aspx?etn=sprk_matter&id=${ID}#x`],
      ['embedded whitespace', `/main.aspx?etn=sprk_matter&id=${ID} x`],
      ['no query', '/main.aspx'],
      ['empty', ''],
      ['null', null],
      ['undefined', undefined],
    ])('never opens %s', (_label, stored) => {
      expect(readRelativeRecordTarget(stored as string | null | undefined)).toBeNull();
      expect(safeRegardingRecordUrl(stored as string | null | undefined)).toBeNull();
    });
  });

  describe('built from the pair', () => {
    it('builds the URL from the pair when the stored value is refused', () => {
      const stored = `https://evil.example/main.aspx?etn=contact&id=${OTHER_ID}`;
      expect(safeRegardingRecordUrl(stored, 'sprk_matter', ID)).toBe(REL);
    });

    it('builds the URL from the pair when no stored value exists', () => {
      expect(safeRegardingRecordUrl(null, 'sprk_matter', `{${ID.toUpperCase()}}`)).toBe(REL);
    });

    it('uses the pair when a relative stored value names another record', () => {
      expect(safeRegardingRecordUrl(`/main.aspx?etn=contact&id=${OTHER_ID}`, 'sprk_matter', ID)).toBe(REL);
    });

    it('returns null when the stored value is refused and the pair is incomplete or invalid', () => {
      const stored = 'javascript:alert(1)';
      expect(safeRegardingRecordUrl(stored, 'sprk_matter', null)).toBeNull();
      expect(safeRegardingRecordUrl(stored, null, ID)).toBeNull();
      expect(safeRegardingRecordUrl(stored, 'sprk_matter', 'nope')).toBeNull();
      expect(safeRegardingRecordUrl(stored, 'matter"><x', ID)).toBeNull();
      expect(safeRegardingRecordUrl(null)).toBeNull();
    });
  });

  describe('resolveRegardingRecordTarget', () => {
    it('returns the lowercase table and "D" id', () => {
      expect(resolveRegardingRecordTarget(null, 'Contact', `{${OTHER_ID.toUpperCase()}}`)).toEqual({
        entityName: 'contact',
        entityId: OTHER_ID,
      });
    });
  });

  describe('buildRegardingRecordUrl', () => {
    it('builds the canonical form only from a valid table and GUID', () => {
      expect(buildRegardingRecordUrl('sprk_matter', ID)).toBe(REL);
      expect(buildRegardingRecordUrl('sprk_matter', 'x')).toBeNull();
      expect(buildRegardingRecordUrl('', ID)).toBeNull();
    });
  });

  describe('resolveRegardingPairTable', () => {
    it('reads the table logical name of a record type row', async () => {
      const retrieveRecord = jest.fn().mockResolvedValue({ sprk_recordlogicalname: 'sprk_matter' });
      await expect(resolveRegardingPairTable({ retrieveRecord }, `{${ID}}`)).resolves.toBe('sprk_matter');
      expect(retrieveRecord).toHaveBeenCalledWith('sprk_recordtype_ref', ID, '?$select=sprk_recordlogicalname');
    });

    it('returns null on a fault, a missing api, or an invalid type id', async () => {
      const failing = { retrieveRecord: jest.fn().mockRejectedValue(new Error('403')) };
      await expect(resolveRegardingPairTable(failing, ID)).resolves.toBeNull();
      await expect(resolveRegardingPairTable(null, ID)).resolves.toBeNull();
      await expect(resolveRegardingPairTable({ retrieveRecord: jest.fn() }, 'nope')).resolves.toBeNull();
      const bad = { retrieveRecord: jest.fn().mockResolvedValue({ sprk_recordlogicalname: 'a b' }) };
      await expect(resolveRegardingPairTable(bad, ID)).resolves.toBeNull();
    });
  });
});
