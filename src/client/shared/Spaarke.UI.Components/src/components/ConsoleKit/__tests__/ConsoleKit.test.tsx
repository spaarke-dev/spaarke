/**
 * ConsoleKit — unit tests (task 057). Covers the stated contracts:
 *  - EvidenceLine tiers, context line, null fact = Missing (never 0 / blank), no confidence percentage
 *  - state table covers all nine states; Routine displays as Done (R-4); closure mapping (R-14)
 *  - StatusBar / RecordRow: mapped tone, id/class/who/when, no edit/delete control (FR-21, R-13)
 *  - AggregateCard: count + link; null count = Missing
 *  - ADR-021: no colour literals in kit sources; every component renders in dark theme
 */

import '@testing-library/jest-dom';
import * as fs from 'fs';
import * as path from 'path';
import * as React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

import {
  AggregateCard,
  CONSOLE_STATE_BADGES,
  EvidenceLine,
  RecordRow,
  StatusBar,
  resolveConsoleState,
  resolveDecisionRecordState,
} from '..';
import type { ConsoleState } from '..';
import { decisionRecords, evidenceRefs } from '../__fixtures__/consoleKitFixtures';

function renderThemed(ui: React.ReactElement, dark = false) {
  return render(<FluentProvider theme={dark ? webDarkTheme : webLightTheme}>{ui}</FluentProvider>);
}

const ALL_STATES: ConsoleState[] = [
  'Open',
  'Decided',
  'Done',
  'Dismissed',
  'ClearedItself',
  'Superseded',
  'RuleRetired',
  'Authorized',
  'Denied',
];

describe('EvidenceLine', () => {
  it('renders a Fact plainly with source, as-of and the clause it tested', () => {
    const e = evidenceRefs.fact;
    renderThemed(<EvidenceLine tier={e.tier} text={e.text} source={e.source} asOf={e.asOf} clause={e.clause} />);
    const line = screen.getByTestId('evidence-line');
    expect(line).toHaveAttribute('data-tier', 'Fact');
    expect(screen.getByText(e.text as string)).toBeInTheDocument();
    expect(line).toHaveTextContent('tested: exists: invoice received');
    expect(line).not.toHaveTextContent('context, not tested');
  });

  it('labels a stored Observation as Interpretation', () => {
    const e = evidenceRefs.interpretation;
    renderThemed(<EvidenceLine tier={e.tier} text={e.text} clause={e.clause} />);
    expect(screen.getByTestId('evidence-line')).toHaveAttribute('data-tier', 'Interpretation');
    expect(screen.getByText('Interpretation')).toBeInTheDocument();
  });

  it('marks evidence with no clause as "context, not tested"', () => {
    const e = evidenceRefs.context;
    renderThemed(<EvidenceLine tier={e.tier} text={e.text} source={e.source} asOf={e.asOf} />);
    expect(screen.getByTestId('evidence-line')).toHaveTextContent('context, not tested');
  });

  it('renders a null fact as Missing, never 0 or blank', () => {
    const e = evidenceRefs.nullFact;
    renderThemed(<EvidenceLine tier={e.tier} text={e.text} subject="Spend to date" clause={e.clause} />);
    const line = screen.getByTestId('evidence-line');
    expect(line).toHaveAttribute('data-tier', 'Missing');
    expect(screen.getByText('Missing')).toBeInTheDocument();
    expect(line).toHaveTextContent('Spend to date: no value recorded');
    expect(line).not.toHaveTextContent(/\b0\b/);
  });

  it('treats an empty string as Missing but a real 0 as a fact', () => {
    const { unmount } = renderThemed(<EvidenceLine tier="Fact" text="" />);
    expect(screen.getByTestId('evidence-line')).toHaveAttribute('data-tier', 'Missing');
    unmount();
    renderThemed(<EvidenceLine tier="Fact" text={0} />);
    expect(screen.getByTestId('evidence-line')).toHaveAttribute('data-tier', 'Fact');
    expect(screen.getByText('0')).toBeInTheDocument();
  });

  it('shows only a stored Observation as Interpretation; an unknown or Missing tier shows Missing', () => {
    const cases: [string, string][] = [
      ['Observation', 'Interpretation'],
      ['Interpretation', 'Interpretation'],
      ['Missing', 'Missing'],
      ['Guess', 'Missing'],
    ];
    for (const [tier, shown] of cases) {
      const { unmount } = renderThemed(<EvidenceLine tier={tier as never} text="x" />);
      expect(screen.getByTestId('evidence-line')).toHaveAttribute('data-tier', shown);
      unmount();
    }
  });

  it('renders a NaN value as Missing, not "NaN"', () => {
    renderThemed(<EvidenceLine tier="Fact" text={NaN} />);
    expect(screen.getByTestId('evidence-line')).toHaveAttribute('data-tier', 'Missing');
    expect(screen.getByTestId('evidence-line')).not.toHaveTextContent('NaN');
  });

  it('shows no confidence percentage', () => {
    const e = evidenceRefs.interpretation;
    // @ts-expect-error confidence is deliberately not part of the contract (#5)
    renderThemed(<EvidenceLine tier={e.tier} text={e.text} confidence={0.94} />);
    expect(screen.getByTestId('evidence-line').textContent).not.toMatch(/%|0\.94/);
  });
});

describe('Console state table', () => {
  it('maps every one of the nine states to a label and a StatusBadge tone', () => {
    expect(Object.keys(CONSOLE_STATE_BADGES).sort()).toEqual([...ALL_STATES].sort());
    for (const s of ALL_STATES) {
      expect(CONSOLE_STATE_BADGES[s].label).toBeTruthy();
      expect(['neutral', 'info', 'success', 'warning', 'critical']).toContain(CONSOLE_STATE_BADGES[s].tone);
    }
  });

  it('derives a Decision Record state from outcome + class, and Routine reads as Done (R-4)', () => {
    expect(resolveDecisionRecordState('Authorized', 'Judgement')).toBe('Authorized');
    expect(resolveDecisionRecordState('Denied', 'Judgement')).toBe('Denied');
    expect(resolveDecisionRecordState('Dismissed', 'Dismissal')).toBe('Dismissed');
    expect(resolveDecisionRecordState('Authorized', 'Routine')).toBe('Done');
    expect(resolveDecisionRecordState('Denied', 'Routine')).toBe('Done');
    expect(resolveDecisionRecordState(null)).toBe('Open');
  });

  it('derives closure states from stored columns (R-14) and shows Routine as Done (R-4)', () => {
    expect(resolveConsoleState(null)).toBe('Open');
    expect(resolveConsoleState('Acted', 'Judgement')).toBe('Decided');
    expect(resolveConsoleState('Acted', 'Routine')).toBe('Done');
    expect(resolveConsoleState('Dismissed', 'Dismissal')).toBe('Dismissed');
    expect(resolveConsoleState('ConditionCleared')).toBe('ClearedItself');
    expect(resolveConsoleState('Superseded')).toBe('Superseded');
    expect(resolveConsoleState('PolicyRetired')).toBe('RuleRetired');
  });
});

describe('StatusBar', () => {
  it.each([
    ['Decided', 'success'],
    ['Done', 'success'],
    ['Dismissed', 'warning'],
    ['ClearedItself', 'info'],
    ['Superseded', 'neutral'],
    ['RuleRetired', 'neutral'],
  ] as [ConsoleState, string][])('renders %s with the %s tone', (state, tone) => {
    renderThemed(<StatusBar state={state} />);
    expect(screen.getByTestId('status-bar')).toHaveAttribute('data-state', state);
    expect(screen.getByTestId('status-badge')).toHaveAttribute('data-tone', tone);
    expect(screen.getByText(CONSOLE_STATE_BADGES[state].label)).toBeInTheDocument();
  });

  it('uses the info intent for Dismissed and the badge-matched intent elsewhere', () => {
    const { unmount } = renderThemed(<StatusBar state="Dismissed" />);
    expect(screen.getByTestId('status-bar')).toHaveAttribute('data-intent', 'info');
    unmount();
    renderThemed(<StatusBar state="Denied" />);
    expect(screen.getByTestId('status-bar')).toHaveAttribute('data-intent', 'error');
  });

  it('shows record id, class, who and when where present', () => {
    const r = decisionRecords[0];
    renderThemed(
      <StatusBar
        state="Decided"
        recordId={r.sprk_decisionnumber}
        recordClass={r.sprk_recordclass}
        decidedBy={r.confirmedByName}
        decidedOn={r.sprk_decidedon}
      />
    );
    const bar = screen.getByTestId('status-bar');
    expect(bar).toHaveTextContent('DR-00042');
    expect(bar).toHaveTextContent('Judgement');
    expect(bar).toHaveTextContent('by A. Reviewer');
  });

  it('omits who/when when there is no person (a condition that cleared on its own)', () => {
    renderThemed(<StatusBar state="ClearedItself" />);
    expect(screen.getByTestId('status-bar')).not.toHaveTextContent('by ');
  });

  it('forwards a ref and is focusable programmatically', () => {
    const ref = React.createRef<HTMLDivElement>();
    renderThemed(<StatusBar ref={ref} state="Decided" />);
    expect(ref.current).not.toBeNull();
    ref.current?.focus();
    expect(document.activeElement).toBe(ref.current);
  });

  it('has no buttons or links (no edit or delete affordance)', () => {
    renderThemed(<StatusBar state="Decided" recordId="DR-1" />);
    expect(screen.queryAllByRole('button')).toHaveLength(0);
    expect(screen.queryAllByRole('link')).toHaveLength(0);
  });
});

describe('RecordRow', () => {
  it('renders one line with state, headline, id, class, who and when', () => {
    const r = decisionRecords[1];
    renderThemed(
      <RecordRow
        recordId={r.sprk_decisionnumber}
        title={r.sprk_name}
        state={resolveDecisionRecordState(r.sprk_decisionoutcome, r.sprk_recordclass)}
        recordClass={r.sprk_recordclass}
        decidedBy={r.confirmedByName}
        decidedOn={r.sprk_decidedon}
      />
    );
    const row = screen.getByTestId('record-row');
    expect(row).toHaveTextContent('Done');
    expect(row).toHaveTextContent(r.sprk_name);
    expect(row).toHaveTextContent('DR-00043 · Routine · A. Reviewer');
  });

  it('calls onOpen with the record id from the Open link', () => {
    const onOpen = jest.fn();
    renderThemed(<RecordRow recordId="DR-0042" title="t" state="Decided" onOpen={onOpen} />);
    fireEvent.click(screen.getByText('Open ›'));
    expect(onOpen).toHaveBeenCalledWith('DR-0042');
  });

  it('keeps the full entry behind a closed-by-default disclosure', () => {
    renderThemed(
      <RecordRow recordId="DR-1" title="t" state="Decided">
        <span>entry body</span>
      </RecordRow>
    );
    const header = screen.getByRole('button', { name: 'Full entry' });
    expect(header).toHaveAttribute('aria-expanded', 'false');
    fireEvent.click(header);
    expect(header).toHaveAttribute('aria-expanded', 'true');
  });

  it('offers no edit or delete control, with or without a detail', () => {
    renderThemed(
      <RecordRow recordId="DR-1" title="t" state="Decided" onOpen={() => undefined}>
        <span>entry body</span>
      </RecordRow>
    );
    const row = screen.getByTestId('record-row');
    expect(row.textContent).not.toMatch(/edit|delete|remove/i);
    const controls = screen.getAllByRole('button').map(b => b.textContent);
    expect(controls).toEqual(['Open ›', 'Full entry']);
  });
});

describe('AggregateCard', () => {
  it('shows the count, the sentence and a link that fires onOpen', () => {
    const onOpen = jest.fn();
    renderThemed(
      <AggregateCard count={7} label="emails waiting for review" linkLabel="Open Email Review" onOpen={onOpen} />
    );
    expect(screen.getByTestId('aggregate-count')).toHaveTextContent('7 emails waiting for review');
    fireEvent.click(screen.getByText('Open Email Review ›'));
    expect(onOpen).toHaveBeenCalledTimes(1);
  });

  it('renders a real zero as 0 and a null count as Missing', () => {
    const { unmount } = renderThemed(
      <AggregateCard count={0} label="emails" linkLabel="Open" onOpen={() => undefined} />
    );
    expect(screen.getByTestId('aggregate-count')).toHaveTextContent('0 emails');
    unmount();
    renderThemed(<AggregateCard count={null} label="emails" linkLabel="Open" onOpen={() => undefined} />);
    expect(screen.getByTestId('aggregate-count')).toHaveTextContent('Missing');
  });

  it('renders a NaN count as Missing, not "NaN"', () => {
    renderThemed(<AggregateCard count={NaN} label="emails" linkLabel="Open" onOpen={() => undefined} />);
    expect(screen.getByTestId('aggregate-count')).toHaveTextContent('Missing');
    expect(screen.getByTestId('aggregate-count')).not.toHaveTextContent('NaN');
  });
});

describe('ADR-021', () => {
  it('renders every kit component under the dark theme', () => {
    renderThemed(
      <>
        <EvidenceLine tier="Fact" text="x" clause="c" />
        <StatusBar state="Decided" recordId="DR-1" />
        <RecordRow recordId="DR-1" title="t" state="Done" />
        <AggregateCard count={1} label="l" linkLabel="go" onOpen={() => undefined} />
      </>,
      true
    );
    expect(screen.getByTestId('evidence-line')).toBeInTheDocument();
    expect(screen.getByTestId('status-bar')).toBeInTheDocument();
    expect(screen.getByTestId('record-row')).toBeInTheDocument();
    expect(screen.getByTestId('aggregate-card')).toBeInTheDocument();
  });

  it('kit sources contain no hex, rgb/hsl or named colour literal', () => {
    const dirs = [path.join(__dirname, '..'), path.join(__dirname, '..', '..', 'StatusBadge')];
    const files = dirs
      .flatMap(d => fs.readdirSync(d).map(f => path.join(d, f)))
      .filter(f => /\.(tsx?|ts)$/.test(f) && fs.statSync(f).isFile());
    expect(files.length).toBeGreaterThan(5);
    const literal =
      /#[0-9a-fA-F]{3,8}\b|\b(?:rgba?|hsla?)\(|(?:color|background|border\w*)\s*:\s*['"](?:white|black|red|green|blue|gray|grey|orange|yellow|purple)['"]/;
    for (const f of files) {
      const code = fs
        .readFileSync(f, 'utf8')
        .split('\n')
        .filter(l => !l.trim().startsWith('*') && !l.trim().startsWith('//'))
        .join('\n');
      expect({ f, hit: literal.test(code) }).toEqual({ f, hit: false });
    }
  });
});
