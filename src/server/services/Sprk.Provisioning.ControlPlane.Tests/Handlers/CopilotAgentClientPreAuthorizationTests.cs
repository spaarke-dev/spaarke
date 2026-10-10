// -----------------------------------------------------------------------------
// CopilotAgentClientPreAuthorizationTests.cs
//
// T257 — the shared "Spaarke Copilot Agent" client (a secret-free public client; design note
// projects/customer-provisioning-orchestration-r1/notes/t257-copilot-agent-design.md §3) must be pre-authorized on every
// customer BFF app registration for user_impersonation, so no user sees a consent prompt. No new mechanism: the client
// joins H3's existing PreAuthorizedClientAppIds list (T240a). This file pins the three links of that chain:
//   1. Bicep: platform-controlplane carries a copilotAgentClientAppId parameter (empty by default — the app does not
//      exist until the operator creates it), threads it to the Worker module, and the Worker module appends it, when
//      set, to the list it emits as EntraAppRegOptions__PreAuthorizedClientAppIds__N.
//   2. Configuration: the emitted indexed settings bind to EntraAppRegOptions.PreAuthorizedClientAppIds.
//   3. H3: PlanClientAccess pre-authorizes the add-in AND the Copilot client on the user_impersonation scope.
// The compiled platform-controlplane.json is what Deploy-ControlPlane.ps1 deploys, so the Bicep checks read it.
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph.Models;
using Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class CopilotAgentClientPreAuthorizationTests
{
    private const string AddInClientId = "1958aec2-0218-495e-8e3c-37133e9b8357";
    private const string CopilotClientId = "7c0ff1ee-0000-4000-8000-000000000257";
    private const string BffAppId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string Origin = "https://spaarke-acme.crm.dynamics.com";
    private static readonly Guid ScopeId = Guid.Parse("99999999-8888-7777-6666-555555555555");

    [Fact]
    public void Bicep_RootTemplate_HasAnEmptyByDefault_CopilotClientParameter_ThreadedToTheWorkerModule()
    {
        using var template = LoadCompiledTemplate();
        var root = template.RootElement;

        var param = root.GetProperty("parameters").GetProperty("copilotAgentClientAppId");
        param.GetProperty("type").GetString().Should().Be("string");
        param.GetProperty("defaultValue").GetString().Should().BeEmpty(
            "the client app does not exist until the operator creates it — a placeholder would make H3 reject every run");

        var workerParams = WorkerModule(root).GetProperty("properties").GetProperty("parameters");
        workerParams.GetProperty("copilotAgentClientAppId").GetProperty("value").GetString()
            .Should().Be("[parameters('copilotAgentClientAppId')]");
    }

    [Fact]
    public void Bicep_WorkerModule_AppendsTheCopilotClient_OnlyWhenSet_AndEmitsTheCombinedList()
    {
        using var template = LoadCompiledTemplate();
        var inner = WorkerModule(template.RootElement).GetProperty("properties").GetProperty("template");

        inner.GetProperty("parameters").GetProperty("copilotAgentClientAppId").GetProperty("defaultValue").GetString()
            .Should().BeEmpty();

        var variables = inner.GetProperty("variables");
        variables.GetProperty("effectivePreAuthorizedClientAppIds").GetString().Should().Be(
            "[concat(parameters('preAuthorizedClientAppIds'), if(empty(parameters('copilotAgentClientAppId')), createArray(), createArray(parameters('copilotAgentClientAppId'))))]");

        var settings = variables.GetProperty("entraAppRegSettings").GetString();
        settings.Should().Contain("EntraAppRegOptions__PreAuthorizedClientAppIds__{0}")
            .And.Contain("length(variables('effectivePreAuthorizedClientAppIds'))")
            .And.Contain("variables('effectivePreAuthorizedClientAppIds')[lambdaVariables('i')]")
            .And.NotContain("length(parameters('preAuthorizedClientAppIds'))",
                "the emitted list must be the combined one, or the Copilot client never reaches H3");
    }

    [Fact]
    public void Bicep_SourceAndDevParameters_DeclareTheParameter()
    {
        var root = RepoRoot();
        File.ReadAllText(Path.Combine(root, "infrastructure", "bicep", "platform-controlplane.bicep"))
            .Should().Contain("param copilotAgentClientAppId string = ''")
            .And.Contain("copilotAgentClientAppId: copilotAgentClientAppId");
        File.ReadAllText(Path.Combine(root, "infrastructure", "bicep", "modules", "controlplane-worker-app-service.bicep"))
            .Should().Contain("param copilotAgentClientAppId string = ''")
            .And.Contain("var effectivePreAuthorizedClientAppIds = concat(preAuthorizedClientAppIds, empty(copilotAgentClientAppId) ? [] : [copilotAgentClientAppId])");
        File.ReadAllText(Path.Combine(root, "infrastructure", "bicep", "parameters", "platform-controlplane-dev.bicepparam"))
            .Should().MatchRegex(@"param copilotAgentClientAppId = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}'",
                "dev states the client explicitly: 'Spaarke Copilot Agent - Dev' was created 2026-10-09 (T257 live step 1)");
    }

    [Fact]
    public void TheEmittedIndexedSettings_BindToPreAuthorizedClientAppIds_InOrder()
    {
        // The exact App Service setting names the Worker module emits (the binder's list syntax, ':' form).
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EntraAppRegOptions:PreAuthorizedClientAppIds:0"] = AddInClientId,
                ["EntraAppRegOptions:PreAuthorizedClientAppIds:1"] = CopilotClientId,
            })
            .Build();

        var options = new EntraAppRegOptions();
        config.GetSection(nameof(EntraAppRegOptions)).Bind(options);

        options.PreAuthorizedClientAppIds.Should().Equal(AddInClientId, CopilotClientId);
    }

    [Fact]
    public void H3_PreAuthorizesTheAddIn_AndTheCopilotClient_OnUserImpersonation()
    {
        var plan = GraphAppRegistrationProvisioner.PlanClientAccess(App(preAuthorized: [Pre(AddInClientId)]), [Origin],
            [AddInClientId, CopilotClientId]);

        plan.Error.Should().BeNull();
        plan.Patch!.Api!.PreAuthorizedApplications.Should().HaveCount(2);
        plan.Patch.Api.PreAuthorizedApplications!.Select(p => p.AppId).Should().BeEquivalentTo([AddInClientId, CopilotClientId]);
        plan.Patch.Api.PreAuthorizedApplications!.Should().OnlyContain(p =>
            p.DelegatedPermissionIds!.SequenceEqual(new[] { ScopeId.ToString("D") }),
            "the Copilot agent requests api://{bffAppId}/user_impersonation — the only scope a customer BFF app exposes");
    }

    [Fact]
    public void H3_AnAppAlreadyCarryingTheCopilotClient_PlansNoApiChange()
    {
        var plan = GraphAppRegistrationProvisioner.PlanClientAccess(
            App(spa: [Origin], preAuthorized: [Pre(AddInClientId), Pre(CopilotClientId)]), [Origin], [AddInClientId, CopilotClientId]);

        plan.Should().Be(new GraphAppRegistrationProvisioner.ClientAccessPlan(null, null));
    }

    private static Application App(
        IEnumerable<string>? spa = null,
        IEnumerable<PreAuthorizedApplication>? preAuthorized = null) => new()
    {
        AppId = BffAppId,
        Spa = new SpaApplication { RedirectUris = (spa ?? []).ToList() },
        Api = new ApiApplication
        {
            Oauth2PermissionScopes = [new PermissionScope { Id = ScopeId, Value = "user_impersonation", IsEnabled = true, Type = "User" }],
            PreAuthorizedApplications = (preAuthorized ?? []).ToList(),
        },
    };

    private static PreAuthorizedApplication Pre(string appId) => new()
    {
        AppId = appId,
        DelegatedPermissionIds = [ScopeId.ToString("D")],
    };

    private static JsonDocument LoadCompiledTemplate()
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "infrastructure", "bicep", "platform-controlplane.json")));

    private static JsonElement WorkerModule(JsonElement root)
    {
        // The Worker module is the nested deployment whose inner template declares preAuthorizedClientAppIds.
        foreach (var resource in Resources(root))
        {
            if (resource.TryGetProperty("properties", out var props)
                && props.TryGetProperty("template", out var inner)
                && inner.TryGetProperty("parameters", out var innerParams)
                && innerParams.TryGetProperty("preAuthorizedClientAppIds", out _))
            {
                return resource;
            }
        }

        throw new InvalidOperationException("platform-controlplane.json has no module declaring preAuthorizedClientAppIds.");
    }

    private static IEnumerable<JsonElement> Resources(JsonElement root)
    {
        var resources = root.GetProperty("resources");
        return resources.ValueKind == JsonValueKind.Array
            ? resources.EnumerateArray()
            : resources.EnumerateObject().Select(p => p.Value);
    }

    private static string RepoRoot()
    {
        // A worktree's .git is a FILE; a regular checkout's is a directory (parity with ArmTemplateInspectorTests).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var gitMarker = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitMarker) || File.Exists(gitMarker)) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException($"Could not locate the repo root walking up from '{AppContext.BaseDirectory}'.");
    }
}
