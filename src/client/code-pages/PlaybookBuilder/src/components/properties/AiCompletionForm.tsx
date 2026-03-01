/**
 * AiCompletionForm - Configuration form for AI Completion nodes.
 *
 * Allows users to configure AI completion settings:
 * - System prompt (multiline)
 * - User prompt template (multiline, supports template variables)
 * - Model deployment ID (dropdown, accepts string for now)
 * - Temperature (SpinButton 0.0-2.0, step 0.1)
 * - Max tokens (SpinButton 100-32000)
 * - Skill IDs (multi-select, accepts string array prop for now)
 *
 * @see ADR-021 - Fluent UI v9 design system (dark mode required)
 * @see ADR-013 - AI Architecture
 */

import { useCallback, useMemo, memo } from "react";
import {
    makeStyles,
    tokens,
    Text,
    Input,
    Label,
    Textarea,
    Dropdown,
    Option,
    SpinButton,
    Checkbox,
    shorthands,
} from "@fluentui/react-components";
import type {
    DropdownProps,
    OptionOnSelectData,
    SelectionEvents,
    SpinButtonChangeEvent,
    SpinButtonOnChangeData,
    CheckboxOnChangeData,
} from "@fluentui/react-components";
import type { NodeFormProps } from "../../types/forms";

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
    promptArea: {
        minHeight: "100px",
    },
    parameterRow: {
        display: "grid",
        gridTemplateColumns: "1fr 1fr",
        gap: tokens.spacingHorizontalM,
    },
    skillsGroup: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
        maxHeight: "150px",
        overflowY: "auto",
        ...shorthands.padding(tokens.spacingVerticalXS, "0"),
    },
    emptyState: {
        color: tokens.colorNeutralForeground3,
        fontStyle: "italic",
    },
});

// ---------------------------------------------------------------------------
// Config shape
// ---------------------------------------------------------------------------

interface AiCompletionConfig {
    systemPrompt: string;
    userPromptTemplate: string;
    modelDeploymentId: string;
    temperature: number;
    maxTokens: number;
    skillIds: string[];
}

const DEFAULT_CONFIG: AiCompletionConfig = {
    systemPrompt: "",
    userPromptTemplate: "",
    modelDeploymentId: "",
    temperature: 0.7,
    maxTokens: 4096,
    skillIds: [],
};

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

interface AiCompletionFormProps extends NodeFormProps {
    /** Available model deployment options. */
    availableModels?: Array<{ id: string; name: string; description?: string }>;
    /** Available skill options for multi-select. */
    availableSkills?: Array<{ id: string; name: string; description?: string }>;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function parseConfig(json: string): AiCompletionConfig {
    try {
        const parsed = JSON.parse(json) as Partial<AiCompletionConfig>;
        return {
            systemPrompt: typeof parsed.systemPrompt === "string"
                ? parsed.systemPrompt
                : DEFAULT_CONFIG.systemPrompt,
            userPromptTemplate: typeof parsed.userPromptTemplate === "string"
                ? parsed.userPromptTemplate
                : DEFAULT_CONFIG.userPromptTemplate,
            modelDeploymentId: typeof parsed.modelDeploymentId === "string"
                ? parsed.modelDeploymentId
                : DEFAULT_CONFIG.modelDeploymentId,
            temperature: typeof parsed.temperature === "number"
                && parsed.temperature >= 0
                && parsed.temperature <= 2
                ? parsed.temperature
                : DEFAULT_CONFIG.temperature,
            maxTokens: typeof parsed.maxTokens === "number"
                && parsed.maxTokens >= 100
                && parsed.maxTokens <= 32000
                ? parsed.maxTokens
                : DEFAULT_CONFIG.maxTokens,
            skillIds: Array.isArray(parsed.skillIds)
                ? parsed.skillIds.filter((id): id is string => typeof id === "string")
                : DEFAULT_CONFIG.skillIds,
        };
    } catch {
        return { ...DEFAULT_CONFIG };
    }
}

function serializeConfig(config: AiCompletionConfig): string {
    return JSON.stringify(config);
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const AiCompletionForm = memo(function AiCompletionForm({
    nodeId,
    configJson,
    onConfigChange,
    availableModels = [],
    availableSkills = [],
}: AiCompletionFormProps) {
    const styles = useStyles();
    const config = useMemo(() => parseConfig(configJson), [configJson]);

    const update = useCallback(
        (patch: Partial<AiCompletionConfig>) => {
            onConfigChange(serializeConfig({ ...config, ...patch }));
        },
        [config, onConfigChange],
    );

    // -- Handlers --

    const handleSystemPromptChange = useCallback(
        (e: React.ChangeEvent<HTMLTextAreaElement>) => {
            update({ systemPrompt: e.target.value });
        },
        [update],
    );

    const handleUserPromptChange = useCallback(
        (e: React.ChangeEvent<HTMLTextAreaElement>) => {
            update({ userPromptTemplate: e.target.value });
        },
        [update],
    );

    const handleModelChange: DropdownProps["onOptionSelect"] = useCallback(
        (_event: SelectionEvents, data: OptionOnSelectData) => {
            update({ modelDeploymentId: data.optionValue ?? "" });
        },
        [update],
    );

    const handleTemperatureChange = useCallback(
        (_e: SpinButtonChangeEvent, data: SpinButtonOnChangeData) => {
            if (data.value !== undefined && data.value !== null) {
                // Round to 1 decimal place to avoid floating point drift
                update({ temperature: Math.round(data.value * 10) / 10 });
            }
        },
        [update],
    );

    const handleMaxTokensChange = useCallback(
        (_e: SpinButtonChangeEvent, data: SpinButtonOnChangeData) => {
            if (data.value !== undefined && data.value !== null) {
                update({ maxTokens: data.value });
            }
        },
        [update],
    );

    const handleSkillToggle = useCallback(
        (skillId: string) =>
            (_e: React.ChangeEvent<HTMLInputElement>, data: CheckboxOnChangeData) => {
                const newIds = data.checked
                    ? [...config.skillIds, skillId]
                    : config.skillIds.filter((id) => id !== skillId);
                update({ skillIds: newIds });
            },
        [config.skillIds, update],
    );

    // Find selected model name for display
    const selectedModelName = useMemo(() => {
        if (!config.modelDeploymentId) return "";
        const model = availableModels.find((m) => m.id === config.modelDeploymentId);
        return model?.name ?? config.modelDeploymentId;
    }, [config.modelDeploymentId, availableModels]);

    // -- Render --

    return (
        <div className={styles.form}>
            {/* System Prompt */}
            <div className={styles.field}>
                <Label htmlFor={`${nodeId}-systemPrompt`} size="small">
                    System Prompt
                </Label>
                <Textarea
                    id={`${nodeId}-systemPrompt`}
                    size="small"
                    className={styles.promptArea}
                    value={config.systemPrompt}
                    onChange={handleSystemPromptChange}
                    placeholder="You are an AI assistant that helps analyze legal documents..."
                    resize="vertical"
                />
                <Text className={styles.fieldHint}>
                    Instructions that define the AI assistant's behavior and role
                </Text>
            </div>

            {/* User Prompt Template */}
            <div className={styles.field}>
                <Label htmlFor={`${nodeId}-userPromptTemplate`} size="small" required>
                    User Prompt Template
                </Label>
                <Textarea
                    id={`${nodeId}-userPromptTemplate`}
                    size="small"
                    className={styles.promptArea}
                    value={config.userPromptTemplate}
                    onChange={handleUserPromptChange}
                    placeholder={"Analyze the following document:\n\n{{extraction.output.content}}\n\nProvide a summary focusing on key terms."}
                    resize="vertical"
                />
                <Text className={styles.fieldHint}>
                    Supports template variables: {"{{nodeName.output.fieldName}}"}
                </Text>
            </div>

            {/* Model Deployment */}
            <div className={styles.field}>
                <Label htmlFor={`${nodeId}-modelDeploymentId`} size="small">
                    AI Model
                </Label>
                {availableModels.length > 0 ? (
                    <Dropdown
                        id={`${nodeId}-modelDeploymentId`}
                        size="small"
                        value={selectedModelName}
                        selectedOptions={config.modelDeploymentId ? [config.modelDeploymentId] : []}
                        onOptionSelect={handleModelChange}
                        placeholder="Select a model..."
                    >
                        <Option key="default" value="" text="(Default)">
                            (Default - uses playbook default model)
                        </Option>
                        {availableModels.map((model) => (
                            <Option key={model.id} value={model.id} text={model.name}>
                                {model.name}
                                {model.description ? ` - ${model.description}` : ""}
                            </Option>
                        ))}
                    </Dropdown>
                ) : (
                    <Input
                        id={`${nodeId}-modelDeploymentId`}
                        size="small"
                        value={config.modelDeploymentId}
                        onChange={(e) => update({ modelDeploymentId: e.target.value })}
                        placeholder="Model deployment ID (e.g., gpt-4o)"
                    />
                )}
                <Text className={styles.fieldHint}>
                    AI model deployment to use for this completion
                </Text>
            </div>

            {/* Temperature + Max Tokens (side by side) */}
            <div className={styles.parameterRow}>
                <div className={styles.field}>
                    <Label htmlFor={`${nodeId}-temperature`} size="small">
                        Temperature
                    </Label>
                    <SpinButton
                        id={`${nodeId}-temperature`}
                        size="small"
                        value={config.temperature}
                        onChange={handleTemperatureChange}
                        min={0}
                        max={2}
                        step={0.1}
                    />
                    <Text className={styles.fieldHint}>
                        0.0 (focused) to 2.0 (creative)
                    </Text>
                </div>

                <div className={styles.field}>
                    <Label htmlFor={`${nodeId}-maxTokens`} size="small">
                        Max Tokens
                    </Label>
                    <SpinButton
                        id={`${nodeId}-maxTokens`}
                        size="small"
                        value={config.maxTokens}
                        onChange={handleMaxTokensChange}
                        min={100}
                        max={32000}
                        step={100}
                    />
                    <Text className={styles.fieldHint}>
                        100-32,000 tokens
                    </Text>
                </div>
            </div>

            {/* Skill IDs */}
            <div className={styles.field}>
                <Label size="small">Skills</Label>
                {availableSkills.length > 0 ? (
                    <div className={styles.skillsGroup}>
                        {availableSkills.map((skill) => (
                            <Checkbox
                                key={skill.id}
                                checked={config.skillIds.includes(skill.id)}
                                onChange={handleSkillToggle(skill.id)}
                                label={skill.name}
                            />
                        ))}
                    </div>
                ) : (
                    <Text className={styles.emptyState}>
                        No skills available. Skills can be configured in the scope settings.
                    </Text>
                )}
            </div>
        </div>
    );
});
