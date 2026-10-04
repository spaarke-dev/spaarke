/**
 * The one Word file-name rule (task 089) — extracted unchanged from SaveFlow (default Document Name) and useSaveFlow
 * (upload file name), and now also used by the ribbon's Quick Save. These pin the rule itself; the pane's own
 * suites (SaveFlow.documentName, useSaveFlow.*) still pin its use there.
 */
import { stripDocumentExtension, toDocxFileName } from '../documentFileName';

describe('stripDocumentExtension — the Document Name default', () => {
  it.each([
    ['Brief.docx', 'Brief'],
    ['Brief.DOCX', 'Brief'],
    ['Memo.doc', 'Memo'],
    ['Untitled Document', 'Untitled Document'],
    ['report.docx.docx', 'report.docx'],
  ])('%s → %s', (input, expected) => {
    expect(stripDocumentExtension(input)).toBe(expected);
  });
});

describe('toDocxFileName — the uploaded file name', () => {
  it.each([
    ['Brief', 'Brief.docx'],
    ['Brief.docx', 'Brief.docx'],
    ['Brief.DOCX', 'Brief.DOCX'],
    ['  Brief  ', 'Brief.docx'],
    ['', 'document.docx'],
    ['   ', 'document.docx'],
    [undefined, 'document.docx'],
  ])('%p → %s', (input, expected) => {
    expect(toDocxFileName(input)).toBe(expected);
  });
});
