import {
  buildRegardingRecordUrl,
  readSameOriginRecordTarget,
  resolveRegardingPairTable,
  resolveRegardingRecordTarget,
  safeRegardingRecordUrl,
} from '../safeRegardingRecordUrl';

const ID = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';
const OTHER_ID = '11111111-2222-3333-4444-555555555555';
const REL = `/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=${ID}`;
const ORG = 'https://org.crm.dynamics.com';
const ABS = `${ORG}/main.aspx?appid=11111111-1111-1111-1111-111111111111&pagetype=entityrecord&etn=sprk_matter&id=${ID}`;

describe('safeRegardingRecordUrl', () => {
  describe('stored value accepted', () => {
    it('opens a same-origin relative record URL in its canonical form', () => {
      expect(safeRegardingRecordUrl(REL, null, null, ORG)).toBe(REL);
    });

    it('accepts any parameter order and normalizes an upper-case or braced id', () => {
      const stored = `/main.aspx?etn=sprk_matter&id={${ID.toUpperCase()}}&pagetype=entityrecord`;
      expect(safeRegardingRecordUrl(stored, null, null, ORG)).toBe(REL);
    });

    it('drops every parameter other than the record (rebuilds the canonical relative form)', () => {
      expect(safeRegardingRecordUrl(`/main.aspx?appid=1&etn=sprk_matter&id=${ID}&cmdbar=false`, null, null, ORG)).toBe(
        REL
      );
    });

    it('accepts a stored value that names the supplied pair', () => {
      expect(safeRegardingRecordUrl(REL, 'SPRK_MATTER', `{${ID.toUpperCase()}}`, ORG)).toBe(REL);
    });

    it('accepts an absolute URL on the current origin (with appid) and returns its canonical relative form', () => {
      expect(safeRegardingRecordUrl(ABS, null, null, ORG)).toBe(REL);
      expect(readSameOriginRecordTarget(ABS, ORG)).toEqual({ entityName: 'sprk_matter', entityId: ID });
    });

    it('accepts an absolute URL on the current origin without appid, in any scheme/host case', () => {
      expect(
        safeRegardingRecordUrl(`HTTPS://ORG.crm.dynamics.com/MAIN.ASPX?etn=sprk_matter&id=${ID}`, null, null, ORG)
      ).toBe(REL);
    });

    it('accepts an absolute http URL when the current origin is http (same scheme, host and port)', () => {
      const http = 'http://localhost:8080';
      expect(safeRegardingRecordUrl(`${http}/main.aspx?etn=sprk_matter&id=${ID}`, null, null, http)).toBe(REL);
    });

    it('compares against window.location.origin when no origin is injected', () => {
      const current = window.location.origin; // jsdom: http://localhost
      expect(safeRegardingRecordUrl(`${current}/main.aspx?etn=sprk_matter&id=${ID}`)).toBe(REL);
    });

    it('treats the default port as part of the same origin', () => {
      expect(safeRegardingRecordUrl(`${ORG}:443/main.aspx?etn=sprk_matter&id=${ID}`, null, null, ORG)).toBe(REL);
    });
  });

  describe('stored value refused', () => {
    it.each([
      ['absolute with another host', `https://evil.example/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=${ID}`],
      [
        'a host that only starts with the org host',
        `https://org.crm.dynamics.com.evil.example/main.aspx?etn=sprk_matter&id=${ID}`,
      ],
      ['a different subdomain of the org domain', `https://other.crm.dynamics.com/main.aspx?etn=sprk_matter&id=${ID}`],
      ['userinfo naming the org host', `https://org.crm.dynamics.com@evil.example/main.aspx?etn=sprk_matter&id=${ID}`],
      ['credentials on the org host', `https://user:pw@org.crm.dynamics.com/main.aspx?etn=sprk_matter&id=${ID}`],
      [
        'the org host over http (scheme differs from the current origin)',
        `http://org.crm.dynamics.com/main.aspx?etn=sprk_matter&id=${ID}`,
      ],
      ['the org host on another port', `https://org.crm.dynamics.com:8443/main.aspx?etn=sprk_matter&id=${ID}`],
      ['the org host with another path', `${ORG}/other.aspx?etn=sprk_matter&id=${ID}`],
      ['the org host with a path prefix lookalike', `${ORG}/main.aspxx?etn=sprk_matter&id=${ID}`],
      ['the org host with no etn', `${ORG}/main.aspx?id=${ID}`],
      ['the org host with a repeated id', `${ORG}/main.aspx?etn=sprk_matter&id=${ID}&id=${OTHER_ID}`],
      ['the org host with a fragment', `${ORG}/main.aspx?etn=sprk_matter&id=${ID}#x`],
      ['an ftp scheme on the org host', `ftp://org.crm.dynamics.com/main.aspx?etn=sprk_matter&id=${ID}`],
      ['javascript scheme', 'javascript:alert(1)'],
      ['data scheme', 'data:text/html,<script>alert(1)</script>'],
      ['protocol-relative', `//evil.example/main.aspx?etn=sprk_matter&id=${ID}`],
      ['protocol-relative naming the org host', `//org.crm.dynamics.com/main.aspx?etn=sprk_matter&id=${ID}`],
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
      expect(readSameOriginRecordTarget(stored as string | null | undefined, ORG)).toBeNull();
      expect(safeRegardingRecordUrl(stored as string | null | undefined, null, null, ORG)).toBeNull();
    });

    it('refuses every absolute URL when the current origin is unknown', () => {
      expect(readSameOriginRecordTarget(ABS, '')).toBeNull();
      expect(readSameOriginRecordTarget(ABS, 'null')).toBeNull();
      expect(readSameOriginRecordTarget(ABS, 'not a url')).toBeNull();
    });

    it('does not take a same-origin absolute URL for the default (window) origin when it is another origin', () => {
      expect(safeRegardingRecordUrl(ABS)).toBeNull(); // jsdom origin is http://localhost, not the org
      expect(safeRegardingRecordUrl(`https://localhost/main.aspx?etn=sprk_matter&id=${ID}`)).toBeNull(); // https vs http
    });
  });

  describe('built from the pair', () => {
    it('builds the URL from the pair when the stored value is refused', () => {
      const stored = `https://evil.example/main.aspx?etn=contact&id=${OTHER_ID}`;
      expect(safeRegardingRecordUrl(stored, 'sprk_matter', ID, ORG)).toBe(REL);
    });

    it('builds the URL from the pair when the stored value is an absolute URL on another origin', () => {
      expect(safeRegardingRecordUrl(ABS, 'sprk_matter', ID, 'https://other.crm.dynamics.com')).toBe(REL);
    });

    it('builds the URL from the pair when no stored value exists', () => {
      expect(safeRegardingRecordUrl(null, 'sprk_matter', `{${ID.toUpperCase()}}`, ORG)).toBe(REL);
    });

    it('uses the pair when a same-origin stored value (relative or absolute) names another record', () => {
      expect(safeRegardingRecordUrl(`/main.aspx?etn=contact&id=${OTHER_ID}`, 'sprk_matter', ID, ORG)).toBe(REL);
      expect(safeRegardingRecordUrl(`${ORG}/main.aspx?etn=contact&id=${OTHER_ID}`, 'sprk_matter', ID, ORG)).toBe(REL);
    });

    it('returns null when the stored value is refused and the pair is incomplete or invalid', () => {
      const stored = 'javascript:alert(1)';
      expect(safeRegardingRecordUrl(stored, 'sprk_matter', null, ORG)).toBeNull();
      expect(safeRegardingRecordUrl(stored, null, ID, ORG)).toBeNull();
      expect(safeRegardingRecordUrl(stored, 'sprk_matter', 'nope', ORG)).toBeNull();
      expect(safeRegardingRecordUrl(stored, 'matter"><x', ID, ORG)).toBeNull();
      expect(safeRegardingRecordUrl(null, null, null, ORG)).toBeNull();
    });
  });

  describe('resolveRegardingRecordTarget', () => {
    it('returns the lowercase table and "D" id', () => {
      expect(resolveRegardingRecordTarget(null, 'Contact', `{${OTHER_ID.toUpperCase()}}`, ORG)).toEqual({
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
