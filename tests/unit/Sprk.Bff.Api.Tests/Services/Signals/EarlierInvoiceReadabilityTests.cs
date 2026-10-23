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
/// caller reads, and the submission read evaluates the vendor org and the tracking-number In list (text compared without regard to
/// case, as Dataverse does), so a read that asks the wrong question gets the wrong rows.
/// </summary>
public sealed class EarlierInvoiceReadabilityTests
{
    private static readonly Guid Org = Guid.Parse("0f0f0f0f-0000-0000-0000-000000000001");
    private static readonly Guid OtherOrg = Guid.Parse("0f0f0f0f-0000-0000-0000-000000000002");

    private readonly HashSet<Guid> _readableInvoices = [];
    private readonly List<(Guid Org, string Number, Guid? Invoice)> _submissions = [];
    private readonly Dictionary<string, string> _faults = new(StringComparer.Ordinal);
    private readonly List<string> _paths = [];

    private static EarlierInvoiceReadability.Reference ById(Guid id) => new(id, null, null);

    private static EarlierInvoiceReadability.Reference ByNumber(string number, Guid? org = null) => new(null, org ?? Org, number);

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
                ["value"] = set == "sprk_invoices"
                    ? _readableInvoices.Select(id => new Dictionary<string, object?> { ["sprk_invoiceid"] = id }).ToList()
                    : Submissions(filter),
                ["@odata.nextLink"] = $"https://example.crm.dynamics.com/api/data/v9.2/{set}?$skiptoken=2",
            })));
        }

        object rows = set switch
        {
            "sprk_invoices" => Regex.Matches(filter, "'([0-9a-f-]{36})'").Select(m => Guid.Parse(m.Groups[1].Value))
                .Where(_readableInvoices.Contains)
                .Select(id => new Dictionary<string, object?> { ["sprk_invoiceid"] = id })
                .ToList(),
            "sprk_invoicesubmissions" => Submissions(filter),
            _ => throw new InvalidOperationException($"unexpected read {path}"),
        };
        return Task.FromResult(DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { value = rows })));
    }

    private List<Dictionary<string, object?>> Submissions(string filter)
    {
        // Like Dataverse: the number list is a JSON array (matched without regard to case); the vendor org list, when present, restricts the orgs.
        var numbers = InList(filter, "sprk_trackingnumber").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orgs = filter.Contains("PropertyName='sprk_vendororg'", StringComparison.Ordinal)
            ? InList(filter, "sprk_vendororg").Select(Guid.Parse).ToHashSet()
            : null;
        return _submissions.Where(s => (orgs is null || orgs.Contains(s.Org)) && numbers.Contains(s.Number))
            .Select(s => new Dictionary<string, object?>
            {
                ["sprk_trackingnumber"] = s.Number,
                ["_sprk_vendororg_value"] = s.Org,
                ["_sprk_invoice_value"] = s.Invoice,
            })
            .ToList();
    }

    /// <summary>The values of <c>In(PropertyName='{property}',PropertyValues=[...])</c>: single-quoted GUIDs, or JSON strings.</summary>
    private static List<string> InList(string filter, string property)
    {
        var marker = $"In(PropertyName='{property}',PropertyValues=";
        var array = filter[(filter.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
        if (array.Length > 1 && array[1] == '\'')
        {
            return Regex.Matches(array[..array.IndexOf(']', StringComparison.Ordinal)], "'([^']*)'").Select(m => m.Groups[1].Value).ToList();
        }

        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(array));
        using var doc = JsonDocument.ParseValue(ref reader);
        return doc.RootElement.EnumerateArray().Select(e => e.GetString()!).ToList();
    }

    private string SubmissionPath() => _paths.Single(p => p.StartsWith("sprk_invoicesubmissions?", StringComparison.Ordinal));

    private int Reads(string set) => _paths.Count(p => p.StartsWith(set + "?", StringComparison.Ordinal));

    [Fact]
    public async Task AnInvoiceId_IsReadable_OnlyWhenTheCallersInvoiceReadReturnsIt()
    {
        var readable = ById(Invoice(readable: true));
        var unreadable = ById(Invoice(readable: false));

        (await ReadableAsync(readable, unreadable)).Should().BeEquivalentTo([readable]);
        Reads("sprk_invoices").Should().Be(1);
        Reads("sprk_invoicesubmissions").Should().Be(0, "an id needs only the invoice read");
    }

    [Fact]
    public async Task ATrackingNumber_IsResolvedThroughTheOneSubmissionOfItsVendorOrg()
    {
        var readableInvoice = Invoice(readable: true);
        _submissions.Add((Org, "N-1", readableInvoice));
        _submissions.Add((Org, "N-2", Invoice(readable: false)));
        _submissions.Add((Org, "N-4", readableInvoice));
        _submissions.Add((Org, "n-4", readableInvoice)); // the same number to Dataverse: ambiguous
        _submissions.Add((Org, "N-5", null));
        _submissions.Add((OtherOrg, "N-6", readableInvoice));
        var kept = ByNumber("N-1");

        var readable = await ReadableAsync(kept, ByNumber("N-2"), ByNumber("N-3"), ByNumber("N-4"), ByNumber("N-5"), ByNumber("N-6"));
        readable.Should().BeEquivalentTo([kept], "an unreadable invoice, no submission, two, one without an invoice, or another firm's: never readable");
        Reads("sprk_invoicesubmissions").Should().Be(1);
    }

    [Fact]
    public async Task ATrackingNumberIsSentAsAJsonStringInTheInList_NotAnODataDoubledQuote()
    {
        // The number is firm-supplied text. The In list is parsed as a JSON array: a doubled quote (O''Brien) is a 400 on Dataverse, a
        // JSON string ("O'Brien") matches the row (both proven live on spaarkedev1, task 157 round 2). A quote or backslash is escaped.
        const string number = "O'Brien&Co #1.1";
        const string awkward = "say \"hi\"\\";
        _submissions.Add((Org, number, Invoice(readable: true)));
        _submissions.Add((Org, awkward, Invoice(readable: true)));

        (await ReadableAsync(ByNumber(number), ByNumber(awkward))).Should().HaveCount(2);

        var sent = _paths.Single(p => p.StartsWith("sprk_invoicesubmissions?", StringComparison.Ordinal));
        sent.Should().NotContain("O'Brien").And.NotContain("&Co", "the URL carries the escaped filter");
        var filter = Uri.UnescapeDataString(sent);
        filter.Should().Contain("\"O'Brien&Co #1.1\"").And.Contain("\"say \\\"hi\\\"\\\\\"").And.NotContain("''");
    }

    [Fact]
    public async Task AReferenceWithNeitherAnIdNorAVendorOrgAndNumber_IsNeverReadable_AndReadsNothing()
    {
        (await ReadableAsync(new EarlierInvoiceReadability.Reference(null, null, "N-1"), new EarlierInvoiceReadability.Reference(null, Org, " ")))
            .Should().BeEmpty();
        _paths.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sprk_invoices", "500")]
    [InlineData("sprk_invoices", "401")]
    [InlineData("sprk_invoices", "unparseable")]
    [InlineData("sprk_invoices", "throw")]
    [InlineData("sprk_invoicesubmissions", "500")]
    [InlineData("sprk_invoicesubmissions", "401")]
    [InlineData("sprk_invoicesubmissions", "unparseable")]
    [InlineData("sprk_invoicesubmissions", "throw")]
    [InlineData("sprk_invoices", "nextLink")]
    [InlineData("sprk_invoicesubmissions", "nextLink")]
    public async Task AFaultInEitherRead_MakesNothingReadable(string set, string fault)
    {
        var invoice = Invoice(readable: true);
        _submissions.Add((Org, "N-1", invoice));
        _faults[set] = fault;

        (await ReadableAsync(ById(invoice), ByNumber("N-1"))).Should().BeEmpty();
    }

    [Fact]
    public async Task A403OnTheSubmissionTable_LeavesNumbersUnresolved_ButStillJudgesIds()
    {
        var invoice = Invoice(readable: true);
        _submissions.Add((Org, "N-1", invoice));
        _faults["sprk_invoicesubmissions"] = "403";

        (await ReadableAsync(ById(invoice), ByNumber("N-1"))).Should().BeEquivalentTo([ById(invoice)]);
    }

    [Fact]
    public async Task A403OnTheInvoiceTable_MakesNoInvoiceReadable()
    {
        _faults["sprk_invoices"] = "403";

        (await ReadableAsync(ById(Invoice(readable: true)))).Should().BeEmpty();
    }

    [Fact]
    public async Task Reads_AreOneSubmissionReadPerChunkOf200Numbers_AcrossVendorOrgs_AndOneInvoiceRead()
    {
        _submissions.Add((Org, "A", Invoice(readable: true)));
        _submissions.Add((Org, "B", Invoice(readable: true)));
        _submissions.Add((OtherOrg, "C", Invoice(readable: true)));
        (await ReadableAsync(ByNumber("A"), ByNumber("B"), ByNumber("C", OtherOrg), ById(Invoice(readable: true)))).Should().HaveCount(4);
        Reads("sprk_invoicesubmissions").Should().Be(1, "two vendor orgs, one read");
        Reads("sprk_invoices").Should().Be(1);

        // The same number in two orgs is ONE value in the list; the exact (org, number) pair is matched after the read.
        _paths.Clear();
        _submissions.Add((Org, "S", Invoice(readable: true)));
        _submissions.Add((OtherOrg, "S", Invoice(readable: false)));
        (await ReadableAsync(ByNumber("S"), ByNumber("S", OtherOrg))).Should().BeEquivalentTo([ByNumber("S")]);
        Reads("sprk_invoicesubmissions").Should().Be(1);

        _paths.Clear();
        var many = Enumerable.Range(0, 201).Select(_ => ById(Invoice(readable: true))).ToArray();
        (await ReadableAsync(many)).Should().HaveCount(201);
        _paths.Select(p => Regex.Matches(p, "'[0-9a-f-]{36}'").Count).Should().BeEquivalentTo([200, 1]);

        _paths.Clear();
        var numbers = Enumerable.Range(0, 201).Select(i => $"N-{i}").ToList();
        foreach (var n in numbers) _submissions.Add((Org, n, Invoice(readable: true)));
        (await ReadableAsync(numbers.Select(n => ByNumber(n)).ToArray())).Should().HaveCount(201);
        Reads("sprk_invoicesubmissions").Should().Be(2, "201 numbers: chunks of 200 and 1");
    }

    [Fact]
    public async Task AChunkNamingMoreThan20Orgs_IsReadInGroupsOf20Orgs_EachFilteredOnItsOrgs_AndThePairIsStillMatchedExactly()
    {
        var orgs = Enumerable.Range(0, EarlierInvoiceReadability.OrgsPerRead + 1).Select(_ => Guid.NewGuid()).ToList();
        foreach (var org in orgs) _submissions.Add((org, "SAME", Invoice(readable: true)));
        _submissions.Add((OtherOrg, "SAME", Invoice(readable: true))); // a firm nobody asked about
        _submissions.Add((orgs[^1], "ONLY-IN-LAST", Invoice(readable: true)));

        var readable = await ReadableAsync([.. orgs.Select(o => ByNumber("SAME", o)), ByNumber("ONLY-IN-LAST", orgs[^1])]);

        readable.Should().HaveCount(orgs.Count + 1);
        var reads = _paths.Where(p => p.StartsWith("sprk_invoicesubmissions?", StringComparison.Ordinal)).Select(Uri.UnescapeDataString).ToList();
        reads.Should().HaveCount(2, "21 orgs: groups of 20 and 1");
        reads.Should().AllSatisfy(r => r.Should().Contain("PropertyName='sprk_vendororg'", "every read names its orgs, so a page cannot truncate"));
        reads.Select(r => InList(r, "sprk_vendororg").Count).Should().BeEquivalentTo([20, 1]);
        reads.Single(r => InList(r, "sprk_vendororg").Count == 1).Should().Contain("ONLY-IN-LAST");
        reads.Single(r => InList(r, "sprk_vendororg").Count == 20).Should().NotContain("ONLY-IN-LAST", "a group asks only its own orgs' numbers");
    }

    [Fact]
    public async Task TheVendorOrgFilterNamesTheOrgsOfTheChunk_WhenThereAreAtMost20()
    {
        _submissions.Add((Org, "A", Invoice(readable: true)));

        (await ReadableAsync(ByNumber("A"))).Should().HaveCount(1);
        Uri.UnescapeDataString(SubmissionPath()).Should().Contain($"PropertyName='sprk_vendororg',PropertyValues=['{Org:D}']");
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
        var invoice = Invoice(readable: true);
        _submissions.Add((Org, "N-1", Invoice(readable: true)));

        var shown = await ShownAsync(new() { ["both"] = [ById(invoice), ByNumber("N-1")], ["one"] = [ById(Invoice(readable: true))] });

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
        var shown = await ShownAsync(new() { ["bad"] = [ById(Invoice(readable: true)), new EarlierInvoiceReadability.Reference(null, null, "N-1")] });

        shown.Should().BeEmpty();
        _paths.Should().BeEmpty("an item that cannot be shown needs no read");
    }

    [Theory]
    [InlineData("sprk_invoices", "500")]
    [InlineData("sprk_invoices", "401")]
    [InlineData("sprk_invoices", "unparseable")]
    [InlineData("sprk_invoices", "throw")]
    [InlineData("sprk_invoicesubmissions", "500")]
    [InlineData("sprk_invoicesubmissions", "throw")]
    public async Task ShownAsync_OnAFault_NothingIsShown(string set, string fault)
    {
        _submissions.Add((Org, "N-1", Invoice(readable: true)));
        _faults[set] = fault;

        var shown = await ShownAsync(new() { ["byId"] = [ById(Invoice(readable: true))], ["byNumber"] = [ByNumber("N-1")] });

        shown.Should().BeEmpty();
    }
}
