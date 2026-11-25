/**
 * R4 task 035 (FR-22, spec design §4): no code in the external SPA hands `sanitizeEmailHtml` the `allowRemoteImages` option,
 * and every message body that reaches `dangerouslySetInnerHTML` is the sanitizer's own output.
 *
 * A source scan over `src/` with comments stripped (a comment may explain the rule; only code can break it), with must-fire and
 * must-not-fire controls for the matcher itself. Enforced here because no type stops a caller spreading options in.
 */
import fs from 'fs';
import path from 'path';
import { describe, expect, it } from 'vitest';

const SRC = path.resolve(__dirname, '../src');

function sourceFiles(dir: string): string[] {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) return sourceFiles(full);
    return /\.(ts|tsx)$/.test(entry.name) ? [full] : [];
  });
}

/** Source with block and line comments removed (strings are kept: a mention in a string is still a use). */
function withoutComments(text: string): string {
  return text.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:'"`])\/\/.*$/gm, '$1');
}

/** What a line of code must not contain: the option, or any second argument to the sanitizer (a spread of caller options). */
function remoteImageEscapesIn(code: string): string[] {
  const found: string[] = [];
  if (/allowRemoteImages/.test(code)) found.push('allowRemoteImages');
  if (/sanitizeEmailHtml\s*\(\s*[^()]*,/.test(code)) found.push('sanitizeEmailHtml with a second argument');
  return found;
}

describe('the sanitizer is never given allowRemoteImages (FR-22)', () => {
  it('no source file under src/ passes the option or any options argument', () => {
    const offenders = sourceFiles(SRC)
      .map(file => ({
        file: path.relative(SRC, file),
        found: remoteImageEscapesIn(withoutComments(fs.readFileSync(file, 'utf8'))),
      }))
      .filter(x => x.found.length > 0);

    expect(offenders).toEqual([]);
  });

  it('the scan sees the sanitizer calls it is meant to guard (not a vacuous pass)', () => {
    const calls = sourceFiles(SRC).filter(f =>
      /sanitizeEmailHtml\s*\(/.test(withoutComments(fs.readFileSync(f, 'utf8')))
    );

    expect(calls.map(f => path.basename(f))).toContain('RecordMessages.tsx');
  });

  it('the message list hands dangerouslySetInnerHTML only sanitizer output', () => {
    const code = withoutComments(fs.readFileSync(path.join(SRC, 'components/detail/RecordMessages.tsx'), 'utf8'));
    const sinks = code.match(/dangerouslySetInnerHTML=\{\{[^}]*\}\}/g) ?? [];

    expect(sinks).toEqual(['dangerouslySetInnerHTML={{ __html: sanitizedHtml }}']);
    expect(code).toMatch(
      /sanitizedHtml\s*=\s*React\.useMemo\(\(\) => \(isHtml && body \? sanitizeEmailHtml\(body\) : ''\)/
    );
  });

  describe('the matcher', () => {
    it('fires on the option, and on any options argument or spread (must-fire)', () => {
      expect(remoteImageEscapesIn('sanitizeEmailHtml(body, { allowRemoteImages: true })')).not.toEqual([]);
      expect(remoteImageEscapesIn('const o = { allowRemoteImages: false };')).not.toEqual([]);
      expect(remoteImageEscapesIn('sanitizeEmailHtml(body, opts)')).not.toEqual([]);
      expect(remoteImageEscapesIn('sanitizeEmailHtml(body, { ...callerOptions })')).not.toEqual([]);
    });

    it('is quiet on the plain call and on a comment that names the rule (must-not-fire)', () => {
      expect(remoteImageEscapesIn('sanitizeEmailHtml(body)')).toEqual([]);
      expect(remoteImageEscapesIn("isHtml && body ? sanitizeEmailHtml(body) : ''")).toEqual([]);
      expect(
        remoteImageEscapesIn(
          withoutComments('// never pass allowRemoteImages\n/* allowRemoteImages */ sanitizeEmailHtml(body)')
        )
      ).toEqual([]);
    });
  });
});
