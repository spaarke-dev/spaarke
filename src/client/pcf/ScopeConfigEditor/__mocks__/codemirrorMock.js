/**
 * Mock for CodeMirror modules in Jest tests.
 *
 * CodeMirror uses DOM APIs not available in jsdom,
 * so we stub the modules for unit testing.
 */

// Mock EditorState
const EditorState = {
    create: jest.fn().mockReturnValue({
        doc: { toString: () => "" },
    }),
};

// Mock EditorView
const mockEditorView = jest.fn().mockImplementation(({ state, parent, dispatch }) => ({
    state,
    dispatch: dispatch || jest.fn(),
    destroy: jest.fn(),
    dom: parent,
}));
// Mock static properties/factories
mockEditorView.updateListener = { of: jest.fn().mockReturnValue({}) };
mockEditorView.editable = { of: jest.fn().mockReturnValue({}) };
const EditorView = mockEditorView;

// Mock json language support
const json = jest.fn().mockReturnValue({});

// Mock @codemirror/commands
const defaultKeymap = [];
const historyKeymap = [];
const history = jest.fn().mockReturnValue({});

// Mock @codemirror/language
const syntaxHighlighting = jest.fn().mockReturnValue({});
const defaultHighlightStyle = {};
const bracketMatching = jest.fn().mockReturnValue({});

// Mock @codemirror/view items
const lineNumbers = jest.fn().mockReturnValue({});
const highlightActiveLine = jest.fn().mockReturnValue({});
const highlightActiveLineGutter = jest.fn().mockReturnValue({});
const keymap = { of: jest.fn().mockReturnValue({}) };
const drawSelection = jest.fn().mockReturnValue({});

// Mock StateField / StateEffect
const StateField = {
    define: jest.fn().mockReturnValue({}),
};
const StateEffect = {
    define: jest.fn().mockReturnValue({ of: jest.fn() }),
};

module.exports = {
    EditorState,
    EditorView,
    json,
    defaultKeymap,
    historyKeymap,
    history,
    syntaxHighlighting,
    defaultHighlightStyle,
    bracketMatching,
    lineNumbers,
    highlightActiveLine,
    highlightActiveLineGutter,
    keymap,
    drawSelection,
    StateField,
    StateEffect,
};
