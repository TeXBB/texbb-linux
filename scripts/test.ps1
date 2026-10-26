param([switch]$IncludeUi)
. (Join-Path $PSScriptRoot 'Environment.ps1')
Push-Location $LinuxRoot
try {
    $TestRun = Join-Path $LinuxTemporary ('test-runs/' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $TestRun -Force | Out-Null
    & $LinuxDotnet build tests/TeXBB.Core.Tests/TeXBB.Core.Tests.csproj -m:1 -nr:false -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Core test build failed.' }
    & $LinuxDotnet run --project tests/TeXBB.Core.Tests/TeXBB.Core.Tests.csproj --no-build --no-restore -- (Join-Path $TestRun 'core')
    if ($LASTEXITCODE -ne 0) { throw 'Core checks failed.' }
    if ($IncludeUi) {
        & $LinuxDotnet build tests/TeXBB.Desktop.Probe/TeXBB.Desktop.Probe.csproj -m:1 -nr:false -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw 'UI test build failed.' }
        & $LinuxDotnet run --project tests/TeXBB.Desktop.Probe/TeXBB.Desktop.Probe.csproj --no-build --no-restore -- (Join-Path $TestRun 'ui')
        if ($LASTEXITCODE -ne 0) { throw 'Headless desktop checks failed.' }
    }
    Write-Output ('Test artifacts: ' + $TestRun)
} finally { Pop-Location }
