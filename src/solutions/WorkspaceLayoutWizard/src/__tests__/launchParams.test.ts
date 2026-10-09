/**
 * launchParams.test.ts - the Workspace Layout Wizard's launch `data` contract (spaarke-ontology-platform-r1
 * task 113). `main.tsx` (code page) and the Console's in-app renderer parse the SAME string with the SAME
 * function; these cases are the strings WorkspaceGrid / ManageWorkspacesPane / the Personalize banner send.
 *
 * Classification (ADR-038 section 7): MAINTAIN - the launch-data contract between the launchers and the wizard.
 */
import { parseLayoutWizardData } from "../launchParams";

describe("parseLayoutWizardData", () => {
  it("create (WorkspaceGrid / banner)", () => {
    expect(parseLayoutWizardData("mode=create&bffBaseUrl=https%3A%2F%2Fbff.example")).toEqual({
      mode: "create",
      layoutId: null,
      layoutTemplateId: null,
      sectionsJson: null,
      sourceName: null,
      templateFilter: undefined,
      startAtStep: null,
    });
  });

  it("edit with the SpaarkeAi template subset (ManageWorkspacesPane)", () => {
    const parsed = parseLayoutWizardData(
      "mode=edit&bffBaseUrl=x&layoutId=l-1&templateFilter=" + encodeURIComponent("one-column,two-column"),
    );
    expect(parsed.mode).toBe("edit");
    expect(parsed.layoutId).toBe("l-1");
    expect(parsed.templateFilter).toEqual(["one-column", "two-column"]);
  });

  it("saveAs carries the source layout so the wizard can pre-populate", () => {
    const sections = JSON.stringify({ schemaVersion: 1, rows: [] });
    const parsed = parseLayoutWizardData(
      `mode=saveAs&layoutId=l-2&layoutTemplateId=three-column&sectionsJson=${encodeURIComponent(sections)}&name=${encodeURIComponent("Ops & Legal")}`,
    );
    expect(parsed).toMatchObject({
      mode: "saveAs",
      layoutId: "l-2",
      layoutTemplateId: "three-column",
      sectionsJson: sections,
      sourceName: "Ops & Legal",
    });
  });

  it("an unknown or missing mode means create; startAtStep passes through opaquely", () => {
    expect(parseLayoutWizardData("mode=bogus").mode).toBe("create");
    expect(parseLayoutWizardData("").mode).toBe("create");
    expect(parseLayoutWizardData("startAtStep=choose-layout").startAtStep).toBe("choose-layout");
  });
});
