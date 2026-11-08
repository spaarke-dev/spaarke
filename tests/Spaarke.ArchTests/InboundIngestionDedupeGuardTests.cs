using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 034 (FR-21 part b, W3 direction): the dedupe on the message id of the inbound
/// ingestors. A concurrent or repeated delivery of one message must resolve to ONE communication row, whichever path delivers it.
/// </summary>
/// <remarks>
/// <para><b>Three dedupe authorities, each pinned.</b> (1) The mailbox path (<c>IncomingCommunicationProcessor</c>): the Graph
/// message id, then the canonical internet message id, then the race-proof create keyed on the internet message id; a delivery
/// that loses the race returns before any side effect (attachments, archive, thread, association, enrichment). (2) The upload
/// capture (<c>EmailUploadCaptureService</c>): the same race-proof create, and a reconciled save skips the association, thread
/// and enrichment. (3) The chat path: <c>IncomingMessagingJobHandler</c> checks an idempotency key built from the ACS message id,
/// takes a lock, re-checks, ingests, and only then marks the key; <c>MessagingIngestor.IngestAsync</c> has that one caller.</para>
/// <para>The race-proof create itself (<c>DataverseServiceClientImpl.CreateCommunicationRaceProofAsync</c>) is pinned for the
/// behaviour the callers rely on: a UNIQUE-key duplicate reconciles to the existing row and reports <c>true</c>; a blank id
/// creates unguarded; the failure to re-read the canonical row surfaces; there is no retry loop (NFR-03, never retry blindly
/// after an ambiguous failure).</para>
/// <para><b>Known limit.</b> The rules read source text (line comments stripped). They prove the order and the keys; the
/// alternate key itself is Dataverse metadata (task 020) and the duplicate-fault classifier has its own unit tests
/// (<c>AlternateKeyDuplicateClassifierTests</c>). Behaviour tests cover the chat handler
/// (<c>IncomingMessagingJobHandlerTests</c>) and the upload reconcile (<c>EmailUploadCaptureSeamTests</c>).</para>
/// <para><b>Controls.</b> As <see cref="InboundIngestionCreatorsGuardTests"/>: each rule has must-fire mutations of the real source
/// and must-not-fire changes that leave the rule intact. Enforced by: this guard (ArchTests in CI and the Stop hook). No
/// baseline: there are no violations today.</para>
/// </remarks>
public class InboundIngestionDedupeGuardTests
{
    private const string HandlerFile = "src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/IncomingMessagingJobHandler.cs";
    private const string DataverseClientFile = InboundIngestionCreatorsGuardTests.DataverseClientFile;

    private static readonly Regex PlainCommunicationCreate = new(@"\.CreateAsync\(\s*communication\b", RegexOptions.Compiled);
    private static readonly Regex IngestCall = new(@"\.IngestAsync\(", RegexOptions.Compiled);

    // ── Rules ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The text of the first <c>{ ... }</c> block at or after <paramref name="from"/>, ignoring braces inside string and char literals.</summary>
    internal static string? Block(string code, int from)
    {
        var open = code.IndexOf('{', from);
        if (open < 0)
            return null;

        var depth = 0;
        var inString = false;
        for (var i = open; i < code.Length; i++)
        {
            var c = code[i];
            if (inString)
            {
                if (c == '\\')
                    i++;
                else if (c == '"')
                    inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '\'':
                    i += i + 1 < code.Length && code[i + 1] == '\\' ? 3 : 2; // 'x' or '\x'
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                        return code.Substring(open, i - open + 1);
                    break;
            }
        }

        return null;
    }

    /// <summary>
    /// The <c>if (wasDuplicate)</c> branch exists, contains <paramref name="mustContain"/>, and every <paramref name="afterwards"/> side
    /// effect appears only after the branch ends.
    /// </summary>
    internal static IReadOnlyList<string> DuplicateBranchViolations(string label, string code, string mustContain, string[] afterwards)
    {
        var violations = new List<string>();
        var start = code.IndexOf("if (wasDuplicate)", StringComparison.Ordinal);
        if (start < 0)
        {
            violations.Add($"{label} has no `if (wasDuplicate)` branch after the race-proof create: a delivery that loses the create race "
                + "must resolve to the canonical row and stop (FR-C1 / NFR-02).");
            return violations;
        }

        var block = Block(code, start);
        if (block is null || !block.Contains(mustContain, StringComparison.Ordinal))
        {
            violations.Add($"{label}: the `if (wasDuplicate)` branch must contain `{mustContain}` (the duplicate delivery ends there).");
            return violations;
        }

        var end = code.IndexOf(block, start, StringComparison.Ordinal) + block.Length;
        foreach (var effect in afterwards)
        {
            var at = code.IndexOf(effect, StringComparison.Ordinal);
            if (at < 0)
                violations.Add($"{label}: `{effect}` is missing; the dedupe rule names it as a side effect a duplicate delivery must not reach.");
            else if (at < end)
                violations.Add($"{label}: `{effect}` runs before (or inside) the duplicate check, so a delivery that lost the race would repeat it.");
        }

        return violations;
    }

    /// <summary>The mailbox path: the three dedupe steps in order, the keyed race-proof create, and the keys written on the row.</summary>
    internal static IReadOnlyList<string> MailboxViolations(string code)
    {
        var violations = new List<string>();
        violations.AddRange(InboundIngestionCreatorsGuardTests.InOrder("IncomingCommunicationProcessor", code,
            "await ExistsByGraphMessageIdAsync(graphMessageId",
            "await TryGetCanonicalByInternetMessageIdAsync(message.InternetMessageId",
            "await ResolveInboundFilingAsync(",
            "await CreateCommunicationRecordAsync(",
            "if (wasDuplicate)"));

        if (PlainCommunicationCreate.IsMatch(code))
        {
            violations.Add("IncomingCommunicationProcessor creates the communication with a plain CreateAsync. A repeated or concurrent delivery "
                + "of one email must resolve to one row: create through CreateCommunicationRaceProofAsync (UNIQUE key on sprk_internetmessageid).");
        }

        if (Regex.Matches(code, @"CreateCommunicationRaceProofAsync\(\s*communication\s*,\s*message\.InternetMessageId\s*,\s*ct\s*\)").Count != 1)
        {
            violations.Add("IncomingCommunicationProcessor must create the communication through CreateCommunicationRaceProofAsync(communication, "
                + "message.InternetMessageId, ct), keyed by the message's InternetMessageId.");
        }

        if (!Regex.IsMatch(code, @"\[""sprk_graphmessageid""\]\s*=\s*graphMessageId") ||
            !Regex.IsMatch(code, @"\[""sprk_internetmessageid""\]\s*=\s*message\.InternetMessageId"))
        {
            violations.Add("IncomingCommunicationProcessor must write sprk_graphmessageid and sprk_internetmessageid on the row: they are the keys "
                + "the next delivery is deduped on.");
        }

        violations.AddRange(DuplicateBranchViolations("IncomingCommunicationProcessor", code, "return;",
        [
            "CrossPathLink.FindAndLinkArchiveDocumentsAsync(", "_associationResolver.ApplyToNewRecordAsync(", "await ResolveThreadAndShareabilityAsync(",
            "_participantIndexer.WriteParticipantsAsync(", "await ProcessIncomingAttachmentsAsync(", "await ArchiveEmlAsync(",
            "_enrichmentService.EnrichAsync(",
        ]));
        return violations;
    }

    /// <summary>The upload capture: the keyed race-proof create, and a reconciled save re-associates nothing.</summary>
    internal static IReadOnlyList<string> UploadViolations(string code)
    {
        var violations = new List<string>();
        if (PlainCommunicationCreate.IsMatch(code))
        {
            violations.Add("EmailUploadCaptureService creates the communication with a plain CreateAsync; a same-email save must reconcile to the "
                + "existing row through CreateCommunicationRaceProofAsync.");
        }

        if (Regex.Matches(code, @"\.CreateCommunicationRaceProofAsync\(\s*communication\s*,\s*email\.InternetMessageId\s*,\s*ct\s*\)").Count != 1)
        {
            violations.Add("EmailUploadCaptureService must create through CreateCommunicationRaceProofAsync(communication, email.InternetMessageId, ct), "
                + "keyed by the saved email's InternetMessageId.");
        }

        if (!Regex.IsMatch(code, @"communication\[""sprk_internetmessageid""\]\s*=\s*email\.InternetMessageId"))
            violations.Add("EmailUploadCaptureService must write sprk_internetmessageid on the row: it is the key the next save is deduped on.");

        violations.AddRange(DuplicateBranchViolations("EmailUploadCaptureService", code, "ReconciledToExisting: true",
            ["_associationResolver.ApplyToNewRecordAsync(", "await ResolveThreadAsync(", "_enrichmentService.EnrichAsync("]));
        return violations;
    }

    /// <summary>The race-proof create: reconcile a UNIQUE-key duplicate, create unguarded only without an id, never retry.</summary>
    internal static IReadOnlyList<string> RaceProofImplementationViolations(string code)
    {
        var declaration = code.IndexOf("Task<(Guid Id, bool WasDuplicate)> CreateCommunicationRaceProofAsync(", StringComparison.Ordinal);
        if (declaration < 0)
            return ["DataverseServiceClientImpl no longer declares CreateCommunicationRaceProofAsync(Entity, string?, CancellationToken) returning (Guid Id, bool WasDuplicate)."];

        var body = Block(code, declaration);
        if (body is null)
            return ["CreateCommunicationRaceProofAsync has no readable body; the rule lost its signal."];

        var violations = new List<string>();
        foreach (var (needle, why) in new[]
                 {
                     ("IsNullOrWhiteSpace(internetMessageId)", "a blank id creates unguarded (the key excludes nulls)"),
                     ("return (plainId, false);", "the unguarded create reports not-a-duplicate"),
                     ("catch (Exception ex) when (IsAlternateKeyDuplicate(ex))", "a UNIQUE-key duplicate fault is the signal that another writer won"),
                     ("GetCommunicationByInternetMessageIdAsync(internetMessageId", "the canonical row is re-read by the same key"),
                     ("return (existing.Id, true);", "the loser resolves to the existing id and says it was a duplicate"),
                     ("throw new InvalidOperationException(", "a canonical row that cannot be re-read surfaces, not a dropped message"),
                 })
        {
            if (!body.Contains(needle, StringComparison.Ordinal))
                violations.Add($"CreateCommunicationRaceProofAsync lost `{needle}`: {why}.");
        }

        if (Regex.IsMatch(body, @"\bwhile\s*\(|\bfor\s*\(|\bforeach\s*\(|Task\.Delay|Retry"))
        {
            violations.Add("CreateCommunicationRaceProofAsync retries. After an ambiguous failure the create must not be repeated blindly (NFR-03); "
                + "the unique key, not a retry, is the dedupe.");
        }

        return violations;
    }

    /// <summary>The chat path: key from the ACS message id, check, lock, re-check, ingest, then mark; the lock is always released.</summary>
    internal static IReadOnlyList<string> ChatHandlerViolations(string code)
    {
        var violations = new List<string>();
        violations.AddRange(InboundIngestionCreatorsGuardTests.InOrder("IncomingMessagingJobHandler", code,
            "var idempotencyKey = IdempotencyKeyFor(acsMessageId);",
            "IsEventProcessedAsync(idempotencyKey",
            "TryAcquireProcessingLockAsync(idempotencyKey",
            "IsEventProcessedAsync(idempotencyKey",
            "ingestor.IngestAsync(",
            "MarkEventAsProcessedAsync(idempotencyKey",
            "ReleaseProcessingLockAsync(idempotencyKey"));

        var firstMark = code.IndexOf("MarkEventAsProcessedAsync(", StringComparison.Ordinal);
        var ingest = code.IndexOf("ingestor.IngestAsync(", StringComparison.Ordinal);
        if (firstMark >= 0 && ingest >= 0 && firstMark < ingest)
        {
            violations.Add("IncomingMessagingJobHandler marks the message processed before the ingest. Mark after the persist so a crash in between "
                + "re-processes (at-least-once) instead of dropping the message.");
        }

        return violations;
    }

    /// <summary>The chat ingestor writes the id the handler dedupes on.</summary>
    internal static IReadOnlyList<string> ChatKeyViolations(string code) =>
        Regex.IsMatch(code, @"communication\[""sprk_acsmessageid""\]\s*=\s*request\.ProviderMessageId")
            ? []
            : ["MessagingIngestor must write sprk_acsmessageid from the request's ProviderMessageId: it is the id the job handler dedupes on."];

    /// <summary>Only the job handler ingests a chat message, so only it can skip the dedupe.</summary>
    internal static IReadOnlyList<string> IngestCallerViolations(IEnumerable<(string Path, string Code)> files)
    {
        var callers = files.Where(f => IngestCall.IsMatch(f.Code)).Select(f => f.Path).Order(StringComparer.Ordinal).ToList();
        var violations = new List<string>();
        foreach (var caller in callers.Where(c => !string.Equals(c, HandlerFile, StringComparison.Ordinal)))
        {
            violations.Add($"{caller} calls an ingestor's IngestAsync. The chat ingestor is deduped by IncomingMessagingJobHandler (idempotency key, "
                + "lock, re-check, mark); a second caller would skip it. Route through the handler.");
        }

        if (!callers.Contains(HandlerFile, StringComparer.Ordinal))
            violations.Add("IncomingMessagingJobHandler no longer calls IngestAsync; update the dedupe rules with the new entry point.");
        return violations;
    }

    // ── The guard, against the real source ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheMailboxPath_DedupesOnTheMessageId_AndStopsOnALostRace()
    {
        var violations = MailboxViolations(InboundIngestionCreatorsGuardTests.Code(InboundIngestionCreatorsGuardTests.IncomingProcessorFile));
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void TheUploadCapture_DedupesOnTheMessageId_AndSkipsAReconciledSave()
    {
        var violations = UploadViolations(InboundIngestionCreatorsGuardTests.Code(InboundIngestionCreatorsGuardTests.UploadCaptureFile));
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void TheRaceProofCreate_ReconcilesAUniqueKeyDuplicate_AndNeverRetries()
    {
        var violations = RaceProofImplementationViolations(InboundIngestionCreatorsGuardTests.Code(DataverseClientFile));
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void TheChatPath_DedupesOnTheAcsMessageId_BeforeAndAfterTheIngest()
    {
        var violations = new List<string>();
        violations.AddRange(ChatHandlerViolations(InboundIngestionCreatorsGuardTests.Code(HandlerFile)));
        violations.AddRange(ChatKeyViolations(InboundIngestionCreatorsGuardTests.Code(InboundIngestionCreatorsGuardTests.MessagingIngestorFile)));
        violations.AddRange(IngestCallerViolations(InboundIngestionCreatorsGuardTests.ServerFiles()));
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    // ── Controls: the block reader ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("if (x) { a(\"}\"); b('}'); }", "{ a(\"}\"); b('}'); }")]
    [InlineData("if (x) { if (y) { z(); } return; } tail();", "{ if (y) { z(); } return; }")]
    [InlineData("if (x) { log(\"{Id} and \\\"quoted\\\" {Other}\"); }", "{ log(\"{Id} and \\\"quoted\\\" {Other}\"); }")]
    public void BlockReader_StopsAtTheMatchingBrace_NotAtOneInsideALiteral(string code, string expected) =>
        Assert.Equal(expected, Block(code, 0));

    // ── Controls: per-rule, as mutations of the real source ────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> RulesFor(string file, string code) => file switch
    {
        InboundIngestionCreatorsGuardTests.IncomingProcessorFile => MailboxViolations(code),
        InboundIngestionCreatorsGuardTests.UploadCaptureFile => UploadViolations(code),
        InboundIngestionCreatorsGuardTests.MessagingIngestorFile => ChatKeyViolations(code),
        DataverseClientFile => RaceProofImplementationViolations(code),
        HandlerFile => ChatHandlerViolations(code),
        _ => throw new ArgumentOutOfRangeException(nameof(file), file, "no dedupe rule for this file"),
    };

    public static TheoryData<string, string, string, string> MustFire => new()
    {
        // Mailbox: keyed race-proof create, the three steps in order, the early return before every side effect.
        { InboundIngestionCreatorsGuardTests.IncomingProcessorFile, "communication, message.InternetMessageId, ct);", "communication, graphMessageId, ct);", "keyed by the message's InternetMessageId" },
        { InboundIngestionCreatorsGuardTests.IncomingProcessorFile, "return await _communicationService.CreateCommunicationRaceProofAsync(", "await _genericEntityService.CreateAsync(communication, ct); return await _communicationService.CreateCommunicationRaceProofAsync(", "plain CreateAsync" },
        { InboundIngestionCreatorsGuardTests.IncomingProcessorFile, "if (wasDuplicate)", "if (wasDuplicate && false)", "no `if (wasDuplicate)` branch" },
        { InboundIngestionCreatorsGuardTests.IncomingProcessorFile, "if (wasDuplicate)", "await ProcessIncomingAttachmentsAsync(null!, \"\", \"\", communicationId, owner, ct);\n        if (wasDuplicate)", "runs before (or inside) the duplicate check" },
        { InboundIngestionCreatorsGuardTests.IncomingProcessorFile, "var canonicalDuplicate = await TryGetCanonicalByInternetMessageIdAsync(message.InternetMessageId, ct);", "DataverseEntity? canonicalDuplicate = null;", "await TryGetCanonicalByInternetMessageIdAsync(message.InternetMessageId" },
        { InboundIngestionCreatorsGuardTests.IncomingProcessorFile, "[\"sprk_internetmessageid\"] = message.InternetMessageId,", "[\"sprk_internetmessageid\"] = graphMessageId,", "must write sprk_graphmessageid and sprk_internetmessageid" },
        { InboundIngestionCreatorsGuardTests.IncomingProcessorFile, "                message.InternetMessageId, communicationId, mailboxEmail);\n            return;", "                message.InternetMessageId, communicationId, mailboxEmail);", "must contain `return;`" },
        // Upload capture.
        { InboundIngestionCreatorsGuardTests.UploadCaptureFile, "ReconciledToExisting: true", "ReconciledToExisting: false", "ReconciledToExisting: true" },
        { InboundIngestionCreatorsGuardTests.UploadCaptureFile, ".CreateCommunicationRaceProofAsync(communication, email.InternetMessageId, ct);", ".CreateCommunicationRaceProofAsync(communication, null, ct);", "keyed by the saved email's InternetMessageId" },
        { InboundIngestionCreatorsGuardTests.UploadCaptureFile, "if (wasDuplicate)", "if (wasDuplicate && false)", "no `if (wasDuplicate)` branch" },
        // The race-proof create itself.
        { DataverseClientFile, "catch (Exception ex) when (IsAlternateKeyDuplicate(ex))", "catch (Exception ex) when (false)", "IsAlternateKeyDuplicate" },
        { DataverseClientFile, "return (existing.Id, true);", "return (existing.Id, false);", "return (existing.Id, true);" },
        // Chat path.
        { HandlerFile, "var normalized = _normalizer.Normalize(payload);", "await _idempotency.MarkEventAsProcessedAsync(idempotencyKey, expiration: null, ct); var normalized = _normalizer.Normalize(payload);", "before the ingest" },
        { HandlerFile, "TryAcquireProcessingLockAsync(idempotencyKey, lockDuration: null, ct)", "TryAcquireProcessingLockAsync(acsMessageId, lockDuration: null, ct)", "TryAcquireProcessingLockAsync(idempotencyKey" },
        { HandlerFile, "var idempotencyKey = IdempotencyKeyFor(acsMessageId);", "var idempotencyKey = acsMessageId;", "IdempotencyKeyFor(acsMessageId)" },
        { InboundIngestionCreatorsGuardTests.MessagingIngestorFile, "communication[\"sprk_acsmessageid\"] = request.ProviderMessageId;", "// dropped", "must write sprk_acsmessageid" },
    };

    [Theory]
    [MemberData(nameof(MustFire))]
    public void EachRule_FiresOnAMutationOfTheRealSource(string file, string from, string to, string expected)
    {
        var source = InboundIngestionCreatorsGuardTests.Read(file);
        Assert.True(source.Contains(from, StringComparison.Ordinal), $"control is vacuous: `{from}` is not in {file}");

        var violations = RulesFor(file, InboundIngestionCreatorsGuardTests.CodeOf(source.Replace(from, to, StringComparison.Ordinal)));

        Assert.Contains(violations, v => v.Contains(expected, StringComparison.Ordinal));
    }

    public static TheoryData<string, string, string> MustNotFire => new()
    {
        // A comment that names the forbidden create is not code.
        { InboundIngestionCreatorsGuardTests.IncomingProcessorFile, "public sealed class IncomingCommunicationProcessor", "// _genericEntityService.CreateAsync(communication, ct) while (true) Retry\npublic sealed class IncomingCommunicationProcessor" },
        { InboundIngestionCreatorsGuardTests.UploadCaptureFile, "public sealed class EmailUploadCaptureService", "// .CreateAsync(communication, ct)\npublic sealed class EmailUploadCaptureService" },
        { DataverseClientFile, "public async Task<(Guid Id, bool WasDuplicate)> CreateCommunicationRaceProofAsync(", "// while (true) { Task.Delay(1); } Retry\n    public async Task<(Guid Id, bool WasDuplicate)> CreateCommunicationRaceProofAsync(" },
        { HandlerFile, "public string JobType => JobTypeName;", "// MarkEventAsProcessedAsync( before ingestor.IngestAsync(\n    public string JobType => JobTypeName;" },
        // A whitespace-only change (blank lines before the class) changes nothing the rules read, and does not tie a control to log wording.
        { InboundIngestionCreatorsGuardTests.IncomingProcessorFile, "public sealed class IncomingCommunicationProcessor", "\n\n   \n\npublic sealed class IncomingCommunicationProcessor" },
        { InboundIngestionCreatorsGuardTests.UploadCaptureFile, "public sealed class EmailUploadCaptureService", "\n\n   \n\npublic sealed class EmailUploadCaptureService" },
        { InboundIngestionCreatorsGuardTests.MessagingIngestorFile, "public sealed class MessagingIngestor", "\n\n   \n\npublic sealed class MessagingIngestor" },
    };

    [Theory]
    [MemberData(nameof(MustNotFire))]
    public void EachRule_IgnoresAChangeThatBreaksNothing(string file, string from, string to)
    {
        var source = InboundIngestionCreatorsGuardTests.Read(file);
        Assert.True(source.Contains(from, StringComparison.Ordinal), $"control is vacuous: `{from}` is not in {file}");

        var violations = RulesFor(file, InboundIngestionCreatorsGuardTests.CodeOf(source.Replace(from, to, StringComparison.Ordinal)));

        Assert.Empty(violations);
    }

    [Fact]
    public void IngestCallers_FireOnASecondCaller_AndWhenTheHandlerStopsCalling()
    {
        var second = IngestCallerViolations(
            [(HandlerFile, "await ingestor.IngestAsync(r, ct);"), ("src/server/api/Sprk.Bff.Api/Api/X.cs", "await i.IngestAsync(r, ct);")]);
        Assert.Contains(second, v => v.Contains("X.cs", StringComparison.Ordinal));

        var none = IngestCallerViolations([("src/server/api/Sprk.Bff.Api/Api/Y.cs", "var ok = 1;")]);
        Assert.Contains(none, v => v.Contains("no longer calls IngestAsync", StringComparison.Ordinal));

        Assert.Empty(IngestCallerViolations([(HandlerFile, "await ingestor.IngestAsync(r, ct);")]));
    }
}
