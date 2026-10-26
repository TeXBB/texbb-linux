param([switch]$PublishLinux)
. (Join-Path $PSScriptRoot 'Environment.ps1')
Push-Location $LinuxRoot
try {
    if ($PublishLinux) {
        $PublishDirectory = Join-Path $LinuxTemporary ('publish/linux-x64-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        & $LinuxDotnet publish src/TeXBB.Desktop/TeXBB.Desktop.csproj --configuration Release --runtime linux-x64 --self-contained true --output $PublishDirectory -m:1 -nr:false -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw 'Linux cross-publish failed.' }
        Write-Output ('Linux executable: ' + (Join-Path $PublishDirectory 'TeXBB'))
    } else {
        & $LinuxDotnet build src/TeXBB.Desktop/TeXBB.Desktop.csproj --configuration Debug -m:1 -nr:false -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
    }
} finally { Pop-Location }
