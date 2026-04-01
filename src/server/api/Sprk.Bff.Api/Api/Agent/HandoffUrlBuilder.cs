using System;
using System.Collections.Generic;
using System.Web;

namespace Sprk.Bff.Api.Api.Agent;

/// <summary>
/// Generates deep-link URLs for handoff from M365 Copilot to
/// Analysis Workspace and wizard Code Pages.
/// </summary>
public sealed class HandoffUrlBuilder
{
    private readonly string _dataverseBaseUrl;

    public HandoffUrlBuilder(string dataverseBaseUrl)
    {
        _dataverseBaseUrl = dataverseBaseUrl.TrimEnd('/');
    }

    /// <summary>
    /// Generates a deep-link to the Analysis Workspace with full context.
    /// Opens via Xrm.Navigation.navigateTo targeting the AnalysisWorkspaceLauncher web resource.
    /// </summary>
    public string BuildAnalysisWorkspaceUrl(
        Guid analysisId,
        Guid sourceFileId,
        Guid? playbookId = null)
    {
        var parameters = new Dictionary<string, string>
        {
            ["analysisId"] = analysisId.ToString(),
            ["sourceFileId"] = sourceFileId.ToString()
        };

        if (playbookId.HasValue)
            parameters["playbookId"] = playbookId.Value.ToString();

        return BuildWebResourceUrl("sprk_AnalysisWorkspaceLauncher", parameters);
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
