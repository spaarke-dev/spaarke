/**
 * NarrativeCitedText component tests — R5 task 011 (FR-A1).
 *
 * `buildSegments` itself (the pure text-splitting function) is covered
 * verbatim by `NarrativeCitedText.buildSegments.test.ts` (task 035) — those
 * tests are UNCHANGED by this task and remain the source of truth for the
 * matching rule.
 *
 * This file covers the COMPONENT's contract after the R5 task 011 rewrite:
 * `NarrativeCitedText` now takes a single item's own `regardingName` /
 * `regardingEntityType` / `regardingId` scalars (never an externally
 * supplied `references[]` array), so:
 *   - There is no LLM-authored per-channel narrative field consumed here —
 *     `narrative` + the regarding scalars are always the SAME item's fields.
 *   - The candidate reference `buildSegments` receives is ALWAYS derived
 *     from those same props — never from anywhere else (cross-item pairing
 *     is structurally impossible: the component has no prop shape that could
 *     carry a second item's data).
 *   - The rendered link target ALWAYS equals the passed
 *     regardingEntityType/regardingId (the item's own server-composed link).
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';

import { NarrativeCitedText, hasInlineRegardingMention } from '../src/components/NarrativeCitedText';
import type { NarrativeCitedTextProps } from '../src/components/NarrativeCitedText';

function renderWith(props: NarrativeCitedTextProps, theme = webLightTheme): ReturnType<typeof render> {
  return render(
    <FluentProvider theme={theme}>
      <NarrativeCitedText {...props} />
    </FluentProvider>
  );
}

describe('NarrativeCitedText — deterministic single-item rendering (R5 task 011 / FR-A1)', () => {
  it('renders plain text when no regarding target is supplied', () => {
    renderWith({ narrative: 'Review motion to dismiss.' });
    expect(screen.getByText('Review motion to dismiss.')).toBeInTheDocument();
    expect(screen.queryByRole('link')).toBeNull();
  });

  it('renders plain text (no link) when the regarding name is NOT textually present in narrative — caller owns the fallback link', () => {
    renderWith({
      narrative: 'Review motion to dismiss.',
      regardingName: 'Acme Matter',
      regardingEntityType: 'sprk_matter',
      regardingId: '11111111-1111-1111-1111-111111111111',
    });
    // No link rendered by THIS component for the not-found case (by design —
    // see the component's JSDoc: the caller renders its own fallback link so
    // the same entity's link is never rendered twice for one row).
    expect(screen.queryByRole('link')).toBeNull();
    expect(screen.getByText('Review motion to dismiss.')).toBeInTheDocument();
  });

  it("wraps the regarding name as an inline link when it IS textually present in narrative, and the link target equals the same item's regarding fields", () => {
    const onOpenRecord = jest.fn();
    renderWith({
      narrative: 'Review motion to dismiss for Acme Matter.',
      regardingName: 'Acme Matter',
      regardingEntityType: 'sprk_matter',
      regardingId: '11111111-1111-1111-1111-111111111111',
      onOpenRecord,
    });

    const link = screen.getByRole('link', { name: /Acme Matter/i });
    expect(link).toBeInTheDocument();
    fireEvent.click(link);

    // Link target equals the item's own server-composed link (regardingEntityType/regardingId) — never anything else.
    expect(onOpenRecord).toHaveBeenCalledTimes(1);
    expect(onOpenRecord).toHaveBeenCalledWith('sprk_matter', '11111111-1111-1111-1111-111111111111');
  });

  it('click is a no-op when onOpenRecord is omitted (graceful degradation)', () => {
    renderWith({
      narrative: 'Review motion to dismiss for Acme Matter.',
      regardingName: 'Acme Matter',
      regardingEntityType: 'sprk_matter',
      regardingId: '11111111-1111-1111-1111-111111111111',
    });
    const link = screen.getByRole('link', { name: /Acme Matter/i });
    expect(() => fireEvent.click(link)).not.toThrow();
  });

  it('two independently rendered instances never bleed fields into each other — structurally impossible cross-item pairing', () => {
    const onOpenRecordA = jest.fn();
    const onOpenRecordB = jest.fn();

    render(
      <FluentProvider theme={webLightTheme}>
        <NarrativeCitedText
          narrative="Widget Co needs a response."
          regardingName="Widget Co"
          regardingEntityType="sprk_matter"
          regardingId="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
          onOpenRecord={onOpenRecordA}
        />
        <NarrativeCitedText
          narrative="Gadget Inc filed a motion."
          regardingName="Gadget Inc"
          regardingEntityType="sprk_project"
          regardingId="bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"
          onOpenRecord={onOpenRecordB}
        />
      </FluentProvider>
    );

    fireEvent.click(screen.getByRole('link', { name: /Widget Co/i }));
    expect(onOpenRecordA).toHaveBeenCalledWith('sprk_matter', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa');
    expect(onOpenRecordB).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('link', { name: /Gadget Inc/i }));
    expect(onOpenRecordB).toHaveBeenCalledWith('sprk_project', 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb');
    expect(onOpenRecordA).toHaveBeenCalledTimes(1);
  });

  it('renders null when narrative is empty', () => {
    const { container } = renderWith({ narrative: '' });
    // FluentProvider still renders its own wrapper divs; the component itself
    // contributes nothing (no text, no link).
    expect(screen.queryByRole('link')).toBeNull();
    expect(container.textContent).toBe('');
  });

  it('ADR-021 dark mode: renders under webDarkTheme; source has zero hard-coded hex color literals', () => {
    const { container } = renderWith(
      {
        narrative: 'Review motion to dismiss for Acme Matter.',
        regardingName: 'Acme Matter',
        regardingEntityType: 'sprk_matter',
        regardingId: '11111111-1111-1111-1111-111111111111',
      },
      webDarkTheme
    );
    expect(container.firstChild).not.toBeNull();
    expect(screen.getByRole('link', { name: /Acme Matter/i })).toBeInTheDocument();

    const fs = require('fs') as typeof import('fs');
    const path = require('path') as typeof import('path');
    const full = path.resolve(__dirname, '../src/components/NarrativeCitedText.tsx');
    const source = fs.readFileSync(full, 'utf8');
    const codeOnly = source
      .replace(/\/\*[\s\S]*?\*\//g, '') // block comments
      .replace(/(^|[^:])\/\/.*$/gm, '$1'); // line comments
    const hexColorRe = /[\s:'"(]#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\b/;
    expect(codeOnly.match(hexColorRe)).toBeNull();
  });
});

describe('hasInlineRegardingMention — deterministic match rule (single source of truth: buildSegments)', () => {
  it('returns false when there is no regarding target', () => {
    expect(hasInlineRegardingMention('Review motion to dismiss for Acme Matter.')).toBe(false);
  });

  it('returns false when the name is not present in the narrative', () => {
    expect(hasInlineRegardingMention('Review motion to dismiss.', 'Acme Matter', 'sprk_matter', 'm-1')).toBe(false);
  });

  it('returns true when the name IS present in the narrative (case-insensitive)', () => {
    expect(hasInlineRegardingMention('REVIEW MOTION FOR ACME MATTER.', 'Acme Matter', 'sprk_matter', 'm-1')).toBe(true);
  });

  it('returns false when regardingId is missing (no click target — matches buildSegments guard)', () => {
    expect(hasInlineRegardingMention('Acme Matter filed a motion.', 'Acme Matter', 'sprk_matter', '')).toBe(false);
  });
});
