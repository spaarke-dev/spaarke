/**
 * paraIdExtension.ts — R3 FR-09/FR-10 (spaarkeai-compose-r3 task 011).
 *
 * The `w14:paraId` identity extension for the Compose editor, factored into its
 * own module (like the additive `marks/*` schema pieces) so it is a PURE, headless
 * schema unit — importable by ComposeEditor for mounting AND by the FR-09/FR-10
 * unit tests WITHOUT dragging in the React component's `@spaarke/auth` /
 * ComposeAiToolbar graph (which resolves only from a SharedLibs dist build).
 *
 * `@tiptap/extension-unique-id` is MIT — TipTap open-sourced it from Pro in June
 * 2025. NFR-03 / owner rule (2026-07-14): the minting extension MUST resolve to
 * `@tiptap/extension-unique-id`, NEVER `@tiptap-pro/*`. No TipTap product feature,
 * no AGPL.
 */
import UniqueID from '@tiptap/extension-unique-id';

/**
 * ProseMirror node types that carry a `w14:paraId`. The server pre-parser
 * (task 010) walks `body.Descendants<Paragraph>()`, which counts EVERY OOXML
 * `<w:p>` — and a heading is a `<w:p>` with a heading `pStyle`. mammoth maps
 * those headings to ProseMirror `heading` nodes (not `paragraph`), so to keep the
 * client node sequence aligned with the server's paragraph index (FR-09 doc-order
 * carry), both `paragraph` AND `heading` must bear paraIds. `stampParaIds` (in
 * docxBridge) walks this SAME set — keep the two lists in sync.
 */
export const PARAID_NODE_TYPES = ['paragraph', 'heading'] as const;

/**
 * Generate an OOXML-valid `w14:paraId` (`ST_LongHexNumber`): an 8-hex string in
 * the range `0 < x < 0x80000000`. Used as `@tiptap/extension-unique-id`'s
 * `generateID` (below) so a paragraph SPLIT re-mints a directly-valid `w14:paraId`
 * (FR-10), and matches the server `ParaIdPreParser` mint range/format (task 010,
 * `RandomNumberGenerator.GetInt32(1, int.MaxValue)` → `X8`) so client- and
 * server-minted ids are indistinguishable to the save-side splice.
 *
 * Uses the Web Crypto CSPRNG (`crypto.getRandomValues`) — present in browsers and
 * in the jsdom/Node test env. The value is masked to 31 bits (`& 0x7fffffff`, i.e.
 * `< 0x80000000`) and 0 is excluded (bumped to 1), then upper-hex padded to 8 chars.
 *
 * Lives here (with the extension that consumes it) — NOT in docxBridge — so a test
 * that mocks docxBridge cannot accidentally strip the extension's `generateID`.
 */
export function generateOoxmlParaId(): string {
  const buf = new Uint32Array(1);
  // globalThis.crypto: browsers + Node >=19 / jsdom. No Math.random fallback —
  // paraIds are identity keys; a weak PRNG would risk collisions.
  globalThis.crypto.getRandomValues(buf);
  let v = buf[0] & 0x7fffffff; // clamp to ST_LongHexNumber upper bound (< 0x80000000)
  if (v === 0) v = 1; // exclude 0 (ST_LongHexNumber is strictly > 0)
  return v.toString(16).toUpperCase().padStart(8, '0');
}

/**
 * R3 paraId identity extension — ADDITIVE to the LOCKED Spike #1 extension list
 * (registered as its own array, the locked list is never mutated). Configured for
 * the `paragraph` type with `attributeName: 'paraId'` and an OOXML-shaped
 * `generateID` ({@link generateOoxmlParaId}). Its built-in split dedup is the FR-10
 * minting mechanism: on a paragraph split it keeps the id on one half and re-mints
 * an OOXML-valid id on the other — NO custom plugin.
 *
 * `.extend` overrides ONLY `addGlobalAttributes` to keep the id OFF the rendered
 * DOM (FR-09: a hidden node attribute, `renderHTML: () => ({})`). Everything else —
 * the `onCreate`/`appendTransaction` minting + split dedup — is inherited unchanged.
 * Load-time ids are stamped explicitly from the server map AFTER `setContent`
 * (see `stampParaIds` in docxBridge); the extension's own minting only supplies ids
 * the server did not (new/seed paragraphs) and re-mints on split.
 */
export const COMPOSE_R3_PARAID = [
  UniqueID.extend({
    addGlobalAttributes() {
      return [
        {
          types: this.options.types,
          attributes: {
            [this.options.attributeName]: {
              default: null,
              // Parse a data-* id if one is ever present in source HTML (round-trip
              // safety), but NEVER emit it — the paraId is a hidden node attribute
              // only (FR-09: no data-*/visible output on the rendered DOM).
              parseHTML: (element: HTMLElement) => element.getAttribute(`data-${this.options.attributeName}`),
              renderHTML: () => ({}),
            },
          },
        },
      ];
    },
  }).configure({
    types: [...PARAID_NODE_TYPES],
    attributeName: 'paraId',
    generateID: generateOoxmlParaId,
  }),
];
