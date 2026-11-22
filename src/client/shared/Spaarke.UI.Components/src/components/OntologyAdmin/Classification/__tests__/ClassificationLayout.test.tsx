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

  it('gives the Guidance column the largest share of the width', async () => {
    renderArea();
    await screen.findByTestId('classification-row-c1');
    const headers = screen.getAllByRole('columnheader');
    const guidance = headers.findIndex(h => h.textContent === 'Guidance');
    expect(guidance).toBeGreaterThanOrEqual(0);
    expect(Number.isNaN(widthOf(headers[guidance]))).toBe(false);
    const others = headers
      .filter((_h, i) => i !== guidance)
      .map(widthOf)
      .filter(w => !Number.isNaN(w));
    expect(widthOf(headers[guidance])).toBeGreaterThan(Math.max(...others));
  });
});
