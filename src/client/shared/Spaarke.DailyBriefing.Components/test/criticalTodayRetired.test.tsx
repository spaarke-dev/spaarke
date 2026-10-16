/**
 * Task 062 (spaarke-ontology-platform-r1; FR-31, FR-32): "Critical Today" is retired as a list and the model-chosen "Top action" is
 * removed from the Briefing. sprk_highpriority is a rank input of the worklist read route and sprk_monitor a subscription to Know, so
 * nothing the list meant is lost, but no surface lists flagged records and no model picks what to do first.
 *
 * Mounts the real DailyBriefingApp with a /render answer that STILL carries the retired fields (a stale server or a deployed
 * BRIEF-NARRATE-TLDR row not yet re-synced): the widget must ignore them, not render them.
 *
 * Classification (ADR-038 section 7): MAINTAIN - the negative guard for the retired surfaces.
 */
import * as React from 'react';
import * as fs from 'fs';
import * as path from 'path';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { DailyBriefingApp } from '../src/components/DailyBriefingApp';
import { authenticatedFetch } from '@spaarke/auth';

class ResizeObserverStub {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}
(window as unknown as { ResizeObserver: unknown }).ResizeObserver = ResizeObserverStub;

const SRC = path.resolve(__dirname, '../src');

function staleRenderResponse(): { json: () => Promise<unknown> } {
  return {
    json: async () => ({
      tldr: {
        summary: 'You have 3 items to do.',
        keyTakeaways: ['2 of them are past due.'],
        topAction: 'Review the Flagged Co engagement letter',
        categoryCount: 1,
        priorityItemCount: 0,
        itemRefs: [],
      },
      channelNarratives: [
        {
          category: 'matters',
          bullets: [
            {
              narrative: 'Channel Matter',
              itemIds: ['item-1'],
              primaryEntityType: 'sprk_matter',
              primaryEntityId: '11111111-1111-1111-1111-111111111111',
              primaryEntityName: 'Channel Matter',
            },
          ],
        },
      ],
      generatedAtUtc: '2026-10-16T08:00:00Z',
      highPriorityItems: [
        {
          entityType: 'sprk_matter',
          entityId: '22222222-2222-2222-2222-222222222222',
          name: 'Flagged Co',
          highPriority: true,
          monitor: true,
          kindLabel: 'Matter',
          action: 'Overdue',
          reason: 'Both',
        },
      ],
    }),
  };
}

function walk(dir: string): string[] {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap(e => {
    const full = path.join(dir, e.name);
    return e.isDirectory() ? walk(full) : [full];
  });
}

const codeOnly = (source: string): string => source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:])\/\/.*$/gm, '$1');

describe('Critical Today is retired and no model picks the top action (task 062)', () => {
  beforeEach(() => {
    (authenticatedFetch as jest.Mock).mockReset();
    (authenticatedFetch as jest.Mock).mockImplementation(() => Promise.resolve(staleRenderResponse()));
  });

  it('renders the summary and the channel rows, but no Critical Today list, no Critical tile and no Top action', async () => {
    render(
      <FluentProvider theme={webLightTheme}>
        <DailyBriefingApp params={{}} />
      </FluentProvider>
    );

    expect(await screen.findByText('You have 3 items to do.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Channel Matter/ })).toBeInTheDocument();

    expect(screen.queryByText(/critical today/i)).toBeNull();
    expect(screen.queryByText(/high priority/i)).toBeNull();
    expect(screen.queryByText('Critical')).toBeNull();
    expect(screen.queryByText(/top action/i)).toBeNull();
    expect(screen.queryByText(/Flagged Co/)).toBeNull();
  });

  it('the package no longer contains a HighPrioritySection or a topAction field', () => {
    expect(fs.existsSync(path.join(SRC, 'components', 'HighPrioritySection.tsx'))).toBe(false);

    const offenders = walk(SRC)
      .filter(f => /\.(ts|tsx)$/.test(f))
      .filter(f =>
        /HighPrioritySection|topAction|Top action|highPriorityItems|highPriorityFailedEntityTypes/.test(
          codeOnly(fs.readFileSync(f, 'utf8'))
        )
      )
      .map(f => path.relative(SRC, f));
    expect(offenders).toEqual([]);
  });
});
