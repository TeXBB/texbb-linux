$ErrorActionPreference = 'Stop'
$LinuxRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$LinuxTemporary = Join-Path $LinuxRoot '.codex-tmp'
& git -C $LinuxRoot check-ignore --quiet .codex-tmp/environment
if ($LASTEXITCODE -ne 0) { throw 'Linux temporary output must be ignored before use.' }
foreach ($Name in @('dotnet-home', 'nuget', 'nuget-http', 'tmp')) {
    [void](New-Item -ItemType Directory -Path (Join-Path $LinuxTemporary $Name) -Force)
}
$env:DOTNET_CLI_HOME = Join-Path $LinuxTemporary 'dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:NUGET_PACKAGES = Join-Path $LinuxTemporary 'nuget'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $LinuxTemporary 'nuget-http'
$env:TEMP = Join-Path $LinuxTemporary 'tmp'
$env:TMP = $env:TEMP
$LinuxDotnet = Join-Path $LinuxRoot '.codex-tools/dotnet/dotnet.exe'
if (Test-Path -LiteralPath $LinuxDotnet) {
    $env:DOTNET_ROOT = Split-Path -Parent $LinuxDotnet
} else {
    $LinuxDotnet = 'dotnet'
}
