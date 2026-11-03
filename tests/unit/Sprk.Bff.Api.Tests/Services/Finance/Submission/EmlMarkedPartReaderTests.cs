using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Office;
using Xunit;
using W = Sprk.Bff.Api.Tests.Services.Finance.Submission.FromEmailWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// <see cref="EmlMarkedPartReader"/> (ebilling-r1 task 095, model §9.1 step 3, uac-r2 F2-c). It is the submission job's byte read of a
/// from-email source: one read of the <c>.eml</c> row, then SPE. Each marked part is checked at its ordinal, with its name and its
/// SHA-256. When the save did not finish, the outcome is <c>email_save_failed</c>. A mismatch is <c>document_unavailable</c>. A fault
/// is retried. The logs never carry a file name.
/// </summary>
public class EmlMarkedPartReaderTests
{
    private readonly W _world = new();
    private readonly Mock<IGenericEntityService> _rows = new();
    private readonly CapturingLogger _log = new();
    private static readonly Guid SubmissionId = Guid.NewGuid();

    private EmlMarkedPartReader Reader() => new(_rows.Object, _world.Spe.Object, _log);

    private Entity StoreEml(params Sprk.Bff.Api.Models.Office.AttachmentReference[] attachments)
    {
        var row = _world.AddEarlierEml(W.EmlId, null, attachments);
        row["versionnumber"] = 4711L;
        _rows.Setup(r => r.RetrieveAsync("sprk_document", W.EmlId, It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(row);
        return row;
    }

    private static FromEmailMarkedPart Part(int ordinal, string name, byte[] bytes, string mark) => new(ordinal, name, EmlMimeParts.Sha256Hex(bytes), mark);

    private static readonly FromEmailMarkedPart[] Marks =
    {
        Part(0, "Invoice 1001.pdf", W.PdfBytes, "Invoice"),
        Part(1, "Timesheet.xlsx", W.XlsxBytes, "Supporting"),
    };

    [Fact]
    public async Task Read_EachMarkedPartAtItsOrdinal_ReturnsItsBytes_AndTheRowVersion()
    {
        StoreEml(W.Invoice, W.Timesheet);

        var read = await Reader().ReadAsync(SubmissionId, W.EmlId, Marks, CancellationToken.None);

        read.Outcome.Should().Be(EmlMarkedPartReadOutcome.Read);
        read.RowVersion.Should().Be(4711L);
        read.Parts.Select(p => Encoding.ASCII.GetString(p.Content)).Should().Equal(
            Encoding.ASCII.GetString(W.PdfBytes), Encoding.ASCII.GetString(W.XlsxBytes));
    }

    private static FromEmailMarkedPart Mismatch(string kind) => kind switch
    {
        "ordinal" => Marks[0] with { Ordinal = 1 },
        "missing ordinal" => Marks[0] with { Ordinal = 5 },
        "name" => Marks[0] with { FileName = "Invoice 1002.pdf" },
        _ => Marks[0] with { Sha256 = new string('a', 64) },
    };

    [Theory]
    [InlineData("ordinal")]
    [InlineData("missing ordinal")]
    [InlineData("name")]
    [InlineData("sha")]
    public async Task Read_APartThatDiffersFromItsMark_IsDocumentUnavailable_AndLogsNoFileName(string kind)
    {
        var mark = Mismatch(kind);
        StoreEml(W.Invoice, W.Timesheet);

        var read = await Reader().ReadAsync(SubmissionId, W.EmlId, new[] { mark }, CancellationToken.None);

        read.Outcome.Should().Be(EmlMarkedPartReadOutcome.DocumentUnavailable);
        read.Parts.Should().BeEmpty();
        _log.Messages.Should().NotBeEmpty().And.OnlyContain(m => !m.Contains("Invoice 1001") && !m.Contains("Invoice 1002") && !m.Contains(".pdf"));
    }

    [Fact]
    public async Task Read_TheRowIsGone_IsEmailSaveFailed()
    {
        _rows.Setup(r => r.RetrieveAsync("sprk_document", W.EmlId, It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ThrowsAsync(W.NotFound());

        (await Reader().ReadAsync(SubmissionId, W.EmlId, Marks, CancellationToken.None)).Outcome.Should().Be(EmlMarkedPartReadOutcome.EmailSaveFailed);
    }

    [Fact]
    public async Task Read_TheRowHasNoFile_IsEmailSaveFailed()
    {
        _rows.Setup(r => r.RetrieveAsync("sprk_document", W.EmlId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", W.EmlId));

        (await Reader().ReadAsync(SubmissionId, W.EmlId, Marks, CancellationToken.None)).Outcome.Should().Be(EmlMarkedPartReadOutcome.EmailSaveFailed);
    }

    [Fact]
    public async Task Read_TheFileIsNotInStorage_IsEmailSaveFailed()
    {
        StoreEml(W.Invoice, W.Timesheet);
        _world.Files.Clear();

        (await Reader().ReadAsync(SubmissionId, W.EmlId, Marks, CancellationToken.None)).Outcome.Should().Be(EmlMarkedPartReadOutcome.EmailSaveFailed);
    }

    [Fact]
    public async Task Read_TheFileIsNotAMimeMessage_IsDocumentUnavailable()
    {
        var row = StoreEml(W.Invoice, W.Timesheet);
        _world.Files[(row.GetAttributeValue<string>("sprk_graphdriveid"), row.GetAttributeValue<string>("sprk_graphitemid"))] = Array.Empty<byte>();

        (await Reader().ReadAsync(SubmissionId, W.EmlId, Marks, CancellationToken.None)).Outcome.Should().Be(EmlMarkedPartReadOutcome.DocumentUnavailable);
    }

    [Fact]
    public async Task Read_ADataverseFault_IsTransient()
    {
        _rows.Setup(r => r.RetrieveAsync("sprk_document", W.EmlId, It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());

        (await Reader().ReadAsync(SubmissionId, W.EmlId, Marks, CancellationToken.None)).Outcome.Should().Be(EmlMarkedPartReadOutcome.Transient);
    }

    [Fact]
    public async Task Read_AStorageFault_IsTransient()
    {
        StoreEml(W.Invoice, W.Timesheet);
        _world.Spe.Setup(s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("503"));

        (await Reader().ReadAsync(SubmissionId, W.EmlId, Marks, CancellationToken.None)).Outcome.Should().Be(EmlMarkedPartReadOutcome.Transient);
    }

    private sealed class CapturingLogger : ILogger<EmlMarkedPartReader>
    {
        public readonly List<string> Messages = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
