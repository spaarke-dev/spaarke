/**
 * docxBridge.paraId.test.ts — R3 FR-08/FR-09/FR-10 (spaarkeai-compose-r3 task 011).
 *
 * Pure coverage for the `w14:paraId` id generator + the license provenance of the
 * minting extension. The editor-driven behaviour (stamp doc-order, DOM-absence,
 * split re-mint, untouched-keep) lives in `ComposeEditor.paraId.test.tsx` — it needs
 * the real `COMPOSE_R3_PARAID` extension + a live TipTap editor.
 */
import * as fs from 'fs';
import * as path from 'path';
import { generateOoxmlParaId } from '../widgets/paraIdExtension';

describe('generateOoxmlParaId — OOXML-valid w14:paraId minting (FR-10)', () => {
  it('always returns an 8-char upper-hex string', () => {
    for (let i = 0; i < 2000; i++) {
      const id = generateOoxmlParaId();
      expect(id).toMatch(/^[0-9A-F]{8}$/);
    }
  });

  it('always yields a value in the ST_LongHexNumber range 0 < x < 0x80000000', () => {
    for (let i = 0; i < 5000; i++) {
      const v = parseInt(generateOoxmlParaId(), 16);
      expect(v).toBeGreaterThan(0);
      expect(v).toBeLessThan(0x80000000);
    }
  });

  it('produces distinct ids across calls (CSPRNG-backed, not a constant)', () => {
    const seen = new Set<string>();
    for (let i = 0; i < 1000; i++) seen.add(generateOoxmlParaId());
    // A CSPRNG over a 31-bit space must not collide meaningfully across 1000 draws.
    expect(seen.size).toBeGreaterThan(990);
  });
});

describe('license provenance — @tiptap/extension-unique-id is MIT, NOT @tiptap-pro (NFR-03)', () => {
  const pkgRoot = path.resolve(__dirname, '../..');

  it('resolves the minting extension to @tiptap/extension-unique-id under an MIT license', () => {
    const manifestPath = path.join(pkgRoot, 'node_modules/@tiptap/extension-unique-id/package.json');
    const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8')) as {
      name: string;
      license: string;
    };
    expect(manifest.name).toBe('@tiptap/extension-unique-id');
    expect(manifest.license).toBe('MIT');
  });

  it('declares @tiptap/extension-unique-id (never a @tiptap-pro/* package) in package.json', () => {
    const pkg = JSON.parse(fs.readFileSync(path.join(pkgRoot, 'package.json'), 'utf8')) as {
      dependencies?: Record<string, string>;
      devDependencies?: Record<string, string>;
    };
    const allDeps = { ...pkg.dependencies, ...pkg.devDependencies };
    expect(allDeps['@tiptap/extension-unique-id']).toBeDefined();
    const proDeps = Object.keys(allDeps).filter(name => name.startsWith('@tiptap-pro/'));
    expect(proDeps).toEqual([]);
  });

  it('has no @tiptap-pro package installed in node_modules', () => {
    expect(fs.existsSync(path.join(pkgRoot, 'node_modules/@tiptap-pro'))).toBe(false);
  });
});
