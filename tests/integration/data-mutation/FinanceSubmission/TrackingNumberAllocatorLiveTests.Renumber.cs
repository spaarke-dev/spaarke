// ebilling-r1 task 095d-1c (notes 095c 16.6 "Fault classification", 095d-1a known limit K2; the merge gate for 095d-1b/1c) - LIVE proof of the
// one seam the renumber's unit tests cannot measure: WHERE Dataverse puts the scope key's fault when it refuses a renumber's submission UPDATE
// inside an ExecuteTransaction. TrackingNumberAllocator.IsLostRaceOnTheSubmissionUpdate accepts exactly two (index, code) pairs as a lost race:
// 0x80060892 at index 0 or with no index, and 0x80040237 at index 0. Any other pair propagates as a 500 (fail closed, nothing written).
//
// Opt-in as the rest of this class: Skipped unless SPAARKE_LIVE_TRACKING_DATAVERSE_URL is set, and Skipped (with the reason) when the
// environment is not at phase P2 or lacks sprk_invoicesubmission.sprk_previoussequence (the 16.6.1 pre-check).
// IT WRITES TO DATAVERSE (creates, renumbers and deletes its own rows): run it only with the owner's go. 095d-1c did NOT run it.
//
// WHAT IS REAL: the production TrackingNumberAllocator over the production DataverseServiceClientImpl, against the live scope key. The only
// addition is a pass-through recorder around ExecuteTransactionAsync that keeps each refused transaction's FailedRequestIndex and the fault codes
// in its chain before the allocator sees the exception. Every row is named with this class's prefix and deleted in `finally` (asserted by query).
using System.ServiceModel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.FinanceSubmission;

public sealed partial class TrackingNumberAllocatorLiveTests
{
    private const int AlternateKeyDuplicateCode = unchecked((int)0x80060892);
    private const int CannotInsertDuplicateKeyCode = unchecked((int)0x80040237);

    /// <summary>Rounds before the test gives up on seeing two renumbers collide (each round uses two fresh rows).</summary>
    private const int MaxRenumberRounds = 5;

    /// <summary>
    /// 095d-1a K2 merge gate: two no-firm submissions with one firm invoice number are renumbered into ONE firm's scope at the same moment. The
    /// scope key lets one take <c>.1</c> and refuses the other's whole transaction; the loser's faulted request INDEX and fault CODE must be a
    /// pair the allocator classifies as a lost race, and the loser must then have retried to <c>.2</c>.
    /// </summary>
    [SkippableFact]
    public async Task TwoRenumbersIntoOneFirmScope_TheLosersFaultIsALostRacePair_AndItRetriesToTheNextNumber()
    {
        var url = Environment.GetEnvironmentVariable(UrlVar);
        Skip.If(string.IsNullOrWhiteSpace(url), $"{UrlVar} is not set.");

        using var dataverse = CreateClient(url!);
        IGenericEntityService entities = dataverse;
        var firmId = await FirstFirmAsync(entities);
        var created = new List<Guid>();
        try
        {
            for (var round = 1; round <= MaxRenumberRounds; round++)
            {
                var invoiceNumber = $"{NamePrefix}-{Guid.NewGuid():N}";
                var pair = new[] { Guid.NewGuid(), Guid.NewGuid() };
                foreach (var id in pair)
                {
                    var made = await new TrackingNumberAllocator(entities, dataverse, NullLogger<TrackingNumberAllocator>.Instance).CreateSubmissionAsync(
                        id, firmId: null, invoiceNumber, new Dictionary<string, object> { ["sprk_name"] = $"{NamePrefix} renumber round {round}" });
                    Skip.If(made.Outcome == TrackingNumberOutcome.Refused,
                        $"a no-firm submission cannot be created here ({made.Reason}): the environment is not at phase P2.");
                    created.Add(id);
                }

                var versions = new Dictionary<Guid, string>();
                foreach (var id in pair)
                {
                    versions[id] = await VersionOfAsync(entities, id);
                }

                var faults = new List<(Guid Submission, int? Index, IReadOnlyList<int> Codes)>();
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var racers = pair.Select(id => Task.Run(async () =>
                {
                    var allocator = new TrackingNumberAllocator(Recording(entities, id, faults), dataverse, NullLogger<TrackingNumberAllocator>.Instance);
                    await start.Task;
                    return (Id: id, Result: await allocator.RenumberSubmissionAsync(id, versions[id], firmId, invoiceNumber, _ => new RenumberApply(null, [])));
                })).ToList();
                start.SetResult();
                var results = await Task.WhenAll(racers);
                foreach (var (id, result) in results)
                {
                    _out.WriteLine($"round {round}: {id} -> {result.Outcome} {result.TrackingNumber} {result.Reason}");
                }

                Skip.If(results.Any(r => r.Result is { Outcome: RenumberOutcome.Refused, Reason: TrackingNumberReasons.SchemaMissing or TrackingNumberReasons.ScopeKeyInactive }),
                    "the renumber is refused here (schema_missing or scope_key_inactive): run the 16.6.1 and scope pre-checks first.");
                results.Should().OnlyContain(r => r.Result.Outcome == RenumberOutcome.Renumbered, "both renumbers commit: the loser retries");
                results.Select(r => r.Result.TrackingNumber).Should().BeEquivalentTo(new[] { $"{invoiceNumber}.1", $"{invoiceNumber}.2" },
                    "one takes .1 in the firm's scope, the other retried to .2");

                List<(Guid Submission, int? Index, IReadOnlyList<int> Codes)> seen;
                lock (faults)
                {
                    seen = [.. faults];
                }

                foreach (var f in seen)
                {
                    _out.WriteLine($"round {round}: refused transaction of {f.Submission}: FailedRequestIndex={f.Index?.ToString() ?? "none"}, " +
                        $"codes={string.Join(",", f.Codes.Select(c => $"0x{c:X8}"))}");
                }

                if (seen.Count == 0)
                {
                    _out.WriteLine($"round {round}: the two renumbers did not collide; next round.");
                    continue;
                }

                // THE SEAM: every refused transaction is one of the two pairs the allocator retries (any other pair would have propagated).
                seen.Should().OnlyContain(
                    f => (f.Index == null || f.Index == 0) && f.Codes.Contains(AlternateKeyDuplicateCode) || f.Index == 0 && f.Codes.Contains(CannotInsertDuplicateKeyCode),
                    "the loser's fault is on the submission update (index 0, or no index) with a key duplicate code");
                results.Single(r => r.Id == seen[0].Submission).Result.TrackingNumber.Should().Be($"{invoiceNumber}.2", "the loser retried to the next n");

                var stored = await ReadRenumberedRowsAsync(entities, invoiceNumber);
                stored.Should().HaveCount(2);
                stored.Should().OnlyContain(
                    s => string.Equals(s.Scope, TrackingNumberAllocator.ScopeOf(firmId), StringComparison.OrdinalIgnoreCase) && s.PreviousSequence != null,
                    "both rows moved into the firm's scope and kept their none-scope sequence (16.6.1)");
                return;
            }

            throw new Xunit.Sdk.XunitException($"No lost race was observed in {MaxRenumberRounds} rounds: the seam was not measured. Re-run.");
        }
        finally
        {
            await CleanUpAsync(entities, created);
        }
    }

    /// <summary>The real entity service, with each refused ExecuteTransaction's index and fault codes recorded before it is rethrown. The
    /// renumber uses only RetrieveMultipleAsync and ExecuteTransactionAsync; the mock is Strict, so any other call fails the test.</summary>
    private static IGenericEntityService Recording(IGenericEntityService real, Guid submission, List<(Guid Submission, int? Index, IReadOnlyList<int> Codes)> faults)
    {
        var mock = new Mock<IGenericEntityService>(MockBehavior.Strict);
        mock.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression q, CancellationToken ct) => real.RetrieveMultipleAsync(q, ct));
        mock.Setup(e => e.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Returns((FetchExpression f, CancellationToken ct) => real.RetrieveMultipleAsync(f, ct));
        mock.Setup(e => e.ExecuteTransactionAsync(It.IsAny<IReadOnlyList<OrganizationRequest>>(), It.IsAny<CancellationToken>()))
            .Returns(async (IReadOnlyList<OrganizationRequest> requests, CancellationToken ct) =>
            {
                try
                {
                    await real.ExecuteTransactionAsync(requests, ct);
                }
                catch (DataverseTransactionException ex)
                {
                    lock (faults)
                    {
                        faults.Add((submission, ex.FailedRequestIndex, FaultCodesOf(ex)));
                    }

                    throw;
                }
            });
        return mock.Object;
    }

    /// <summary>Every <see cref="OrganizationServiceFault"/> code in the exception chain, inner faults included.</summary>
    private static List<int> FaultCodesOf(Exception ex)
    {
        var codes = new List<int>();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is FaultException<OrganizationServiceFault> fault)
            {
                for (var f = fault.Detail; f is not null; f = f.InnerFault)
                {
                    codes.Add(f.ErrorCode);
                }
            }
        }

        return codes;
    }

    private static async Task<string> VersionOfAsync(IGenericEntityService entities, Guid submissionId)
    {
        var rows = await entities.RetrieveMultipleAsync(new QueryExpression(TrackingNumberAllocator.SubmissionEntity)
        {
            ColumnSet = new ColumnSet("versionnumber"),
            Criteria = { Conditions = { new ConditionExpression("sprk_invoicesubmissionid", ConditionOperator.Equal, submissionId) } },
        });
        var row = rows.Entities.Should().ContainSingle().Subject;
        return row.TryGetAttributeValue<long>("versionnumber", out var v) && v > 0
            ? v.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : row.RowVersion;
    }

    private static async Task<List<(Guid Id, string? Scope, int? PreviousSequence)>> ReadRenumberedRowsAsync(IGenericEntityService entities, string invoiceNumber)
    {
        var found = await entities.RetrieveMultipleAsync(new QueryExpression(TrackingNumberAllocator.SubmissionEntity)
        {
            ColumnSet = new ColumnSet(TrackingNumberAllocator.ScopeColumn, TrackingNumberAllocator.PreviousSequenceColumn),
            Criteria = { Conditions = { new ConditionExpression(TrackingNumberAllocator.FirmInvoiceNumberColumn, ConditionOperator.Equal, invoiceNumber) } },
        });
        return found.Entities
            .Select(e => (e.Id, (string?)e.GetAttributeValue<string>(TrackingNumberAllocator.ScopeColumn), e.GetAttributeValue<int?>(TrackingNumberAllocator.PreviousSequenceColumn)))
            .ToList();
    }
}
