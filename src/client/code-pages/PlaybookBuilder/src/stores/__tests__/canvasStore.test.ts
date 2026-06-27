/**
 * canvasStore tests — R3 task 094.
 *
 * Covers the two new store affordances added by tasks 091 + 092:
 *   - renameOutputVariableReferences(oldName, newName) — rewrites every
 *     `{{oldName.output.*}}` reference across node.data + configJson
 *   - onConnect — when source is a Condition node with no sourceHandle, the
 *     edge is DEFERRED via pendingBranchConnection; otherwise a normal edge
 *     is created.
 */
import { useCanvasStore } from '../../stores/canvasStore';
import type { PlaybookNode } from '../../types/canvas';
import type { Connection } from '@xyflow/react';

function reset() {
  useCanvasStore.getState().reset();
}

beforeEach(reset);
afterEach(reset);

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function nodeWithTemplateField(id: string, value: string): PlaybookNode {
  return {
    id,
    type: 'sendEmail',
    position: { x: 0, y: 0 },
    data: {
      label: `Send Email ${id}`,
      type: 'sendEmail',
      outputVariable: `out_${id}`,
      emailBody: value,
    },
  };
}

function nodeWithConfigJsonMapping(id: string, mappingValue: string): PlaybookNode {
  return {
    id,
    type: 'updateRecord',
    position: { x: 0, y: 0 },
    data: {
      label: `Update ${id}`,
      type: 'updateRecord',
      outputVariable: `out_${id}`,
      configJson: JSON.stringify({
        fieldMappings: [{ field: 'name', type: 'string', value: mappingValue }],
      }),
    },
  };
}

// ---------------------------------------------------------------------------
// renameOutputVariableReferences (task 091)
// ---------------------------------------------------------------------------

describe('canvasStore.renameOutputVariableReferences', () => {
  it('rewrites references in node.data.emailBody', () => {
    useCanvasStore.setState({
      nodes: [nodeWithTemplateField('a', 'Hello {{classify.output.documentType}}!')],
    });

    const mutated = useCanvasStore.getState().renameOutputVariableReferences('classify', 'classifier');

    expect(mutated).toBe(1);
    const updated = useCanvasStore.getState().nodes[0];
    expect(updated.data.emailBody).toBe('Hello {{classifier.output.documentType}}!');
  });

  it('rewrites references in configJson.fieldMappings[].value', () => {
    useCanvasStore.setState({
      nodes: [nodeWithConfigJsonMapping('b', '{{classify.output.documentType}}')],
    });

    const mutated = useCanvasStore.getState().renameOutputVariableReferences('classify', 'classifier');

    expect(mutated).toBe(1);
    const cfg = JSON.parse(useCanvasStore.getState().nodes[0].data.configJson as string);
    expect(cfg.fieldMappings[0].value).toBe('{{classifier.output.documentType}}');
  });

  it('rewrites references across multiple nodes and counts mutations', () => {
    useCanvasStore.setState({
      nodes: [
        nodeWithTemplateField('a', '{{foo.output.x}} and {{foo.output.y}}'),
        nodeWithConfigJsonMapping('b', '{{foo.output.x}}'),
        nodeWithTemplateField('c', 'no references here'),
      ],
    });

    const mutated = useCanvasStore.getState().renameOutputVariableReferences('foo', 'bar');

    expect(mutated).toBe(2);
    const [a, b, c] = useCanvasStore.getState().nodes;
    expect(a.data.emailBody).toBe('{{bar.output.x}} and {{bar.output.y}}');
    const bCfg = JSON.parse(b.data.configJson as string);
    expect(bCfg.fieldMappings[0].value).toBe('{{bar.output.x}}');
    expect(c.data.emailBody).toBe('no references here');
  });

  it('no-ops when oldName / newName are empty or identical', () => {
    useCanvasStore.setState({
      nodes: [nodeWithTemplateField('a', '{{foo.output.x}}')],
    });
    expect(useCanvasStore.getState().renameOutputVariableReferences('', 'bar')).toBe(0);
    expect(useCanvasStore.getState().renameOutputVariableReferences('foo', '')).toBe(0);
    expect(useCanvasStore.getState().renameOutputVariableReferences('foo', 'foo')).toBe(0);
    expect(useCanvasStore.getState().nodes[0].data.emailBody).toBe('{{foo.output.x}}');
  });

  it('gracefully skips malformed configJson without throwing', () => {
    const malformed: PlaybookNode = {
      id: 'm',
      type: 'updateRecord',
      position: { x: 0, y: 0 },
      data: { label: 'm', type: 'updateRecord', configJson: 'not-json' },
    };
    useCanvasStore.setState({ nodes: [malformed] });

    expect(() => useCanvasStore.getState().renameOutputVariableReferences('foo', 'bar')).not.toThrow();
    expect(useCanvasStore.getState().nodes[0].data.configJson).toBe('not-json');
  });
});

// ---------------------------------------------------------------------------
// onConnect — Condition node branch interception (task 092)
// ---------------------------------------------------------------------------

describe('canvasStore.onConnect (task 092)', () => {
  function conditionNode(id: string): PlaybookNode {
    return {
      id,
      type: 'condition',
      position: { x: 0, y: 0 },
      data: { label: 'Cond', type: 'condition' },
    };
  }

  function plainNode(id: string): PlaybookNode {
    return {
      id,
      type: 'updateRecord',
      position: { x: 0, y: 0 },
      data: { label: 'plain', type: 'updateRecord' },
    };
  }

  it('defers edge creation and sets pendingBranchConnection when source is Condition without sourceHandle', () => {
    useCanvasStore.setState({
      nodes: [conditionNode('c1'), plainNode('p1')],
      edges: [],
      pendingBranchConnection: null,
    });

    const conn: Connection = { source: 'c1', target: 'p1', sourceHandle: null, targetHandle: null };
    useCanvasStore.getState().onConnect(conn);

    const state = useCanvasStore.getState();
    expect(state.edges).toHaveLength(0);
    expect(state.pendingBranchConnection).not.toBeNull();
    expect(state.pendingBranchConnection?.source).toBe('c1');
  });

  it('creates a trueBranch edge immediately when Condition source has sourceHandle="true"', () => {
    useCanvasStore.setState({
      nodes: [conditionNode('c1'), plainNode('p1')],
      edges: [],
      pendingBranchConnection: null,
    });

    const conn: Connection = { source: 'c1', target: 'p1', sourceHandle: 'true', targetHandle: null };
    useCanvasStore.getState().onConnect(conn);

    const state = useCanvasStore.getState();
    expect(state.edges).toHaveLength(1);
    expect(state.edges[0].type).toBe('trueBranch');
    expect(state.pendingBranchConnection).toBeNull();
  });

  it('creates a normal smoothstep edge when source is NOT a Condition node', () => {
    useCanvasStore.setState({
      nodes: [plainNode('p1'), plainNode('p2')],
      edges: [],
      pendingBranchConnection: null,
    });

    const conn: Connection = { source: 'p1', target: 'p2', sourceHandle: null, targetHandle: null };
    useCanvasStore.getState().onConnect(conn);

    const state = useCanvasStore.getState();
    expect(state.edges).toHaveLength(1);
    expect(state.edges[0].type).toBe('smoothstep');
    expect(state.pendingBranchConnection).toBeNull();
  });
});
