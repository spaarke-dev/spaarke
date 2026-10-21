using System.Net;
using Moq;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// unified-access-control-r2 task 188 (main-session decision 2026-10-20): the APP-ONLY world the assistant tools' document
/// narrowing reads. <see cref="Narrowing"/> is the REAL <see cref="ExternalDataService.ReadAndNarrowDocumentsAsync"/> over
/// <see cref="ExternalRootEventReadTests.FakeDataverse"/> (by-key reads answered from seeded rows, projected through
/// <c>$select</c>), so a handler test seeds what the BFF's own identity reads, apart from what the user's mocked client
/// returns. A test that gives the user's read a masked (null) link proves the decision never reads the user's columns.
/// </summary>
internal sealed class AppOnlyDocumentWorld
{
    private readonly ExternalRootEventReadTests.FakeDataverse _dataverse = new();
    private readonly ExternalDataService _service;
    private int _calls;

    public AppOnlyDocumentWorld() => _service = _dataverse.NewService(withFilingReader: false);

    /// <summary>The narrowing the handlers take (counts its calls).</summary>
    public AppOnlyDocumentNarrowing Narrowing => (documentIds, ct) =>
    {
        Interlocked.Increment(ref _calls);
        return _service.ReadAndNarrowDocumentsAsync(documentIds, ct);
    };

    /// <summary>How many times a handler asked for the narrowing.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Every request the app-only reader sent (unescaped).</summary>
    public IReadOnlyList<string> AllReads => _dataverse.Urls.ToList();

    /// <summary>The by-key reads sent to <paramref name="set"/> (<c>sprk_documents</c>, <c>sprk_communications</c>, <c>sprk_communicationthreads</c>).</summary>
    public IReadOnlyList<string> Reads(string set) => _dataverse.KeyReads(set);

    /// <summary>Entity sets whose app-only by-key reads answer this status.</summary>
    public Dictionary<string, HttpStatusCode> Faults => _dataverse.KeyReadFaults;

    /// <summary>A document as the BFF reads it app-only (<paramref name="rawCommunication"/>: an unreadable link value).</summary>
    public Guid Document(Guid id, int? permission = null, Guid? communication = null, Guid? parent = null, object? rawCommunication = null)
    {
        _dataverse.AddAncestorDocument(id, permission, communication, parent, rawCommunication);
        return id;
    }

    /// <summary>A communication as the 065 rule reads it (<c>null</c> flag or thread: hidden).</summary>
    public void Communication(Guid id, bool? internalOnly, Guid? thread) => _dataverse.AddCommunication(id, internalOnly, thread);

    /// <summary>A thread with its privacy (Open = 100000000, Private = 100000001).</summary>
    public void Thread(Guid id, int privacy) => _dataverse.AddThread(id, privacy);
}

/// <summary>Task 188: assertions and stand-ins shared by the assistant tools' document narrowing tests.</summary>
internal static class DocumentNarrowingReads
{
    /// <summary>For a handler a test never lets narrow (an internal caller, a metadata-only test): any call fails the test.</summary>
    public static AppOnlyDocumentNarrowing NeverCalled { get; } = (_, _) =>
        throw new InvalidOperationException("The document narrowing was not expected to run (test).");

    /// <summary>
    /// The user's own client was never asked for a read the narrowing makes (a by-key document read, the communications, the
    /// threads): every one of them runs app-only.
    /// </summary>
    public static void VerifyNoNarrowingReadAsTheUser(Mock<IDataverseUserClient> dataverse)
    {
        dataverse.Verify(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communications?")), It.IsAny<CancellationToken>()), Times.Never,
            "the communications are read app-only");
        dataverse.Verify(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_communicationthreads?")), It.IsAny<CancellationToken>()), Times.Never,
            "the threads are read app-only");
        dataverse.Verify(d => d.GetAsync(It.Is<string>(p => p.StartsWith("sprk_documents?") && p.Contains("sprk_documentid eq ")), It.IsAny<CancellationToken>()), Times.Never,
            "the documents and their ancestors are read by id app-only");
    }
}
