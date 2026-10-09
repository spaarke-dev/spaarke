/**
 * The compile-time contract of `AuthenticatedFetchFn` / `ResponseFetchFn` / `OkResponse`.
 *
 * `authenticatedFetch` never resolves with a failed `Response` — it throws `ApiError` / `AuthError`.
 * Its type says so (`Promise<OkResponse>`, `status` a 2xx literal), which is what makes a dead
 * `res.status === 404` after it a compile error and keeps a fetch that RETURNS failures from being
 * passed where a throwing one is required. This suite pins that contract by compiling
 * `fixtures/okResponse.types.fixture.ts` with the TypeScript compiler and the package's own
 * compiler options:
 *
 *  1. As written, the fixture compiles with ZERO diagnostics — so every must-not-fire line is clean
 *     and every `@ts-expect-error` is used (an unused one is TS2578).
 *  2. With the directives removed, each line after a `@ts-expect-error TSnnnn` reports exactly that
 *     code — so the must-fire lines fail for the stated reason, not an unrelated error.
 *
 * Widening `OkResponse` (e.g. `status: number`) or `AuthenticatedFetchFn` (`Promise<Response>`) fails
 * this suite.
 */
import * as path from 'path';
import * as ts from 'typescript';

const PACKAGE_DIR = path.resolve(__dirname, '..');
const FIXTURE = path.join(__dirname, 'fixtures', 'okResponse.types.fixture.ts');
const DIRECTIVE = /\/\/\s*@ts-expect-error\s+TS(\d+)/;

function compilerOptions(): ts.CompilerOptions {
  const configPath = path.join(PACKAGE_DIR, 'tsconfig.json');
  const read = ts.readConfigFile(configPath, ts.sys.readFile);
  if (read.error) throw new Error(ts.flattenDiagnosticMessageText(read.error.messageText, '\n'));
  const parsed = ts.parseJsonConfigFileContent(read.config, ts.sys, PACKAGE_DIR);
  // The package's options (strict, lib dom, …) — minus emit, and with the fixture inside rootDir.
  return {
    ...parsed.options,
    noEmit: true,
    rootDir: PACKAGE_DIR,
    declaration: false,
    declarationMap: false,
    sourceMap: false,
  };
}

/** Compile the fixture, optionally with its text replaced; diagnostics for the fixture only. */
function diagnosticsFor(fixtureText?: string): ts.Diagnostic[] {
  const options = compilerOptions();
  const host = ts.createCompilerHost(options);
  if (fixtureText !== undefined) {
    const getSourceFile = host.getSourceFile.bind(host);
    host.getSourceFile = (fileName, languageVersion, onError, shouldCreate) =>
      path.resolve(fileName) === FIXTURE
        ? ts.createSourceFile(fileName, fixtureText, languageVersion, true)
        : getSourceFile(fileName, languageVersion, onError, shouldCreate);
  }
  const program = ts.createProgram([FIXTURE], options, host);
  return ts.getPreEmitDiagnostics(program).filter(d => d.file && path.resolve(d.file.fileName) === FIXTURE);
}

function lineOf(d: ts.Diagnostic): number {
  return d.file!.getLineAndCharacterOfPosition(d.start ?? 0).line; // 0-based
}

function describeDiagnostics(diags: readonly ts.Diagnostic[]): string {
  return diags
    .map(d => `line ${lineOf(d) + 1}: TS${d.code} ${ts.flattenDiagnosticMessageText(d.messageText, ' ')}`)
    .join('\n');
}

// Two whole-program compiles (lib.dom included) — slower than a unit test.
jest.setTimeout(120_000);

describe('OkResponse / AuthenticatedFetchFn / ResponseFetchFn compile-time contract', () => {
  const source = ts.sys.readFile(FIXTURE);
  if (source === undefined) throw new Error(`fixture not found: ${FIXTURE}`);
  const lines = source.split(/\r?\n/);
  /** 0-based line of each directive's TARGET line, and the code expected there. */
  const expectations = lines.flatMap((text, i) => {
    const m = DIRECTIVE.exec(text);
    return m ? [{ line: i + 1, code: Number(m[1]) }] : [];
  });

  it('declares the must-fire cases it checks', () => {
    expect(expectations.map(e => e.code).sort()).toEqual([2322, 2322, 2367, 2367, 2367, 2678]);
  });

  it('compiles clean as written: every @ts-expect-error fires and no must-not-fire line errors', () => {
    const diags = diagnosticsFor();
    expect(describeDiagnostics(diags)).toBe('');
  });

  it('reports exactly the declared error on each must-fire line once the directives are removed', () => {
    const stripped = lines.map(text => (DIRECTIVE.test(text) ? '' : text)).join('\n');
    const diags = diagnosticsFor(stripped);

    for (const { line, code } of expectations) {
      const onLine = diags.filter(d => lineOf(d) === line).map(d => d.code);
      expect({ line: line + 1, codes: onLine }).toEqual({ line: line + 1, codes: [code] });
    }
    // Nothing else fails: every diagnostic is one of the declared ones.
    const expectedLines = new Set(expectations.map(e => e.line));
    expect(describeDiagnostics(diags.filter(d => !expectedLines.has(lineOf(d))))).toBe('');
  });
});
