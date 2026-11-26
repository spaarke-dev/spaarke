/**
 * Guard: the client field-mapping engine stays deleted
 * (field-mapping-server-write-path-r1 task 035, spec FR-22; ADR-002 WP-1/WP-2, ADR-038).
 *
 * Why. Creation-time field mapping is applied on the server, once, by `CreateTimeFieldMapping` inside the record-creation
 * service; a record created through the BFF is mapped whichever client created it. The client engine (the apply function
 * the `Create*Wizard` services used to call, with its handler and types) was a second, divergent copy of the rules and
 * left every record created outside a wizard unmapped. Tasks 021-029 moved every creator to the server path and task 035
 * deleted the engine. A new wizard or a copied old snippet can bring it back without any test noticing, so this scan
 * fails the build when the entry point's name appears in any TypeScript source under the repository's `src/`.
 *
 * What it checks. Every `.ts` / `.tsx` file under `<repo>/src` (node_modules, dist, out, bin, obj and dot-directories are
 * skipped) must not contain the forbidden identifier as a whole word. The word is built from pieces below, so this file
 * holds no literal hit and needs no self-exclusion. Whole-word means the identifier is not part of a longer one:
 * `applyFieldMapping` (singular) and `applyFieldMappingsX` are not hits; a hit in a comment or a string is still a hit,
 * because a comment that names the function is how the next developer finds it and re-creates it.
 *
 * A guard that scans nothing must not pass: the walk starts from a location checked against known files, and the real
 * scan fails when it reads fewer than MIN_FILES files.
 *
 * Fix a failure by creating through the BFF (`createRecordViaBffWithWarnings`, naming the host record as the mapping
 * source) and letting the server map. Do not add an allow-list entry; if the name is needed in prose, reword it.
 *
 * Enforced by: this test, run by the package's jest suite (`npm test` in `src/client/shared/Spaarke.UI.Components`).
 */
import * as fs from 'fs';
import * as path from 'path';

/** The retired entry point's name, assembled so this file never contains it. */
const FORBIDDEN_TOKEN = 'apply' + 'FieldMappings';
const FORBIDDEN_WORD = new RegExp(`(?<![A-Za-z0-9_$])${FORBIDDEN_TOKEN}(?![A-Za-z0-9_$])`);

/** A scan that reads fewer files than this walked the wrong place. The repository's `src/` holds several thousand. */
const MIN_FILES = 500;

/** Generous: a cold read of several thousand files on a developer machine has taken a minute. */
const SCAN_TIMEOUT_MS = 300_000;

const SKIPPED_DIRECTORIES = new Set(['node_modules', 'dist', 'out', 'bin', 'obj']);
const SOURCE_FILE = /\.(ts|tsx)$/;

export interface IScannedFile {
  /** Path as reported in a failure (relative to the repository `src/`, forward slashes, in the real scan). */
  path: string;
  text: string;
}

export interface IForbiddenHit {
  path: string;
  /** 1-based line of the first hit in the file. */
  line: number;
}

/** Pure: the files whose text contains the forbidden word, each with the line of its first hit. */
export function findForbidden(files: readonly IScannedFile[]): IForbiddenHit[] {
  const hits: IForbiddenHit[] = [];
  for (const file of files) {
    const lines = file.text.split(/\r?\n/);
    const index = lines.findIndex(l => FORBIDDEN_WORD.test(l));
    if (index >= 0) hits.push({ path: file.path, line: index + 1 });
  }
  return hits;
}

/** The repository's `src/` directory, resolved from this file; throws (never returns a wrong directory) if it is not it. */
export function resolveRepoSrc(fromDir: string = __dirname): string {
  // <repo>/src/client/shared/Spaarke.UI.Components/src/__tests__/architecture -> <repo>/src
  const repoSrc = path.resolve(fromDir, '..', '..', '..', '..', '..', '..');
  const marker = path.join(repoSrc, 'client', 'shared', 'Spaarke.UI.Components', 'package.json');
  if (path.basename(repoSrc) !== 'src' || !fs.existsSync(marker)) {
    throw new Error(
      `noClientFieldMapping: expected the repository src/ at ${repoSrc} (containing ${marker}); ` +
        'the test file moved or the layout changed, so the scan would read the wrong tree. Fix the path, do not skip the guard.'
    );
  }
  return repoSrc;
}

/** Reads every source file under `root`, skipping build output, dependencies and dot-directories; symlinks are not followed. */
export function readSourceFiles(root: string): IScannedFile[] {
  const files: IScannedFile[] = [];
  const walk = (dir: string): void => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      if (entry.isDirectory()) {
        if (entry.name.startsWith('.') || SKIPPED_DIRECTORIES.has(entry.name)) continue;
        walk(path.join(dir, entry.name));
      } else if (entry.isFile() && SOURCE_FILE.test(entry.name)) {
        const full = path.join(dir, entry.name);
        files.push({
          path: path.relative(root, full).split(path.sep).join('/'),
          text: fs.readFileSync(full, 'utf8'),
        });
      }
    }
  };
  walk(root);
  return files;
}

describe('noClientFieldMapping (FR-22): the client field-mapping engine stays deleted', () => {
  describe('controls', () => {
    it('must fire: a source that names the retired entry point is reported with its path and line', () => {
      const fixture: IScannedFile[] = [
        {
          path: 'client/x/wizardService.ts',
          text: `import { ${FORBIDDEN_TOKEN} } from './engine';\nawait ${FORBIDDEN_TOKEN}(args);`,
        },
        { path: 'client/x/clean.ts', text: 'export const ok = 1;' },
        { path: 'client/x/comment.tsx', text: `// ${FORBIDDEN_TOKEN} used to live here\nconst a = 1;` },
      ];

      expect(findForbidden(fixture)).toEqual([
        { path: 'client/x/wizardService.ts', line: 1 },
        { path: 'client/x/comment.tsx', line: 1 },
      ]);
    });

    it('must not fire: clean sources and whole-word near-misses are not reported', () => {
      const fixture: IScannedFile[] = [
        { path: 'a.ts', text: 'export const mapOnServer = true;' },
        { path: 'b.ts', text: 'const applyFieldMapping = 1; // singular is a different identifier' },
        {
          path: 'c.ts',
          text: `const ${FORBIDDEN_TOKEN}X = 1; const my_${FORBIDDEN_TOKEN} = 2; const IA${FORBIDDEN_TOKEN} = 3;`,
        },
        { path: 'd.ts', text: 'const IApplyFieldMappingsArgs = 1; // different case' },
      ];

      expect(findForbidden(fixture)).toEqual([]);
    });
  });

  describe('the real tree', () => {
    // One walk for both tests; the first read of the tree can be slow on a cold file cache.
    let files: IScannedFile[] = [];
    beforeAll(() => {
      files = readSourceFiles(resolveRepoSrc());
    }, SCAN_TIMEOUT_MS);

    it('resolves the repository src/ and fails loudly when it cannot', () => {
      expect(path.basename(resolveRepoSrc())).toBe('src');
      expect(() => resolveRepoSrc(path.join(__dirname, '..'))).toThrow(/expected the repository src\//);
    });

    it('scans a real number of files (a guard that scans nothing must not pass)', () => {
      expect(files.length).toBeGreaterThanOrEqual(MIN_FILES);
      expect(
        files.some(
          f => f.path === 'client/shared/Spaarke.UI.Components/src/__tests__/architecture/noClientFieldMapping.test.ts'
        )
      ).toBe(true);
    });

    it('finds no use of the retired client mapping entry point anywhere under src/', () => {
      const hits = findForbidden(files);

      const message =
        'The client field-mapping engine was retired in field-mapping-server-write-path-r1 task 035 (FR-22); creation-time ' +
        'mapping is applied on the server (CreateTimeFieldMapping inside the record-creation service). The forbidden name ' +
        `appears in:\n${hits.map(h => `  src/${h.path}:${h.line}`).join('\n')}\n` +
        'Create through the BFF with the host record as the mapping source instead of mapping on the client.';
      if (hits.length > 0) throw new Error(message);
      expect(hits).toEqual([]);
    });
  });
});
