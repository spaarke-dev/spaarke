using System;
using System.Collections.Generic;
using System.Web;

namespace Sprk.Bff.Api.Api.Agent;

/// <summary>
/// Generates deep-link URLs for handoff from M365 Copilot to
/// the SpaarkeAi three-pane Analysis surface and wizard Code Pages.
/// </summary>
public sealed class HandoffUrlBuilder
{
    private readonly string _dataverseBaseUrl;

    public HandoffUrlBuilder(string dataverseBaseUrl)
    {
        _dataverseBaseUrl = dataverseBaseUrl.TrimEnd('/');
    }

    /// <summary>
    /// Generates a deep-link to the SpaarkeAi three-pane Analysis surface (`sprk_spaarkeai`)
    /// for an existing analysis record, mirroring the `openSpaarkeAi` deep-link shape
    /// (<c>launch-resolver.ts</c>'s <c>SpaarkeAiLaunchParams</c> — ai-advanced-capabilities-
    /// analysis-hub-r1 task 052, spec §13.3 / FR-16). Opens the same surface
    /// <c>Xrm.Navigation.navigateTo({webresourceName:'sprk_spaarkeai', ...})</c> would produce
    /// client-side.
    /// </summary>
    /// <remarks>
    /// Retirement task 060 (spec §13.5 / FR-18) repoints this method from the now-retired
    /// legacy Analysis Workspace launcher web resource (would 404 once the legacy code page
    /// is deleted in task 063) to the go-forward SpaarkeAi surface. The method name and
    /// 3-argument signature are kept stable to avoid
    /// call-site churn across <c>PlaybookStatusEndpoints</c>, <c>PlaybookInvocationService</c>,
    /// and <c>AgentErrorHandler</c> — only the emitted URL shape changes.
    ///
    /// Deliberately NOT carried into the new shape: <paramref name="playbookId"/> is accepted
    /// for call-site source compatibility but is DROPPED from the emitted URL — it has no
    /// place in the <c>openSpaarkeAi</c> contract (<c>SpaarkeAiLaunchParams</c> has no
    /// playbookId field) and no consumer in <c>main.tsx</c>. When <paramref name="sourceFileId"/>
    /// is non-empty it is threaded as <c>entityLogicalName=sprk_document</c> +
    /// <c>entityId</c> so the opened session also carries document context — this mirrors the
    /// "Entity form command bar" launch context already supported by
    /// <c>SpaarkeAiLaunchParams</c>, so no new param is invented.
    /// </remarks>
    public string BuildAnalysisWorkspaceUrl(
        Guid analysisId,
        Guid sourceFileId,
        Guid? playbookId = null)
    {
        var parameters = new Dictionary<string, string>
        {
            ["analysisId"] = analysisId.ToString()
        };

        if (sourceFileId != Guid.Empty)
        {
            parameters["entityLogicalName"] = "sprk_document";
            parameters["entityId"] = sourceFileId.ToString();
        }

        return BuildWebResourceUrl("sprk_spaarkeai", parameters);
    }

    /// <summary>
    /// Generates a deep-link to the Document Upload Wizard with pre-filled matter context.
    /// </summary>
    public string BuildDocumentUploadWizardUrl(
        string parentEntityType,
        Guid parentEntityId,
        string parentEntityName,
        Guid? containerId = null)
    {
        var parameters = new Dictionary<string, string>
        {
            ["parentEntityType"] = parentEntityType,
            ["parentEntityId"] = parentEntityId.ToString(),
            ["parentEntityName"] = parentEntityName
        };

        if (containerId.HasValue)
            parameters["containerId"] = containerId.Value.ToString();

        return BuildWebResourceUrl("sprk_documentuploadwizard", parameters);
    }

    /// <summary>
    /// Refusal-affordance overload (spaarke-ai-architecture-redesign-r2 task 040, FR-A1-11,
    /// design D-F0(d)): composes the Document Upload wizard deep-link for the R5-E
    /// <c>sprk_document</c> hard-block refusal. Host-scopes to a known matter record when
    /// <paramref name="matterId"/> is supplied (assumes <c>sprk_matter</c> — the only host
    /// entity type carried by <c>ChatInvocationContext.MatterId</c>); degrades to a valid
    /// UNSCOPED standalone-mode link (still fully actionable — <c>DocumentUploadWizardDialog</c>
    /// treats a missing parent entity as standalone mode and its <c>AssociateToStep</c> resolves
    /// the target, or the user can Skip to an unassociated upload) when no host record is known.
    /// Never returns null/empty — the caller always has an actionable link to relay.
    /// </summary>
    public string BuildDocumentUploadWizardUrl(Guid? matterId)
    {
        if (matterId is not { } id || id == Guid.Empty)
        {
            return BuildWebResourceUrl("sprk_documentuploadwizard", new Dictionary<string, string>());
        }

        return BuildDocumentUploadWizardUrl(
            parentEntityType: "sprk_matter",
            parentEntityId: id,
            parentEntityName: string.Empty);
    }

    /// <summary>
    /// Generates a deep-link to the Summarize Files Wizard.
    /// </summary>
    public string BuildSummarizeFilesWizardUrl(Guid documentId, string bffBaseUrl)
    {
        var parameters = new Dictionary<string, string>
        {
            ["documentId"] = documentId.ToString(),
            ["bffBaseUrl"] = bffBaseUrl
        };

        return BuildWebResourceUrl("sprk_summarizefileswizard", parameters);
    }

    /// <summary>
    /// Generates a deep-link to the Playbook Library Code Page.
    /// </summary>
    public string BuildPlaybookLibraryUrl(Guid? documentId = null, Guid? matterId = null)
    {
        var parameters = new Dictionary<string, string>();

        if (documentId.HasValue)
            parameters["documentId"] = documentId.Value.ToString();
        if (matterId.HasValue)
            parameters["matterId"] = matterId.Value.ToString();

        return BuildWebResourceUrl("sprk_playbooklibrary", parameters);
    }

    /// <summary>
    /// Generates a deep-link to the Create Matter Wizard.
    /// </summary>
    public string BuildCreateMatterWizardUrl(string? matterName = null)
    {
        var parameters = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(matterName))
            parameters["matterName"] = matterName;

        return BuildWebResourceUrl("sprk_creatematterwizard", parameters);
    }

    /// <summary>
    /// Generates a deep-link to the Create Event Wizard with matter context.
    /// </summary>
    public string BuildCreateEventWizardUrl(Guid? matterId = null, string? matterName = null)
    {
        var parameters = new Dictionary<string, string>();

        if (matterId.HasValue)
            parameters["matterId"] = matterId.Value.ToString();
        if (!string.IsNullOrEmpty(matterName))
            parameters["matterName"] = matterName;

        return BuildWebResourceUrl("sprk_createeventwizard", parameters);
    }

    /// <summary>
    /// Builds a Dataverse web resource URL with encoded data parameters.
    /// Format: {dataverseBaseUrl}/main.aspx?pagetype=webresource&webresourceName={name}&data={encodedParams}
    /// </summary>
    private string BuildWebResourceUrl(string webResourceName, Dictionary<string, string> parameters)
    {
        var dataString = string.Join("&",
            parameters.Select(kvp => $"{HttpUtility.UrlEncode(kvp.Key)}={HttpUtility.UrlEncode(kvp.Value)}"));

        var encodedData = HttpUtility.UrlEncode(dataString);

        return $"{_dataverseBaseUrl}/main.aspx?pagetype=webresource&webresourceName={webResourceName}&data={encodedData}";
    }
}
