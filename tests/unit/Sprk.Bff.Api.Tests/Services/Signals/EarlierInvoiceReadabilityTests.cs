using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// Ontology task 157 (D-176, #2118): <see cref="EarlierInvoiceReadability"/>, the earlier-invoice check the worklist read and ebilling
/// task 119's invoice review share. The caller's Dataverse is an EVALUATING double: the invoice read returns the In-listed ids the
/// caller reads, so a read that asks the wrong question gets the wrong rows. #2294 / DEF-043: no tracking number is ever looked up
/// (<see cref="EarlierInvoiceReadability.ReferencesOf"/>); any other read fails the test.
/// </summary>
public sealed class EarlierInvoiceReadabilityTests
{
    private readonly HashSet<Guid> _readableInvoices = [];
    private readonly Dictionary<string, string> _faults = new(StringComparer.Ordinal);
    private readonly List<string> _paths = [];

    private static EarlierInvoiceReadability.Reference ById(Guid id) => new(id);

    private static readonly EarlierInvoiceReadability.Reference Unusable = new(null);

    private Guid Invoice(bool readable)
    {
        var id = Guid.NewGuid();
        if (readable) _readableInvoices.Add(id);
        return id;
    }

    private Task<IReadOnlySet<EarlierInvoiceReadability.Reference>> ReadableAsync(params EarlierInvoiceReadability.Reference[] references)
    {
        var caller = new Mock<IDataverseUserClient>(MockBehavior.Strict);
        caller.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string path, CancellationToken _) => Answer(path));
        return new EarlierInvoiceReadability(NullLogger<EarlierInvoiceReadability>.Instance).ReadableAsync(caller.Object, references, CancellationToken.None);
    }

    private Task<DataverseUserResponse> Answer(string path)
    {
        _paths.Add(path);
        var set = path[..path.IndexOf('?')];
        if (_faults.TryGetValue(set, out var fault) && fault != "nextLink")
        {
            return fault switch
            {
                "500" => Task.FromResult(DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "boom")),
                "401" => Task.FromResult(DataverseUserResponse.Fail(401, DataverseUserClientErrorCodes.AccessDenied, "token expired")),
                "403" => Task.FromResult(DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "denied")),
                "unparseable" => Task.FromResult(DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { rows = 1 }))),
                "throw" => Task.FromException<DataverseUserResponse>(new TimeoutException("read timed out")),
                _ => throw new ArgumentOutOfRangeException(nameof(path), fault, "unknown fault"),
            };
        }

        var filter = Uri.UnescapeDataString(path[(path.IndexOf("$filter=", StringComparison.Ordinal) + 8)..]);
        if (fault == "nextLink")
        {
            // A truncated page: the rows it holds are right, but more follow.
            return Task.FromResult(DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new Dictionary<string, object>
            {
                ["value"] = _readableInvoices.Select(id => new Dictionary<string, object?> { ["sprk_invoiceid"] = id }).ToList(),
                ["@odata.nextLink"] = $"https://example.crm.dynamics.com/api/data/v9.2/{set}?$skiptoken=2",
            })));
        }

        object rows = set switch
        {
            "sprk_invoices" => Regex.Matches(filter, "'([0-9a-f-]{36})'").Select(m => Guid.Parse(m.Groups[1].Value))
                .Where(_readableInvoices.Contains)
                .Select(id => new Dictionary<string, object?> { ["sprk_invoiceid"] = id })
                .ToList(),
            _ => throw new InvalidOperationException($"unexpected read {path}"),
        };
        return Task.FromResult(DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { value = rows })));
    }

    private int Reads(string set) => _paths.Count(p => p.StartsWith(set + "?", StringComparison.Ordinal));

    [Fact]
    public async Task AnInvoiceId_IsReadable_OnlyWhenTheCallersInvoiceReadReturnsIt()
    {
        var readable = ById(Invoice(readable: true));
        var unreadable = ById(Invoice(readable: false));

        (await ReadableAsync(readable, unreadable)).Should().BeEquivalentTo([readable]);
        Reads("sprk_invoices").Should().Be(1);
        _paths.Should().OnlyContain(p => p.StartsWith("sprk_invoices?", StringComparison.Ordinal), "an id needs only the invoice read");
    }

    [Fact]
    public async Task AReferenceWithNoInvoiceId_IsNeverReadable_AndReadsNothing()
    {
        (await ReadableAsync(Unusable, ById(Guid.Empty))).Should().BeEmpty();
        _paths.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sprk_invoices", "500")]
    [InlineData("sprk_invoices", "401")]
    [InlineData("sprk_invoices", "unparseable")]
    [InlineData("sprk_invoices", "throw")]
    [InlineData("sprk_invoices", "nextLink")]
    public async Task AFaultInTheInvoiceRead_MakesNothingReadable(string set, string fault)
    {
        _faults[set] = fault;

        (await ReadableAsync(ById(Invoice(readable: true)), ById(Invoice(readable: true)))).Should().BeEmpty();
    }

    [Fact]
    public async Task A403OnTheInvoiceTable_MakesNoInvoiceReadable()
    {
        _faults["sprk_invoices"] = "403";

        (await ReadableAsync(ById(Invoice(readable: true)))).Should().BeEmpty();
    }

    [Fact]
    public async Task Reads_AreOneInvoiceReadPerChunkOf200Ids()
    {
        var many = Enumerable.Range(0, 201).Select(_ => ById(Invoice(readable: true))).ToArray();
        (await ReadableAsync(many)).Should().HaveCount(201);
        _paths.Select(p => Regex.Matches(p, "'[0-9a-f-]{36}'").Count).Should().BeEquivalentTo([200, 1]);
    }

    // ---- ReferencesOf: what ONE stored Duplicate finding is judged by (#2294 / DEF-043) ----

    [Fact]
    public void ReferencesOf_AFindingQuotingInvoiceIds_IsJudgedByEachDistinctId_Unchanged()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        EarlierInvoiceReadability.ReferencesOf([a, b, a, Guid.Empty], sameInvoiceRepeat: false, currentInvoiceId: null)
            .Should().Equal(ById(a), ById(b));
        EarlierInvoiceReadability.ReferencesOf([a], sameInvoiceRepeat: true, currentInvoiceId: Guid.NewGuid())
            .Should().Equal([ById(a)], "an id wins even on a same-invoice repeat");
    }

    [Fact]
    public void ReferencesOf_ANumberOnlyFinding_IdentifiesNothing_WhateverTheCurrentInvoice()
    {
        // Must fire: a cross-invoice Duplicate identified only by its tracking number is withheld; no number is ever looked up.
        EarlierInvoiceReadability.ReferencesOf([], sameInvoiceRepeat: false, currentInvoiceId: Guid.NewGuid())
            .Should().Equal([Unusable]);
        EarlierInvoiceReadability.ReferencesOf([], sameInvoiceRepeat: false, currentInvoiceId: null).Should().Equal([Unusable]);
    }

    [Fact]
    public void ReferencesOf_ASameInvoiceRepeat_IsJudgedByItsOwnCurrentInvoice_AndWithheldWithoutOne()
    {
        var current = Guid.NewGuid();

        EarlierInvoiceReadability.ReferencesOf([], sameInvoiceRepeat: true, currentInvoiceId: current).Should().Equal([ById(current)]);
        EarlierInvoiceReadability.ReferencesOf([], sameInvoiceRepeat: true, currentInvoiceId: null).Should().Equal([Unusable]);
        EarlierInvoiceReadability.ReferencesOf([], sameInvoiceRepeat: true, currentInvoiceId: Guid.Empty).Should().Equal([Unusable]);
    }

    [Theory]
    [InlineData("duplicate.line_same_invoice", true)]
    [InlineData("duplicate.line_prior_submission", false)]
    [InlineData("duplicate.invoice_number", false)]
    [InlineData("DUPLICATE.LINE_SAME_INVOICE", false)]
    [InlineData(null, false)]
    public void IsSameInvoiceRepeat_IsExactlyEbillingsSameInvoiceReasonCode(string? reason, bool expected)
    {
        EarlierInvoiceReadability.IsSameInvoiceRepeat(reason).Should().Be(expected);
        Sprk.Bff.Api.Services.Signals.Billing.BillingReasonCodes.DuplicateOfEarlierLine.Should().Be("duplicate.line_same_invoice");
    }

    // ---- ShownAsync: the ONE combining rule (an item is shown only with at least one reference, every one readable) ----

    private Task<IReadOnlySet<string>> ShownAsync(Dictionary<string, IReadOnlyCollection<EarlierInvoiceReadability.Reference>> refsByItem)
    {
        var caller = new Mock<IDataverseUserClient>(MockBehavior.Strict);
        caller.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string path, CancellationToken _) => Answer(path));
        return new EarlierInvoiceReadability(NullLogger<EarlierInvoiceReadability>.Instance)
            .ShownAsync(caller.Object, refsByItem, CancellationToken.None);
    }

    [Fact]
    public async Task ShownAsync_AnItemWithNoReferences_IsNotShown_AndCausesNoRead()
    {
        var shown = await ShownAsync(new() { ["empty"] = [] });

        shown.Should().BeEmpty("an item with no references is NOT shown");
        _paths.Should().BeEmpty();
    }

    [Fact]
    public async Task ShownAsync_AnItemWhoseReferencesAreAllReadable_IsShown()
    {
        var shown = await ShownAsync(new() { ["both"] = [ById(Invoice(readable: true)), ById(Invoice(readable: true))], ["one"] = [ById(Invoice(readable: true))] });

        shown.Should().BeEquivalentTo(["both", "one"]);
    }

    [Fact]
    public async Task ShownAsync_AnItemWithOneUnreadableReference_IsNotShown_AndOthersAre()
    {
        var shown = await ShownAsync(new()
        {
            ["mixed"] = [ById(Invoice(readable: true)), ById(Invoice(readable: false))],
            ["clear"] = [ById(Invoice(readable: true))],
        });

        shown.Should().BeEquivalentTo(["clear"]);
    }

    [Fact]
    public async Task ShownAsync_AnItemWithAnUnusableReference_IsNotShown_AndCausesNoRead()
    {
        var shown = await ShownAsync(new() { ["bad"] = [ById(Invoice(readable: true)), Unusable] });

        shown.Should().BeEmpty();
        _paths.Should().BeEmpty("an item that cannot be shown needs no read");
    }

    [Theory]
    [InlineData("sprk_invoices", "500")]
    [InlineData("sprk_invoices", "401")]
    [InlineData("sprk_invoices", "unparseable")]
    [InlineData("sprk_invoices", "throw")]
    public async Task ShownAsync_OnAFault_NothingIsShown(string set, string fault)
    {
        _faults[set] = fault;

        var shown = await ShownAsync(new() { ["a"] = [ById(Invoice(readable: true))], ["b"] = [ById(Invoice(readable: true))] });

        shown.Should().BeEmpty();
    }
}
