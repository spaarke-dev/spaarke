using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Identity;
using FluentAssertions;
using Json.Schema;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Classification;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Seam.Finance;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Seam.Intake;

/// <summary>
/// Live evaluation of app-only agreement AI (spaarke-intake-pipeline-r1 task 004, FR-02): the production <see cref="AgreementAi"/>
/// facade, <see cref="ActionRunner"/> and <see cref="PromptSchemaRenderer"/> with the REAL authored <c>agreement-classify</c> and
/// <c>agreement-edit-plan</c> Actions (loaded from <c>infra/dataverse/actions/*.action.json</c>, tier and temperature as authored) and a
/// REAL Azure OpenAI client, over the accelerator's <c>segment_clauses.py</c> segmentation of the three synthetic NDAs and the
/// synthetic MSA (<c>tests/fixtures/intake/agreement-edit-plan/*.segmentation.json</c>). Only the Binding table, the Action rows'
/// storage and the registry read are doubled (the registry is the repo's mirror of the live <c>sprk_agreementtype</c> rows).
/// </summary>
/// <remarks>
/// <para>
/// Runs only when <c>AGREEMENT_EDIT_PLAN_LIVE_OPENAI_ENDPOINT</c> is set; otherwise every test reports Skipped (CI does not set it).
/// Authenticates with <see cref="DefaultAzureCredential"/>: run locally with <c>AZURE_TOKEN_CREDENTIALS=AzureCliCredential</c> after
/// <c>az login</c> (ADR-028 E-2). Optional: <c>AGREEMENT_EDIT_PLAN_LIVE_STANDARD_MODEL</c> and <c>AGREEMENT_EDIT_PLAN_LIVE_REASONING_MODEL</c>
/// override the deployment names the Actions' tier maps to (default: the platform defaults, as <see cref="ModelTierDeploymentResolver"/>
/// resolves them); <c>AGREEMENT_EDIT_PLAN_LIVE_REPORT</c> is a file the counts, codes, ids and latency are appended to;
/// <c>AGREEMENT_EDIT_PLAN_LIVE_PLAN_DIR</c> is a folder (outside the repo) that each NDA's plan JSON is written to, for the engine dry-run.
/// </para>
/// <para>
/// <b>What is asserted and what is only recorded.</b> Asserted: the classifier's verdict per document, the plan validating against the
/// accelerator's <c>edit-plan.schema.json</c>, every topic in the pinned standard, and <c>review_basis</c> = "Spaarke NDA – Mutual" v1.2.
/// Recorded, not asserted: the refused-edit count and codes, the model-call latency and the prompt and output sizes (the raw completion
/// API reports no token usage). Neither the test output nor the report holds document or clause text, only counts, codes and ids (ADR-015).
/// Read-only: nothing is written to Dataverse or Azure; the corpus read is the synthetic one only (NFR-10).
/// </para>
/// </remarks>
[Trait("Category", "LiveIntegration")]
public sealed class AgreementAiEditPlanLiveTests
{
    private const string NdaStandardName = "Spaarke NDA – Mutual";
    private const string NdaStandardVersion = "1.2";
    private const double NdaGate = 0.85;

    private static readonly string? EndpointValue = Environment.GetEnvironmentVariable("AGREEMENT_EDIT_PLAN_LIVE_OPENAI_ENDPOINT");
    private static readonly object ReportLock = new();
    private static readonly object SchemaLock = new();
    private static readonly Guid ClassifyActionId = Guid.Parse("a0000000-0000-4000-8000-000000000011");
    private static readonly Guid EditPlanActionId = Guid.Parse("a0000000-0000-4000-8000-000000000012");

    private readonly ITestOutputHelper _output;

    public AgreementAiEditPlanLiveTests(ITestOutputHelper output) => _output = output;

    [SkippableTheory]
    [InlineData("nda-01-bluefin-harbor-mutual")]
    [InlineData("nda-02-bluefin-harbor-counterparty-markup")]
    [InlineData("nda-03-bluefin-harbor-injection")]
    public async Task BuildEditPlan_ForASyntheticNdaOnTheLiveModel_ReturnsASchemaValidPlanOnThePinnedStandard(string corpusName)
    {
        Skip.If(string.IsNullOrWhiteSpace(EndpointValue), "AGREEMENT_EDIT_PLAN_LIVE_OPENAI_ENDPOINT is not set.");
        var rig = LiveRig.Create(EndpointValue!);
        var segmentation = Segmentation(corpusName);
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"## {corpusName}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Deployments: reasoning tier -> '{rig.ReasoningDeployment}', standard -> '{rig.StandardDeployment}'; sections {SectionCount(segmentation)}");

        try
        {
            var verdict = await rig.Ai.ClassifyAsync(new AgreementClassifyRequest { DocumentText = DocumentText(segmentation), TenantId = "live-test" });
            report.AppendLine(CultureInfo.InvariantCulture,
                $"- classify: isAgreement={verdict.IsAgreement} composite={verdict.Composite} candidates=[{string.Join(", ", verdict.Candidates.Select(c => FormattableString.Invariant($"{c.SubDomainKey}:{c.Confidence:0.00}")))}] latency_ms={rig.LastLatencyMs}");
            verdict.IsSubDomain(AgreementSubDomains.Nda, NdaGate).Should().BeTrue("a synthetic NDA classifies as an NDA at or above the gate");

            var plan = await rig.Ai.BuildEditPlanAsync(new AgreementEditPlanRequest { SegmentationJson = segmentation, TenantId = "live-test" });
            report.AppendLine(CultureInfo.InvariantCulture, $"- edit plan: latency_ms={rig.LastLatencyMs} prompt_chars={rig.LastPromptChars} output_chars={rig.LastOutputChars} (token usage is not exposed by the raw completion API)");
            report.AppendLine(CultureInfo.InvariantCulture, $"- edits={plan.EditCount} topics=[{string.Join(", ", plan.TopicIds)}]");
            report.AppendLine(CultureInfo.InvariantCulture, $"- refusedEdits={plan.RefusedEdits.Count} [{string.Join("; ", plan.RefusedEdits.Select(r => $"#{r.Index} {r.Code} {r.TopicId}"))}]");
            report.AppendLine(CultureInfo.InvariantCulture, $"- unplaceable={plan.UnplaceableFindings.Count} [{string.Join("; ", plan.UnplaceableFindings.Select(u => $"{u.TopicId}@section{u.SectionIndex}"))}]");

            using var json = JsonDocument.Parse(plan.PlanJson);
            var schemaErrors = ValidateAgainstAcceleratorSchema(json.RootElement);
            report.AppendLine(CultureInfo.InvariantCulture, $"- schema valid: {schemaErrors.Count == 0}{(schemaErrors.Count == 0 ? string.Empty : " at " + string.Join(", ", schemaErrors))}");
            var basis = json.RootElement.GetProperty("review_basis");
            report.AppendLine(CultureInfo.InvariantCulture, $"- review_basis: '{basis.GetProperty("playbook").GetString()}' v{basis.GetProperty("version").GetString()}");

            if (Environment.GetEnvironmentVariable("AGREEMENT_EDIT_PLAN_LIVE_PLAN_DIR") is { Length: > 0 } planDir)
            {
                Directory.CreateDirectory(planDir);
                await File.WriteAllTextAsync(Path.Combine(planDir, $"{corpusName}.plan.json"), plan.PlanJson);
            }

            schemaErrors.Should().BeEmpty("the plan must satisfy src/accelerators/core/schemas/edit-plan.schema.json");
            var topicIds = AcceleratorTopicIds();
            topicIds.Should().HaveCount(24, "the pinned standard has 24 topics");
            // An empty plan is schema-valid (OnlyContain would fail on an empty collection), so the quantifier is All.
            json.RootElement.GetProperty("edits").EnumerateArray().Select(e => e.GetProperty("topic").GetString())
                .All(t => topicIds.Contains(t!)).Should().BeTrue("every topic is in the pinned topics.yaml");
            plan.TopicIds.All(topicIds.Contains).Should().BeTrue();
            plan.EditCount.Should().Be(json.RootElement.GetProperty("edits").GetArrayLength());
            plan.StandardName.Should().Be(NdaStandardName);
            plan.StandardVersion.Should().Be(NdaStandardVersion);
            basis.GetProperty("playbook").GetString().Should().Be(NdaStandardName);
            basis.GetProperty("version").GetString().Should().Be(NdaStandardVersion);
        }
        catch (AgreementAiException ex)
        {
            // Reason code, consumer type and a JSON path only (ADR-015): never model output.
            report.AppendLine(CultureInfo.InvariantCulture, $"- FAILED: {ex.ReasonCode} consumer={ex.ConsumerType} message={ex.Message}");
            report.AppendLine(CultureInfo.InvariantCulture, $"- failing edit shape (member: null or string length, no text): {DescribeEditShape(rig.LastOutput, ex.Message)}");
            throw;
        }
        finally
        {
            Publish(report.ToString());
        }
    }

    [SkippableFact]
    public async Task Classify_ForTheSyntheticMsaOnTheLiveModel_IsNotAnNda_AndNoEditPlanCallIsMade()
    {
        Skip.If(string.IsNullOrWhiteSpace(EndpointValue), "AGREEMENT_EDIT_PLAN_LIVE_OPENAI_ENDPOINT is not set.");
        var rig = LiveRig.Create(EndpointValue!);
        var msa = Segmentation("msa-01-bluefin-harbor-services");
        var report = new StringBuilder();
        report.AppendLine("## msa-01-bluefin-harbor-services");
        report.AppendLine(CultureInfo.InvariantCulture, $"Deployments: reasoning tier -> '{rig.ReasoningDeployment}', standard -> '{rig.StandardDeployment}'; sections {SectionCount(msa)}");

        try
        {
            var verdict = await rig.Ai.ClassifyAsync(new AgreementClassifyRequest { DocumentText = DocumentText(msa), TenantId = "live-test" });
            var isNda = verdict.IsSubDomain(AgreementSubDomains.Nda, NdaGate);
            report.AppendLine(CultureInfo.InvariantCulture,
                $"- classify: isAgreement={verdict.IsAgreement} composite={verdict.Composite} candidates=[{string.Join(", ", verdict.Candidates.Select(c => FormattableString.Invariant($"{c.SubDomainKey}:{c.Confidence:0.00}")))}] isNda={isNda} latency_ms={rig.LastLatencyMs}");

            // The orchestrator's rule (task 015): an NDA gets a plan, anything else does not.
            if (isNda)
            {
                await rig.Ai.BuildEditPlanAsync(new AgreementEditPlanRequest { SegmentationJson = msa, TenantId = "live-test" });
            }

            isNda.Should().BeFalse("a services agreement is not an NDA");
            rig.ModelCalls.Should().Be(1, "only the classifier ran: no edit-plan call is made for a non-NDA");
            report.AppendLine("- edit plan: not built (not an NDA)");
        }
        catch (AgreementAiException ex)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"- FAILED: {ex.ReasonCode} consumer={ex.ConsumerType} message={ex.Message}");
            throw;
        }
        finally
        {
            Publish(report.ToString());
        }
    }

    // ── Rig ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The production facade over a real model, with the call latency and sizes of the last model call captured.</summary>
    private sealed class LiveRig
    {
        public AgreementAi Ai { get; set; } = null!;
        public string ReasoningDeployment { get; private init; } = string.Empty;
        public string StandardDeployment { get; private init; } = string.Empty;
        public long LastLatencyMs { get; private set; }
        public int LastPromptChars { get; private set; }
        public int LastOutputChars { get; private set; }
        public int ModelCalls { get; private set; }

        /// <summary>The last raw model reply; read only to describe the SHAPE of a refused edit, never written out.</summary>
        public string LastOutput { get; private set; } = string.Empty;

        public static LiveRig Create(string endpoint)
        {
            var defaults = new DocumentIntelligenceOptions();
            var options = new DocumentIntelligenceOptions
            {
                OpenAiEndpoint = endpoint,
                StandardModel = Environment.GetEnvironmentVariable("AGREEMENT_EDIT_PLAN_LIVE_STANDARD_MODEL") ?? defaults.StandardModel,
                ReasoningModel = Environment.GetEnvironmentVariable("AGREEMENT_EDIT_PLAN_LIVE_REASONING_MODEL"),
            };
            var real = new OpenAiClient(Options.Create(options), NullLogger<OpenAiClient>.Instance, managedIdentityCredential: new DefaultAzureCredential());

            var rig = new LiveRig
            {
                ReasoningDeployment = ModelTierDeploymentResolver.Resolve(AiModelTier.Reasoning, options),
                StandardDeployment = ModelTierDeploymentResolver.Resolve(AiModelTier.Standard, options),
            };
            var model = new Mock<IOpenAiClient>();
            model.Setup(o => o.GetStructuredCompletionRawAsync(
                    It.IsAny<string>(), It.IsAny<BinaryData>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<int?>(), It.IsAny<float?>(), It.IsAny<CancellationToken>()))
                .Returns(async (string prompt, BinaryData schema, string name, string? deployment, int? maxTokens, float? temperature, CancellationToken ct) =>
                {
                    var started = TimeProvider.System.GetTimestamp();
                    var output = await real.GetStructuredCompletionRawAsync(prompt, schema, name, deployment, maxTokens, temperature, ct);
                    rig.LastLatencyMs = (long)TimeProvider.System.GetElapsedTime(started).TotalMilliseconds;
                    rig.LastPromptChars = prompt.Length;
                    rig.LastOutputChars = output.Length;
                    rig.LastOutput = output;
                    rig.ModelCalls++;
                    return output;
                });

            var routing = new Mock<IConsumerRoutingService>();
            routing.Setup(r => r.ResolveActionAsync(ConsumerTypes.AgreementClassify, It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ClassifyActionId);
            routing.Setup(r => r.ResolveActionAsync(ConsumerTypes.AgreementEditPlan, It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(EditPlanActionId);
            var catalog = new Mock<IScopeResolverService>();
            catalog.Setup(s => s.GetActionAsync(ClassifyActionId, It.IsAny<CancellationToken>())).ReturnsAsync(AuthoredAction(
                ClassifyActionId, "Agreement Classify", "agreement-classify", AuthoredFlatSystemPrompt("agreement-classify")));
            catalog.Setup(s => s.GetActionAsync(EditPlanActionId, It.IsAny<CancellationToken>())).ReturnsAsync(AuthoredAction(
                EditPlanActionId, "Agreement Edit Plan", "agreement-edit-plan", AuthoredActionMirror.SystemPrompt("agreement-edit-plan")));

            var registry = new Mock<IAgreementTypeRegistryReader>();
            registry.Setup(r => r.GetRowsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(RegistryRows());

            var ai = new AgreementAi(
                new ActionResolver(routing.Object, catalog.Object, NullLogger<ActionResolver>.Instance),
                new ActionRunner(model.Object, new PromptSchemaRenderer(NullLogger<PromptSchemaRenderer>.Instance), NullLogger<ActionRunner>.Instance, Options.Create(options)),
                registry.Object,
                new AgreementTypeRegistryPromptAssembler(),
                TimeProvider.System,
                NullLogger<AgreementAi>.Instance);
            rig.Ai = ai;
            return rig;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private static AnalysisAction AuthoredAction(Guid id, string name, string actionCode, string systemPrompt)
    {
        // Tier and temperature as authored in the mirror, so a live run exercises the Action as the catalog would hand it out.
        var mirror = JsonNode.Parse(File.ReadAllText(Path.Combine(AuthoredActionMirror.RepoRoot(), "infra", "dataverse", "actions", $"{actionCode}.action.json")))!.AsObject();
        return new AnalysisAction
        {
            Id = id,
            Name = name,
            SystemPrompt = systemPrompt,
            OutputSchemaJson = AuthoredActionMirror.OutputSchema(actionCode),
            Temperature = mirror["temperature"]!.GetValue<decimal>(),
            ModelTier = Enum.Parse<AiModelTier>(mirror["modelTier"]!.GetValue<string>()),
        };
    }

    private static string AuthoredFlatSystemPrompt(string actionCode)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AuthoredActionMirror.RepoRoot(), "infra", "dataverse", "actions", $"{actionCode}.action.json")));
        return doc.RootElement.GetProperty("systemPrompt").GetString()!;
    }

    /// <summary>The repo's mirror of the live <c>sprk_agreementtype</c> registry (what the production reader returns).</summary>
    private static IReadOnlyList<AgreementTypeRow> RegistryRows()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AuthoredActionMirror.RepoRoot(), "infra", "dataverse", "sprk_agreementtype-rows.json")));
        return doc.RootElement.GetProperty("rows").EnumerateArray()
            .Where(r => !(r.TryGetProperty("sprk_isselectable", out var selectable) && selectable.ValueKind == JsonValueKind.False))
            .Select(r => new AgreementTypeRow(
                r.GetProperty("sprk_key").GetString()!,
                r.TryGetProperty("sprk_name", out var name) ? name.GetString() : null,
                r.TryGetProperty("sprk_classificationcue", out var cue) && cue.ValueKind == JsonValueKind.String ? cue.GetString() : null,
                r.TryGetProperty("sprk_isfallback", out var fallback) && fallback.ValueKind == JsonValueKind.True,
                null))
            .ToList();
    }

    /// <summary>For a failure path like <c>$.edits[6].summary</c>: which members of that edit are null and the length of the others. Never a value.</summary>
    private static string DescribeEditShape(string modelOutput, string failureMessage)
    {
        var match = System.Text.RegularExpressions.Regex.Match(failureMessage, @"\$\.edits\[(\d+)\]");
        if (!match.Success || modelOutput.Length == 0)
        {
            return "n/a";
        }

        using var doc = JsonDocument.Parse(modelOutput);
        var edit = doc.RootElement.GetProperty("edits")[int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)];
        return string.Join(", ", edit.EnumerateObject().Select(p => p.Value.ValueKind switch
        {
            JsonValueKind.Null => $"{p.Name}=null",
            JsonValueKind.String => $"{p.Name}=str({p.Value.GetString()!.Length})",
            _ => $"{p.Name}={p.Value.ValueKind}",
        }));
    }

    private static string Segmentation(string corpusName) =>
        File.ReadAllText(Path.Combine(AuthoredActionMirror.RepoRoot(), "tests", "fixtures", "intake", "agreement-edit-plan", $"{corpusName}.segmentation.json"));

    private static int SectionCount(string segmentationJson)
    {
        using var doc = JsonDocument.Parse(segmentationJson);
        return doc.RootElement.GetProperty("sections").GetArrayLength();
    }

    /// <summary>The visible text of a segmentation, one section per line: what the classifier is given.</summary>
    private static string DocumentText(string segmentationJson)
    {
        using var doc = JsonDocument.Parse(segmentationJson);
        return string.Join('\n', doc.RootElement.GetProperty("sections").EnumerateArray().Select(s => s.GetProperty("text").GetString()));
    }

    private static HashSet<string> AcceleratorTopicIds()
    {
        var yaml = File.ReadAllText(Path.Combine(AuthoredActionMirror.RepoRoot(), "src", "accelerators", "practices", "nda", "topics.yaml"));
        return System.Text.RegularExpressions.Regex.Matches(yaml, @"^\s*-\s+id:\s+(nda\.[a-z0-9-]+)\s*$", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToHashSet();
    }

    /// <summary>Evaluates a plan against the accelerator's schema file. Returns the failing instance locations (empty = valid).</summary>
    private static List<string> ValidateAgainstAcceleratorSchema(JsonElement plan)
    {
        // The $id is dropped so this copy never collides in the global schema registry with the facade's embedded copy of the same schema.
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(AuthoredActionMirror.RepoRoot(), "src", "accelerators", "core", "schemas", "edit-plan.schema.json")))!.AsObject();
        node.Remove("$id");
        var schema = JsonSchema.FromText(node.ToJsonString());
        var options = new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true };
        lock (SchemaLock)
        {
            var result = schema.Evaluate(plan, options);
            return result.IsValid
                ? []
                : result.Details.Where(d => !d.IsValid && d.Errors is not null).Select(d => d.InstanceLocation.ToString()).Distinct().ToList();
        }
    }

    private void Publish(string section)
    {
        _output.WriteLine(section);
        if (Environment.GetEnvironmentVariable("AGREEMENT_EDIT_PLAN_LIVE_REPORT") is { Length: > 0 } reportPath)
        {
            lock (ReportLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
                File.AppendAllText(reportPath, section + Environment.NewLine);
            }
        }
    }
}
