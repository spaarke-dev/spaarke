import { makeStyles, tokens, Text, shorthands } from '@fluentui/react-components';
import { Settings20Regular } from '@fluentui/react-icons';
import { useCanvasStore } from '../../stores';
import { NodePropertiesForm } from './NodePropertiesForm';

const useStyles = makeStyles({
  panel: {
    width: '300px',
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderLeft('1px', 'solid', tokens.colorNeutralStroke1),
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.overflow('hidden'),
  },
  header: {
    padding: tokens.spacingVerticalM,
    ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke2),
    backgroundColor: tokens.colorNeutralBackground1,
  },
  headerTitle: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  content: {
    flex: 1,
    ...shorthands.overflow('auto'),
    padding: tokens.spacingVerticalM,
  },
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
    padding: tokens.spacingHorizontalL,
  },
  emptyIcon: {
    fontSize: '48px',
    color: tokens.colorNeutralForeground4,
  },
});

/**
 * Properties panel component that shows node configuration
 * when a node is selected, or an empty state otherwise.
 */
export function PropertiesPanel() {
  const styles = useStyles();
  const { selectedNodeId, nodes } = useCanvasStore((state) => ({
    selectedNodeId: state.selectedNodeId,
    nodes: state.nodes,
  }));

  const selectedNode = selectedNodeId
    ? nodes.find((n) => n.id === selectedNodeId)
    : null;

  return (
    <div className={styles.panel}>
      <div className={styles.header}>
        <div className={styles.headerTitle}>
          <Settings20Regular />
          <Text weight="semibold" size={400}>
            Properties
          </Text>
        </div>
      </div>

      <div className={styles.content}>
        {selectedNode ? (
          <NodePropertiesForm node={selectedNode} />
        ) : (
          <div className={styles.emptyState}>
            <Settings20Regular className={styles.emptyIcon} />
            <Text size={300}>No node selected</Text>
            <Text size={200}>
              Select a node on the canvas to view and edit its properties
            </Text>
          </div>
        )}
      </div>
    </div>
  );
}
