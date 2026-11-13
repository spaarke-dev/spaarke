/**
 * R4 task 009, D-12 (#2769): the Documents grid's columns overlapped at the record modal's width. Fluent's DataGrid lays
 * a row out as flex cells that share the width equally (`flex: 1 1 0px`), and a cell's content (the type badge, the
 * summary button with its fixed 300px cap, the row actions) kept its own width and spilled into the next column.
 *
 * The contract: the name takes the flexible width (it grows twice as fast as the summary), type, date and actions are
 * fixed, every cell clips, and the name, type and summary truncate with an ellipsis. Header cell and body cell of a column
 * carry the same sizing. jsdom has no layout, so this pins the CSS contract: the element carries the atomic classes the
 * declarations compile to (Griffel names a class by property + value; `mergeClasses` keeps one class per property, so a
 * class present here is the one in effect). Fails on the old grid: no column had a sizing class of its own.
 *
 * The real DocumentLibrary and Fluent grid run; only the network seam (bffApiCall) is substituted.
 */
import * as React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { FluentProvider, makeStyles, webLightTheme } from '@fluentui/react-components';
import { AccessLevel } from '../src/types';

const { bffApiCall } = vi.hoisted(() => ({ bffApiCall: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall, bffApiBlob: vi.fn() };
});

import { DocumentLibrary } from '../src/components/DocumentLibrary';

const SLOW = { timeout: 60000 };

const ROW = {
  sprk_documentid: 'd-1',
  sprk_name: 'A very long engagement letter name that cannot fit in its column at the modal width.pdf',
  sprk_documenttype: 'Engagement letter (signed counterpart)',
  sprk_summary: 'A summary long enough to need more than the space its column has at the record modal width.',
  createdon: '2026-01-05T10:00:00Z',
};

/** The atomic classes a style object compiles to (Griffel class names are a hash of property + value + media). */
type Style = Parameters<typeof makeStyles>[0][string];

function atomicClassesOf(style: Style): string[] {
  const useProbe = makeStyles({ probe: style });
  let classes = '';
  const Probe: React.FC = () => {
    classes = useProbe().probe;
    return null;
  };
  const { unmount } = render(<Probe />);
  unmount();
  // The `___…` class names the makeStyles call itself, not a declaration.
  return classes.split(/\s+/).filter(c => c && !c.startsWith('___'));
}

function expectStyle(el: Element, style: Style): void {
  const missing = atomicClassesOf(style).filter(c => !el.classList.contains(c));
  expect(missing).toEqual([]);
}

const FLEXIBLE = (grow: number): Style => ({
  flexGrow: grow,
  flexShrink: 1,
  flexBasis: '0px',
  minWidth: 0,
  overflow: 'hidden',
});
const FIXED = (basis: string): Style => ({
  flexGrow: 0,
  flexShrink: 0,
  flexBasis: basis,
  minWidth: 0,
  overflow: 'hidden',
});

/** Column order: Name, Type, Date Added, AI Summary, actions. */
const COLUMNS = [
  ['Name', FLEXIBLE(2)],
  ['Type', FIXED('120px')],
  ['Date Added', FIXED('112px')],
  ['AI Summary', FLEXIBLE(1)],
  ['actions', FIXED('112px')],
] as const;

async function renderGrid(): Promise<HTMLElement> {
  bffApiCall.mockImplementation(async (path: string) => {
    if (path.endsWith('/documents')) return { value: [ROW] };
    throw new Error(`unexpected path ${path}`);
  });
  render(
    <FluentProvider theme={webLightTheme}>
      <DocumentLibrary projectId="p-d12" accessLevel={AccessLevel.Collaborate} />
    </FluentProvider>
  );
  await screen.findByText(ROW.sprk_name);
  return screen.getByRole('grid');
}

beforeEach(() => {
  bffApiCall.mockReset();
});

describe('Documents grid column sizing (D-12)', () => {
  it(
    'each column has its sizing on the header cell and on the body cell: name flexible, type / date / actions fixed',
    async () => {
      const grid = await renderGrid();
      const [headerRow, bodyRow] = within(grid).getAllByRole('row');
      const headers = within(headerRow).getAllByRole('columnheader');
      const cells = within(bodyRow).getAllByRole('gridcell');
      expect(headers).toHaveLength(COLUMNS.length);
      expect(cells).toHaveLength(COLUMNS.length);

      COLUMNS.forEach(([label, style], i) => {
        if (label !== 'actions') expect(headers[i].textContent).toBe(label);
        expectStyle(headers[i], style);
        expectStyle(cells[i], style);
      });
    },
    SLOW
  );

  it(
    'the name, the type badge and the summary shrink and end in an ellipsis instead of overflowing',
    async () => {
      const grid = await renderGrid();
      const cells = within(within(grid).getAllByRole('row')[1]).getAllByRole('gridcell');

      // Name: the row's flex container may shrink, and so may its text (Fluent's truncating Text).
      const nameText = within(cells[0]).getByText(ROW.sprk_name);
      expectStyle(nameText.parentElement as HTMLElement, { minWidth: 0, overflow: 'hidden' });
      expectStyle(nameText, { minWidth: 0 });
      // A truncated name can still be read in full (review round 1, F2).
      expect(nameText.getAttribute('title')).toBe(ROW.sprk_name);

      // Type: the badge is capped at its cell, its text truncates, and the full type is in its title.
      const typeText = within(cells[1]).getByText(ROW.sprk_documenttype);
      expectStyle(typeText, { minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' });
      const badge = typeText.parentElement as HTMLElement;
      expectStyle(badge, { maxWidth: '100%', minWidth: 0 });
      expect(badge.getAttribute('title')).toBe(ROW.sprk_documenttype);

      // Summary: the button is capped at its cell (no fixed 300px), and its text truncates.
      const summaryText = within(cells[3]).getByText(ROW.sprk_summary);
      expectStyle(summaryText, { minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' });
      const summaryButton = summaryText.closest('button') as HTMLButtonElement;
      expectStyle(summaryButton, { maxWidth: '100%', minWidth: 0 });
      expect(summaryButton.style.maxWidth).toBe('');
    },
    SLOW
  );
});
