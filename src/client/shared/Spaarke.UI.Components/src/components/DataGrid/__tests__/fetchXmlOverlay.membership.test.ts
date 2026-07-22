/**
 * Unit tests for overlayMembershipFilter (spaarkeai-assistant-enhancements-r1
 * task 050 feature — behavior.membershipFilter).
 *
 * Coverage:
 *  - non-empty ids → IN(attribute, ids) with one <value> per id
 *  - empty ids → impossible-match (operator='null' on the id attribute) so a
 *    member-of-nothing user sees an EMPTY grid, not everyone's records
 *  - conditions land in the top-level <filter type='and'> alongside base conditions
 *  - falsy fetchXml / attribute → input returned unchanged
 *  - malformed fetchXml → graceful degradation (input returned unchanged)
 */

import { overlayMembershipFilter } from '../fetchXmlOverlay';

const BASE = `<fetch><entity name="sprk_event"><attribute name="sprk_eventid"/><filter type="and"><condition attribute="statecode" operator="eq" value="0"/></filter></entity></fetch>`;

describe('overlayMembershipFilter', () => {
  it('injects an IN(ids) condition for non-empty ids', () => {
    const out = overlayMembershipFilter(BASE, 'sprk_eventid', ['id-1', 'id-2', 'id-3']);
    const doc = new DOMParser().parseFromString(out, 'text/xml');

    const membershipCond = Array.from(doc.querySelectorAll('condition')).find(
      c => c.getAttribute('attribute') === 'sprk_eventid' && c.getAttribute('operator') === 'in'
    );
    expect(membershipCond).toBeDefined();

    const values = Array.from(membershipCond!.querySelectorAll('value')).map(v => v.textContent);
    expect(values).toEqual(['id-1', 'id-2', 'id-3']);

    // Base condition is preserved.
    expect(out).toContain('attribute="statecode"');
  });

  it('lands the membership condition in the existing top-level <filter type="and">', () => {
    const out = overlayMembershipFilter(BASE, 'sprk_eventid', ['id-1']);
    const doc = new DOMParser().parseFromString(out, 'text/xml');
    const entity = doc.querySelector('entity')!;
    const topFilters = Array.from(entity.children).filter(
      c => c.tagName.toLowerCase() === 'filter' && c.getAttribute('type') === 'and'
    );
    // Reuses the single existing top-level filter — no second filter element.
    expect(topFilters).toHaveLength(1);
    expect(topFilters[0].querySelector('condition[operator="in"]')).not.toBeNull();
  });

  it('injects an impossible-match (operator=null) for an empty id list', () => {
    const out = overlayMembershipFilter(BASE, 'sprk_eventid', []);
    const doc = new DOMParser().parseFromString(out, 'text/xml');

    const nullCond = Array.from(doc.querySelectorAll('condition')).find(
      c => c.getAttribute('attribute') === 'sprk_eventid' && c.getAttribute('operator') === 'null'
    );
    expect(nullCond).toBeDefined();
    // No IN condition when the user is a member of nothing.
    expect(doc.querySelector('condition[operator="in"]')).toBeNull();
  });

  it('returns the input unchanged when fetchXml is falsy', () => {
    expect(overlayMembershipFilter('', 'sprk_eventid', ['id-1'])).toBe('');
  });

  it('returns the input unchanged when attribute is empty', () => {
    expect(overlayMembershipFilter(BASE, '', ['id-1'])).toBe(BASE);
  });

  it('degrades gracefully on malformed fetchXml', () => {
    const malformed = '<fetch><entity name="sprk_event"'; // unterminated
    expect(overlayMembershipFilter(malformed, 'sprk_eventid', ['id-1'])).toBe(malformed);
  });
});
