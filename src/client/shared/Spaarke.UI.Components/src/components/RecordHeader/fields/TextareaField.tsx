/**
 * TextareaField — multiline read-only field renderer with "show more" popover (FR-04).
 *
 * Renders a label + multiline value clamped to `maxLines` (default 3) using
 * standard `-webkit-line-clamp` box-orient trick. If the content overflows the
 * clamped box, a "Show more" Fluent v9 `Link` appears beneath the clamped text
 * and, on click, opens a Fluent v9 `Popover` containing the full value in a
 * scrollable content area.
 *
 * Null / undefined values render as the em-dash "—".
 *
 * Grid integration: applies `gridColumn: span N` on the outer wrapper per FR-03
 * so the cell honors its span within a `FieldGrid` (2-col or 3-col).
 *
 * Per ADR-021: Fluent v9 + Griffel + semantic tokens exclusively. No hex / rgb
 * / hsl literals. Popover chrome (background, border, text) inherits Fluent
 * theming so the component adapts to light, dark, and high-contrast modes.
 *
 * Per ADR-022 (React 16/17 boundary): plain functional component using only
 * `useState`, `useRef`, `useEffect`, `useLayoutEffect`, `useCallback` — safe
 * to consume from PCFs (`react@16.14`). No React-18 exclusive APIs.
 *
 * @see FR-04 record-header-and-notepad-r1 spec
 * @see ADR-021 Fluent UI v9 design system
 * @see ADR-022 PCF platform libraries
 *
 * @example
 * ```tsx
 * <TextareaField
 *   span={3}
 *   label="Matter Description"
 *   value={values.sprk_matterdescription as string | null}
 *   maxLines={3}
 * />
 * ```
 */
import * as React from 'react';
import {
  makeStyles,
  mergeClasses,
  tokens,
  Text,
  Label,
  Link,
  Popover,
  PopoverTrigger,
  PopoverSurface,
} from '@fluentui/react-components';

const { useCallback, useEffect, useRef, useState } = React;

/**
 * The em-dash rendered for null / undefined / empty values (FR-04).
 */
const EMPTY_PLACEHOLDER = '—';

/**
 * Props for {@link TextareaField}.
 */
export interface ITextareaFieldProps {
  /** Field label rendered above the clamped value. */
  label: string;

  /**
   * Multiline value to render. `null` / `undefined` render as the em-dash "—".
   * Empty string is treated as empty (also renders em-dash).
   */
  value: string | null | undefined;

  /**
   * Grid column span. FieldGrid does not set `gridColumn` on children per
   * FR-03 — this component applies `gridColumn: span N` inline on its wrapper.
   */
  span: 1 | 2 | 3;

  /**
   * Maximum number of lines shown in the clamped view. Overflow triggers the
   * "Show more" link + popover.
   *
   * @defaultValue 3
   */
  maxLines?: number;

  /** Optional extra className applied to the outer wrapper. */
  className?: string;
}

const useStyles = makeStyles({
  /** Outer wrapper — vertical stack of label + clamped value + optional link. */
  wrapper: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
  },
  /** The label rendered above the value. */
  label: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  /**
   * The clamped value container. Uses the `-webkit-box` + `-webkit-line-clamp`
   * combination — the only CSS mechanism widely supported today for a strict
   * line-count clamp with an ellipsis. `white-space: pre-line` preserves user
   * newlines while still allowing wrapping.
   *
   * `--sprk-textarea-max-lines` is a CSS custom property assigned inline via
   * the component's `style` prop so `maxLines` is a real prop (not baked into
   * static Griffel styles).
   */
  clamped: {
    display: '-webkit-box',
    WebkitBoxOrient: 'vertical',
    // Line count is set inline via CSS var — see wrapper style prop below.
    WebkitLineClamp: 'var(--sprk-textarea-max-lines, 3)',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'pre-line',
    wordBreak: 'break-word',
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase300,
  },
  /** Em-dash placeholder — same styling as the value row for alignment. */
  placeholder: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground3,
  },
  /** "Show more" link — subtle, small font. */
  showMoreLink: {
    fontSize: tokens.fontSizeBase200,
    // `alignSelf` keeps the link left-aligned within the flex column.
    alignSelf: 'flex-start',
  },
  /**
   * Popover content surface for the full text. Scrollable when text is long;
   * width bounded per U-03 (spec unresolved question) at 320..480px.
   */
  popoverSurface: {
    minWidth: '320px',
    maxWidth: '480px',
    maxHeight: '480px',
    overflowY: 'auto',
    padding: tokens.spacingVerticalM,
  },
  popoverLabel: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    marginBottom: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground1,
  },
  popoverBody: {
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase300,
  },
});

/**
 * Multiline read-only field with a "Show more" popover when the value overflows.
 *
 * See file-level JSDoc for full contract, empty-state, and grid-span behavior.
 */
export const TextareaField: React.FC<ITextareaFieldProps> = ({
  label,
  value,
  span,
  maxLines = 3,
  className,
}) => {
  const styles = useStyles();

  // Ref to the clamped element so we can measure overflow.
  const clampedRef = useRef<HTMLDivElement | null>(null);
  const [hasOverflow, setHasOverflow] = useState(false);

  // Normalize value: null / undefined / empty-string all render em-dash.
  const isEmpty = value === null || value === undefined || value === '';
  const displayValue = isEmpty ? EMPTY_PLACEHOLDER : (value as string);

  /**
   * Measure whether the clamped content overflows.
   *
   * `scrollHeight` reflects the full unclamped content height; `clientHeight`
   * reflects the visible clamped box. When `scrollHeight > clientHeight`, the
   * clamp truncated content and we need to show the "Show more" affordance.
   *
   * Runs on mount + whenever `value` / `maxLines` change. Uses plain
   * `useEffect` (not `useLayoutEffect`) so it stays React-16/17-safe without
   * pulling in server-side-render warnings; a single re-render after layout
   * is acceptable here because the affordance appears below the clamp region
   * (no visual jump above the clamp).
   */
  const measureOverflow = useCallback(() => {
    if (isEmpty) {
      setHasOverflow(false);
      return;
    }
    const el = clampedRef.current;
    if (!el) return;
    setHasOverflow(el.scrollHeight > el.clientHeight);
  }, [isEmpty]);

  useEffect(() => {
    measureOverflow();
  }, [measureOverflow, value, maxLines]);

  // Inline style for wrapper: applies gridColumn span + the CSS var driving
  // `-webkit-line-clamp` in the Griffel class.
  const wrapperInlineStyle: React.CSSProperties = {
    gridColumn: `span ${span}`,
    // Cast to `any` — the CSS custom property is not part of the standard
    // CSSProperties typing but is honored by all major browsers + jsdom.
    ['--sprk-textarea-max-lines' as any]: String(maxLines),
  };

  return (
    <div
      className={mergeClasses(styles.wrapper, className)}
      style={wrapperInlineStyle}
      data-field-type="textarea"
      data-span={span}
    >
      <Label className={styles.label}>{label}</Label>
      {isEmpty ? (
        <Text className={styles.placeholder} aria-label={`${label}: no value`}>
          {EMPTY_PLACEHOLDER}
        </Text>
      ) : (
        <>
          <div
            ref={clampedRef}
            className={styles.clamped}
            data-testid="sprk-textarea-clamped"
            title={displayValue}
          >
            {displayValue}
          </div>
          {hasOverflow && (
            <Popover positioning="below-start" withArrow>
              <PopoverTrigger disableButtonEnhancement>
                <Link
                  as="button"
                  type="button"
                  appearance="default"
                  className={styles.showMoreLink}
                  data-testid="sprk-textarea-show-more"
                >
                  Show more
                </Link>
              </PopoverTrigger>
              <PopoverSurface
                className={styles.popoverSurface}
                data-testid="sprk-textarea-popover"
                aria-label={`${label} full text`}
              >
                <div className={styles.popoverLabel}>{label}</div>
                <div className={styles.popoverBody}>{displayValue}</div>
              </PopoverSurface>
            </Popover>
          )}
        </>
      )}
    </div>
  );
};

TextareaField.displayName = 'TextareaField';
