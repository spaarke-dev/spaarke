/**
 * gridStyles — shared cell styling for every Fluent v9 DataGrid in the SPE Admin App.
 *
 * Added 2026-08-26 (UAT round 7: "the grid columns overlap; they should not overlap; and some
 * space between columns").
 *
 * ## Why the columns overlapped
 *
 * Two independent causes, both easy to miss:
 *
 * 1. **`<Text truncate>` does not stop text wrapping.** In Fluent v9, `truncate` only applies
 *    `overflow: hidden` + `text-overflow: ellipsis`. Suppressing the line break needs
 *    `wrap={false}` as well. Without it a long unbroken value — a container ID, a GUID, a filename
 *    with hyphens — wraps onto three lines and grows the row instead of ellipsising. Every
 *    truncating cell in this app was missing `wrap={false}`.
 *
 * 2. **A flex child does not shrink below its content by default.** `DataGridCell` is a flex
 *    container and its content box has the CSS default `min-width: auto`, so a wide child pushes
 *    past the column boundary that `columnSizingOptions` set and paints over the next column's
 *    content. `min-width: 0` on the cell and its child is what actually confines it.
 *
 * Fixing only one of the two leaves the bug: (1) alone still overflows horizontally, (2) alone
 * still wraps to three lines.
 *
 * ## Usage
 *
 *   const grid = useGridStyles();
 *   <DataGridHeaderCell className={grid.headerCell}>…</DataGridHeaderCell>
 *   <DataGridCell className={grid.cell}>…</DataGridCell>
 *
 * and give any truncating `<Text>` BOTH props: `<Text truncate wrap={false}>`.
 *
 * ADR-021: Fluent design tokens only.
 */

import { makeStyles, tokens } from "@fluentui/react-components";

export const useGridStyles = makeStyles({
  /**
   * Clips cell content to its column and adds the gutter the operator asked for.
   *
   * The `& > *` rule reaches the single element `renderCell` returns — usually a `Text`, `Badge`
   * or `Button`. It is what stops that element from establishing a wider content box than the
   * cell it lives in.
   */
  cell: {
    overflow: "hidden",
    minWidth: 0,
    paddingRight: tokens.spacingHorizontalL,
    "& > *": {
      minWidth: 0,
      maxWidth: "100%",
    },
  },

  /** Matching gutter on the header so labels stay aligned with the values beneath them. */
  headerCell: {
    paddingRight: tokens.spacingHorizontalL,
  },
});
