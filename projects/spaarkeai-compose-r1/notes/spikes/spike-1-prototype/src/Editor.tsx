// THROWAWAY prototype - Spike #1 (spaarkeai-compose-r1)
// Validates TipTap OOB + DOCX bridge wiring. NOT promoted to src/.
//
// Wiring contract documented here serves as the reference for R1 task 045
// (ComposeEditor.tsx in src/solutions/SpaarkeAi/src/components/compose/).

import React, { useCallback, useState } from "react";
import { useEditor, EditorContent } from "@tiptap/react";
import StarterKit from "@tiptap/starter-kit";
import Table from "@tiptap/extension-table";
import TableRow from "@tiptap/extension-table-row";
import TableCell from "@tiptap/extension-table-cell";
import TableHeader from "@tiptap/extension-table-header";
import Link from "@tiptap/extension-link";
import Image from "@tiptap/extension-image";
import Underline from "@tiptap/extension-underline";
import TaskList from "@tiptap/extension-task-list";
import TaskItem from "@tiptap/extension-task-item";
import CharacterCount from "@tiptap/extension-character-count";
import TextAlign from "@tiptap/extension-text-align";

// Bridge libraries: import path — IMPORT via mammoth (DOCX → HTML); EXPORT via docx (build new DOCX from TipTap JSON).
// See spike-1-tiptap-docx-roundtrip.md "Section 4: Bridge library choice".
import mammoth from "mammoth";

const STARTER_KIT_CONFIG = {
  // StarterKit bundles: Document, Paragraph, Text, Bold, Italic, Strike, Code,
  // CodeBlock, Heading, BulletList, OrderedList, ListItem, Blockquote,
  // HardBreak, HorizontalRule, History, Dropcursor, Gapcursor.
  heading: { levels: [1, 2, 3, 4, 5, 6] as const },
};

const ALL_OOB_EXTENSIONS = [
  StarterKit.configure(STARTER_KIT_CONFIG),
  Underline,
  Link.configure({ openOnClick: false, autolink: true }),
  Image.configure({ inline: false, allowBase64: true }),
  Table.configure({ resizable: true }),
  TableRow,
  TableHeader,
  TableCell,
  TaskList,
  TaskItem.configure({ nested: true }),
  CharacterCount,
  TextAlign.configure({ types: ["heading", "paragraph"] }),
];

/**
 * SpikeEditor — minimal TipTap editor with OOB-only extensions.
 *
 * Wiring intent (R1 target — ComposeEditor.tsx):
 *  - Receives DOCX bytes via SPE drive-item id (BFF endpoint POST /api/compose/load-document)
 *  - Converts DOCX → HTML via mammoth, sets as initial content
 *  - On Save: serialize TipTap JSON → build docx via `docx` library → POST /api/compose/save-document
 *  - Selection events feed JPS scope inputs (compose-selection)
 *
 * No custom extensions. No tracked-changes integration. No comments-as-w:comment.
 */
export const SpikeEditor: React.FC<{ initialDocxBytes?: ArrayBuffer }> = ({
  initialDocxBytes,
}) => {
  const [conversionReport, setConversionReport] = useState<string>("");

  const editor = useEditor({
    extensions: ALL_OOB_EXTENSIONS,
    content: "<p>Empty — load a DOCX fixture to begin.</p>",
  });

  const handleLoadDocx = useCallback(
    async (file: File) => {
      if (!editor) return;
      const arrayBuffer = await file.arrayBuffer();
      // mammoth: DOCX → HTML. We log messages (the bridge's "diff report")
      // to capture what was dropped/degraded.
      const result = await mammoth.convertToHtml({ arrayBuffer });
      editor.commands.setContent(result.value);
      setConversionReport(
        result.messages
          .map((m) => `[${m.type}] ${m.message}`)
          .join("\n") || "no warnings"
      );
    },
    [editor]
  );

  // Export path is implemented in src/exportDocx.ts (separate file for clarity).

  if (!editor) return <div>Loading editor...</div>;

  return (
    <div style={{ padding: "1rem", fontFamily: "Segoe UI, sans-serif" }}>
      <h1>Spike #1 — TipTap OOB Editor</h1>
      <input
        type="file"
        accept=".docx"
        onChange={(e) => {
          const f = e.target.files?.[0];
          if (f) handleLoadDocx(f);
        }}
      />
      <pre style={{ background: "#f5f5f5", padding: "0.5rem", fontSize: "0.85rem" }}>
        Mammoth conversion report:{"\n"}{conversionReport}
      </pre>
      <EditorContent
        editor={editor}
        style={{
          border: "1px solid #ccc",
          minHeight: "500px",
          padding: "1rem",
        }}
      />
      <div>
        Characters: {editor.storage.characterCount.characters()} | Words:{" "}
        {editor.storage.characterCount.words()}
      </div>
    </div>
  );
};
