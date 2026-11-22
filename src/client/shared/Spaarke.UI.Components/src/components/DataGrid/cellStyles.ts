/**
 * Body-cell and header-cell styles of the `<DataGrid />` framework, exported so a grid that cannot
 * use `<DataGrid />` itself (SemanticSearch's `SearchResultsGrid`) renders its cells the same way
 * instead of keeping a hand-copied set that drifts (#2866). Spread into a module-scope `makeStyles`.
 */
import { tokens, type GriffelStyle } from '@fluentui/react-components';
import { dataGridTokens } from './tokens';

/** Body cell: Power Apps OOB body row height ≈ 38px, Segoe UI 14px, single line with ellipsis. */
export const dataGridCellStyle: GriffelStyle = {
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  minHeight: '38px',
  paddingTop: tokens.spacingVerticalXS,
  paddingBottom: tokens.spacingVerticalXS,
  paddingLeft: tokens.spacingHorizontalM,
  paddingRight: tokens.spacingHorizontalM,
  fontFamily: "'Segoe UI', 'Segoe UI Web', Arial, sans-serif",
  fontSize: tokens.fontSizeBase300,
  fontWeight: tokens.fontWeightRegular,
  color: tokens.colorNeutralForeground1,
};

/** Header cell: Power Apps OOB header row height ≈ 41px; colours from `dataGridTokens.header`. */
export const dataGridHeaderCellStyle: GriffelStyle = {
  minHeight: '41px',
  paddingTop: tokens.spacingVerticalS,
  paddingBottom: tokens.spacingVerticalS,
  paddingLeft: tokens.spacingHorizontalM,
  paddingRight: tokens.spacingHorizontalM,
  fontFamily: "'Segoe UI', 'Segoe UI Web', Arial, sans-serif",
  fontSize: tokens.fontSizeBase300,
  fontWeight: tokens.fontWeightSemibold,
  backgroundColor: dataGridTokens.header.background,
  color: dataGridTokens.header.foreground,
};
