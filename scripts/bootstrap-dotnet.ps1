param([string]$Channel = '10.0')
$ErrorActionPreference = 'Stop'
$BootstrapRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
foreach ($IgnoredPath in @('.codex-tmp/downloads', '.codex-tools/dotnet')) {
    & git -C $BootstrapRoot check-ignore --quiet $IgnoredPath
    if ($LASTEXITCODE -ne 0) { throw 'SDK and download directories must be ignored.' }
}
. (Join-Path $PSScriptRoot 'Environment.ps1')
$Metadata = Invoke-RestMethod -Uri ('https://builds.dotnet.microsoft.com/dotnet/release-metadata/' + $Channel + '/releases.json') -TimeoutSec 60
$Version = $Metadata.'latest-sdk'
$Release = $Metadata.releases | Where-Object { $_.sdk.version -eq $Version } | Select-Object -First 1
$Archive = $Release.sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name.EndsWith('.zip') } | Select-Object -First 1
if (-not $Archive -or $Archive.hash -notmatch '^[A-Fa-f0-9]{128}$') { throw 'Official SDK metadata is incomplete.' }
$ArchiveUri = [Uri]$Archive.url
if ($ArchiveUri.Scheme -ne 'https' -or $ArchiveUri.Host -notin @('builds.dotnet.microsoft.com', 'download.visualstudio.microsoft.com')) {
    throw 'Unexpected SDK download source.'
}
$SdkDirectory = Join-Path $BootstrapRoot '.codex-tools/dotnet'
$ZipDirectory = Join-Path $BootstrapRoot '.codex-tmp/downloads'
[void](New-Item -ItemType Directory -Path $ZipDirectory,$SdkDirectory -Force)
$ZipPath = Join-Path $ZipDirectory ('dotnet-sdk-' + $Version + '-win-x64.zip')
if (-not (Test-Path -LiteralPath $ZipPath)) {
    Invoke-WebRequest -Uri $ArchiveUri -OutFile $ZipPath -TimeoutSec 900
}
if ((Get-FileHash -LiteralPath $ZipPath -Algorithm SHA512).Hash -ine $Archive.hash) { throw 'SDK SHA512 verification failed.' }
Expand-Archive -LiteralPath $ZipPath -DestinationPath $SdkDirectory -Force
Write-Output ('Verified local .NET SDK: ' + $Version)
& (Join-Path $SdkDirectory 'dotnet.exe') --list-sdks
