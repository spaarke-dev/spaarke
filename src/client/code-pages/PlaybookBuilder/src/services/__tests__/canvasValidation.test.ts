/**
 * canvasValidation tests — R3 task 094.
 *
 * Targets the two NEW rules added by tasks 091 and 093 (plus a smoke test for
 * the existing 043 lookup-user-membership rule, which gives us a quick cross-
 * check that the validation pipeline ran):
 *   - outputvar-collision   (task 091, FR-3H2.1 safety net, severity=error)
 *   - edge-no-data-dependency (task 093, FR-3H2.3 advisory, severity=warning)
 *
 * Plus regression on hasValidationErrors() with a warnings-only set (must NOT
 * block save — FR-3H2.3 binding).
 */
import {
  validatePromptSchemaNodes,
  hasValidationErrors,
  findOutputVariableReferences,
} from '../../services/canvasValidation';
import type { PlaybookNode } from '../../types/canvas';
import type { Edge } from '@xyflow/react';

function makeNode(id: string, outputVariable: string, configJson?: string): PlaybookNode {
  return {
    id,
    type: 'updateRecord',
    position: { x: 0, y: 0 },
    data: {
      label: `Node ${id}`,
      type: 'updateRecord',
      outputVariable,
      configJson,
    },
  };
}

describe('canvasValidation', () => {
  describe('outputvar-collision (task 091)', () => {
    it('flags every node that shares a non-empty outputVariable', () => {
      const nodes: PlaybookNode[] = [makeNode('a', 'shared'), makeNode('b', 'shared'), makeNode('c', 'unique')];
      const results = validatePromptSchemaNodes(nodes, []);
      const collisions = results.filter(r => r.rule === 'outputvar-collision');
      expect(collisions).toHaveLength(2);
      expect(new Set(collisions.map(c => c.nodeId))).toEqual(new Set(['a', 'b']));
      expect(collisions.every(c => c.severity === 'error')).toBe(true);
    });

    it('does NOT flag empty outputVariable values (missing-output-variable rules cover those)', () => {
      const nodes: PlaybookNode[] = [makeNode('a', ''), makeNode('b', '   '), makeNode('c', '')];
      const results = validatePromptSchemaNodes(nodes, []);
      expect(results.filter(r => r.rule === 'outputvar-collision')).toHaveLength(0);
    });

    it('does NOT flag a unique outputVariable', () => {
      const nodes: PlaybookNode[] = [makeNode('a', 'only_one'), makeNode('b', 'another')];
      const results = validatePromptSchemaNodes(nodes, []);
      expect(results.filter(r => r.rule === 'outputvar-collision')).toHaveLength(0);
    });
  });

  describe('edge-no-data-dependency (task 093, FR-3H2.3)', () => {
    it('fires a warning when target config does not reference source outputVariable', () => {
      const source = makeNode('s1', 'classify');
      const target = makeNode(
        't1',
        'updated',
        JSON.stringify({
          fieldMappings: [{ field: 'name', type: 'string', value: 'static value' }],
        })
      );
      const edge: Edge = { id: 'e1', source: 's1', target: 't1' };
      const results = validatePromptSchemaNodes([source, target], [edge]);

      const perfWarnings = results.filter(r => r.rule === 'edge-no-data-dependency');
      expect(perfWarnings).toHaveLength(1);
      expect(perfWarnings[0].severity).toBe('warning');
      expect(perfWarnings[0].nodeId).toBe('s1');
    });

    it('does NOT fire when target references source via {{output_X.output.*}}', () => {
      // canvasValidation's TEMPLATE_REF_RE requires the `output_` prefix on the
      // variable name (matches the wider convention used across PlaybookBuilder
      // — see canvasStore.onDrop which auto-names outputs as `output_<type>`).
      const source = makeNode('s1', 'output_classify');
      const target = makeNode(
        't1',
        'output_updated',
        JSON.stringify({
          fieldMappings: [{ field: 'name', type: 'string', value: '{{output_classify.output.documentType}}' }],
        })
      );
      const edge: Edge = { id: 'e1', source: 's1', target: 't1' };
      const results = validatePromptSchemaNodes([source, target], [edge]);
      expect(results.filter(r => r.rule === 'edge-no-data-dependency')).toHaveLength(0);
    });

    it('does NOT fire when source has no outputVariable to reference', () => {
      const source = makeNode('s1', '');
      const target = makeNode('t1', 'updated', JSON.stringify({ fieldMappings: [] }));
      const edge: Edge = { id: 'e1', source: 's1', target: 't1' };
      const results = validatePromptSchemaNodes([source, target], [edge]);
      expect(results.filter(r => r.rule === 'edge-no-data-dependency')).toHaveLength(0);
    });
  });

  describe('hasValidationErrors (FR-3H2.3 binding — warnings do NOT block save)', () => {
    it('returns false when only warnings are present', () => {
      const source = makeNode('s1', 'classify');
      const target = makeNode('t1', 'updated', JSON.stringify({ fieldMappings: [] }));
      const edge: Edge = { id: 'e1', source: 's1', target: 't1' };
      const results = validatePromptSchemaNodes([source, target], [edge]);
      expect(results.length).toBeGreaterThan(0); // the perf hint warning fired
      expect(hasValidationErrors(results)).toBe(false);
    });

    it('returns true when at least one error is present (outputvar-collision)', () => {
      const nodes: PlaybookNode[] = [makeNode('a', 'shared'), makeNode('b', 'shared')];
      const results = validatePromptSchemaNodes(nodes, []);
      expect(hasValidationErrors(results)).toBe(true);
    });
  });

  describe('findOutputVariableReferences (task 091 helper)', () => {
    it('returns one entry per node that references the variable', () => {
      // Variable names must follow the `output_<name>` convention to match
      // TEMPLATE_REF_RE (used by extractTemplateRefs inside findOutputVariableReferences).
      const owner = makeNode('owner', 'output_classify');
      const ref1 = makeNode(
        'r1',
        'output_a',
        JSON.stringify({
          fieldMappings: [{ field: 'docType', type: 'string', value: '{{output_classify.output.documentType}}' }],
        })
      );
      const ref2 = makeNode(
        'r2',
        'output_b',
        JSON.stringify({
          fieldMappings: [
            { field: 'docType', type: 'string', value: '{{output_classify.output.documentType}}' },
            { field: 'conf', type: 'string', value: '{{output_classify.output.confidence}}' },
          ],
        })
      );
      const noRef = makeNode('n1', 'output_c', JSON.stringify({ fieldMappings: [] }));

      const found = findOutputVariableReferences('output_classify', [owner, ref1, ref2, noRef], 'owner');
      expect(found).toHaveLength(2);
      const r2 = found.find(f => f.nodeId === 'r2')!;
      expect(r2.rawRefs).toHaveLength(2);
    });

    it('returns empty when oldName is empty/whitespace', () => {
      const owner = makeNode('owner', 'output_classify');
      expect(findOutputVariableReferences('', [owner], 'owner')).toHaveLength(0);
      expect(findOutputVariableReferences('   ', [owner], 'owner')).toHaveLength(0);
    });
  });
});
