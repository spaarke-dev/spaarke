/**
 * ModelSelector — AI model deployment dropdown.
 *
 * Reads from modelStore. Shows active models with provider badge and context window.
 * Only rendered for AI node types (aiAnalysis, aiCompletion).
 */

import { memo, useEffect, useCallback } from "react";
import {
    makeStyles,
    tokens,
    shorthands,
    Dropdown,
    Option,
    Badge,
    Spinner,
    Text,
} from "@fluentui/react-components";
import { useModelStore } from "../../stores/modelStore";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

interface ModelSelectorProps {
    modelDeploymentId?: string;
    onModelChange: (modelId: string | undefined) => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap("4px"),
    },
    optionRow: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap("8px"),
    },
    optionName: {
        flex: 1,
    },
    contextHint: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase200,
    },
});

// ---------------------------------------------------------------------------
// Provider badge colors
// ---------------------------------------------------------------------------

function getProviderAppearance(provider: string): "informative" | "success" | "warning" | "important" {
    const p = provider.toLowerCase();
    if (p.includes("azure")) return "informative";
    if (p.includes("openai")) return "success";
    if (p.includes("anthropic")) return "warning";
    return "important";
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ModelSelector = memo(function ModelSelector({
    modelDeploymentId,
    onModelChange,
}: ModelSelectorProps) {
    const styles = useStyles();

    const models = useModelStore((s) => s.models);
    const isLoading = useModelStore((s) => s.isLoading);
    const loadModelDeployments = useModelStore((s) => s.loadModelDeployments);
    const getActiveModels = useModelStore((s) => s.getActiveModels);
    const getModelById = useModelStore((s) => s.getModelById);

    // Lazy load on first render
    useEffect(() => {
        if (models.length === 0) {
            loadModelDeployments();
        }
    }, [models.length, loadModelDeployments]);

    const handleSelect = useCallback(
        (_: unknown, data: { optionValue?: string }) => {
            onModelChange(data.optionValue === "__default__" ? undefined : data.optionValue);
        },
        [onModelChange],
    );

    if (isLoading) {
        return <Spinner size="tiny" label="Loading models..." />;
    }

    const activeModels = getActiveModels();
    const selectedModel = modelDeploymentId ? getModelById(modelDeploymentId) : undefined;

    return (
        <div className={styles.root}>
            <Dropdown
                size="small"
                value={selectedModel ? selectedModel.name : "(Default)"}
                selectedOptions={modelDeploymentId ? [modelDeploymentId] : ["__default__"]}
                onOptionSelect={handleSelect}
            >
                <Option key="__default__" value="__default__">
                    (Default — System Model)
                </Option>
                {activeModels.map((model) => (
                    <Option key={model.id} value={model.id} text={model.name}>
                        <div className={styles.optionRow}>
                            <span className={styles.optionName}>{model.name}</span>
                            <Badge size="small" appearance={getProviderAppearance(model.provider)}>
                                {model.provider}
                            </Badge>
                        </div>
                    </Option>
                ))}
            </Dropdown>
            <Text className={styles.contextHint}>
                {selectedModel
                    ? `Context: ${(selectedModel.contextWindow / 1000).toFixed(0)}k tokens`
                    : "Uses the system default AI model"}
            </Text>
        </div>
    );
});
