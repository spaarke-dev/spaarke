/**
 * Worklist (MatterCard + IssueLine) — unit tests (task 051). The stated contract:
 *  - ONE component renders every Work Item shape (cross-source, threshold, Do-lane) and every core type (matter,
 *    project, work assignment, service request, Not filed) from data, with no per-type code (FR-25, D-34..D-36)
 *  - a Decide line shows headline + rule short name + age; a Do line shows headline + rule short name + due state
 *    computed from sprk_duedate in the viewer's local date (D-23)
 *  - the WHOLE line opens: click, Enter and Space raise the open-item event with the item id and the visible list
 *  - the component never re-sorts (row-contract requirement 2); a row resolves to an object (requirement 1)
 *  - negative: no button/menu/code/clause mark/badge inside a line; no witness value; no colour literal (ADR-021)
 */

import '@testing-library/jest-dom';
import * as fs from 'fs';
import * as path from 'path';
import * as React from 'react';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

import { MatterCard, composeIssueLine, formatAge, formatDueState } from '..';
import type { OpenItemEvent, WorklistItem } from '..';
import {
  TODAY,
  dueSoonItem,
  dueTodayItem,
  lateAssignmentItem,
  localNoonIso,
  matterCore,
  overdueTaskItem,
  pathBItem,
  projectCore,
  serviceRequestCore,
  thresholdItem,
  workAssignmentCore,
} from '../__fixtures__/worklistFixtures';

function renderThemed(ui: React.ReactElement, dark = false) {
  return render(<FluentProvider theme={dark ? webDarkTheme : webLightTheme}>{ui}</FluentProvider>);
}

const ALL_FOUR = [pathBItem, thresholdItem, overdueTaskItem, lateAssignmentItem];
const INTERACTIVE =
  'button, a, input, select, textarea, [role="button"], [role="menu"], [role="menuitem"], [role="link"]';

describe('MatterCard — one component, every shape', () => {
  it('renders a Path B, a Threshold and two Do items as ONE card with four lines, in the order given', () => {
    renderThemed(<MatterCard core={matterCore} items={ALL_FOUR} today={TODAY} />);
    expect(screen.getAllByTestId('matter-card')).toHaveLength(1);
    const lines = screen.getAllByTestId('issue-line');
    expect(lines).toHaveLength(4);
    expect(lines.map(l => l.getAttribute('data-signal-id'))).toEqual(ALL_FOUR.map(i => i.signalId));
  });

  it('shows the matter by name and number', () => {
    renderThemed(<MatterCard core={matterCore} items={[pathBItem]} today={TODAY} />);
    expect(screen.getByText('Acme v. Beta')).toBeInTheDocument();
    expect(screen.getByTestId('matter-card-detail')).toHaveTextContent('Matter · MTR-00042');
  });

  it('renders project, work assignment and service request cards through the same component, from data only', () => {
    renderThemed(
      <>
        <MatterCard core={projectCore} items={[pathBItem]} today={TODAY} />
        <MatterCard core={workAssignmentCore} items={[lateAssignmentItem]} today={TODAY} />
        <MatterCard core={serviceRequestCore} items={[overdueTaskItem]} today={TODAY} />
      </>
    );
    const cards = screen.getAllByTestId('matter-card');
    expect(cards.map(c => c.getAttribute('data-core-type'))).toEqual([
      'sprk_project',
      'sprk_workassignment',
      'sprk_servicerequest',
    ]);
    expect(screen.getByText('Contract refresh 2026')).toBeInTheDocument();
    expect(within(cards[0]).getByTestId('matter-card-detail')).toHaveTextContent('Project · PRJT-00007');
    expect(within(cards[1]).getByTestId('matter-card-detail')).toHaveTextContent('Work assignment · WRK-00019');
    expect(within(cards[2]).getByText('Onboarding request from Northwind')).toBeInTheDocument();
    expect(within(cards[2]).getByTestId('matter-card-detail')).toHaveTextContent('Service request · SVCR-00003');
  });

  it('renders a type it has never heard of, from the row data (D-36: no per-type code)', () => {
    renderThemed(
      <MatterCard
        core={{
          recordType: 'sprk_futuretype',
          recordId: 'eeee',
          typeLabel: 'Engagement',
          name: 'Zeta',
          number: 'ENG-1',
        }}
        items={[overdueTaskItem]}
        today={TODAY}
      />
    );
    expect(screen.getByText('Zeta')).toBeInTheDocument();
    expect(screen.getByTestId('matter-card-detail')).toHaveTextContent('Engagement · ENG-1');
  });

  it('renders the Not filed card (no core record, D-35) with the same component', () => {
    renderThemed(<MatterCard core={null} items={[overdueTaskItem]} today={TODAY} />);
    const card = screen.getByTestId('matter-card');
    expect(card).toHaveAttribute('data-core-type', 'not-filed');
    expect(within(card).getByText('Not filed')).toBeInTheDocument();
    expect(within(card).getByText('Send engagement letter')).toBeInTheDocument();
  });

  it('never renders an empty card', () => {
    renderThemed(<MatterCard core={matterCore} items={[]} today={TODAY} />);
    expect(screen.queryByTestId('matter-card')).not.toBeInTheDocument();
  });

  it('shows the optional note under the lines', () => {
    renderThemed(
      <MatterCard core={matterCore} items={[pathBItem]} note="+1 other open item on this matter, not in this filter" />
    );
    expect(screen.getByText('+1 other open item on this matter, not in this filter')).toBeInTheDocument();
  });
});

describe('IssueLine content (D-23)', () => {
  it('a Decide line shows the headline, the rule short name and the age', () => {
    renderThemed(<MatterCard core={matterCore} items={[pathBItem]} today={TODAY} />);
    const line = screen.getByTestId('issue-line');
    expect(line).toHaveTextContent('Invoice 4411 from Acme LLP');
    expect(line).toHaveTextContent('Fee or scope change');
    expect(screen.getByTestId('issue-timing')).toHaveTextContent('3d');
    expect(screen.getByTestId('issue-timing')).toHaveAttribute('data-tone', 'age');
  });

  it('a Do line shows the headline, the rule short name and the due state from sprk_duedate', () => {
    renderThemed(<MatterCard core={matterCore} items={[overdueTaskItem]} today={TODAY} />);
    const line = screen.getByTestId('issue-line');
    expect(line).toHaveTextContent('Send engagement letter');
    expect(line).toHaveTextContent('Overdue task');
    expect(screen.getByTestId('issue-timing')).toHaveTextContent('5d overdue');
    expect(screen.getByTestId('issue-timing')).toHaveAttribute('data-tone', 'overdue');
  });

  it('a Do line never shows the item age, and a Decide line never shows a due state', () => {
    const decideWithDue: WorklistItem = { ...pathBItem, dueDate: '2026-10-01' };
    const doWithRaised: WorklistItem = { ...overdueTaskItem, raisedOn: localNoonIso(2026, 8, 1) };
    expect(composeIssueLine(decideWithDue, TODAY).timing.text).toBe('3d');
    expect(composeIssueLine(doWithRaised, TODAY).timing.text).toBe('5d overdue');
  });

  it('due state wording: overdue, late, due today, due later (calendar days, viewer local date)', () => {
    expect(formatDueState('2026-10-04', TODAY)).toEqual({ text: '5d overdue', tone: 'overdue' });
    expect(formatDueState('2026-10-06', TODAY, 'late')).toEqual({ text: '3d late', tone: 'overdue' });
    expect(formatDueState('2026-10-08', TODAY)).toEqual({ text: '1d overdue', tone: 'overdue' });
    expect(formatDueState('2026-10-09', TODAY)).toEqual({ text: 'due today', tone: '3d' });
    const soon = formatDueState('2026-10-12', TODAY);
    expect(soon.text).toMatch(/^due /);
    expect(soon.text).toContain('12');
    expect(soon.tone).toBe('3d');
    expect(formatDueState('2026-10-15', TODAY).tone).toBe('7d');
    expect(formatDueState('2026-10-18', TODAY).tone).toBe('10d');
    expect(formatDueState('2026-12-01', TODAY).tone).toBe('none');
  });

  it('a Date Only due date is a calendar date, not a UTC instant (no off-by-one in a negative-offset zone)', () => {
    // The same day at the viewer's late evening: still "due today", not overdue.
    expect(formatDueState('2026-10-09', new Date(2026, 9, 9, 23, 30)).text).toBe('due today');
  });

  it('age wording: today, Nd, and a plain statement when the raised date is missing', () => {
    expect(formatAge(localNoonIso(2026, 9, 9), TODAY)).toBe('today');
    expect(formatAge(localNoonIso(2026, 9, 8), TODAY)).toBe('1d');
    expect(formatAge(localNoonIso(2026, 8, 29), TODAY)).toBe('10d');
    expect(formatAge(localNoonIso(2026, 9, 12), TODAY)).toBe('today'); // clock skew never reads negative
    expect(formatAge(null, TODAY)).toBe('Age unknown');
  });

  it('missing data reads as a statement, never blank or zero', () => {
    expect(composeIssueLine({ signalId: 'x', lane: 'Do' }, TODAY)).toEqual({
      headline: 'Untitled item',
      ruleShortName: null,
      timing: { text: 'No due date', tone: 'none' },
    });
    expect(
      composeIssueLine({ signalId: 'x', lane: 'Decide', subjectName: '  ', title: 'Stored title' }, TODAY).headline
    ).toBe('Stored title');
  });

  it('the late/overdue wording comes from the item, and the due-soon and due-today lines render', () => {
    renderThemed(
      <MatterCard core={matterCore} items={[lateAssignmentItem, dueSoonItem, dueTodayItem]} today={TODAY} />
    );
    const timings = screen.getAllByTestId('issue-timing').map(t => t.textContent);
    expect(timings[0]).toBe('3d late');
    expect(timings[1]).toMatch(/^due .*12/);
    expect(timings[2]).toBe('due today');
  });
});

describe('the whole line opens (click, Enter, Space)', () => {
  it('click raises the open-item event with the item id, the core record and the visible list', async () => {
    const onOpenItem = jest.fn<void, [OpenItemEvent]>();
    renderThemed(<MatterCard core={matterCore} items={ALL_FOUR} onOpenItem={onOpenItem} today={TODAY} />);
    await userEvent.click(screen.getAllByTestId('issue-line')[1]);
    expect(onOpenItem).toHaveBeenCalledTimes(1);
    expect(onOpenItem).toHaveBeenCalledWith({
      itemId: thresholdItem.signalId,
      core: { recordType: 'sprk_matter', recordId: matterCore.recordId },
      visibleList: ALL_FOUR.map(i => i.signalId),
    });
  });

  it('clicking the headline text, the rule name or the timing text (not just the edge) opens the line', async () => {
    const onOpenItem = jest.fn();
    renderThemed(<MatterCard core={matterCore} items={[pathBItem]} onOpenItem={onOpenItem} today={TODAY} />);
    await userEvent.click(screen.getByText('Invoice 4411 from Acme LLP'));
    await userEvent.click(screen.getByText('Fee or scope change'));
    await userEvent.click(screen.getByTestId('issue-timing'));
    expect(onOpenItem).toHaveBeenCalledTimes(3);
  });

  it('Enter and Space on a focused line each raise the event', async () => {
    const onOpenItem = jest.fn<void, [OpenItemEvent]>();
    renderThemed(<MatterCard core={matterCore} items={ALL_FOUR} onOpenItem={onOpenItem} today={TODAY} />);
    const lines = screen.getAllByTestId('issue-line');
    lines[2].focus();
    await userEvent.keyboard('{Enter}');
    lines[3].focus();
    await userEvent.keyboard(' ');
    expect(onOpenItem.mock.calls.map(c => c[0].itemId)).toEqual([
      overdueTaskItem.signalId,
      lateAssignmentItem.signalId,
    ]);
  });

  it('every line is keyboard reachable in the order given (Tab)', async () => {
    renderThemed(<MatterCard core={matterCore} items={ALL_FOUR} today={TODAY} />);
    const lines = screen.getAllByTestId('issue-line');
    await userEvent.tab();
    expect(lines[0]).toHaveFocus();
    await userEvent.tab();
    expect(lines[1]).toHaveFocus();
  });

  it('the visible list is the host-supplied browse set across cards, copied, and defaults to the card items', async () => {
    const onOpenItem = jest.fn<void, [OpenItemEvent]>();
    const visible = [pathBItem.signalId, thresholdItem.signalId, overdueTaskItem.signalId];
    renderThemed(
      <MatterCard
        core={projectCore}
        items={[overdueTaskItem]}
        visibleList={visible}
        onOpenItem={onOpenItem}
        today={TODAY}
      />
    );
    await userEvent.click(screen.getByTestId('issue-line'));
    const event = onOpenItem.mock.calls[0][0];
    expect(event.visibleList).toEqual(visible);
    expect(event.visibleList).not.toBe(visible);
    expect(event.core).toEqual({ recordType: 'sprk_project', recordId: projectCore.recordId });
  });

  it('a Not filed line raises the event with core = null', async () => {
    const onOpenItem = jest.fn<void, [OpenItemEvent]>();
    renderThemed(<MatterCard core={null} items={[dueSoonItem]} onOpenItem={onOpenItem} today={TODAY} />);
    await userEvent.click(screen.getByTestId('issue-line'));
    expect(onOpenItem).toHaveBeenCalledWith({
      itemId: dueSoonItem.signalId,
      core: null,
      visibleList: [dueSoonItem.signalId],
    });
  });

  it('does not throw when no handler is given', async () => {
    renderThemed(<MatterCard core={matterCore} items={[pathBItem]} today={TODAY} />);
    await userEvent.click(screen.getByTestId('issue-line'));
  });
});

describe('rank and membership are given, never computed (row-contract requirement 2)', () => {
  it('renders rank-ordered props as given, whatever the age or due order', () => {
    const given = [dueSoonItem, overdueTaskItem, dueTodayItem, lateAssignmentItem]; // deliberately not date order
    renderThemed(<MatterCard core={matterCore} items={given} today={TODAY} />);
    expect(screen.getAllByTestId('issue-line').map(l => l.getAttribute('data-signal-id'))).toEqual(
      given.map(i => i.signalId)
    );
  });

  it('does not mutate the array it is given', () => {
    const given: WorklistItem[] = [thresholdItem, pathBItem];
    const snapshot = [...given];
    renderThemed(<MatterCard core={matterCore} items={given} today={TODAY} />);
    expect(given).toEqual(snapshot);
  });

  it('renders every item given, with no de-duplication or truncation of membership', () => {
    const many = Array.from({ length: 12 }, (_, i) => ({ ...pathBItem, signalId: `id-${i}` }));
    renderThemed(<MatterCard core={matterCore} items={many} today={TODAY} />);
    expect(screen.getAllByTestId('issue-line')).toHaveLength(12);
  });
});

describe('a row resolves to an object (row-contract requirement 1)', () => {
  it('every line carries its Signal id and every card its core record', () => {
    renderThemed(<MatterCard core={matterCore} items={ALL_FOUR} today={TODAY} />);
    for (const line of screen.getAllByTestId('issue-line')) {
      expect(line.getAttribute('data-signal-id')).toMatch(/^[0-9a-f-]{36}$/);
    }
    const card = screen.getByTestId('matter-card');
    expect(card).toHaveAttribute('data-core-type', 'sprk_matter');
    expect(card).toHaveAttribute('data-core-id', matterCore.recordId);
  });

  it('a line always has headline text, even with no subject name and no title', () => {
    renderThemed(<MatterCard core={matterCore} items={[{ signalId: 'only-id', lane: 'Decide' }]} today={TODAY} />);
    expect(screen.getByTestId('issue-line')).toHaveTextContent('Untitled item');
  });
});

describe('negative: what a line must not contain', () => {
  it('each line is the one activatable element: no button, link, input or menu inside it', () => {
    renderThemed(<MatterCard core={matterCore} items={[...ALL_FOUR, dueSoonItem]} today={TODAY} />);
    const lines = screen.getAllByTestId('issue-line');
    for (const line of lines) {
      expect(line.querySelectorAll(INTERACTIVE)).toHaveLength(0);
      expect(line.parentElement?.querySelectorAll(INTERACTIVE)).toHaveLength(1); // the line itself, nothing beside it
    }
  });

  it('the card has no control other than its lines (no card menu, no card button)', () => {
    renderThemed(<MatterCard core={matterCore} items={ALL_FOUR} today={TODAY} />);
    expect(screen.getByTestId('matter-card').querySelectorAll(INTERACTIVE)).toHaveLength(ALL_FOUR.length);
  });

  it('no badge, rule code, clause mark or status chip is rendered', () => {
    const withExtras = {
      ...pathBItem,
      policyCode: 'COMMS-FEE-007',
      sentence: 'A fee or scope change was received from Fenwick on 12 Sep and no budget revision since',
      witness: 'Fenwick & Calder',
      clause: 'exists: invoice received',
      evidence: [{ tier: 'Fact', text: 'Witness text 77' }],
    } as WorklistItem;
    const { container } = renderThemed(<MatterCard core={matterCore} items={[withExtras]} today={TODAY} />);
    expect(container.querySelector('[class*="fui-Badge"]')).toBeNull();
    expect(container.querySelector('[data-testid="status-badge"]')).toBeNull();
    const text = container.textContent ?? '';
    expect(text).not.toContain('COMMS-FEE-007');
    expect(text).not.toContain('Fenwick');
    expect(text).not.toContain('exists:');
    expect(text).not.toContain('Fact');
    expect(text).not.toContain('Witness text 77');
  });

  it('never shows a witness value: only the subject name, the rule short name and the timing', () => {
    renderThemed(<MatterCard core={matterCore} items={[pathBItem]} today={TODAY} />);
    expect(screen.getByTestId('issue-line').textContent).toBe('Invoice 4411 from Acme LLPFee or scope change3d');
  });
});

describe('ADR-021 and structure', () => {
  const dir = path.join(__dirname, '..');
  const sources = fs
    .readdirSync(dir)
    .filter(f => /\.tsx?$/.test(f))
    .map(f => path.join(dir, f));

  it('renders every shape in the dark theme', () => {
    renderThemed(
      <>
        <MatterCard core={matterCore} items={ALL_FOUR} today={TODAY} />
        <MatterCard core={projectCore} items={[dueSoonItem]} today={TODAY} />
        <MatterCard core={null} items={[dueTodayItem]} today={TODAY} />
      </>,
      true
    );
    expect(screen.getAllByTestId('matter-card')).toHaveLength(3);
    expect(screen.getAllByTestId('issue-line')).toHaveLength(6);
  });

  it('component sources contain no hex, rgb/hsl or named colour literal', () => {
    expect(sources.length).toBeGreaterThanOrEqual(5);
    const literal =
      /#[0-9a-fA-F]{3,8}\b|\b(?:rgba?|hsla?)\(|(?:color|background\w*|border\w*|outline\w*)\s*:\s*['"](?:white|black|red|green|blue|gray|grey|orange|yellow|purple|transparent)['"]/i;
    for (const f of sources) {
      const code = fs
        .readFileSync(f, 'utf8')
        .split('\n')
        .filter(l => !l.trim().startsWith('*') && !l.trim().startsWith('//') && !l.trim().startsWith('/*'))
        .join('\n');
      expect({ f, hit: literal.test(code) }).toEqual({ f, hit: false });
    }
  });

  it('the component has no per-type branch (D-36): no table name or core-type literal in code', () => {
    for (const f of sources.filter(s => /(MatterCard|IssueLine|composeIssueLine)\.tsx?$/.test(s))) {
      const code = fs
        .readFileSync(f, 'utf8')
        .split('\n')
        .filter(l => !l.trim().startsWith('*') && !l.trim().startsWith('//') && !l.trim().startsWith('/*'))
        .join('\n');
      expect({ f, hit: /sprk_\w+|===\s*['"](?:Matter|Project|matter|project)['"]/.test(code) }).toEqual({
        f,
        hit: false,
      });
    }
  });

  it('no sort call anywhere in the component sources (requirement 2)', () => {
    for (const f of sources) {
      const code = fs
        .readFileSync(f, 'utf8')
        .split('\n')
        .filter(l => !l.trim().startsWith('*') && !l.trim().startsWith('//'))
        .join('\n');
      expect({ f, hit: /\.(?:sort|toSorted|reverse)\s*\(/.test(code) }).toEqual({ f, hit: false });
    }
  });

  it('the repo has exactly one worklist row component: no second MatterCard / IssueLine / WorklistRow file', () => {
    const roots = [
      path.resolve(__dirname, '../../../../..'), // src/client/shared
      path.resolve(__dirname, '../../../../../../../solutions'), // src/solutions
    ].filter(r => fs.existsSync(r));
    const found: string[] = [];
    const walk = (d: string) => {
      for (const e of fs.readdirSync(d, { withFileTypes: true })) {
        if (e.name === 'node_modules' || e.name === 'dist' || e.name === '.git') continue;
        const p = path.join(d, e.name);
        if (e.isDirectory()) walk(p);
        else if (/^(MatterCard|IssueLine|WorkItemRow|WorklistRow|WorklistLine|SignalRow)\.tsx?$/.test(e.name))
          found.push(p);
      }
    };
    roots.forEach(walk);
    expect(found.map(f => path.basename(f)).sort()).toEqual(['IssueLine.tsx', 'MatterCard.tsx']);
    expect(
      found.every(f =>
        f.includes(`${path.sep}Spaarke.UI.Components${path.sep}src${path.sep}components${path.sep}Worklist${path.sep}`)
      )
    ).toBe(true);
  });

  it('only MatterCard is exported as a component (IssueLine is its part, not a second row component)', async () => {
    const barrel = await import('..');
    expect(Object.keys(barrel).filter(k => /^[A-Z]/.test(k))).toEqual(['MatterCard']);
  });
});

describe('due-state tone palette (SmartTodo: overdue red / 0-3d dark orange / 4-7d yellow / 8-10d grey)', () => {
  const colorOf = (item: WorklistItem, dark = false) => {
    const { unmount } = renderThemed(<MatterCard core={matterCore} items={[item]} today={TODAY} />, dark);
    const el = screen.getByTestId('issue-timing');
    const out = { tone: el.getAttribute('data-tone'), color: getComputedStyle(el).color };
    unmount();
    return out;
  };
  const doItem = (dueDate: string): WorklistItem => ({ ...overdueTaskItem, dueDate });

  const EXPECTED: Array<[string, WorklistItem, string, string]> = [
    ['overdue', doItem('2026-10-04'), 'overdue', 'var(--colorPaletteRedForeground1)'],
    ['0-3 days', doItem('2026-10-12'), '3d', 'var(--colorPaletteDarkOrangeForeground1)'],
    ['4-7 days', doItem('2026-10-14'), '7d', 'var(--colorPaletteYellowForeground1)'],
    ['8-10 days', doItem('2026-10-18'), '10d', 'var(--colorNeutralForeground2)'],
    ['11+ days', doItem('2026-12-01'), 'none', 'var(--colorNeutralForeground3)'],
    ['Decide age', pathBItem, 'age', 'var(--colorNeutralForeground3)'],
  ];

  it.each(EXPECTED)('%s takes its tone token', (_label, item, tone, token) => {
    expect(colorOf(item)).toEqual({ tone, color: token });
  });

  it('the four urgency tiers each have a distinct colour (no two tiers swapped or merged)', () => {
    const colors = EXPECTED.slice(0, 4).map(([, item]) => colorOf(item).color);
    expect(new Set(colors).size).toBe(4);
  });

  it('every tier text colour has WCAG AA contrast (4.5:1) on the card background in light and dark', () => {
    const lum = (hex: string) => {
      const [r, g, b] = [1, 3, 5]
        .map(i => parseInt(hex.slice(i, i + 2), 16) / 255)
        .map(v => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4));
      return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    };
    const ratio = (a: string, b: string) => {
      const [hi, lo] = [lum(a), lum(b)].sort((x, y) => y - x);
      return (hi + 0.05) / (lo + 0.05);
    };
    const tokensByTier = [
      'colorPaletteRedForeground1',
      'colorPaletteDarkOrangeForeground1',
      'colorPaletteYellowForeground1',
      'colorNeutralForeground2',
      'colorNeutralForeground3',
    ] as const;
    for (const theme of [webLightTheme, webDarkTheme]) {
      for (const t of tokensByTier) {
        expect({ t, ok: ratio(theme[t], theme.colorNeutralBackground1) >= 4.5 }).toEqual({ t, ok: true });
      }
    }
  });

  it('the colour is a theme variable in dark mode too (the host provider supplies the dark value)', () => {
    expect(colorOf(doItem('2026-10-04'), true).color).toBe('var(--colorPaletteRedForeground1)');
    expect(webDarkTheme.colorPaletteRedForeground1).not.toBe(webLightTheme.colorPaletteRedForeground1);
    expect(webDarkTheme.colorPaletteYellowForeground1).not.toBe(webLightTheme.colorPaletteYellowForeground1);
  });
});

describe('focus ring', () => {
  it('a focused line shows a visible ring: focus-visible outline style, width and token colour', () => {
    renderThemed(<MatterCard core={matterCore} items={[pathBItem]} today={TODAY} />);
    const line = screen.getByTestId('issue-line');
    const focusCss = Array.from(document.styleSheets)
      .flatMap(sh => Array.from((sh as CSSStyleSheet).cssRules))
      .filter(r => (r as CSSStyleRule).selectorText?.endsWith(':focus-visible'))
      .filter(r => Array.from(line.classList).some(c => (r as CSSStyleRule).selectorText === `.${c}:focus-visible`))
      .map(r => r.cssText)
      .join('\n');
    expect(focusCss).toContain('outline-style: solid');
    expect(focusCss).toContain('outline-width: var(--strokeWidthThick)');
    expect(focusCss).toContain('outline-color: var(--colorStrokeFocus2)');
  });
});
