/**
 * Triage categories table layout contract (task 201).
 *
 * Defect: the table lays its columns out equally (Fluent's fixed table layout), so Guidance got the same ~120 px as the narrow
 * columns and a ~140-character guidance preview wrapped into about twelve lines, making every row ~220 px tall while Category, Read by
 * and Last 30 days had spare width. The fix gives Guidance the largest share of the width and lets long words wrap.
 *
 * jsdom cannot measure layout, so this pins what it can see: the column widths from the stylesheet Griffel injects.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { ClassificationArea } from '..';
import type { ClassificationList, ClassificationService } from '..';

const GUIDANCE =
  'Emails that propose, confirm or question a change to the agreed rates, fees, scope of work or staffing of a matter, including estimates.';

const data: ClassificationList = {
  categories: [
    {
      id: 'c1',
      name: 'Fee / rate change',
      guidance: GUIDANCE,
      enabled: true,
      version: 1,
      volume30Days: 1,
      recall: {
        status: 'NotMeasured',
        recall: null,
        labelledSetSize: null,
        measuredOn: null,
        measuredBeforeLastEdit: false,
      },
      readBy: [],
    },
  ],
  recallFloor: { percent: 80, minItems: 50 },
  volumeWindowDays: 30,
  volumeAvailable: true,
  referencesAvailable: true,
};

const service = { list: jest.fn(async () => data) } as unknown as ClassificationService;

const widthOf = (el: Element): number => parseFloat(getComputedStyle(el).width);

const renderArea = () =>
  render(
    <FluentProvider theme={webLightTheme}>
      <ClassificationArea service={service} />
    </FluentProvider>
  );

describe('ClassificationArea table layout (task 201)', () => {
  it('shows the guidance preview text', async () => {
    renderArea();
    const row = await screen.findByTestId('classification-row-c1');
    expect(row).toHaveTextContent(GUIDANCE.slice(0, 100));
  });

  // Fix round 1: percentage widths starved the fixed-content columns at the Console's ~600 px centre pane (Edit, 10%, was narrower than the
  // small button's 64 px min-width; the switch did not fit). Fixed-content columns carry a PIXEL width sized to content + 16 px padding.
  const MIN_PX = [
    ['Category', 0, 88],
    ['On / Off', 1, 104],
    ['Read by', 3, 72],
    ['Last 30 days', 4, 72],
    ['Recall', 5, 104],
    ['Edit', 6, 80],
  ] as const;

  it.each(MIN_PX)('the %s column has a pixel width of at least %i px', async (_label, index, min) => {
    renderArea();
    const row = await screen.findByTestId('classification-row-c1');
    const headers = screen.getAllByRole('columnheader');
    expect(headers).toHaveLength(7);
    const raw = getComputedStyle(headers[index]).width;
    expect(raw).toMatch(/px$/);
    expect(parseFloat(raw)).toBeGreaterThanOrEqual(min);
    expect(getComputedStyle(row.querySelectorAll('td, [role="cell"]')[index]).width).toBe(raw);
  });

  it('gives the Guidance column no fixed width, so it takes all the width the fixed columns leave', async () => {
    renderArea();
    await screen.findByTestId('classification-row-c1');
    const headers = screen.getAllByRole('columnheader');
    expect(headers[2]).toHaveTextContent('Guidance');
    expect(getComputedStyle(headers[2]).width).not.toMatch(/(px|%)$/);
    const fixed = headers.filter((_h, i) => i !== 2).map(widthOf);
    expect(fixed.every(w => !Number.isNaN(w))).toBe(true);
    // 520 px of fixed columns: nothing overflows from a 560 px table
    expect(fixed.reduce((a, b) => a + b, 0)).toBeLessThanOrEqual(520);
  });

  it('breaks words mid-word only in the Guidance cell, never on every cell', async () => {
    renderArea();
    const row = await screen.findByTestId('classification-row-c1');
    const cells = Array.from(row.querySelectorAll('td, [role="cell"]'));
    expect(cells).toHaveLength(7);
    cells.forEach((cell, index) => {
      const wrap = getComputedStyle(cell).overflowWrap;
      if (index === 2) expect(wrap).toBe('anywhere');
      else expect(wrap).not.toBe('anywhere');
    });
  });
});
