import {
  DIMMED_MARK_KINDS,
  PROCEDURE_FILTERS,
  RULE_FILTERS,
  RULE_MARKS,
  buildCountCards,
  gapBarText,
  isProcedureDimmed,
  isRuleDimmed,
  markBadge,
  matchesProcedureFilter,
  matchesRuleFilter,
  overrideNote,
  procedureState,
  publishedNote,
  resolveFilter,
  ruleBadges,
  runningCount,
  shortDate,
  sourceLabel,
  todayIso,
  versionLines,
} from '../services/model';
import {
  FENWICK,
  MATTER_GAP,
  WA_GAP,
  mark,
  matterProcedures,
  matterRules,
  procedure,
  projectRules,
  rule,
  waProcedures,
} from './fixtures';

const byId = (id: string, list = matterProcedures.procedures) => list.find(p => p.id === id)!;
const ruleBy = (code: string, list = matterRules.rules) => list.find(r => r.code === code)!;

describe('words', () => {
  it('shortDate and todayIso', () => {
    expect(shortDate('2026-11-02')).toBe('2 Nov 2026');
    expect(shortDate('not a date')).toBe('not a date');
    expect(shortDate(null)).toBe('');
    expect(todayIso(new Date(2026, 0, 5))).toBe('2026-01-05');
  });

  it('sourceLabel says how a Procedure reaches the record', () => {
    const rate = byId('p-rate');
    expect(sourceLabel(rate.sources[0], 'Fenwick')).toBe('Assigned to this record');
    expect(sourceLabel(rate.sources[1], 'Fenwick')).toBe('Firm · Fenwick');
    expect(sourceLabel(byId('p-ocg').sources[0], null)).toBe('All records');
    const parent = byId('p-rate', waProcedures.procedures).sources[0];
    expect(sourceLabel(parent, 'Fenwick')).toBe('Assigned to matter 2026-LIT-4471');
    expect(sourceLabel({ ...parent, kind: 'SomethingNew' }, null)).toBe('SomethingNew');
  });
});

describe('Procedure state', () => {
  const gaps = matterProcedures.gaps;

  it('one badge per state', () => {
    expect(procedureState(byId('p-ocg'), gaps)).toEqual({ label: 'In force', tone: 'success' });
    expect(procedureState(byId('p-rate'), gaps)).toEqual({ label: 'Pinned', tone: 'info' });
    expect(procedureState(byId('p-travel'), gaps)).toEqual({ label: 'Excluded here', tone: 'neutral' });
    expect(procedureState(byId('p-next'), gaps)).toEqual({ label: 'Not yet in force', tone: 'neutral' });
    expect(procedureState(byId('p-fee'), gaps)).toEqual({ label: 'No version', tone: 'critical' });
  });

  it('a gap says its kind; an exclusion on a parent says so; an unknown status is neutral with its own text', () => {
    expect(procedureState(byId('p-rate', waProcedures.procedures), waProcedures.gaps)).toEqual({ label: 'Conflicting pins', tone: 'critical' });
    expect(procedureState(byId('p-travel', waProcedures.procedures), waProcedures.gaps).label).toBe('Excluded on parent');
    const retired = { ...MATTER_GAP, kind: 'PinnedVersionRetired' };
    expect(procedureState(byId('p-fee'), [retired]).label).toBe('Pinned version retired');
    expect(procedureState(procedure({ id: 'x', name: 'x', status: 'Archived' }), [])).toEqual({ label: 'Archived', tone: 'neutral' });
  });

  it('only excluded and not-yet-in-force Procedures are dimmed', () => {
    expect(matterProcedures.procedures.filter(isProcedureDimmed).map(p => p.id)).toEqual(['p-travel', 'p-next']);
  });

  it('version cell: "pinned to vN, vM available" for a pin that skips a newer version', () => {
    expect(versionLines(byId('p-rate'))).toEqual({ main: 'v1', note: 'pinned to v1, v2 available' });
    expect(versionLines(byId('p-ocg'))).toEqual({ main: 'v2', note: null });
    expect(versionLines(byId('p-travel'))).toEqual({ main: '—', note: null });
  });

  it('the line under "Applies through": where the deciding row is, or the conflict', () => {
    expect(overrideNote(byId('p-rate'), matterProcedures.gaps)).toBe('Pinned here');
    expect(overrideNote(byId('p-travel'), matterProcedures.gaps)).toBe('Excluded here');
    expect(overrideNote(byId('p-travel', waProcedures.procedures), waProcedures.gaps)).toBe('Excluded on matter 2026-LIT-4471');
    expect(overrideNote(byId('p-rate', waProcedures.procedures), waProcedures.gaps)).toBe('Parents pin different versions');
    expect(overrideNote(byId('p-ocg'), [])).toBeNull();
  });

  it('the gap bar names the firm, the date and each reason', () => {
    const text = gapBarText(FENWICK.name, '2026-10-21', [MATTER_GAP, WA_GAP]);
    expect(text).toContain('Fenwick & Calder LLP invoices for work dated 21 Oct 2026 are held');
    expect(text).toContain('Northwind fee schedule: ');
    expect(text).toContain('Fenwick rate card: ');
    expect(gapBarText(null, '2026-10-21', [])).toMatch(/^Invoices for work dated/);
  });
});

describe('Procedure filters and cards', () => {
  const list = matterProcedures.procedures;

  it('each filter holds exactly its rows', () => {
    const ids = (f: Parameters<typeof matchesProcedureFilter>[0]) => list.filter(p => matchesProcedureFilter(f, p)).map(p => p.id);
    expect(ids('all')).toHaveLength(6);
    expect(ids('pinned')).toEqual(['p-rate']);
    expect(ids('excluded')).toEqual(['p-travel']);
    expect(ids('attention')).toEqual(['p-fee']);
  });

  it('cards: counts cover the whole list, the selected card again returns to All, a card at 0 is disabled', () => {
    const onSelect = jest.fn();
    const cards = buildCountCards({
      idPrefix: 'procedures',
      noun: { singular: 'Procedure', plural: 'Procedures' },
      filters: PROCEDURE_FILTERS,
      rows: list,
      selected: 'pinned',
      onSelect,
      allLabel: 'All',
    });
    expect(cards.map(c => [c.label, c.value, c.selected])).toEqual([
      ['All', 6, false],
      ['Pinned', 1, true],
      ['Excluded', 1, false],
      ['Needs attention', 1, false],
    ]);
    expect(cards[1].ariaLabel).toBe('Pinned, 1 Procedure');
    cards[1].onClick?.();
    expect(onSelect).toHaveBeenLastCalledWith('all');
    cards[2].onClick?.();
    expect(onSelect).toHaveBeenLastCalledWith('excluded');
    expect(cards[0].disableWhenEmpty).toBe(false);
    expect(cards.slice(1).every(c => c.disableWhenEmpty)).toBe(true);
  });

  it('a filter whose card fell to 0 resolves back to All', () => {
    expect(resolveFilter(PROCEDURE_FILTERS, list, 'pinned')).toBe('pinned');
    expect(resolveFilter(PROCEDURE_FILTERS, list.filter(p => p.id !== 'p-rate'), 'pinned')).toBe('all');
    expect(resolveFilter(PROCEDURE_FILTERS, [], 'all')).toBe('all');
  });
});

describe('Rule marks', () => {
  it('each known kind has its own badge', () => {
    expect(markBadge(mark('Excluded'))).toEqual({ label: 'Excluded', tone: 'neutral' });
    expect(markBadge(mark('Changed'))).toEqual({ label: 'Changed here', tone: 'info' });
    expect(markBadge(mark('Stale'))).toEqual({ label: 'Base rule updated', tone: 'warning' });
    expect(markBadge(mark('BaseRetired'))).toEqual({ label: 'Base rule retired', tone: 'warning' });
  });

  it('an UNKNOWN kind is a neutral badge with the kind text; it is not dimmed (the array is open, 084e adds Outranked)', () => {
    expect(markBadge(mark('Outranked'))).toEqual({ label: 'Outranked', tone: 'neutral' });
    const r = rule({ policyId: 'r-x', code: 'X', marks: [mark('Outranked')] });
    expect(ruleBadges(r)).toEqual([{ label: 'Outranked', tone: 'neutral' }]);
    expect(isRuleDimmed(r)).toBe(false);
    expect(RULE_MARKS.Outranked).toBeUndefined();
  });

  it('the dimmed set is ONE mapping: adding a kind to it dims that kind (how 084e adds Outranked)', () => {
    const outranked = rule({ policyId: 'r-x', code: 'X', marks: [mark('Outranked')] });
    const set = DIMMED_MARK_KINDS as Set<string>;
    set.add('Outranked');
    try {
      expect(isRuleDimmed(outranked)).toBe(true);
    } finally {
      set.delete('Outranked');
    }
    expect(isRuleDimmed(outranked)).toBe(false);
  });

  it('excluded rules are dimmed; Changed, Stale and BaseRetired rows are not', () => {
    expect(matterRules.rules.filter(isRuleDimmed).map(r => r.code)).toEqual(['VAGUE']);
    expect(projectRules.rules.filter(isRuleDimmed)).toEqual([]);
  });

  it('a rule has one badge per mark, "In force" with none, and a mark without a kind is dropped', () => {
    expect(ruleBadges(ruleBy('HOURS')).map(b => b.label)).toEqual(['Changed here', 'Base rule updated']);
    expect(ruleBadges(ruleBy('BLOCK'))).toEqual([{ label: 'In force', tone: 'success' }]);
    expect(ruleBadges(rule({ policyId: 'r-y', code: 'Y', marks: [mark('')] }))).toEqual([{ label: 'In force', tone: 'success' }]);
    expect(ruleBadges(rule({ policyId: 'r-z', code: 'Z', inForce: false }))).toEqual([{ label: 'Not in force', tone: 'neutral' }]);
  });

  it('the word "displaced" is not in any label the page can produce', () => {
    const labels = [...Object.values(RULE_MARKS).map(b => b.label), ...matterRules.rules.flatMap(r => ruleBadges(r).map(b => b.label))];
    expect(labels.filter(l => /displaced/i.test(l))).toEqual([]);
    expect(RULE_FILTERS.filter(f => /displaced/i.test(f.label))).toEqual([]);
  });

  it('filters: Changed here, Excluded and Needs attention (stale or base retired) hold exactly their rules', () => {
    const codes = (f: Parameters<typeof matchesRuleFilter>[0], list = matterRules.rules) => list.filter(r => matchesRuleFilter(f, r)).map(r => r.code);
    expect(codes('all')).toEqual(['BLOCK', 'HOURS', 'MEALS', 'VAGUE']);
    expect(codes('changed')).toEqual(['HOURS']);
    expect(codes('excluded')).toEqual(['VAGUE']);
    expect(codes('attention')).toEqual(['HOURS']);
    expect(codes('attention', projectRules.rules)).toEqual(['RUSH']);
    expect(runningCount(matterRules.rules)).toBe(3);
    expect(runningCount(projectRules.rules)).toBe(1);
  });

  it('a changed rule shows the published values under what runs; a retired base says so', () => {
    expect(publishedNote(ruleBy('HOURS'))).toBe('Published: 11 hours per timekeeper per day');
    expect(publishedNote(ruleBy('RUSH', projectRules.rules))).toBe('Published rule retired');
    expect(publishedNote(ruleBy('BLOCK'))).toBeNull();
  });
});
