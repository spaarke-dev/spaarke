/**
 * getXrm return typing (task 081 round 4, review F10), checked with the real
 * TypeScript compiler: this package's jest run does not type-check and its
 * `tsc` build excludes tests, so a `@ts-expect-error` here would pin nothing.
 *
 * Contract: only `getXrm()` / `getXrm('webApi')` / a list that starts with
 * `'webApi'` promise a non-optional `WebApi`. Asking for any other capability
 * returns `XrmPartialContext`, whose `WebApi` is optional — the frame is only
 * guaranteed to have what was asked for.
 */
import * as path from 'path';
import * as ts from 'typescript';

const SNIPPET_PATH = path.join(__dirname, '__getXrm_typing_snippet__.ts').replace(/\\/g, '/');

/** Line (0-based) → source text; the line of each statement is what we assert on. */
const SNIPPET = [
  "import { getXrm } from '../xrmContext';",
  "const a = getXrm('navigation');",
  'export const partial = a!.WebApi.retrieveRecord;', // line 2: must error (WebApi possibly undefined)
  'const b = getXrm();',
  'export const byDefault = b!.WebApi.retrieveRecord;', // line 4: ok
  "const c = getXrm('webApi');",
  'export const explicit = c!.WebApi.retrieveRecord;', // line 6: ok
  "const d = getXrm(['webApi', 'utility']);",
  'export const tuple = d!.WebApi.retrieveRecord;', // line 8: ok
  "const e = getXrm(['utility', 'navigation']);",
  'export const listWithoutWebApi = e!.WebApi.retrieveRecord;', // line 10: must error
  'export const nav = a?.Navigation?.navigateTo;', // line 11: ok
].join('\n');

function diagnosticLines(): number[] {
  const options: ts.CompilerOptions = {
    strict: true,
    noEmit: true,
    skipLibCheck: true,
    target: ts.ScriptTarget.ES2020,
    module: ts.ModuleKind.CommonJS,
    moduleResolution: ts.ModuleResolutionKind.Node10,
    types: [],
    lib: ['lib.es2020.d.ts', 'lib.dom.d.ts'],
  };
  const host = ts.createCompilerHost(options);
  const baseGetSourceFile = host.getSourceFile.bind(host);
  const baseFileExists = host.fileExists.bind(host);
  const baseReadFile = host.readFile.bind(host);
  const same = (f: string): boolean => f.replace(/\\/g, '/') === SNIPPET_PATH;
  host.getSourceFile = (fileName, languageVersion, onError, shouldCreate) =>
    same(fileName)
      ? ts.createSourceFile(fileName, SNIPPET, languageVersion, true)
      : baseGetSourceFile(fileName, languageVersion, onError, shouldCreate);
  host.fileExists = f => same(f) || baseFileExists(f);
  host.readFile = f => (same(f) ? SNIPPET : baseReadFile(f));

  const program = ts.createProgram([SNIPPET_PATH], options, host);
  const snippet = program.getSourceFile(SNIPPET_PATH)!;
  return ts
    .getPreEmitDiagnostics(program, snippet)
    .filter(d => d.file && same(d.file.fileName))
    .map(d => snippet.getLineAndCharacterOfPosition(d.start ?? 0).line)
    .sort((x, y) => x - y);
}

describe('getXrm typing (compiler-checked)', () => {
  it('WebApi is optional for non-WebApi capabilities and guaranteed for the WebApi forms', () => {
    expect(diagnosticLines()).toEqual([2, 10]);
  }, 60_000);
});
