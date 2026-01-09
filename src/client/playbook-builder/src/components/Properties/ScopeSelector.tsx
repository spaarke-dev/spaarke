import { useCallback, useMemo } from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Label,
  Dropdown,
  Option,
  Divider,
  shorthands,
  Checkbox,
} from '@fluentui/react-components';
import type {
  DropdownProps,
  OptionOnSelectData,
  SelectionEvents,
  CheckboxOnChangeData,
} from '@fluentui/react-components';
import {
  BrainCircuitRegular,
  LibraryRegular,
  WrenchRegular,
} from '@fluentui/react-icons';
import { useCanvasStore, PlaybookNodeType } from '../../stores';
import {
  useScopeStore,
  SkillItem,
  KnowledgeItem,
  ToolItem,
} from '../../stores/scopeStore';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  sectionHeader: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    marginBottom: tokens.spacingVerticalXS,
  },
  checkboxGroup: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    ...shorthands.padding(tokens.spacingVerticalS, 0),
    maxHeight: '180px',
    overflowY: 'auto',
  },
  checkboxItem: {
    display: 'flex',
    flexDirection: 'column',
  },
  itemDescription: {
    color: tokens.colorNeutralForeground3,
    marginLeft: '28px', // Align with checkbox label
  },
  emptyState: {
    color: tokens.colorNeutralForeground3,
    fontStyle: 'italic',
  },
  disabledMessage: {
    color: tokens.colorNeutralForeground4,
    fontStyle: 'italic',
    ...shorthands.padding(tokens.spacingVerticalS),
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
  },
});

interface ScopeSelectorProps {
  nodeId: string;
  nodeType: PlaybookNodeType;
  skillIds: string[];
  knowledgeIds: string[];
  toolId?: string;
}

/**
 * ScopeSelector component for selecting skills, knowledge, and tools for a node.
 * Filters available options based on the node's action type capabilities.
 */
export function ScopeSelector({
  nodeId,
  nodeType,
  skillIds,
  knowledgeIds,
  toolId,
}: ScopeSelectorProps) {
  const styles = useStyles();
  const updateNode = useCanvasStore((state) => state.updateNode);

  // Get capabilities and available items from scope store
  const { skills, knowledge, tools, getCapabilities } = useScopeStore();
  const capabilities = useMemo(() => getCapabilities(nodeType), [getCapabilities, nodeType]);

  // If no capabilities are enabled, show a message
  if (!capabilities.allowsSkills && !capabilities.allowsKnowledge && !capabilities.allowsTools) {
    return (
      <div className={styles.container}>
        <Divider>Scope</Divider>
        <Text className={styles.disabledMessage}>
          This node type does not support scope selections.
        </Text>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <Divider>Scope</Divider>

      {/* Skills Section */}
      {capabilities.allowsSkills && (
        <SkillsMultiSelect
          skills={skills}
          selectedIds={skillIds}
          nodeId={nodeId}
          onUpdate={updateNode}
        />
      )}

      {/* Knowledge Section */}
      {capabilities.allowsKnowledge && (
        <KnowledgeMultiSelect
          knowledge={knowledge}
          selectedIds={knowledgeIds}
          nodeId={nodeId}
          onUpdate={updateNode}
        />
      )}

      {/* Tool Section */}
      {capabilities.allowsTools && (
        <ToolDropdown
          tools={tools}
          selectedId={toolId}
          nodeId={nodeId}
          onUpdate={updateNode}
        />
      )}
    </div>
  );
}

// ============================================================================
// Skills Multi-Select Component
// ============================================================================

interface SkillsMultiSelectProps {
  skills: SkillItem[];
  selectedIds: string[];
  nodeId: string;
  onUpdate: (nodeId: string, data: Record<string, unknown>) => void;
}

function SkillsMultiSelect({ skills, selectedIds, nodeId, onUpdate }: SkillsMultiSelectProps) {
  const styles = useStyles();

  const handleSkillToggle = useCallback(
    (skillId: string) => (_e: React.ChangeEvent<HTMLInputElement>, data: CheckboxOnChangeData) => {
      const newIds = data.checked
        ? [...selectedIds, skillId]
        : selectedIds.filter((id) => id !== skillId);
      onUpdate(nodeId, { skillIds: newIds });
    },
    [selectedIds, nodeId, onUpdate]
  );

  return (
    <div className={styles.section}>
      <div className={styles.sectionHeader}>
        <BrainCircuitRegular />
        <Label>Skills</Label>
      </div>
      {skills.length === 0 ? (
        <Text className={styles.emptyState}>No skills available</Text>
      ) : (
        <div className={styles.checkboxGroup}>
          {skills.map((skill) => (
            <div key={skill.id} className={styles.checkboxItem}>
              <Checkbox
                checked={selectedIds.includes(skill.id)}
                onChange={handleSkillToggle(skill.id)}
                label={skill.name}
              />
              {skill.description && (
                <Text size={100} className={styles.itemDescription}>
                  {skill.description}
                </Text>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// ============================================================================
// Knowledge Multi-Select Component
// ============================================================================

interface KnowledgeMultiSelectProps {
  knowledge: KnowledgeItem[];
  selectedIds: string[];
  nodeId: string;
  onUpdate: (nodeId: string, data: Record<string, unknown>) => void;
}

function KnowledgeMultiSelect({ knowledge, selectedIds, nodeId, onUpdate }: KnowledgeMultiSelectProps) {
  const styles = useStyles();

  const handleKnowledgeToggle = useCallback(
    (knowledgeId: string) => (_e: React.ChangeEvent<HTMLInputElement>, data: CheckboxOnChangeData) => {
      const newIds = data.checked
        ? [...selectedIds, knowledgeId]
        : selectedIds.filter((id) => id !== knowledgeId);
      onUpdate(nodeId, { knowledgeIds: newIds });
    },
    [selectedIds, nodeId, onUpdate]
  );

  return (
    <div className={styles.section}>
      <div className={styles.sectionHeader}>
        <LibraryRegular />
        <Label>Knowledge</Label>
      </div>
      {knowledge.length === 0 ? (
        <Text className={styles.emptyState}>No knowledge sources available</Text>
      ) : (
        <div className={styles.checkboxGroup}>
          {knowledge.map((item) => (
            <div key={item.id} className={styles.checkboxItem}>
              <Checkbox
                checked={selectedIds.includes(item.id)}
                onChange={handleKnowledgeToggle(item.id)}
                label={item.name}
              />
              {item.description && (
                <Text size={100} className={styles.itemDescription}>
                  {item.description}
                </Text>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// ============================================================================
// Tool Dropdown Component (Single Select)
// ============================================================================

interface ToolDropdownProps {
  tools: ToolItem[];
  selectedId?: string;
  nodeId: string;
  onUpdate: (nodeId: string, data: Record<string, unknown>) => void;
}

function ToolDropdown({ tools, selectedId, nodeId, onUpdate }: ToolDropdownProps) {
  const styles = useStyles();

  const selectedTool = useMemo(
    () => tools.find((t) => t.id === selectedId),
    [tools, selectedId]
  );

  const handleToolChange: DropdownProps['onOptionSelect'] = useCallback(
    (_event: SelectionEvents, data: OptionOnSelectData) => {
      // data.optionValue is the tool ID or empty string for "(None)"
      onUpdate(nodeId, { toolId: data.optionValue || undefined });
    },
    [nodeId, onUpdate]
  );

  return (
    <div className={styles.section}>
      <div className={styles.sectionHeader}>
        <WrenchRegular />
        <Label htmlFor="tool-dropdown">Tool</Label>
      </div>
      <Dropdown
        id="tool-dropdown"
        placeholder="Select a tool..."
        value={selectedTool?.name || ''}
        selectedOptions={selectedId ? [selectedId] : []}
        onOptionSelect={handleToolChange}
      >
        <Option key="none" value="">
          (None)
        </Option>
        {tools.map((tool) => (
          <Option key={tool.id} value={tool.id} text={tool.name}>
            <div>
              <Text weight="semibold">{tool.name}</Text>
              {tool.description && (
                <Text size={100} style={{ display: 'block', color: tokens.colorNeutralForeground3 }}>
                  {tool.description}
                </Text>
              )}
            </div>
          </Option>
        ))}
      </Dropdown>
    </div>
  );
}
