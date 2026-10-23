using System.Globalization;
using System.Text;
using System.Text.Json;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Tests.Seam.ContentUnderstanding;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Seam.Finance;

/// <summary>
/// Live evaluation of code-set matching (ebilling-r1 task 054, D-93): the production <see cref="CodeSetProposalService"/>,
/// <see cref="InvoiceAi"/>, <see cref="ActionRunner"/> and <see cref="PromptSchemaRenderer"/> with the REAL authored
/// <c>invoice-codeset-match</c> Action and a REAL Azure OpenAI client (Fast tier + text-embedding-3-large). The Dataverse side is
/// <see cref="FakeCodingReferenceData"/> loaded from the same files the dev seed uses (<c>scripts/seed-data/ebilling/utbms-*.csv</c> and
/// <c>utbms-codeset-tuning.json</c>), with the 11 dev reference rows and a negative control from
/// <c>projects/ebilling-r1/notes/evaluation-set/code-set-matching/cases.json</c>, all unmapped (so, as on a fresh environment, no
/// examples). Runs only when <c>CODESET_MATCH_LIVE_OPENAI_ENDPOINT</c> is set; otherwise it reports Skipped. Authenticates with
/// <see cref="DefaultAzureCredential"/>: run locally with <c>AZURE_TOKEN_CREDENTIALS=AzureCliCredential</c> after <c>az login</c>
/// (the dev account accepts user tokens, ADR-028 E-2). <c>CODESET_MATCH_LIVE_FAST_MODEL</c> overrides the Fast deployment name.
/// Read-only: nothing is written anywhere.
/// </summary>
[Trait("Category", "LiveIntegration")]
public sealed class CodeSetMatchingLiveTests
{
    private static readonly string? EndpointValue = Environment.GetEnvironmentVariable("CODESET_MATCH_LIVE_OPENAI_ENDPOINT");
    private readonly ITestOutputHelper _output;

    public CodeSetMatchingLiveTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public async Task EveryDevReferenceRow_IsMatchedToTheExpectedSet_AndTheNegativeControlFitsNone()
    {
        Skip.If(string.IsNullOrWhiteSpace(EndpointValue), "CODESET_MATCH_LIVE_OPENAI_ENDPOINT is not set.");

        var data = new FakeCodingReferenceData();
        var setIds = LoadSeedSets(data);
        var cases = LoadCases();
        var rowIds = new Dictionary<Guid, Case>();
        foreach (var c in cases)
        {
            var id = c.Type == "MatterType" ? data.AddMatterType(null, c.Name) : data.AddPracticeArea(null, c.Name);
            rowIds[id] = c;
        }

        var options = new DocumentIntelligenceOptions
        {
            OpenAiEndpoint = EndpointValue!,
            FastModel = Environment.GetEnvironmentVariable("CODESET_MATCH_LIVE_FAST_MODEL") ?? new DocumentIntelligenceOptions().FastModel,
        };
        var model = new OpenAiClient(Options.Create(options), NullLogger<OpenAiClient>.Instance, managedIdentityCredential: new DefaultAzureCredential());
        var actionId = Guid.NewGuid();
        var routing = new Mock<IConsumerRoutingService>();
        routing.Setup(r => r.ResolveActionAsync(ConsumerTypes.InvoiceCodeSetMatch, It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(actionId);
        var catalog = new Mock<IScopeResolverService>();
        catalog.Setup(s => s.GetActionAsync(actionId, It.IsAny<CancellationToken>())).ReturnsAsync(new AnalysisAction
        {
            Id = actionId,
            Name = "Invoice Code Set Match",
            SystemPrompt = AuthoredActionMirror.SystemPrompt(ConsumerTypes.InvoiceCodeSetMatch),
            OutputSchemaJson = AuthoredActionMirror.OutputSchema(ConsumerTypes.InvoiceCodeSetMatch),
            Temperature = 0.0m,
            ModelTier = AiModelTier.Fast,
        });
        var ai = new InvoiceAi(
            new Mock<IPlaybookService>(MockBehavior.Strict).Object,
            model,
            new ActionResolver(routing.Object, catalog.Object, NullLogger<ActionResolver>.Instance),
            new ActionRunner(model, new PromptSchemaRenderer(NullLogger<PromptSchemaRenderer>.Instance), NullLogger<ActionRunner>.Instance, Options.Create(options)),
            ContentUnderstandingTestHost.Unconfigured(),
            NullLogger<InvoiceAi>.Instance);
        var sut = new CodeSetProposalService(data.Entities.Object, data.Records.Object, ai, NullLogger<CodeSetProposalService>.Instance);

        var list = await sut.ProposeAsync();

        var names = setIds.ToDictionary(kv => kv.Value, kv => kv.Key);
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"Code-set matching live run {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}Z, Fast deployment '{options.FastModel}', embeddings '{options.EmbeddingModel}'");
        report.AppendLine("| Row | Type | Expected | Result | Reviewed | Confidence | Embedding top | Pass |");
        report.AppendLine("|---|---|---|---|---|---|---|---|");
        var failures = new List<string>();
        foreach (var proposal in list.Proposals)
        {
            var c = rowIds[proposal.ReferenceId];
            var result = proposal.Outcome switch
            {
                CodeSetMatchOutcome.NoneFits => "none",
                CodeSetMatchOutcome.Failed => "failed",
                _ => names[proposal.Proposed!.CodeSetId],
            };
            var top = proposal.Outcome == CodeSetMatchOutcome.NoneFits ? proposal.Closest : proposal.Reviewed ? null : proposal.Proposed;
            var pass = proposal.Reviewed && result == c.Expected;
            report.AppendLine(CultureInfo.InvariantCulture,
                $"| {c.Name} | {c.Type} | {c.Expected} | {result} | {proposal.Reviewed} | {proposal.Confidence?.ToString("0.00", CultureInfo.InvariantCulture) ?? "-"} | {(top is null ? "-" : $"{top.Name} ({top.Score:0.000})")} | {(pass ? "yes" : "NO")} |");
            report.AppendLine(CultureInfo.InvariantCulture, $"|   reason: {proposal.Rationale.Replace("|", "/", StringComparison.Ordinal)} |||||||");
            if (!pass)
            {
                failures.Add($"{c.Name}: expected {c.Expected}, got {result} (reviewed {proposal.Reviewed})");
            }
        }

        _output.WriteLine(report.ToString());
        if (Environment.GetEnvironmentVariable("CODESET_MATCH_LIVE_REPORT") is { Length: > 0 } reportPath)
        {
            await File.WriteAllTextAsync(reportPath, report.ToString());
        }

        list.Proposals.Should().HaveCount(cases.Count);
        failures.Should().BeEmpty("every row must match its expected set (D-93 acceptance: 11/11 and the negative control none)");
    }

    private sealed record Case(string Type, string Name, string Expected);

    private static List<Case> LoadCases()
    {
        var path = Path.Combine(AuthoredActionMirror.RepoRoot(), "projects", "ebilling-r1", "notes", "evaluation-set", "code-set-matching", "cases.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("cases").EnumerateArray()
            .Select(c => new Case(c.GetProperty("type").GetString()!, c.GetProperty("name").GetString()!, c.GetProperty("expected").GetString()!))
            .ToList();
    }

    /// <summary>Loads the seed CSVs and the tuning file into the fake, as the dev seed loaded them into Dataverse. Returns name → id.</summary>
    private static Dictionary<string, Guid> LoadSeedSets(FakeCodingReferenceData data)
    {
        var folder = Path.Combine(AuthoredActionMirror.RepoRoot(), "scripts", "seed-data", "ebilling");
        using var tuningDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "utbms-codeset-tuning.json")));
        var tuning = tuningDoc.RootElement.GetProperty("sets").EnumerateArray().ToDictionary(
            s => s.GetProperty("name").GetString()!,
            s => (Description: s.GetProperty("description").GetString()!, Hints: s.GetProperty("matchHints").GetString()!));

        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(folder, "utbms-*.csv").Order(StringComparer.Ordinal))
        {
            var rows = ParseCsv(File.ReadAllText(file));
            var header = rows[0];
            int Col(string name) => Array.IndexOf(header, name);
            var first = rows[1];
            var name = first[Col("set_name")];
            tuning.TryGetValue(name, out var tune);
            var id = data.AddSet(name, body: first[Col("body")], description: tune.Description, matchHints: tune.Hints);
            ids[name] = id;
            foreach (var row in rows.Skip(1))
            {
                data.AddCode(id, row[Col("code")], Enum.Parse<BillingCodeKind>(row[Col("code_type")]), row[Col("description")], row[Col("guidance_text")]);
            }
        }

        return ids;
    }

    /// <summary>RFC 4180 CSV (quoted fields with commas, doubled quotes and line breaks).</summary>
    private static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(ch);
                }
            }
            else if (ch == '"')
            {
                quoted = true;
            }
            else if (ch == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                fields.Add(field.ToString());
                field.Clear();
                if (fields.Any(f => f.Length > 0))
                {
                    rows.Add([.. fields]);
                }

                fields.Clear();
            }
            else if (ch != '﻿')
            {
                field.Append(ch);
            }
        }

        fields.Add(field.ToString());
        if (fields.Any(f => f.Length > 0))
        {
            rows.Add([.. fields]);
        }

        return rows;
    }
}
