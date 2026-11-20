/**
 * laneCountFilters.parity.test.ts — the CLIENT half of the shared Decide-lane category corpus
 * (spaarkeai-mcp-bff-api-r1 task 022, spec FR-03).
 *
 * The Copilot tool `spaarke_show_decisions` reports the Decide lane's category counts, computed on the server by
 * `DecideLaneCategories.cs`, which mirrors `bucketLaneItems`. This suite and its C# twin
 * (`tests/unit/domain/Mcp/DecideLaneCategoriesParityTests.cs`) execute the SAME file,
 * `tests/fixtures/decide-lane-category-parity/cases.json`, so a change to either partition that the other does not make
 * fails on the side that lags. The corpus is READ, not imported, so neither side can quietly fork it.
 *
 * Contract under test: for each case, `bucketLaneItems(items, 'Decide')` gives exactly the expected cards, in order, with
 * the expected labels and counts. `laneCountFilters.ts` itself is not changed by this task.
 */
import * as fs from 'fs';
import * as path from 'path';
import { bucketLaneItems } from '../laneCountFilters';
import type { WorklistItem } from '../types';

interface CorpusItem {
  readonly lane: string;
  readonly workType?: string | null;
  readonly invoiceReview: { readonly invoiceId: string; readonly lineId?: string | null } | null;
}

interface CorpusCard {
  readonly key: string;
  readonly label: string;
  readonly count: number;
}

interface CorpusCase {
  readonly name: string;
  readonly items: CorpusItem[];
  readonly expected: CorpusCard[];
}

interface Corpus {
  readonly cases: CorpusCase[];
}

/** Walks up to the repo root (the BFF's Program.cs is the marker), as the C# twin does, then reads the corpus. */
function loadCorpus(): Corpus {
  let dir = __dirname;
  for (let hop = 0; hop < 12; hop++) {
    if (fs.existsSync(path.join(dir, 'src', 'server', 'api', 'Sprk.Bff.Api', 'Program.cs'))) {
      const file = path.join(dir, 'tests', 'fixtures', 'decide-lane-category-parity', 'cases.json');
      return JSON.parse(fs.readFileSync(file, 'utf8')) as Corpus;
    }
    dir = path.dirname(dir);
  }
  throw new Error(`Could not locate the repo root from ${__dirname}`);
}

const corpus = loadCorpus();

function toItems(items: readonly CorpusItem[]): WorklistItem[] {
  return items.map(
    (item, index) =>
      ({
        signalId: `s${index}`,
        lane: item.lane,
        workType: item.workType,
        invoiceReview: item.invoiceReview ?? undefined,
      }) as unknown as WorklistItem
  );
}

describe('Decide-lane category parity corpus (client half)', () => {
  it('has cases to run', () => {
    expect(corpus.cases.length).toBeGreaterThan(0);
  });

  it.each(corpus.cases.map(c => [c.name, c] as const))('%s', (_name, testCase) => {
    const cards = bucketLaneItems(toItems(testCase.items), 'Decide').map(b => ({
      key: b.key,
      label: b.label,
      count: b.items.length,
    }));

    expect(cards).toEqual(testCase.expected);
  });
});
