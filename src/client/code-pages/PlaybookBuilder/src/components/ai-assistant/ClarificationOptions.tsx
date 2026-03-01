/**
 * Clarification Options Component - Interactive multiple-choice UI for AI clarifications
 *
 * Displays clarification questions from the AI assistant with selectable option buttons,
 * "Other" option with free-text input, context display, and responsive styling.
 *
 * @version 2.0.0 (Code Page migration)
 */

import React, { useState, useCallback } from "react";
import {
    Button,
    Input,
    Text,
    makeStyles,
    tokens,
    shorthands,
    mergeClasses,
} from "@fluentui/react-components";
import {
    CheckmarkCircle20Regular,
    Send20Regular,
    Edit20Regular,
} from "@fluentui/react-icons";
import type { ClarificationData } from "../../stores/aiAssistantStore";

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap(tokens.spacingVerticalM),
        ...shorthands.padding(tokens.spacingVerticalS, "0"),
        maxWidth: "100%",
    },
    contextText: {
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase200,
        fontStyle: "italic",
        ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalS),
        backgroundColor: tokens.colorNeutralBackground3,
        ...shorthands.borderRadius(tokens.borderRadiusSmall),
    },
    optionsContainer: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap(tokens.spacingVerticalS),
    },
    optionButton: {
        justifyContent: "flex-start",
        textAlign: "left",
        minHeight: "40px",
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
        backgroundColor: tokens.colorNeutralBackground1,
        ...shorthands.border("1px", "solid", tokens.colorNeutralStroke1),
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
        ":hover": {
            backgroundColor: tokens.colorNeutralBackground1Hover,
            ...shorthands.borderColor(tokens.colorBrandStroke1),
        },
        ":active": {
            backgroundColor: tokens.colorNeutralBackground1Pressed,
        },
    },
    optionButtonSelected: {
        backgroundColor: tokens.colorBrandBackground2,
        ...shorthands.borderColor(tokens.colorBrandStroke1),
        ":hover": {
            backgroundColor: tokens.colorBrandBackground2Hover,
        },
    },
    optionButtonResponded: {
        opacity: 0.7,
        cursor: "default",
    },
    optionText: {
        flex: 1,
        wordBreak: "break-word",
    },
    optionNumber: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        width: "24px",
        height: "24px",
        minWidth: "24px",
        backgroundColor: tokens.colorNeutralBackground3,
        color: tokens.colorNeutralForeground2,
        ...shorthands.borderRadius(tokens.borderRadiusCircular),
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
        marginRight: tokens.spacingHorizontalS,
    },
    optionNumberSelected: {
        backgroundColor: tokens.colorBrandBackground,
        color: tokens.colorNeutralForegroundOnBrand,
    },
    otherSection: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap(tokens.spacingVerticalS),
        ...shorthands.padding(tokens.spacingVerticalS, "0"),
        ...shorthands.borderTop("1px", "solid", tokens.colorNeutralStroke2),
        marginTop: tokens.spacingVerticalXS,
    },
    otherInputRow: {
        display: "flex",
        ...shorthands.gap(tokens.spacingHorizontalS),
        alignItems: "flex-start",
    },
    otherInput: {
        flex: 1,
        minHeight: "36px",
    },
    submitButton: {
        minWidth: "80px",
    },
    selectedResponse: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalS),
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
        backgroundColor: tokens.colorBrandBackground2,
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
        ...shorthands.border("1px", "solid", tokens.colorBrandStroke1),
    },
    selectedResponseText: {
        color: tokens.colorBrandForeground1,
        fontWeight: tokens.fontWeightSemibold,
    },
});

export interface ClarificationOptionsProps {
    clarification: ClarificationData;
    messageId: string;
    onRespond: (
        messageId: string,
        response: { selectedOption: number | "other"; freeText?: string }
    ) => void;
    disabled?: boolean;
}

export const ClarificationOptions: React.FC<ClarificationOptionsProps> = ({
    clarification,
    messageId,
    onRespond,
    disabled = false,
}) => {
    const styles = useStyles();
    const [selectedOption, setSelectedOption] = useState<number | "other" | null>(null);
    const [otherText, setOtherText] = useState("");
    const [showOtherInput, setShowOtherInput] = useState(false);

    const hasResponded = clarification.responded === true;

    const handleOptionClick = useCallback(
        (index: number) => {
            if (hasResponded || disabled) return;
            setSelectedOption(index);
            setShowOtherInput(false);
            onRespond(messageId, { selectedOption: index });
        },
        [hasResponded, disabled, messageId, onRespond]
    );

    const handleOtherClick = useCallback(() => {
        if (hasResponded || disabled) return;
        setSelectedOption("other");
        setShowOtherInput(true);
    }, [hasResponded, disabled]);

    const handleOtherSubmit = useCallback(() => {
        if (hasResponded || disabled || !otherText.trim()) return;
        onRespond(messageId, { selectedOption: "other", freeText: otherText.trim() });
    }, [hasResponded, disabled, otherText, messageId, onRespond]);

    const handleOtherKeyDown = useCallback(
        (e: React.KeyboardEvent<HTMLInputElement>) => {
            if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                handleOtherSubmit();
            }
        },
        [handleOtherSubmit]
    );

    const getSelectedResponseText = (): string => {
        if (!hasResponded) return "";
        if (clarification.selectedOption === "other") {
            return clarification.freeTextResponse ?? "";
        }
        const options = clarification.options ?? [];
        const optionIndex = clarification.selectedOption as number;
        return options[optionIndex] ?? `Option ${optionIndex + 1}`;
    };

    if (hasResponded) {
        return (
            <div className={styles.container}>
                {clarification.context && (
                    <Text className={styles.contextText}>{clarification.context}</Text>
                )}
                <div className={styles.selectedResponse}>
                    <CheckmarkCircle20Regular />
                    <Text className={styles.selectedResponseText}>
                        {getSelectedResponseText()}
                    </Text>
                </div>
            </div>
        );
    }

    const options = clarification.options ?? [];

    return (
        <div className={styles.container} role="group" aria-label="Clarification options">
            {clarification.context && (
                <Text className={styles.contextText}>{clarification.context}</Text>
            )}

            {options.length > 0 && (
                <div className={styles.optionsContainer} role="list">
                    {options.map((option, index) => (
                        <Button
                            key={`option-${index}`}
                            className={mergeClasses(
                                styles.optionButton,
                                selectedOption === index && styles.optionButtonSelected,
                                disabled && styles.optionButtonResponded
                            )}
                            appearance="subtle"
                            disabled={disabled}
                            onClick={() => handleOptionClick(index)}
                            aria-label={`Option ${index + 1}: ${option}`}
                            role="listitem"
                        >
                            <span
                                className={mergeClasses(
                                    styles.optionNumber,
                                    selectedOption === index && styles.optionNumberSelected
                                )}
                            >
                                {index + 1}
                            </span>
                            <Text className={styles.optionText}>{option}</Text>
                        </Button>
                    ))}
                </div>
            )}

            <div className={styles.otherSection}>
                {!showOtherInput ? (
                    <Button
                        className={mergeClasses(
                            styles.optionButton,
                            selectedOption === "other" && styles.optionButtonSelected,
                            disabled && styles.optionButtonResponded
                        )}
                        appearance="subtle"
                        disabled={disabled}
                        onClick={handleOtherClick}
                        icon={<Edit20Regular />}
                        aria-label="Enter a different response"
                    >
                        <Text className={styles.optionText}>Other (type your response)</Text>
                    </Button>
                ) : (
                    <div className={styles.otherInputRow}>
                        <Input
                            className={styles.otherInput}
                            placeholder="Type your response..."
                            value={otherText}
                            onChange={(_, data) => setOtherText(data.value)}
                            onKeyDown={handleOtherKeyDown}
                            disabled={disabled}
                            autoFocus
                            aria-label="Custom response input"
                        />
                        <Button
                            className={styles.submitButton}
                            appearance="primary"
                            disabled={disabled || !otherText.trim()}
                            onClick={handleOtherSubmit}
                            icon={<Send20Regular />}
                            aria-label="Submit custom response"
                        >
                            Send
                        </Button>
                    </div>
                )}
            </div>
        </div>
    );
};

export default ClarificationOptions;
