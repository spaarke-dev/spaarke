/**
 * @jest-environment node
 *
 * The Invoice Review link for a billing Work Item (task 031; EB-D-194, D-207, D-208). The `data` rules are the shared
 * `buildInvoiceReviewLaunch` ones (bare lower-case guids, `lineId` only when present) and the link is the BFF's own text link
 * (`DecisionsTextSummary.InvoiceReviewLink`): `data` URL-encoded once more inside the URL.
 */
import { buildInvoiceReviewLaunch } from '@spaarke/ui-components/components/Worklist/openInvoiceReview';
import { buildBillingLinkOut, consoleBaseUrlOf, normalizeConsoleBaseUrl } from '../lane/billingLinkOut';

const BASE = 'https://org.crm.dynamics.com';
const INVOICE = '11111111-2222-3333-4444-555555555555';
const LINE = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';

/** The query of a link, as the platform reads it: one decode of `data` gives the page's `invoiceId=...&lineId=...`. */
function parts(link: string) {
  const url = new URL(link);
  return { url, data: url.searchParams.get('data'), webresourceName: url.searchParams.get('webresourceName') };
}

describe('buildBillingLinkOut', () => {
  it('links to sprk_invoicereviewpage with invoiceId only for an invoice-level item', () => {
    const link = buildBillingLinkOut({ invoiceId: INVOICE }, BASE)!;
    const { url, data, webresourceName } = parts(link);
    expect(url.origin).toBe(BASE);
    expect(url.pathname).toBe('/main.aspx');
    expect(url.searchParams.get('pagetype')).toBe('webresource');
    expect(webresourceName).toBe('sprk_invoicereviewpage');
    expect(data).toBe(`invoiceId=${INVOICE}`);
  });

  it('adds lineId when the item has a line', () => {
    const { data } = parts(buildBillingLinkOut({ invoiceId: INVOICE, lineId: LINE }, BASE)!);
    expect(data).toBe(`invoiceId=${INVOICE}&lineId=${LINE}`);
  });

  it('puts no lineId in the link for a null, empty or blank line id', () => {
    for (const lineId of [null, undefined, '', '  ', '{}']) {
      const { data } = parts(buildBillingLinkOut({ invoiceId: INVOICE, lineId }, BASE)!);
      expect(data).toBe(`invoiceId=${INVOICE}`);
    }
  });

  it('writes bare lower-case guids whatever case or braces the row carries (AP-3)', () => {
    const { data } = parts(
      buildBillingLinkOut({ invoiceId: `{${INVOICE.toUpperCase()}}`, lineId: ` {${LINE.toUpperCase()}} ` }, BASE)!
    );
    expect(data).toBe(`invoiceId=${INVOICE}&lineId=${LINE}`);
  });

  it('encodes data exactly once in the link (the raw query holds %3D and %26, never %253D)', () => {
    const link = buildBillingLinkOut({ invoiceId: INVOICE, lineId: LINE }, BASE)!;
    expect(link).toContain(`&data=invoiceId%3D${INVOICE}%26lineId%3D${LINE}`);
    expect(link).not.toContain('%25');
  });

  it('uses the data the shared launch builds (the rule is reused, not copied)', () => {
    const target = { invoiceId: INVOICE, lineId: LINE };
    const { data } = parts(buildBillingLinkOut(target, BASE)!);
    expect(data).toBe(buildInvoiceReviewLaunch(target).pageInput.data);
  });

  it('accepts a base with a trailing slash and keeps a base path', () => {
    expect(buildBillingLinkOut({ invoiceId: INVOICE }, `${BASE}/`)).toBe(
      buildBillingLinkOut({ invoiceId: INVOICE }, BASE)
    );
    expect(buildBillingLinkOut({ invoiceId: INVOICE }, `${BASE}/org1/`)!.startsWith(`${BASE}/org1/main.aspx?`)).toBe(
      true
    );
  });

  describe('negative: no link', () => {
    it.each([
      ['no target', null],
      ['an undefined target', undefined],
      ['an empty invoice id', { invoiceId: '' }],
      ['a blank invoice id', { invoiceId: '   ' }],
      ['an invoice id that is only braces', { invoiceId: '{}' }],
      ['an invoice id of only whitespace and braces', { invoiceId: ' { } ' }],
    ])('returns null for %s', (_name, target) => {
      expect(buildBillingLinkOut(target, BASE)).toBeNull();
    });

    it.each([
      ['no base', undefined],
      ['a null base', null],
      ['an empty base', ''],
      ['a plain-text base', 'not a url'],
      ['an http base', 'http://org.crm.dynamics.com'],
      ['a javascript: base', 'javascript:alert(1)'],
      ['a base with user info', 'https://user:pw@org.crm.dynamics.com'],
      ['a base with a query', 'https://org.crm.dynamics.com/?x=1'],
      ['a base with a fragment', 'https://org.crm.dynamics.com/#x'],
    ])('returns null for %s (the widget never guesses a host)', (_name, base) => {
      expect(buildBillingLinkOut({ invoiceId: INVOICE }, base)).toBeNull();
    });
  });
});

describe('normalizeConsoleBaseUrl and consoleBaseUrlOf', () => {
  it('keeps an https origin, drops trailing slashes', () => {
    expect(normalizeConsoleBaseUrl(`${BASE}///`)).toBe(BASE);
    expect(normalizeConsoleBaseUrl(`  ${BASE}  `)).toBe(BASE);
  });

  it('reads consoleBaseUrl from a tool result, and null from anything else', () => {
    expect(consoleBaseUrlOf({ consoleBaseUrl: `${BASE}/`, total: 3 })).toBe(BASE);
    for (const result of [null, undefined, 'x', 7, {}, { consoleBaseUrl: 5 }, { consoleBaseUrl: 'http://x.test' }]) {
      expect(consoleBaseUrlOf(result)).toBeNull();
    }
  });
});
