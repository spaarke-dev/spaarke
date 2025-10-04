# .NET Analyzers & EditorConfig Usage for Spaarke

## Overview
This guide covers configuration and usage of .NET analyzers and EditorConfig for maintaining code quality and consistency in the Spaarke platform.

## Analyzer Packages

### Essential Analyzer NuGet Packages
```xml
<!-- Directory.Build.props - Apply to all projects -->
<Project>
  <ItemGroup>
    <!-- Microsoft Code Analysis -->
    <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="8.0.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    
    <!-- Security Analyzers -->
    <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="3.3.4">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    
    <PackageReference Include="SonarAnalyzer.CSharp" Version="9.16.0.82469">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    
    <!-- AsyncFixer for async/await best practices -->
    <PackageReference Include="AsyncFixer" Version="1.6.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    
    <!-- Meziantou Analyzers for additional rules -->
    <PackageReference Include="Meziantou.Analyzer" Version="2.0.146">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    
    <!-- StyleCop for code style enforcement -->
    <PackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.507">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  
  <!-- Enable nullable reference types globally -->
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

## EditorConfig Configuration

### Root .editorconfig File
```ini
# .editorconfig
root = true

# All files
[*]
charset = utf-8
end_of_line = crlf
indent_style = space
insert_final_newline = true
trim_trailing_whitespace = true

# Code files
[*.{cs,csx,vb,vbx}]
indent_size = 4
tab_width = 4

# C# files
[*.cs]

#### Core EditorConfig Options ####

# Indentation preferences
csharp_indent_case_contents = true
csharp_indent_switch_labels = true
csharp_indent_labels = one_less_than_current

# New line preferences
csharp_new_line_before_open_brace = all
csharp_new_line_before_else = true
csharp_new_line_before_catch = true
csharp_new_line_before_finally = true
csharp_new_line_before_members_in_object_initializers = true
csharp_new_line_before_members_in_anonymous_types = true
csharp_new_line_between_query_expression_clauses = true

#### .NET Coding Conventions ####

# Organize usings
dotnet_sort_system_directives_first = true
dotnet_separate_import_directive_groups = false

# this. preferences
dotnet_style_qualification_for_field = false:warning
dotnet_style_qualification_for_property = false:warning
dotnet_style_qualification_for_method = false:warning
dotnet_style_qualification_for_event = false:warning

# Language keywords vs BCL types preferences
dotnet_style_predefined_type_for_locals_parameters_members = true:warning
dotnet_style_predefined_type_for_member_access = true:warning

# Parentheses preferences
dotnet_style_parentheses_in_arithmetic_binary_operators = always_for_clarity:warning
dotnet_style_parentheses_in_relational_binary_operators = always_for_clarity:warning
dotnet_style_parentheses_in_other_binary_operators = always_for_clarity:warning
dotnet_style_parentheses_in_other_operators = never_if_unnecessary:warning

# Modifier preferences
dotnet_style_require_accessibility_modifiers = for_non_interface_members:warning
dotnet_style_readonly_field = true:warning

# Expression preferences
dotnet_style_object_initializer = true:suggestion
dotnet_style_collection_initializer = true:suggestion
dotnet_style_explicit_tuple_names = true:warning
dotnet_style_null_propagation = true:warning
dotnet_style_coalesce_expression = true:warning
dotnet_style_prefer_is_null_check_over_reference_equality_method = true:warning
dotnet_style_prefer_inferred_tuple_names = true:suggestion
dotnet_style_prefer_inferred_anonymous_type_member_names = true:suggestion
dotnet_style_prefer_auto_properties = true:warning
dotnet_style_prefer_conditional_expression_over_assignment = true:suggestion
dotnet_style_prefer_conditional_expression_over_return = true:suggestion

#### C# Coding Conventions ####

# var preferences
csharp_style_var_for_built_in_types = false:warning
csharp_style_var_when_type_is_apparent = true:warning
csharp_style_var_elsewhere = false:warning

# Expression-bodied members
csharp_style_expression_bodied_methods = when_on_single_line:suggestion
csharp_style_expression_bodied_constructors = false:warning
csharp_style_expression_bodied_operators = when_on_single_line:suggestion
csharp_style_expression_bodied_properties = true:warning
csharp_style_expression_bodied_indexers = true:warning
csharp_style_expression_bodied_accessors = true:warning
csharp_style_expression_bodied_lambdas = true:warning
csharp_style_expression_bodied_local_functions = when_on_single_line:suggestion

# Pattern matching preferences
csharp_style_pattern_matching_over_is_with_cast_check = true:warning
csharp_style_pattern_matching_over_as_with_null_check = true:warning
csharp_style_prefer_switch_expression = true:suggestion
csharp_style_prefer_pattern_matching = true:suggestion
csharp_style_prefer_not_pattern = true:warning

# Null-checking preferences
csharp_style_throw_expression = true:warning
csharp_style_conditional_delegate_call = true:warning

# Modifier preferences
csharp_preferred_modifier_order = public,private,protected,internal,static,extern,new,virtual,abstract,sealed,override,readonly,unsafe,volatile,async:warning
csharp_prefer_static_local_function = true:warning

# Code-block preferences
csharp_prefer_braces = true:warning
csharp_prefer_simple_using_statement = true:suggestion

# Expression preferences
csharp_prefer_simple_default_expression = true:warning
csharp_style_deconstructed_variable_declaration = true:suggestion
csharp_style_pattern_local_over_anonymous_function = true:warning
csharp_style_inlined_variable_declaration = true:warning
csharp_style_prefer_index_operator = true:suggestion
csharp_style_prefer_range_operator = true:suggestion
csharp_style_implicit_object_creation_when_type_is_apparent = true:suggestion

# File scoped namespaces
csharp_style_namespace_declarations = file_scoped:warning

# Unnecessary code rules
csharp_style_unused_value_expression_statement_preference = discard_variable:suggestion
csharp_style_unused_value_assignment_preference = discard_variable:suggestion

#### C# Formatting Rules ####

# Wrapping preferences
csharp_preserve_single_line_statements = false
csharp_preserve_single_line_blocks = true

# Using directive preferences
csharp_using_directive_placement = outside_namespace:warning

#### Naming Conventions ####

# Interfaces must start with I
dotnet_naming_rule.interfaces_should_be_prefixed_with_i.severity = warning
dotnet_naming_rule.interfaces_should_be_prefixed_with_i.symbols = interface
dotnet_naming_rule.interfaces_should_be_prefixed_with_i.style = prefix_interface_with_i

dotnet_naming_symbols.interface.applicable_kinds = interface
dotnet_naming_symbols.interface.applicable_accessibilities = public, internal, private, protected, protected_internal, private_protected

dotnet_naming_style.prefix_interface_with_i.required_prefix = I
dotnet_naming_style.prefix_interface_with_i.capitalization = pascal_case

# Types should be PascalCase
dotnet_naming_rule.types_should_be_pascal_case.severity = warning
dotnet_naming_rule.types_should_be_pascal_case.symbols = types
dotnet_naming_rule.types_should_be_pascal_case.style = pascal_case

dotnet_naming_symbols.types.applicable_kinds = class, struct, interface, enum
dotnet_naming_symbols.types.applicable_accessibilities = public, internal, private, protected, protected_internal, private_protected

dotnet_naming_style.pascal_case.capitalization = pascal_case

# Async methods should end with Async
dotnet_naming_rule.async_methods_should_end_with_async.severity = warning
dotnet_naming_rule.async_methods_should_end_with_async.symbols = async_methods
dotnet_naming_rule.async_methods_should_end_with_async.style = end_with_async

dotnet_naming_symbols.async_methods.applicable_kinds = method
dotnet_naming_symbols.async_methods.applicable_accessibilities = *
dotnet_naming_symbols.async_methods.required_modifiers = async

dotnet_naming_style.end_with_async.required_suffix = Async
dotnet_naming_style.end_with_async.capitalization = pascal_case

# Private fields should be _camelCase
dotnet_naming_rule.private_fields_should_be_camel_case.severity = warning
dotnet_naming_rule.private_fields_should_be_camel_case.symbols = private_fields
dotnet_naming_rule.private_fields_should_be_camel_case.style = camel_case_underscore

dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private

dotnet_naming_style.camel_case_underscore.required_prefix = _
dotnet_naming_style.camel_case_underscore.capitalization = camel_case

# Constants should be PascalCase
dotnet_naming_rule.constants_should_be_pascal_case.severity = warning
dotnet_naming_rule.constants_should_be_pascal_case.symbols = constants
dotnet_naming_rule.constants_should_be_pascal_case.style = pascal_case

dotnet_naming_symbols.constants.applicable_kinds = field, local
dotnet_naming_symbols.constants.required_modifiers = const

#### Analyzer Rules ####

# Microsoft.CodeAnalysis.NetAnalyzers
dotnet_diagnostic.CA1001.severity = warning # Types that own disposable fields should be disposable
dotnet_diagnostic.CA1019.severity = warning # Define accessors for attribute arguments
dotnet_diagnostic.CA1060.severity = warning # Move pinvokes to native methods class
dotnet_diagnostic.CA1061.severity = warning # Do not hide base class methods
dotnet_diagnostic.CA1063.severity = warning # Implement IDisposable correctly
dotnet_diagnostic.CA1065.severity = warning # Do not raise exceptions in unexpected locations
dotnet_diagnostic.CA1816.severity = warning # Dispose methods should call SuppressFinalize
dotnet_diagnostic.CA1824.severity = warning # Mark assemblies with NeutralResourcesLanguageAttribute
dotnet_diagnostic.CA1825.severity = warning # Avoid zero-length array allocations
dotnet_diagnostic.CA1826.severity = warning # Do not use Enumerable methods on indexable collections
dotnet_diagnostic.CA1827.severity = warning # Do not use Count() or LongCount() when Any() can be used
dotnet_diagnostic.CA1828.severity = warning # Do not use CountAsync() or LongCountAsync() when AnyAsync() can be used
dotnet_diagnostic.CA1829.severity = warning # Use Length/Count property instead of Count() when available
dotnet_diagnostic.CA1830.severity = warning # Prefer strongly-typed Append and Insert method overloads on StringBuilder
dotnet_diagnostic.CA1831.severity = warning # Use AsSpan or AsMemory instead of Range-based indexers when appropriate
dotnet_diagnostic.CA1832.severity = warning # Use AsSpan or AsMemory instead of Range-based indexers when appropriate
dotnet_diagnostic.CA1833.severity = warning # Use AsSpan or AsMemory instead of Range-based indexers when appropriate
dotnet_diagnostic.CA1834.severity = warning # Consider using 'StringBuilder.Append(char)' when applicable
dotnet_diagnostic.CA1835.severity = warning # Prefer the 'Memory'-based overloads for 'ReadAsync' and 'WriteAsync'
dotnet_diagnostic.CA1836.severity = warning # Prefer IsEmpty over Count
dotnet_diagnostic.CA1837.severity = warning # Use 'Environment.ProcessId'
dotnet_diagnostic.CA1838.severity = warning # Avoid 'StringBuilder' parameters for P/Invokes
dotnet_diagnostic.CA1839.severity = warning # Use 'Environment.ProcessPath'
dotnet_diagnostic.CA1840.severity = warning # Use 'Environment.CurrentManagedThreadId'
dotnet_diagnostic.CA1841.severity = warning # Prefer Dictionary.Contains methods
dotnet_diagnostic.CA1842.severity = warning # Do not use 'WhenAll' with a single task
dotnet_diagnostic.CA1843.severity = warning # Do not use 'WaitAll' with a single task
dotnet_diagnostic.CA1844.severity = warning # Provide memory-based overrides of async methods when subclassing 'Stream'
dotnet_diagnostic.CA1845.severity = warning # Use span-based 'string.Concat'
dotnet_diagnostic.CA1846.severity = warning # Prefer 'AsSpan' over 'Substring'
dotnet_diagnostic.CA1847.severity = warning # Use char literal for a single character lookup
dotnet_diagnostic.CA1848.severity = warning # Use the LoggerMessage delegates
dotnet_diagnostic.CA1849.severity = warning # Call async methods when in an async method
dotnet_diagnostic.CA2000.severity = warning # Dispose objects before losing scope
dotnet_diagnostic.CA2002.severity = warning # Do not lock on objects with weak identity
dotnet_diagnostic.CA2007.severity = none # Consider calling ConfigureAwait on the awaited task (not needed in ASP.NET Core)
dotnet_diagnostic.CA2008.severity = warning # Do not create tasks without passing a TaskScheduler
dotnet_diagnostic.CA2009.severity = warning # Do not call ToImmutableCollection on an ImmutableCollection value
dotnet_diagnostic.CA2011.severity = warning # Avoid infinite recursion
dotnet_diagnostic.CA2012.severity = warning # Use ValueTasks correctly
dotnet_diagnostic.CA2013.severity = warning # Do not use ReferenceEquals with value types
dotnet_diagnostic.CA2014.severity = warning # Do not use stackalloc in loops
dotnet_diagnostic.CA2015.severity = warning # Do not define finalizers for types derived from MemoryManager<T>
dotnet_diagnostic.CA2016.severity = warning # Forward the 'CancellationToken' parameter to methods

# Security analyzers
dotnet_diagnostic.CA2100.severity = error # Review SQL queries for security vulnerabilities
dotnet_diagnostic.CA3001.severity = error # Review code for SQL injection vulnerabilities
dotnet_diagnostic.CA3003.severity = error # Review code for file path injection vulnerabilities
dotnet_diagnostic.CA3004.severity = error # Review code for information disclosure vulnerabilities
dotnet_diagnostic.CA3006.severity = error # Review code for process command injection vulnerabilities
dotnet_diagnostic.CA3007.severity = error # Review code for open redirect vulnerabilities
dotnet_diagnostic.CA3061.severity = error # Do Not Add Schema By URL
dotnet_diagnostic.CA5350.severity = error # Do Not Use Weak Cryptographic Algorithms
dotnet_diagnostic.CA5351.severity = error # Do Not Use Broken Cryptographic Algorithms
dotnet_diagnostic.CA5358.severity = error # Do Not Use Unsafe Cipher Modes
dotnet_diagnostic.CA5359.severity = error # Do Not Disable Certificate Validation
dotnet_diagnostic.CA5360.severity = error # Do Not Call Dangerous Methods In Deserialization
dotnet_diagnostic.CA5361.severity = error # Do Not Disable SChannel Use of Strong Crypto
dotnet_diagnostic.CA5362.severity = error # Do Not Refer Self In Serializable Class
dotnet_diagnostic.CA5363.severity = error # Do Not Disable Request Validation
dotnet_diagnostic.CA5364.severity = error # Do Not Use Deprecated Security Protocols
dotnet_diagnostic.CA5365.severity = error # Do Not Disable HTTP Header Checking

# AsyncFixer
dotnet_diagnostic.AsyncFixer01.severity = warning # Unnecessary async/await usage
dotnet_diagnostic.AsyncFixer02.severity = warning # Long-running or blocking operations under an async method
dotnet_diagnostic.AsyncFixer03.severity = error # Fire-and-forget async-void methods or delegates
dotnet_diagnostic.AsyncFixer04.severity = warning # Fire-and-forget async call inside a using block
dotnet_diagnostic.AsyncFixer05.severity = warning # Downcasting from a nested task to an outer task

# StyleCop
dotnet_diagnostic.SA1000.severity = warning # Keywords should be spaced correctly
dotnet_diagnostic.SA1001.severity = warning # Commas should be spaced correctly
dotnet_diagnostic.SA1003.severity = warning # Symbols should be spaced correctly
dotnet_diagnostic.SA1008.severity = warning # Opening parenthesis should be spaced correctly
dotnet_diagnostic.SA1009.severity = warning # Closing parenthesis should be spaced correctly
dotnet_diagnostic.SA1010.severity = warning # Opening square brackets should be spaced correctly
dotnet_diagnostic.SA1011.severity = warning # Closing square brackets should be spaced correctly
dotnet_diagnostic.SA1012.severity = warning # Opening curly brackets should be spaced correctly
dotnet_diagnostic.SA1013.severity = warning # Closing curly brackets should be spaced correctly
dotnet_diagnostic.SA1101.severity = none # Prefix local calls with this
dotnet_diagnostic.SA1200.severity = none # Using directives should be placed correctly
dotnet_diagnostic.SA1201.severity = warning # Elements should appear in the correct order
dotnet_diagnostic.SA1202.severity = warning # Elements should be ordered by access
dotnet_diagnostic.SA1204.severity = warning # Static elements should appear before instance elements
dotnet_diagnostic.SA1214.severity = warning # Readonly fields should appear before non-readonly fields
dotnet_diagnostic.SA1300.severity = warning # Element should begin with upper-case letter
dotnet_diagnostic.SA1303.severity = warning # Const field names should begin with upper-case letter
dotnet_diagnostic.SA1309.severity = none # Field names should not begin with underscore
dotnet_diagnostic.SA1400.severity = warning # Access modifier should be declared
dotnet_diagnostic.SA1401.severity = warning # Fields should be private
dotnet_diagnostic.SA1402.severity = warning # File may only contain a single type
dotnet_diagnostic.SA1403.severity = warning # File may only contain a single namespace
dotnet_diagnostic.SA1404.severity = warning # Code analysis suppression should have justification
dotnet_diagnostic.SA1405.severity = warning # Debug.Assert should provide message text
dotnet_diagnostic.SA1500.severity = warning # Braces for multi-line statements should not share line
dotnet_diagnostic.SA1501.severity = warning # Statement should not be on a single line
dotnet_diagnostic.SA1502.severity = warning # Element should not be on a single line
dotnet_diagnostic.SA1503.severity = warning # Braces should not be omitted
dotnet_diagnostic.SA1600.severity = none # Elements should be documented (turn on for public APIs)
dotnet_diagnostic.SA1633.severity = none # File should have header

# SonarAnalyzer
dotnet_diagnostic.S125.severity = warning # Sections of code should not be commented out
dotnet_diagnostic.S927.severity = warning # Parameter names should match base declaration
dotnet_diagnostic.S1066.severity = warning # Collapsible "if" statements should be merged
dotnet_diagnostic.S1075.severity = warning # URIs should not be hardcoded
dotnet_diagnostic.S1118.severity = warning # Utility classes should not have public constructors
dotnet_diagnostic.S1125.severity = warning # Boolean literals should not be redundant
dotnet_diagnostic.S1135.severity = warning # Track uses of "TODO" tags
dotnet_diagnostic.S1186.severity = warning # Methods should not be empty
dotnet_diagnostic.S1199.severity = warning # Nested code blocks should not be used
dotnet_diagnostic.S1481.severity = warning # Unused local variables should be removed
dotnet_diagnostic.S2259.severity = warning # Null pointers should not be dereferenced
dotnet_diagnostic.S2583.severity = warning # Conditionally executed code should be reachable
dotnet_diagnostic.S2589.severity = warning # Boolean expressions should not be gratuitous
dotnet_diagnostic.S2696.severity = warning # Instance members should not write to "static" fields
dotnet_diagnostic.S2743.severity = warning # Static fields should not be used in generic types
dotnet_diagnostic.S3358.severity = warning # Ternary operators should not be nested
dotnet_diagnostic.S3776.severity = warning # Cognitive Complexity of methods should not be too high
dotnet_diagnostic.S4457.severity = warning # Parameter validation in "async"/"await" methods should be wrapped

# Meziantou.Analyzer
dotnet_diagnostic.MA0002.severity = warning # IEqualityComparer<T> or IComparer<T> is missing
dotnet_diagnostic.MA0003.severity = warning # Add parameter name to improve readability
dotnet_diagnostic.MA0004.severity = warning # Use Task.ConfigureAwait(false)
dotnet_diagnostic.MA0006.severity = warning # Use String.Equals instead of equality operator
dotnet_diagnostic.MA0007.severity = warning # Add a comma after the last value
dotnet_diagnostic.MA0008.severity = warning # Add StructLayoutAttribute
dotnet_diagnostic.MA0009.severity = warning # Add regex evaluation timeout
dotnet_diagnostic.MA0016.severity = warning # Prefer return collection abstraction instead of implementation

# XML documentation
[*.{cs,vb}]
# CS1591: Missing XML comment for publicly visible type or member
dotnet_diagnostic.CS1591.severity = suggestion

# JSON files
[*.json]
indent_size = 2

# XML files
[*.{xml,csproj,vbproj,vcxproj,vcxproj.filters,proj,projitems,shproj}]
indent_size = 2

# YAML files
[*.{yml,yaml}]
indent_size = 2

# PowerShell files
[*.ps1]
indent_size = 2

# Shell script files
[*.sh]
end_of_line = lf
indent_size = 2

# Markdown files
[*.md]
trim_trailing_whitespace = false
```

## Banned APIs Configuration

### BannedSymbols.txt
```text
# BannedSymbols.txt - APIs that should not be used in Spaarke
T:System.Net.WebClient;Use HttpClient instead
M:System.IO.File.ReadAllText;Use async methods with FileStream instead
M:System.IO.File.WriteAllText;Use async methods with FileStream instead
M:System.IO.File.ReadAllBytes;Use async methods with FileStream instead
M:System.IO.File.WriteAllBytes;Use async methods with FileStream instead
M:System.Threading.Thread.Sleep;Use Task.Delay instead
M:System.GC.Collect;Do not force garbage collection
T:System.Web.HttpContext;Use IHttpContextAccessor in ASP.NET Core
M:System.DateTime.Now;Use DateTimeOffset.UtcNow instead
M:System.DateTime.Today;Use DateTimeOffset.UtcNow.Date instead
P:System.DateTime.Now;Use DateTimeOffset.UtcNow instead
P:System.DateTime.Today;Use DateTimeOffset.UtcNow.Date instead
M:System.Random.#ctor;Use System.Security.Cryptography.RandomNumberGenerator
T:Newtonsoft.Json.JsonConvert;Use System.Text.Json instead
M:System.String.Format;Use string interpolation instead
M:System.Convert.ToInt32(System.String);Use int.TryParse instead
M:System.Convert.ToInt64(System.String);Use long.TryParse instead
M:System.Convert.ToDouble(System.String);Use double.TryParse instead
```

## Custom Analyzer Rules

### Custom Security Analyzer
```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SpaarkeSecurityAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SPRK001";
    
    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        "Avoid hardcoded connection strings",
        "Connection string '{0}' should not be hardcoded",
        "Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Connection strings should be stored in configuration or Key Vault.");
    
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => 
        ImmutableArray.Create(Rule);
    
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeLiteral, SyntaxKind.StringLiteralExpression);
    }
    
    private void AnalyzeLiteral(SyntaxNodeAnalysisContext context)
    {
        var literal = (LiteralExpressionSyntax)context.Node;
        var value = literal.Token.ValueText;
        
        // Check for connection string patterns
        if (ContainsConnectionString(value))
        {
            var diagnostic = Diagnostic.Create(Rule, literal.GetLocation(), value);
            context.ReportDiagnostic(diagnostic);
        }
    }
    
    private bool ContainsConnectionString(string value)
    {
        var patterns = new[]
        {
            "Data Source=",
            "Initial Catalog=",
            "User ID=",
            "Password=",
            "Server=",
            "Database=",
            "Integrated Security=",
            "AccountName=",
            "AccountKey=",
            "DefaultEndpointsProtocol="
        };
        
        return patterns.Any(p => value.Contains(p, StringComparison.OrdinalIgnoreCase));
    }
}
```

### Custom Async Analyzer
```csharp
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SpaarkeAsyncAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SPRK002";
    
    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        "Use async methods for I/O operations",
        "Method '{0}' performs I/O but is not async",
        "Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "I/O operations should be async to avoid blocking threads.");
    
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => 
        ImmutableArray.Create(Rule);
    
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }
    
    private void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        
        // Skip if already async
        if (method.Modifiers.Any(SyntaxKind.AsyncKeyword))
            return;
        
        // Check for I/O operations
        var ioPatterns = new[]
        {
            "File.Read", "File.Write", "Stream.Read", "Stream.Write",
            "HttpClient.", "DbContext.", "SqlConnection."
        };
        
        var methodBody = method.Body?.ToString() ?? method.ExpressionBody?.ToString() ?? "";
        
        if (ioPatterns.Any(pattern => methodBody.Contains(pattern)))
        {
            var diagnostic = Diagnostic.Create(
                Rule, 
                method.Identifier.GetLocation(), 
                method.Identifier.Text);
            context.ReportDiagnostic(diagnostic);
        }
    }
}
```

## Suppression Strategies

### Global Suppressions
```csharp
// GlobalSuppressions.cs
using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Performance", 
    "CA1822:Mark members as static", 
    Justification = "Methods are used by DI container",
    Scope = "namespaceanddescendants", 
    Target = "~N:Spaarke.Infrastructure.Services")]

[assembly: SuppressMessage(
    "Design",
    "CA1062:Validate arguments of public methods",
    Justification = "Null checks handled by nullable reference types",
    Scope = "namespaceanddescendants",
    Target = "~N:Spaarke.Api.Controllers")]
```

### Inline Suppressions
```csharp
public class DocumentService
{
    // Suppress for legacy compatibility
    #pragma warning disable CA1822 // Mark members as static
    public string GetLegacyFormat(Document doc)
    {
        return doc.ToString();
    }
    #pragma warning restore CA1822
    
    // Or use SuppressMessage attribute
    [SuppressMessage("Performance", "CA1822:Mark members as static", 
        Justification = "Required by interface")]
    public void ProcessDocument(Document doc)
    {
        // Processing logic
    }
}
```

## CI/CD Integration

### Build Pipeline Configuration
```yaml
# azure-pipelines.yml
- task: DotNetCoreCLI@2
  displayName: 'Build with analyzers'
  inputs:
    command: 'build'
    arguments: >
      --configuration Release
      /p:TreatWarningsAsErrors=true
      /p:EnforceCodeStyleInBuild=true
      /p:RunAnalyzersDuringBuild=true
      /p:RunAnalyzersDuringLiveAnalysis=true

- task: DotNetCoreCLI@2
  displayName: 'Run .NET Format'
  inputs:
    command: 'custom'
    custom: 'format'
    arguments: '--verify-no-changes --severity warn'
```

### MSBuild Integration
```xml
<!-- Directory.Build.props -->
<Project>
  <PropertyGroup>
    <AnalysisMode>AllEnabledByDefault</AnalysisMode>
    <AnalysisLevel>latest-all</AnalysisLevel>
    <CodeAnalysisTreatWarningsAsErrors>true</CodeAnalysisTreatWarningsAsErrors>
    <RunAnalyzersDuringBuild>true</RunAnalyzersDuringBuild>
    <RunAnalyzersDuringLiveAnalysis>true</RunAnalyzersDuringLiveAnalysis>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
  </PropertyGroup>
  
  <!-- Code metrics -->
  <PropertyGroup>
    <CodeMetricsEnabled>true</CodeMetricsEnabled>
    <CodeMetricsRuleSet>$(MSBuildThisFileDirectory)CodeMetrics.ruleset</CodeMetricsRuleSet>
  </PropertyGroup>
</Project>
```

## Roslyn Analyzer Testing

```csharp
[Fact]
public async Task SecurityAnalyzer_DetectsHardcodedConnectionString()
{
    var test = @"
        class Program
        {
            void Method()
            {
                var conn = ""Server=localhost;Database=test;User ID=sa;Password=pass"";
            }
        }";
    
    var expected = new DiagnosticResult
    {
        Id = "SPRK001",
        Message = "Connection string 'Server=localhost...' should not be hardcoded",
        Severity = DiagnosticSeverity.Error,
        Locations = new[] { new DiagnosticResultLocation("Test0.cs", 6, 28) }
    };
    
    await VerifyAnalyzerAsync(test, expected);
}
```

## VS Code Integration

### .vscode/settings.json
```json
{
  "omnisharp.enableRoslynAnalyzers": true,
  "omnisharp.enableEditorConfigSupport": true,
  "omnisharp.enableImportCompletion": true,
  "omnisharp.enableAsyncCompletion": true,
  "dotnet.inlayHints.enableInlayHintsForParameters": true,
  "dotnet.inlayHints.enableInlayHintsForLiteralParameters": true,
  "dotnet.inlayHints.enableInlayHintsForIndexerParameters": true,
  "dotnet.inlayHints.enableInlayHintsForObjectCreationParameters": true,
  "dotnet.inlayHints.enableInlayHintsForOtherParameters": true,
  "dotnet.inlayHints.suppressInlayHintsForParametersThatDifferOnlyBySuffix": true,
  "dotnet.inlayHints.suppressInlayHintsForParametersThatMatchMethodIntent": true,
  "dotnet.inlayHints.suppressInlayHintsForParametersThatMatchArgumentName": true,
  "csharp.semanticHighlighting.enabled": true,
  "csharp.suppressDotnetRestoreNotification": false,
  "csharp.suppressBuildAssetsNotification": false,
  "csharp.showOmnisharpLogOnError": true
}
```

## Key Principles for Analyzers in Spaarke

1. **Treat warnings as errors** - Maintain high code quality standards
2. **Use .editorconfig for consistency** - Share formatting across team
3. **Ban dangerous APIs** - Prevent security vulnerabilities
4. **Enforce async patterns** - Per ADR-001 and ADR-004
5. **Custom analyzers for domain rules** - Enforce Spaarke patterns
6. **Suppress with justification** - Document why rules are bypassed
7. **Run in CI/CD** - Catch issues before merge
8. **Keep analyzers updated** - Latest security and performance checks