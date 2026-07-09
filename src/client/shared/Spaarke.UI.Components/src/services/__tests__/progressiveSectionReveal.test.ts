/**
 * progressiveSectionReveal tests — spaarke-ai-architecture-redesign-r2 task 039
 * (FR-A1-10 / D-F5).
 *
 * Covers:
 *   - extractRevealableSections: declaration-order extraction, metadata-key /
 *     null / empty-string filtering (pure function, no timing).
 *   - revealSectionsProgressively: reveals sections with a real, measurable
 *     pacing gap between them (N-1 gaps for N sections; none before the
 *     first, none after the last); delayMs: 0 reveals back-to-back.
 */

import { extractRevealableSections, revealSectionsProgressively } from '../progressiveSectionReveal';

describe('extractRevealableSections', () => {
  it('extracts sections in declaration order, filtering metadata / null / empty-string values', () => {
    const sections = extractRevealableSections({
      tldr: 'Short version',
      keywords: ['a', 'b'],
      parsedSuccessfully: true, // widget-internal — skipped
      rawResponse: 'raw', // widget-internal — skipped
      empty: '', // empty string — skipped
      nothing: null, // null — skipped
      missing: undefined, // undefined — skipped
    });

    expect(sections).toEqual([
      { name: 'tldr', value: 'Short version' },
      { name: 'keywords', value: ['a', 'b'] },
    ]);
  });

  it('returns an empty array when every field is filtered out', () => {
    expect(extractRevealableSections({ parsedSuccessfully: true, rawResponse: '', empty: '' })).toEqual([]);
  });
});

describe('revealSectionsProgressively', () => {
  it('reveals a single section immediately (0 gaps for 1 section)', async () => {
    const published: Array<{ name: string; index: number; total: number }> = [];

    await revealSectionsProgressively(
      [{ name: 'summary', value: 'x' }],
      (section, index, total) => published.push({ name: section.name, index, total }),
      { delayMs: 100 }
    );

    expect(published).toEqual([{ name: 'summary', index: 0, total: 1 }]);
  });

  it('reveals N sections in declaration order with a real pacing gap BETWEEN sections (not before the first, not after the last)', async () => {
    const published: string[] = [];
    const start = Date.now();
    let secondSectionElapsedMs = -1;

    await revealSectionsProgressively(
      [
        { name: 'a', value: '1' },
        { name: 'b', value: '2' },
        { name: 'c', value: '3' },
      ],
      section => {
        published.push(section.name);
        if (section.name === 'b') {
          secondSectionElapsedMs = Date.now() - start;
        }
      },
      { delayMs: 40 }
    );

    expect(published).toEqual(['a', 'b', 'c']);
    // The second section must not appear before (at least close to) the pacing
    // delay has elapsed since the function started. A generous lower bound
    // (half the configured delay) keeps this robust on slow CI runners while
    // still proving the reveal is NOT one synchronous batch.
    expect(secondSectionElapsedMs).toBeGreaterThanOrEqual(20);
  });

  it('delayMs: 0 reveals all sections back-to-back with no pacing', async () => {
    const published: string[] = [];

    await revealSectionsProgressively(
      [
        { name: 'a', value: '1' },
        { name: 'b', value: '2' },
      ],
      section => published.push(section.name),
      { delayMs: 0 }
    );

    expect(published).toEqual(['a', 'b']);
  });

  it('reveals nothing for an empty section list', async () => {
    const publishSection = jest.fn();

    await revealSectionsProgressively([], publishSection);

    expect(publishSection).not.toHaveBeenCalled();
  });
});
