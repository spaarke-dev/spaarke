/**
 * DeliverToIndexForm - Configuration form for Deliver to Index nodes.
 *
 * Allows users to configure RAG semantic indexing parameters:
 * - Index name (target search index)
 * - Source (document or content variable)
 * - Parent entity context (for entity-scoped search)
 * - Metadata key-value pairs
 *
 * Updates typed PlaybookNodeData fields directly so buildConfigJson()
 * in playbookNodeSync produces the correct sprk_configjson for the
 * server-side DeliverToIndexNodeExecutor.
 *
 * @see ADR-021 - Fluent UI v9 design system (dark mode required)
 */

import { useCallback, memo } from "react";
import {
  makeStyles,
  tokens,
  Text,
  Label,
  Input,
  Textarea,
  Dropdown,
  Option,
} from "@fluentui/react-components";
import type {
  OptionOnSelectData,
  SelectionEvents,
} from "@fluentui/react-components";
import type { PlaybookNodeData } from "../../types/playbook";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface DeliverToIndexFormProps {
  nodeId: string;
  data: PlaybookNodeData;
  onUpdate: (field: string, value: unknown) => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  form: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
  },
  field: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
  },
  fieldHint: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase100,
  },
});

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const SOURCE_OPTIONS = [
  { value: "document", label: "Document" },
  { value: "content", label: "Content Variable" },
] as const;

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const DeliverToIndexForm = memo(function DeliverToIndexForm({
  nodeId,
  data,
  onUpdate,
}: DeliverToIndexFormProps) {
  const styles = useStyles();

  const indexName = (data.indexName as string) || "";
  const indexSource = (data.indexSource as string) || "document";
  const indexContentVariable = (data.indexContentVariable as string) || "";
  const indexParentEntityType = (data.indexParentEntityType as string) || "";
  const indexParentEntityId = (data.indexParentEntityId as string) || "";
  const indexParentEntityName = (data.indexParentEntityName as string) || "";
  const indexMetadata = (data.indexMetadata as string) || "";

  const sourceLabel =
    SOURCE_OPTIONS.find((s) => s.value === indexSource)?.label ?? "Document";

  const handleSourceChange = useCallback(
    (_event: SelectionEvents, item: OptionOnSelectData) => {
      if (item.optionValue) {
        onUpdate("indexSource", item.optionValue);
      }
    },
    [onUpdate],
  );

  return (
    <div className={styles.form}>
      {/* Index Name */}
      <div className={styles.field}>
        <Label htmlFor={`${nodeId}-indexName`} size="small" required>
          Index Name
        </Label>
        <Input
          id={`${nodeId}-indexName`}
          size="small"
          value={indexName}
          onChange={(_e, d) => onUpdate("indexName", d.value)}
          placeholder="knowledge"
        />
        <Text className={styles.fieldHint}>
          Target search index name. Supports {"{{template}}"} syntax.
        </Text>
      </div>

      {/* Source */}
      <div className={styles.field}>
        <Label htmlFor={`${nodeId}-indexSource`} size="small">
          Source
        </Label>
        <Dropdown
          id={`${nodeId}-indexSource`}
          size="small"
          value={sourceLabel}
          selectedOptions={[indexSource]}
          onOptionSelect={handleSourceChange}
        >
          {SOURCE_OPTIONS.map((s) => (
            <Option key={s.value} value={s.value}>
              {s.label}
            </Option>
          ))}
        </Dropdown>
        <Text className={styles.fieldHint}>
          Index the document file or a content variable from a previous node.
        </Text>
      </div>

      {/* Content Variable (shown when source=content) */}
      {indexSource === "content" && (
        <div className={styles.field}>
          <Label htmlFor={`${nodeId}-indexContentVar`} size="small" required>
            Content Variable
          </Label>
          <Input
            id={`${nodeId}-indexContentVar`}
            size="small"
            value={indexContentVariable}
            onChange={(_e, d) => onUpdate("indexContentVariable", d.value)}
            placeholder="analyze.output.content"
          />
          <Text className={styles.fieldHint}>
            Output variable name containing text to index.
          </Text>
        </div>
      )}

      {/* Parent Entity Type */}
      <div className={styles.field}>
        <Label htmlFor={`${nodeId}-parentType`} size="small">
          Parent Entity Type
        </Label>
        <Input
          id={`${nodeId}-parentType`}
          size="small"
          value={indexParentEntityType}
          onChange={(_e, d) => onUpdate("indexParentEntityType", d.value)}
          placeholder="matter"
        />
        <Text className={styles.fieldHint}>
          Entity type for scoped search (matter, project, account, etc.)
        </Text>
      </div>

      {/* Parent Entity ID */}
      <div className={styles.field}>
        <Label htmlFor={`${nodeId}-parentId`} size="small">
          Parent Entity ID
        </Label>
        <Input
          id={`${nodeId}-parentId`}
          size="small"
          value={indexParentEntityId}
          onChange={(_e, d) => onUpdate("indexParentEntityId", d.value)}
          placeholder="{{document.parentEntityId}}"
        />
      </div>

      {/* Parent Entity Name */}
      <div className={styles.field}>
        <Label htmlFor={`${nodeId}-parentName`} size="small">
          Parent Entity Name
        </Label>
        <Input
          id={`${nodeId}-parentName`}
          size="small"
          value={indexParentEntityName}
          onChange={(_e, d) => onUpdate("indexParentEntityName", d.value)}
          placeholder="{{document.parentEntityName}}"
        />
      </div>

      {/* Metadata (JSON) */}
      <div className={styles.field}>
        <Label htmlFor={`${nodeId}-indexMetadata`} size="small">
          Metadata (JSON)
        </Label>
        <Textarea
          id={`${nodeId}-indexMetadata`}
          size="small"
          value={indexMetadata}
          onChange={(e) => onUpdate("indexMetadata", e.target.value)}
          placeholder={'{"category": "legal", "source": "wizard"}'}
          resize="vertical"
        />
        <Text className={styles.fieldHint}>
          Optional key-value pairs as JSON. Supports {"{{template}}"} values.
        </Text>
      </div>
    </div>
  );
});
