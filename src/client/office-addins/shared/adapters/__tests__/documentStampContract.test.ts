/**
 * The client's identity-stamp constants equal the server's (spaarkeai-word-add-in-r1 task 089).
 *
 * The server writes the stamp (`OfficeDocumentStamp`, task 014), the client reads it (task 051) and now also writes
 * it into the open document (task 089). TypeScript cannot import a C# constant, so this test READS the C# source
 * and compares the three wire values. If either side changes alone, `getByNamespaceAsync` silently finds nothing
 * and the server stops recognising the client's mark — with no compiler or runtime signal. This is that signal.
 */
import * as fs from 'fs';
import * as path from 'path';
import { STAMP_NAMESPACE, STAMP_ROOT_ELEMENT, STAMP_ID_ELEMENT, buildStampXml } from '../documentStampContract';

const SERVER_STAMP_SOURCE = path.resolve(
  __dirname,
  '../../../../../server/api/Sprk.Bff.Api/Services/Office/OfficeDocumentStamp.cs'
);

function serverConstant(source: string, name: string): string {
  const match = new RegExp(`public\\s+const\\s+string\\s+${name}\\s*=\\s*"([^"]*)"\\s*;`).exec(source);
  if (!match) {
    throw new Error(`OfficeDocumentStamp.${name} not found in ${SERVER_STAMP_SOURCE}`);
  }
  return match[1]!;
}

describe('documentStampContract — client and server agree on the stamp (task 089)', () => {
  const source = fs.readFileSync(SERVER_STAMP_SOURCE, 'utf8');

  it.each([
    ['StampNamespace', STAMP_NAMESPACE],
    ['StampRootElement', STAMP_ROOT_ELEMENT],
    ['StampIdElement', STAMP_ID_ELEMENT],
  ])('OfficeDocumentStamp.%s equals the client value', (name, clientValue) => {
    expect(serverConstant(source, name)).toBe(clientValue);
  });

  it('the XML the client writes has the server-readable shape: the namespace as the ROOT default xmlns, one id child', () => {
    const id = '3fa85f64-5717-4562-b3fc-2c963f66afa6';
    const doc = new DOMParser().parseFromString(buildStampXml(id), 'application/xml');

    expect(doc.getElementsByTagName('parsererror')).toHaveLength(0);
    expect(doc.documentElement.namespaceURI).toBe(STAMP_NAMESPACE);
    expect(doc.documentElement.localName).toBe(STAMP_ROOT_ELEMENT);
    expect(doc.documentElement.getAttribute('xmlns')).toBe(STAMP_NAMESPACE);
    const ids = doc.documentElement.getElementsByTagNameNS(STAMP_NAMESPACE, STAMP_ID_ELEMENT);
    expect(ids).toHaveLength(1);
    expect(ids[0]!.textContent).toBe(id);
  });
});
