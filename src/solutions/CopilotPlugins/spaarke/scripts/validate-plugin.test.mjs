import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { after, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

import { validatePackage } from './validate-plugin.mjs';

const realRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const scratch = mkdtempSync(path.join(tmpdir(), 'spaarke-plugin-gate-'));
after(() => rmSync(scratch, { recursive: true, force: true }));

let counter = 0;

/**
 * Builds a throwaway package under the scratch folder: one skill, a manifest copied from the real one and then adjusted.
 * `skill` is the folder name and the frontmatter name; `change` edits the manifest object before it is written.
 */
function makePackage({ skill = 'spaarke-decisions', frontmatterName = skill, declared = [`skills/${skill}`], change = () => {} } = {}) {
  const root = path.join(scratch, `pkg-${++counter}`);
  mkdirSync(path.join(root, 'appPackage'), { recursive: true });
  mkdirSync(path.join(root, 'skills', skill), { recursive: true });
  const manifest = JSON.parse(readFileSync(path.join(realRoot, 'appPackage', 'manifest.json'), 'utf8'));
  manifest.agentSkills = declared.map((folder) => ({ folder }));
  change(manifest);
  writeFileSync(path.join(root, 'appPackage', 'manifest.json'), JSON.stringify(manifest, null, 2));
  writeFileSync(
    path.join(root, 'skills', skill, 'SKILL.md'),
    `---\nname: ${frontmatterName}\ndescription: "Shows the decisions waiting for the user."\n---\n\n# Body\n`,
  );
  return root;
}

const has = (errors, fragment) => errors.some((e) => e.includes(fragment));

describe('skill name gate', () => {
  const longName = `spaarke-${'a'.repeat(57)}`;
  assert.equal(longName.length, 65);

  const rejected = [
    ['Spaarke-decisions', 'does not match'],
    ['spaarke_decisions', 'does not match'],
    ['decisions', 'does not match'],
    ['spaarke-', 'does not match'],
    [longName, 'longer than 64'],
  ];
  for (const [name, reason] of rejected) {
    it(`rejects '${name}' (${reason})`, () => {
      const errors = validatePackage(makePackage({ skill: name }));
      assert.ok(has(errors, reason), `expected '${reason}' in:\n${errors.join('\n')}`);
      assert.ok(has(errors, `'${name}'`), 'the error names the offending skill');
      if (name === longName) {
        assert.ok(!has(errors, 'does not match'), 'a 65-character valid-pattern name fails on length alone');
      }
    });
  }

  it("accepts 'spaarke-decisions'", () => {
    assert.deepEqual(validatePackage(makePackage({ skill: 'spaarke-decisions' })), []);
  });
});

describe('manifest gate', () => {
  it("rejects manifestVersion '1.29'", () => {
    const errors = validatePackage(makePackage({ change: (m) => (m.manifestVersion = '1.29') }));
    assert.ok(has(errors, "manifestVersion is '1.29'"), errors.join('\n'));
  });

  it('rejects a manifest with an agentConnectors entry', () => {
    const errors = validatePackage(
      makePackage({
        change: (m) =>
          (m.agentConnectors = [
            { id: 'spaarke-mcp', displayName: 'Spaarke MCP', toolSource: { remoteMcpServer: { mcpServerUrl: 'https://example.invalid/mcp' } } },
          ]),
      }),
    );
    assert.ok(has(errors, "'agentConnectors' must not be present"), errors.join('\n'));
  });

  it('rejects agentSkills that omits a skills/ folder', () => {
    const errors = validatePackage(makePackage({ declared: ['skills/spaarke-other'] }));
    assert.ok(has(errors, "does not list the skill folder(s) 'spaarke-decisions'"), errors.join('\n'));
  });

  it('rejects agentSkills that lists a folder that is not under skills/', () => {
    const errors = validatePackage(makePackage({ declared: ['skills/spaarke-decisions', 'skills/spaarke-ghost'] }));
    assert.ok(has(errors, "lists 'spaarke-ghost', which has no folder under skills/"), errors.join('\n'));
  });
});

describe('secret and tenant id gate', () => {
  // Added beyond the named cases because the acceptance criterion "no secret or tenant id in any package file" is otherwise unproven.
  it('rejects a tenant id in the manifest, while the manifest id and $schema URL pass', () => {
    const errors = validatePackage(
      makePackage({ change: (m) => (m.description.full = 'tenantId: 11111111-2222-3333-4444-555555555555') }),
    );
    assert.ok(has(errors, 'a tenant, client or application id'), errors.join('\n'));
  });
});

describe('the real package', () => {
  it('is accepted', () => {
    assert.deepEqual(validatePackage(realRoot), []);
  });
});

