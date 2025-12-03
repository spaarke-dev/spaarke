# Build and Merge Plugin Assembly with Dependencies
# This script builds the plugin and merges all Azure.Identity dependencies into a single DLL

$ErrorActionPreference = "Stop"

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Building Plugin with Dependencies" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Clean and Build
Write-Host "[1/4] Building plugin in Release mode..." -ForegroundColor Yellow
dotnet build -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Build completed`n" -ForegroundColor Green

# Step 2: Find ILRepack.exe
Write-Host "[2/4] Locating ILRepack tool..." -ForegroundColor Yellow

$ilrepackPath = Get-ChildItem -Path "$env:USERPROFILE\.nuget\packages\ilrepack" -Filter "ilrepack.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1

if (-not $ilrepackPath) {
    Write-Host "ILRepack.exe not found. Installing..." -ForegroundColor Yellow
    dotnet tool install --global dotnet-ilrepack

    # Try alternative location
    $ilrepackPath = Get-ChildItem -Path "$env:USERPROFILE\.nuget\packages\ilrepack" -Filter "tools\ilrepack.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
}

if (-not $ilrepackPath) {
    Write-Host "❌ Could not find ILRepack.exe" -ForegroundColor Red
    Write-Host "Installing ILRepack manually..." -ForegroundColor Yellow

    # Download and install ILRepack NuGet package
    $tempDir = New-Item -ItemType Directory -Path "$env:TEMP\ilrepack-temp" -Force
    $nugetUrl = "https://www.nuget.org/api/v2/package/ILRepack/2.0.18"
    $zipFile = "$tempDir\ilrepack.zip"

    Invoke-WebRequest -Uri $nugetUrl -OutFile $zipFile
    Expand-Archive -Path $zipFile -DestinationPath $tempDir -Force

    $ilrepackPath = Get-ChildItem -Path $tempDir -Filter "ilrepack.exe" -Recurse | Select-Object -First 1
}

Write-Host "✅ ILRepack found: $($ilrepackPath.FullName)`n" -ForegroundColor Green

# Step 3: Merge assemblies
Write-Host "[3/4] Merging assemblies..." -ForegroundColor Yellow

$binDir = "bin\Release\net462"
$outputDll = "$binDir\Spaarke.Dataverse.CustomApiProxy.dll"
$mergedDll = "$binDir\Spaarke.Dataverse.CustomApiProxy.Merged.dll"
$keyFile = "SpaarkePlugin.snk"

# Build list of assemblies to merge
$assemblies = @(
    "$binDir\Spaarke.Dataverse.CustomApiProxy.dll",
    "$binDir\Azure.Identity.dll",
    "$binDir\Azure.Core.dll",
    "$binDir\Microsoft.Identity.Client.dll",
    "$binDir\System.Memory.dll",
    "$binDir\System.Text.Json.dll",
    "$binDir\System.Threading.Tasks.Extensions.dll",
    "$binDir\System.Numerics.Vectors.dll",
    "$binDir\System.Runtime.CompilerServices.Unsafe.dll",
    "$binDir\System.Buffers.dll",
    "$binDir\Microsoft.Bcl.AsyncInterfaces.dll",
    "$binDir\System.Text.Encodings.Web.dll"
)

# Add Microsoft.IdentityModel assemblies
$identityModelDlls = Get-ChildItem -Path $binDir -Filter "Microsoft.IdentityModel.*.dll" | ForEach-Object { $_.FullName }
$assemblies += $identityModelDlls

# Filter to only existing files
$existingAssemblies = $assemblies | Where-Object { Test-Path $_ }

Write-Host "Merging $($existingAssemblies.Count) assemblies..." -ForegroundColor Cyan

# Run ILRepack
$ilrepackArgs = @(
    "/internalize",
    "/ndebug",
    "/parallel",
    "/keyfile:$keyFile",
    "/out:$mergedDll"
) + $existingAssemblies

& $ilrepackPath.FullName $ilrepackArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ ILRepack failed" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Assemblies merged successfully`n" -ForegroundColor Green

# Step 4: Replace original with merged
Write-Host "[4/4] Replacing original DLL with merged version..." -ForegroundColor Yellow

Copy-Item -Path $mergedDll -Destination $outputDll -Force
Remove-Item -Path $mergedDll

Write-Host "✅ Plugin DLL ready for deployment`n" -ForegroundColor Green

# Show file info
$fileInfo = Get-Item $outputDll
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "DEPLOYMENT READY" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Plugin DLL: $outputDll" -ForegroundColor Green
Write-Host "File Size: $([math]::Round($fileInfo.Length / 1MB, 2)) MB" -ForegroundColor Green
Write-Host "Modified: $($fileInfo.LastWriteTime)" -ForegroundColor Green
Write-Host ""
Write-Host "Next step: Update plugin assembly in Dataverse using Plugin Registration Tool" -ForegroundColor Yellow
Write-Host ""
