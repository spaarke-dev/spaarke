param(
    [Parameter(Mandatory=$true)]
    [string]$Path,

    [Parameter(Mandatory=$false)]
    [string]$OutCsv = "docx-audit-report.csv"
)
Write-Host "Auditing .docx under: $Path" -ForegroundColor Cyan

$patterns = @{
    # Runtime / Functions
    'Function App' = 'Functions runtime reference';
    'HTTP trigger' = 'Functions HTTP trigger';
    'ServiceBus trigger' = 'Functions ServiceBus trigger';
    'Durable Function' = 'Durable Functions';
    '\[FunctionName\(' = 'Functions attribute';
    '\[HttpTrigger\(' = 'Functions attribute';
    '\[ServiceBusTrigger\(' = 'Functions attribute';
    '\[TimerTrigger\(' = 'Functions attribute';

    # Middleware
    'DocumentAuthorizationMiddleware' = 'Global authz middleware';
    'UacAuthorizationMiddleware' = 'Global authz middleware';
    'DataverseSecurityContextMiddleware' = 'Global context middleware';

    # Abstractions to remove
    'IResourceStore' = 'Generic storage abstraction';
    'SpeResourceStore' = 'Thin wrapper';
    'HybridCacheService' = 'Hybrid cache (L1+L2)';
    'IMemoryCache' = 'L1 cache reference';

    # DI clutter
    'AddSingleton<.*Functions' = 'Functions SDK registration';
}

$rows = @()

Get-ChildItem -Path $Path -Recurse -Include *.docx -File | ForEach-Object {
    $docx = $_.FullName
    $tmp = Join-Path $env:TEMP ([IO.Path]::GetRandomFileName())
    $dir = New-Item -ItemType Directory -Path $tmp -Force
    Copy-Item -LiteralPath $docx -Destination (Join-Path $tmp "doc.zip") -Force
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $tmp "doc.zip"), (Join-Path $tmp "unzipped"))

    Get-ChildItem -Path (Join-Path $tmp "unzipped\word") -Include *.xml -File | ForEach-Object {
        $xmlPath = $_.FullName
        $text = Get-Content -LiteralPath $xmlPath -Raw -ErrorAction SilentlyContinue
        if ($null -ne $text) {
            foreach ($kvp in $patterns.GetEnumerator()) {
                $pat = $kvp.Key
                if ($text -match $pat) {
                    $matches = [regex]::Matches($text, $pat)
                    foreach ($m in $matches) {
                        $start = [Math]::Max(0, $m.Index - 60)
                        $len = [Math]::Min(120, $text.Length - $start)
                        $snippet = $text.Substring($start, $len).Replace("`r"," ").Replace("`n"," ")
                        $rows += [PSCustomObject]@{
                            Document = $docx
                            XmlFile = $xmlPath.Substring((Join-Path $tmp "unzipped\").Length)
                            Pattern = $pat
                            Category = $kvp.Value
                            Offset = $m.Index
                            Snippet = $snippet
                        }
                    }
                }
            }
        }
    }

    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}

$rows | Export-Csv -NoTypeInformation -Path $OutCsv -Encoding UTF8
Write-Host "Audit complete. Report: $OutCsv" -ForegroundColor Green
