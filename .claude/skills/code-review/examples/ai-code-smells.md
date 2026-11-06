# AI Code Smells — reasoning, detection and examples

Read by `code-review` Step 5.5 when you are unsure whether a pattern matches a smell, or whether it is an allowed exception. The rule for each smell (what to flag, severity, action) is inline in Step 5.5; this file holds the full reasoning, detection steps and BAD/GOOD code, moved verbatim from the pre-2026-11-03 SKILL.md.

## Contents

- **Smell 1: Interfaces with Single Implementations** — ADR-010; the two allowed seams
- **Smell 2: Try/Catch Log-Rethrow** — when a catch adds nothing, and when wrapping is fine
- **Smell 3: Null Checks on Non-Nullable Types** — NRT; constructor guards; TypeScript equivalents
- **Smell 4: Code-Restating Comments** — "what" comments vs "why" comments
- **Smell 5: Methods with More Than Three Responsibilities** — concern counting; orchestration exception
- **Report table** — the AI Code Smell Detection output format

The severity for Smell 5 is the one in Step 5.5 (Suggestion by default, Warning when the responsibilities diverge, never Critical on a count).

## Smell 1: Interfaces with Single Implementations

```
Relevant ADR: ADR-010 (DI Minimalism -- register concretes by default)

WHY: AI models default to "best practice" patterns like interface-per-class.
  In Spaarke, ADR-010 explicitly forbids this unless a genuine seam exists
  (only 2 allowed seams).

DETECTION (C#):
  1. Find interface declarations:
     grep -rn "interface I[A-Z]" {file}
  2. For each interface found, search codebase for implementations:
     grep -rn "class .* : .*I{InterfaceName}" --include="*.cs"
  3. FLAG if only ONE implementation exists

DETECTION (TypeScript):
  1. Find interface declarations:
     grep -rn "interface I[A-Z]" {file}
  2. Check if interface is used for DI or just type safety
  3. FLAG if interface wraps a single concrete class/service

EXAMPLE:
  // BAD - AI SMELL: Interface with single implementation
  public interface IDocumentProcessor { Task ProcessAsync(Document doc); }
  public class DocumentProcessor : IDocumentProcessor { ... }
  services.AddSingleton<IDocumentProcessor, DocumentProcessor>();

  // GOOD: Register concrete (ADR-010)
  public class DocumentProcessor { ... }
  services.AddSingleton<DocumentProcessor>();

SEVERITY: Warning
ACTION: Remove interface, register concrete. Exception: if interface is
  in Allowed Seams list (IAccessDataSource, IAuthorizationRule).
```

## Smell 2: Try/Catch Log-Rethrow

```
Relevant: C# best practices -- redundant exception handling

WHY: AI models add try/catch blocks defensively. Catching an exception only
  to log it and rethrow adds no value -- the caller or global exception
  middleware will log it. It also pollutes stack traces and duplicates log
  entries.

DETECTION (C#):
  Look for this pattern (multiline):
    catch (Exception ex)
    {
        _logger.Log*(... ex ...);   // any log call mentioning ex
        throw;                      // rethrow same exception
    }

  grep -A 3 "catch.*Exception" {file} | grep -B 1 "throw;"

DETECTION (TypeScript):
  catch (error) {
    console.error(error);  // or logger.error(error)
    throw error;           // rethrow same error
  }

EXAMPLE:
  // BAD - AI SMELL: Catch-log-rethrow (redundant)
  try { await _store.GetDocumentAsync(id); }
  catch (Exception ex)
  {
      _logger.LogError(ex, "Failed to get document {Id}", id);
      throw;  // Caller or middleware will log this anyway
  }

  // GOOD: Let it propagate (middleware handles logging)
  await _store.GetDocumentAsync(id);

  // ALSO GOOD: Catch to add context, wrap in domain exception
  try { await _store.GetDocumentAsync(id); }
  catch (Exception ex)
  {
      throw new DocumentAccessException("Document not found", ex);
  }

SEVERITY: Warning
ACTION: Remove try/catch unless it adds context (wraps in domain
  exception) or performs recovery logic beyond just logging.
```

## Smell 3: Null Checks on Non-Nullable Types

```
Relevant: C# nullable reference types (NRT) -- project-wide enabled

WHY: AI models add defensive null checks even when the type system guarantees
  non-null. With NRT enabled in Spaarke, a parameter of type string (not
  string?) is guaranteed non-null by the compiler. Checking it adds noise
  and implies the type annotation is wrong.

DETECTION (C#):
  1. Find null checks:
     grep -n "if.*== *null" {file}
     grep -n "if.*is null" {file}
     grep -n "??" {file}
     grep -n "?\." {file}   (null-conditional)

  2. For each null check, verify the variable declared type:
     - If type is non-nullable (e.g., string, Document, int):
       FLAG as AI smell
     - If type is nullable (e.g., string?, Document?, int?):
       SKIP -- null check is appropriate

  3. Special case -- constructor parameter guards:
     ArgumentNullException.ThrowIfNull(param)
     If param type is non-nullable -> FLAG (compiler already prevents null)

DETECTION (TypeScript):
  1. Find null/undefined checks on typed parameters:
     if (param !== null && param !== undefined)
     if (param != null)
  2. Check if parameter has non-nullable type annotation
  3. FLAG if type does not include | null or | undefined

EXAMPLE:
  // BAD - AI SMELL: Null check on non-nullable type
  public async Task<Document> GetAsync(string id)  // string, not string?
  {
      if (id == null) throw new ArgumentNullException(nameof(id));
      // With NRT enabled, compiler prevents null from reaching here
  }

  // GOOD: Trust the type system
  public async Task<Document> GetAsync(string id)
  {
      var result = await _store.GetAsync(id);
      return result;
  }

  // GOOD: Null check on nullable type
  public async Task<Document?> FindAsync(string? id)
  {
      if (id is null) return null;  // Appropriate -- type is nullable
  }

SEVERITY: Suggestion
ACTION: Remove null check if type is non-nullable. If null IS possible,
  update the type annotation to nullable instead.
```

## Smell 4: Code-Restating Comments

```
Relevant: Clean Code principles -- comments should explain "why", not "what"

WHY: AI models produce comments that restate what the code already says.
  These comments add visual noise without adding information. Good comments
  explain WHY something is done, not WHAT is being done.

DETECTION (C# and TypeScript):
  Look for comments where the comment text mirrors the next line of code:

  Pattern indicators:
  - Comment contains the method/variable name being called
  - Comment uses words like "get", "set", "create", "initialize", "return"
    that match the operation on the next line
  - Comment describes a single obvious operation

  Manual review -- read each comment and ask:
    "Does this tell me something I cannot already read from the code?"
    If no -> FLAG

EXAMPLE:
  // BAD - AI SMELL: Code-restating comments
  // Get the document by ID
  var document = await _store.GetDocumentAsync(id);

  // Initialize the list of results
  var results = new List<SearchResult>();

  // Return the response
  return Ok(response);

  // Set the status to active
  entity.Status = StatusCode.Active;

  // GOOD: Comments that add value
  // Graph API returns 404 for soft-deleted items; treat as "not found"
  var document = await _store.GetDocumentAsync(id);

  // Pre-allocate based on typical result set size to avoid resizing
  var results = new List<SearchResult>(capacity: 50);

  // Must return 200 even on empty results -- client polling depends on it
  return Ok(response);

SEVERITY: Suggestion
ACTION: Remove the comment if it restates the code. Keep comments that
  explain business rules, edge cases, workarounds, or non-obvious decisions.
```

## Smell 5: Methods with More Than Three Responsibilities

```
Relevant: Single Responsibility Principle (SRP)

WHY: AI models generate "god methods" that handle multiple unrelated
  concerns in sequence. These methods are hard to test, hard to name,
  and tend to grow over time. A method should do ONE thing.

DETECTION:
  1. Method name analysis:
     - Name contains "And", "Or", "Also", "Then" (e.g., ValidateAndSave)
     - Name is very generic (e.g., ProcessRequest, HandleData, DoWork)
     FLAG if method name suggests multiple operations

  2. Responsibility counting (manual review):
     Read the method body and identify distinct concerns:
     - Input validation
     - Data retrieval / API calls
     - Business logic / transformation
     - Persistence / saving
     - Notification / eventing
     - Logging / telemetry
     - Error handling (beyond simple try/catch)
     FLAG if method contains > 3 of these concerns

  3. Structural indicators:
     - Method has multiple "sections" separated by blank lines
     - Method exceeds 30 lines (from Step 5 threshold)
     - Method has comments acting as section headers
       (e.g., "// Step 1: Validate", "// Step 2: Transform")

  grep -n "And\|Or\|Also\|Then" {file} | grep "public\|private\|async"

EXAMPLE:
  // BAD - AI SMELL: Method with 5 responsibilities
  public async Task<ActionResult> CreateAndProcessDocument(CreateRequest req)
  {
      // Validate input
      if (!ModelState.IsValid) return BadRequest();

      // Create document in SharePoint
      var doc = await _store.CreateAsync(req.ToDocument());

      // Process with AI pipeline
      var summary = await _aiService.SummarizeAsync(doc.Content);

      // Save metadata to Dataverse
      await _dataverse.UpdateAsync(doc.Id, new { Summary = summary });

      // Send notification
      await _notifier.NotifyAsync(req.UserId, "Document ready");

      return Ok(doc);
  }

  // GOOD: Single responsibility per method
  public async Task<ActionResult> CreateDocument(CreateRequest request)
  {
      var doc = await _store.CreateAsync(request.ToDocument());
      await _pipeline.EnqueueProcessingAsync(doc.Id);  // Background job handles rest
      return Ok(doc);
  }

SEVERITY: Suggestion by default; Warning when the responsibilities have different reasons to
  change (they will diverge). Never Critical on a count — orchestration methods that sequence
  several steps are legitimate when each step is a call, not inline logic.
ACTION: Extract responsibilities into separate methods or services.
  Use background jobs for async processing chains.
```

## Report table

```
OUTPUT FORMAT (include in review report after Style and Maintainability):

  ### AI Code Smell Detection

  | # | Smell | Files Affected | Count | Severity |
  |---|-------|----------------|-------|----------|
  | 1 | Interface w/ single impl | AuthService.cs | 1 | Warning |
  | 2 | Try/catch log-rethrow | -- | 0 | -- |
  | 3 | Null check on non-nullable | DataGrid.tsx | 3 | Suggestion |
  | 4 | Code-restating comment | AuthService.cs, DataGrid.tsx | 5 | Suggestion |
  | 5 | Method > 3 responsibilities | DocumentEndpoints.cs | 1 | Warning |

  **AI Smell Score**: {count of warnings + suggestions} findings across {file count} files
  **Verdict**: {Clean / Minor issues / Needs refactoring}
```
