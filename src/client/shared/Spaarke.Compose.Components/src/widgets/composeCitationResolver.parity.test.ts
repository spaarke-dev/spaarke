/**
 * composeCitationResolver.parity.test.ts — the CLIENT half of the shared citation-parity corpus (#699).
 *
 * WHAT THIS ADDS OVER `composeCitationResolver.test.ts`. That suite's parity cases were transcribed by
 * hand from `ComposeCitationResolverSeamTests.cs` — two copies of the same expectations, one per
 * language. Two copies cannot detect drift between them: change the server parser without touching the
 * client and BOTH suites stay green, because each only ever checks its own copy. This suite and its C#
 * twin (`tests/integration/seam/Compose/ComposeCitationParityCorpusTests.cs`) execute the SAME file —
 * `tests/fixtures/compose-citation-parity/cases.json` — so a divergence fails on the side that lags.
 *
 * WHY IT MATTERS FOR #699, which is an ANCHORING defect and not a parsing one. `placeAdvisoryComments`
 * tries the deterministic leg first: `sectionRef` → `resolveCitation` → paraId → live span. When the
 * client parser cannot handle a citation the server (and therefore the review model's vocabulary) can,
 * that leg returns null and the finding falls through to TEXT search — which is where a note can land
 * on the wrong clause. So a silent parser gap does not degrade gracefully; it reopens the defect
 * through a side door, in exactly the cases (precisely-cited findings) the deterministic leg exists to
 * make safe.
 *
 * The corpus is READ, not imported, so neither side can quietly fork it into its own bundle.
 *
 * @see ./composeCitationResolver.ts — the module under test.
 * @see tests/Spaarke.ArchTests/ComposeCitationResolverParityGuardTests.cs — the source-level drift
 *      detector for the vocabulary + shape enum, which is what makes forgetting to add a case hard.
 */
import * as fs from 'fs';
import * as path from 'path';
import { resolveCitation } from './composeCitationResolver';
import type { CitationShape } from './composeCitationResolver';
import type { ParaIdMapEntry } from '../types/compose-contracts';

interface CorpusEntry {
  readonly index: number;
  readonly paraId: string;
  readonly computedNumber: string;
  readonly listPath: number[];
}

interface CorpusCase {
  readonly map: string;
  readonly citation: string;
  readonly shape: CitationShape;
  readonly paraIds: string[];
}

interface Corpus {
  readonly maps: Record<string, CorpusEntry[]>;
  readonly cases: CorpusCase[];
}

/**
 * Walks up for the canonical repo-root marker rather than counting `..` segments — the same resolution
 * `ComposeCorpusFixtureLocator.ResolveCorpusDirectory` uses on the C# side, so moving this package does
 * not silently break the link between the two halves of the parity check.
 */
function resolveCorpusPath(): string {
  let dir = __dirname;
  for (let hop = 0; hop < 12; hop++) {
    if (fs.existsSync(path.join(dir, 'src', 'server', 'api', 'Sprk.Bff.Api', 'Program.cs'))) {
      return path.join(dir, 'tests', 'fixtures', 'compose-citation-parity', 'cases.json');
    }
    const parent = path.dirname(dir);
    if (parent === dir) break;
    dir = parent;
  }
  throw new Error(`Could not locate the repo root from ${__dirname} — the shared parity corpus is unreachable.`);
}

const corpus: Corpus = JSON.parse(fs.readFileSync(resolveCorpusPath(), 'utf8'));

function toParaIdMap(entries: readonly CorpusEntry[]): ParaIdMapEntry[] {
  return entries.map(e => ({
    index: e.index,
    paraId: e.paraId,
    isMinted: false,
    computedNumber: e.computedNumber,
    numberingLevel: e.listPath.length - 1,
    listPath: e.listPath,
  }));
}

describe('resolveCitation — shared parity corpus (executed by BOTH resolvers)', () => {
  it('the corpus is present and non-trivial', () => {
    // Non-vacuity. Every assertion below would hold over an empty case list, so the count is pinned:
    // a corpus that failed to load, or was emptied, must fail loudly rather than pass silently.
    expect(Object.keys(corpus.maps).length).toBeGreaterThanOrEqual(5);
    expect(corpus.cases.length).toBeGreaterThanOrEqual(40);
    for (const c of corpus.cases) {
      expect(corpus.maps[c.map]).toBeDefined();
    }
  });

  it.each(corpus.cases.map(c => [c.map, c.citation, c] as const))(
    '[%s] %j resolves identically to the server',
    (_map, _citation, testCase) => {
      const result = resolveCitation(testCase.citation, toParaIdMap(corpus.maps[testCase.map]));

      expect(result.shape).toBe(testCase.shape);
      expect(result.matches.map(m => m.paraId)).toEqual(testCase.paraIds);
    }
  );
});
