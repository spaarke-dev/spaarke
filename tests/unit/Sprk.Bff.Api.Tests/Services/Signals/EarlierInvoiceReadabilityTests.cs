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
        if (_faults.TryGetValue(set, out var fault))
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
        var org = Guid.Parse(Regex.Match(filter, "_sprk_vendororg_value eq ([0-9a-f-]{36})").Groups[1].Value);
        var values = Regex.Match(filter, @"PropertyValues=\[(.*)\]\)$").Groups[1].Value;
        var numbers = Regex.Matches(values, "'((?:[^']|'')*)'").Select(m => m.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _submissions.Where(s => s.Org == org && numbers.Contains(s.Number))
            .Select(s => new Dictionary<string, object?>
            {
                ["sprk_invoicesubmissionid"] = Guid.NewGuid(),
                ["sprk_trackingnumber"] = s.Number,
                ["_sprk_vendororg_value"] = s.Org,
                ["_sprk_invoice_value"] = s.Invoice,
            })
            .ToList();
    }

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
    public async Task ATrackingNumberIsSentAsAnEscapedODataLiteral()
    {
        // The number is firm-supplied text: a quote must not end the literal (Justification: the filter is built from stored text).
        const string number = "O'Brien&Co #1.1";
        _submissions.Add((Org, number, Invoice(readable: true)));

        (await ReadableAsync(ByNumber(number))).Should().HaveCount(1);
        _paths.Single(p => p.StartsWith("sprk_invoicesubmissions?", StringComparison.Ordinal)).Should().NotContain("O'Brien").And.NotContain("&Co");
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
    public async Task Reads_AreOneSubmissionReadPerVendorOrg_AndOneInvoiceRead_InListsOfAtMost200()
    {
        _submissions.Add((Org, "A", Invoice(readable: true)));
        _submissions.Add((Org, "B", Invoice(readable: true)));
        _submissions.Add((OtherOrg, "C", Invoice(readable: true)));
        (await ReadableAsync(ByNumber("A"), ByNumber("B"), ByNumber("C", OtherOrg), ById(Invoice(readable: true)))).Should().HaveCount(4);
        Reads("sprk_invoicesubmissions").Should().Be(2);
        Reads("sprk_invoices").Should().Be(1);

        _paths.Clear();
        var many = Enumerable.Range(0, 201).Select(_ => ById(Invoice(readable: true))).ToArray();
        (await ReadableAsync(many)).Should().HaveCount(201);
        _paths.Select(p => Regex.Matches(p, "'[0-9a-f-]{36}'").Count).Should().BeEquivalentTo([200, 1]);
    }
}
