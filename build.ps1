param(
    [switch]$SkipTests,
    [switch]$SignRelease
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $root 'src'
$testDir = Join-Path $root 'tests'
$fixturesDir = Join-Path $testDir 'fixtures'
$distDir = Join-Path $root 'dist'

New-Item -ItemType Directory -Force -Path $distDir | Out-Null

$iconResources = @(Get-ChildItem (Join-Path $root 'assets/lucide') -Filter *.png | ForEach-Object { '/resource:' + $_.FullName + ',Icons.' + $_.Name })
$iconResources += '/resource:' + (Join-Path $root 'assets/lucide/LICENSE.txt') + ',Lucide.LICENSE.txt'
Copy-Item -LiteralPath (Join-Path $root 'assets/lucide/LICENSE.txt') -Destination (Join-Path $distDir 'LUCIDE-LICENSE.txt') -Force

$exeOut = Join-Path $distDir 'QuestLauncher.exe'
$testOut = Join-Path $distDir 'QuestLauncher.Tests.exe'
$sumFile = Join-Path $distDir 'SHA256SUMS.txt'

function Write-Checksums {
    Write-Host "==> Writing SHA256SUMS.txt..." -ForegroundColor Cyan
    $sumLines = @()
    foreach ($file in @($exeOut, $testOut)) {
        if (Test-Path $file) {
            $h = (Get-FileHash -Path $file -Algorithm SHA256).Hash.ToLowerInvariant()
            $name = Split-Path -Leaf $file
            $sumLines += "$h  $name"
        }
    }
    [System.IO.File]::WriteAllLines($sumFile, $sumLines, (New-Object System.Text.UTF8Encoding($false)))
    Get-Content $sumFile
}

function Update-LocalLauncher {
    Copy-Item -LiteralPath $exeOut -Destination (Join-Path $root 'QuestLauncher.exe') -Force
    Copy-Item -LiteralPath (Join-Path $distDir 'LUCIDE-LICENSE.txt') -Destination $root -Force
}


if ($SignRelease) {
    if ([string]::IsNullOrEmpty($env:QL_RELEASE_SIGNING_KEY_XML)) {
        throw "QL_RELEASE_SIGNING_KEY_XML environment variable is not set for release signing."
    }
    if (-not (Test-Path $exeOut) -or -not (Test-Path $testOut)) {
        throw "Build artifacts not found in dist/. Run .\build.ps1 before .\build.ps1 -SignRelease."
    }
    Write-Host "==> Signing official release QuestLauncher.exe from CI secret..." -ForegroundColor Cyan
    & $testOut --sign-release $exeOut
    if ($LASTEXITCODE -ne 0) {
        throw "Official release signing failed (exit code $LASTEXITCODE)."
    }
    Write-Checksums
    Update-LocalLauncher
    exit 0
}

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw "Compiler not found (.NET Framework 4.x csc.exe)."
}

Write-Host "==> Building QuestLauncher.exe (production, no test harness)..." -ForegroundColor Cyan
$srcFiles = Get-ChildItem -Path $srcDir -Recurse -Filter *.cs | Sort-Object FullName | ForEach-Object { $_.FullName }

& $csc /nologo /target:winexe /optimize+ /platform:anycpu /utf8output `
    /out:$exeOut `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    $srcFiles $iconResources

if ($LASTEXITCODE -ne 0) {
    throw "QuestLauncher.exe build failed (exit code $LASTEXITCODE)."
}

Write-Host "==> Building test runner QuestLauncher.Tests.exe (with QL_TEST_HARNESS)..." -ForegroundColor Cyan
$testSources = ($srcFiles | Where-Object { $_ -notlike '*\Program.cs' }) + @(Join-Path $testDir 'TestRunner.cs')

$v12Fixture = Join-Path $fixturesDir 'QuestLauncher_v1.2_release.exe.gz'
$v13Fixture = Join-Path $fixturesDir 'QuestLauncher_v1.3_release.exe.gz'

& $csc /nologo /target:exe /optimize+ /platform:anycpu /utf8output `
    /define:QL_TEST_HARNESS `
    /out:$testOut `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /resource:"$v12Fixture,QuestLauncher_v1.2_release.exe.gz" `
    /resource:"$v13Fixture,QuestLauncher_v1.3_release.exe.gz" `
    $testSources $iconResources

if ($LASTEXITCODE -ne 0) {
    throw "QuestLauncher.Tests.exe build failed (exit code $LASTEXITCODE)."
}

if (-not $SkipTests) {
    Write-Host "==> Running automated verification suite..." -ForegroundColor Cyan
    & $testOut $exeOut
    if ($LASTEXITCODE -ne 0) {
        throw "Automated tests failed (exit code $LASTEXITCODE)."
    }
}

Write-Checksums
Update-LocalLauncher
Write-Host "==> Build and verification succeeded!" -ForegroundColor Green
