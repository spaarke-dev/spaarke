/**
 * TldrSection component tests — R5 task 014 (FR-A5, binary anchor resolution).
 *
 * Placed at the package-root `test/` directory (not `src/components/__tests__/`) to match
 * this package's established test-file convention — see the sibling
 * `NarrativeCitedText.test.tsx` / `NarrativeBullet.test.tsx` / `HighPrioritySection.badges.test.ts`,
 * all of which live here rather than under `src/components/__tests__/`. This is a deliberate
 * deviation from the task POML's suggested `<outputs>` path, noted per the directional
 * step-mode allowance (task-execute Step 8.5 / root CLAUDE.md §8.5).
 *
 * This suite covers the WIDGET half of the FR-A5 binary contract (the server half —
 * `DailyBriefingNarrator.BuildTldrItemRefs` — is covered by
 * `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Narrators/DailyBriefingNarratorItemRefsTests.cs`):
 *
 *   - A non-resolving anchor (itemId absent from `resolvableItems`) is DROPPED — rendered as
 *     plain, unlinked text with zero residue. No warn badge, confidence indicator, or
 *     withheld-content placeholder exists ANYWHERE in this component (FR-A6 — there is no
 *     threshold/warn-withhold band to test the absence of; this suite asserts that no such
 *     affordance ever renders, confirming the "doesn't exist" claim rather than a "disabled"
 *     one).
 *   - A resolving anchor (itemId present) is wrapped as a clickable Link pointing at the
 *     resolved entityType/entityId — matched in whichever of summary/keyTakeaways/topAction
 *     contains the anchor text.
 *   - Absent/empty `itemRefs` or `resolvableItems` never crashes rendering (safe defaults).
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';

import { TldrSection } from '../src/components/TldrSection';
import type { TldrSectionProps, TldrResolvableItem } from '../src/components/TldrSection';

// NOTE: "Acme Matter" appears ONLY in `summary` by default so tests using `getByRole`
// (singular) don't collide with multiple matches — dedicated tests below explicitly set
// keyTakeaways/topAction to exercise those fields independently.
function baseTldr(
  overrides: Partial<NonNullable<TldrSectionProps['tldr']>> = {}
): NonNullable<TldrSectionProps['tldr']> {
  return {
    summary: 'You have 2 notifications today, including a follow-up on Acme Matter.',
    keyTakeaways: ['Review the overdue filings.'],
    topAction: 'Check your notifications.',
    categoryCount: 2,
    priorityItemCount: 1,
    ...overrides,
  };
}

function renderTldr(props: Partial<TldrSectionProps> = {}, theme = webLightTheme): ReturnType<typeof render> {
  const merged: TldrSectionProps = {
    tldr: baseTldr(),
    isLoading: false,
    isUnavailable: false,
    unavailableReason: null,
    error: null,
    generatedAt: null,
    ...props,
  };
  return render(
    <FluentProvider theme={theme}>
      <TldrSection {...merged} />
    </FluentProvider>
  );
}

const RESOLVABLE: Record<string, TldrResolvableItem> = {
  'item-1': { entityType: 'sprk_matter', entityId: '11111111-1111-1111-1111-111111111111' },
};

describe('TldrSection — binary anchor resolution (R5 task 014 / FR-A5)', () => {
  it('drops a non-resolving anchor: itemId absent from resolvableItems renders as plain text, no link', () => {
    renderTldr({
      tldr: baseTldr({
        itemRefs: [{ anchorText: 'Acme Matter', itemId: 'item-does-not-exist' }],
      }),
      resolvableItems: RESOLVABLE, // populated, but does NOT contain 'item-does-not-exist'
    });

    // The anchor text still renders — as plain prose, exactly as if itemRefs had never
    // named it. No link, no residue.
    expect(screen.getByText(/Acme Matter/)).toBeInTheDocument();
    expect(screen.queryByRole('link')).toBeNull();
  });

  it('drops every anchor when resolvableItems is omitted entirely (safe default, not a crash)', () => {
    expect(() =>
      renderTldr({
        tldr: baseTldr({ itemRefs: [{ anchorText: 'Acme Matter', itemId: 'item-1' }] }),
        // resolvableItems intentionally omitted
      })
    ).not.toThrow();

    expect(screen.queryByRole('link')).toBeNull();
    expect(screen.getByText(/Acme Matter/)).toBeInTheDocument();
  });

  it('resolves an anchor whose itemId IS present in resolvableItems and links it to the resolved target', () => {
    const onOpenRecord = jest.fn();
    renderTldr({
      tldr: baseTldr({
        itemRefs: [{ anchorText: 'Acme Matter', itemId: 'item-1' }],
      }),
      resolvableItems: RESOLVABLE,
      onOpenRecord,
    });

    const link = screen.getByRole('link', { name: /Acme Matter/i });
    expect(link).toBeInTheDocument();

    fireEvent.click(link);
    expect(onOpenRecord).toHaveBeenCalledWith('sprk_matter', '11111111-1111-1111-1111-111111111111');
  });

  it('resolves an anchor that only appears in keyTakeaways (not the summary)', () => {
    renderTldr({
      tldr: baseTldr({
        summary: 'You have 2 notifications today.',
        keyTakeaways: ['Follow up needed on Acme Matter.'],
        topAction: '',
        itemRefs: [{ anchorText: 'Acme Matter', itemId: 'item-1' }],
      }),
      resolvableItems: RESOLVABLE,
    });

    expect(screen.getByRole('link', { name: /Acme Matter/i })).toBeInTheDocument();
  });

  it('resolves an anchor that only appears in topAction', () => {
    renderTldr({
      tldr: baseTldr({
        summary: 'You have 2 notifications today.',
        keyTakeaways: [],
        topAction: 'Review the filing for Acme Matter.',
        itemRefs: [{ anchorText: 'Acme Matter', itemId: 'item-1' }],
      }),
      resolvableItems: RESOLVABLE,
    });

    expect(screen.getByRole('link', { name: /Acme Matter/i })).toBeInTheDocument();
  });

  it('renders normally with no itemRefs at all (backward compatible with pre-R5-task-014 responses)', () => {
    renderTldr({
      tldr: baseTldr({ itemRefs: undefined }),
    });

    expect(screen.getByText(/Acme Matter/)).toBeInTheDocument();
    expect(screen.queryByRole('link')).toBeNull();
  });

  it('renders normally with an empty itemRefs array', () => {
    renderTldr({
      tldr: baseTldr({ itemRefs: [] }),
      resolvableItems: RESOLVABLE,
    });

    expect(screen.getByText(/Acme Matter/)).toBeInTheDocument();
    expect(screen.queryByRole('link')).toBeNull();
  });
});

describe('TldrSection — no groundedness-score warning/withhold path exists (FR-A6)', () => {
  it('never renders a confidence/warning/withheld affordance for a dropped anchor', () => {
    renderTldr({
      tldr: baseTldr({
        itemRefs: [{ anchorText: 'Acme Matter', itemId: 'item-does-not-exist' }],
      }),
      resolvableItems: RESOLVABLE,
    });

    // No score/warning/withhold vocabulary anywhere in the rendered DOM.
    const forbidden = [
      /low.?confidence/i,
      /unverified/i,
      /withheld/i,
      /groundedness/i,
      /citation unavailable/i,
      /\bunlinked\b/i,
    ];
    for (const pattern of forbidden) {
      expect(screen.queryByText(pattern)).toBeNull();
    }
    // No Fluent Badge beyond the existing "AI Insight" badge (i.e., no second/confidence badge).
    expect(screen.getAllByText('AI Insight')).toHaveLength(1);
  });

  it('a dropped anchor does not change categoryCount/priorityItemCount footer rendering', () => {
    renderTldr({
      tldr: baseTldr({
        itemRefs: [{ anchorText: 'Acme Matter', itemId: 'item-does-not-exist' }],
      }),
      resolvableItems: RESOLVABLE,
    });
    expect(screen.getByText(/2 categories, 1 priority items/)).toBeInTheDocument();
  });
});

describe('TldrSection — ADR-021 dark mode + token compliance', () => {
  it('renders under webDarkTheme with a resolved link', () => {
    renderTldr(
      {
        tldr: baseTldr({ itemRefs: [{ anchorText: 'Acme Matter', itemId: 'item-1' }] }),
        resolvableItems: RESOLVABLE,
      },
      webDarkTheme
    );
    expect(screen.getByRole('link', { name: /Acme Matter/i })).toBeInTheDocument();
  });

  it('source has zero hard-coded hex color literals', () => {
    const fs = require('fs') as typeof import('fs');
    const path = require('path') as typeof import('path');
    const full = path.resolve(__dirname, '../src/components/TldrSection.tsx');
    const source = fs.readFileSync(full, 'utf8');
    const codeOnly = source
      .replace(/\/\*[\s\S]*?\*\//g, '') // block comments
      .replace(/(^|[^:])\/\/.*$/gm, '$1'); // line comments
    const hexColorRe = /[\s:'"(]#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\b/;
    expect(codeOnly.match(hexColorRe)).toBeNull();
  });
});
