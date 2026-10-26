param(
    [string]$ChromePath = 'C:/Program Files/Google/Chrome/Application/chrome.exe',
    [switch]$NoBuild
)

# Windows Chromium smoke only: this does not replace Linux NativeWebView/WebKit testing.
. (Join-Path $PSScriptRoot 'Environment.ps1')
if (-not (Test-Path -LiteralPath $ChromePath -PathType Leaf)) { throw "Chrome was not found: $ChromePath" }
$BrowserRun = Join-Path $LinuxTemporary ('markdown-browser-' + [Guid]::NewGuid().ToString('N'))
& git -C $LinuxRoot check-ignore --quiet $BrowserRun
if ($LASTEXITCODE -ne 0) { throw 'Browser smoke output must be ignored before use.' }
[void](New-Item -ItemType Directory -Path $BrowserRun)
$MathDirectory = Join-Path $BrowserRun 'mathjax'
Expand-Archive -LiteralPath (Join-Path $LinuxRoot 'assets/mathjax-2.7.9.zip') -DestinationPath $MathDirectory
$OldMathRuntime = $env:TEXBB_PREVIEW_MATHJAX_SCRIPT
try {
    $env:TEXBB_PREVIEW_MATHJAX_SCRIPT = Join-Path $MathDirectory 'MathJax.js'
    $FixtureRoot = Join-Path $BrowserRun 'fixtures'
    $TestArguments = @('run', '--project', (Join-Path $LinuxRoot 'tests/TeXBB.Core.Tests/TeXBB.Core.Tests.csproj'), '--no-restore')
    if ($NoBuild) { $TestArguments += '--no-build' }
    $TestArguments += @('--', $FixtureRoot)
    & $LinuxDotnet @TestArguments
    if ($LASTEXITCODE -ne 0) { throw 'Portable tests or browser fixture generation failed.' }
} finally {
    $env:TEXBB_PREVIEW_MATHJAX_SCRIPT = $OldMathRuntime
}

$Fixture = Join-Path $FixtureRoot 'preview/browser-preview.html'
if (-not (Test-Path -LiteralPath $Fixture -PathType Leaf)) { throw 'Browser fixture is missing. Rebuild the test runner.' }
$FixtureUri = ([Uri]$Fixture).AbsoluteUri
$DumpPath = Join-Path $BrowserRun 'rendered-dom.html'
$ErrorPath = Join-Path $BrowserRun 'chrome-stderr.log'
$ChromeArguments = @(
    '--headless', '--disable-gpu', '--disable-background-networking', '--disable-component-update',
    '--no-first-run', '--no-default-browser-check', '--disable-breakpad', '--disable-sync',
    '--allow-file-access-from-files', '--virtual-time-budget=10000', '--dump-dom',
    '--host-resolver-rules="MAP * ~NOTFOUND"',
    ('--user-data-dir="' + (Join-Path $BrowserRun 'chrome-profile') + '"'),
    ('--disk-cache-dir="' + (Join-Path $BrowserRun 'chrome-cache') + '"'),
    ('--crash-dumps-dir="' + (Join-Path $BrowserRun 'crash-dumps') + '"'),
    $FixtureUri
)
$ChromeProcess = Start-Process -FilePath $ChromePath -ArgumentList $ChromeArguments -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput $DumpPath -RedirectStandardError $ErrorPath
if (-not $ChromeProcess.WaitForExit(45000)) {
    $ChromeProcess.Kill()
    throw "Headless Chrome timed out. Inspect $ErrorPath"
}
$ChromeProcess.Refresh()
if ($ChromeProcess.ExitCode -ne 0) { throw "Headless Chrome failed with exit code $($ChromeProcess.ExitCode). Inspect $ErrorPath" }
# Start-Process can still be flushing redirected output just after the process exits.
$Dom = $null
$ReadDeadline = [DateTime]::UtcNow.AddSeconds(5)
do {
    try { $Dom = [IO.File]::ReadAllText($DumpPath) }
    catch [IO.IOException] {
        if ([DateTime]::UtcNow -ge $ReadDeadline) { throw }
        Start-Sleep -Milliseconds 100
    }
} while ($null -eq $Dom)
$ReportMatch = [regex]::Match($Dom, '<pre id="texbb-browser-result">([^<]*)</pre>')
if (-not $ReportMatch.Success) { throw "MathJax did not finish or the probe was blocked. Inspect $DumpPath and $ErrorPath" }
$Report = [Net.WebUtility]::HtmlDecode($ReportMatch.Groups[1].Value) | ConvertFrom-Json
$Report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $BrowserRun 'browser-result.json') -Encoding UTF8
if ($Report.error) { throw $Report.error }
if ($Report.typesetCount -ne 5 -or $Report.renderedFrames -ne 5) { throw "Expected five rendered equations, got $($Report.typesetCount) jax / $($Report.renderedFrames) frames." }
if ($Report.processingFrames -ne 0) { throw 'MathJax still has unfinished frames.' }
if ($Report.mathErrors -ne 0) { throw "MathJax reported $($Report.mathErrors) math errors." }
if (-not $Report.imageLoaded -or $Report.imageSource -notmatch 'project%20with%20spaces/images/my%20plot.png$') { throw 'The relative local image did not load.' }
if (-not $Report.fragmentSameDocument) { throw 'The heading link left the current preview document.' }
if (-not $Report.codePreserved) { throw 'Math delimiters inside the fenced code block changed.' }
if ($Report.remoteResources.Count -ne 0) { throw 'The offline preview requested a remote resource.' }
if ($Report.localFontFaces -lt 1 -or $Report.loadedMathFonts -lt 1) { throw 'No correctly rooted local MathJax web font finished loading.' }
Write-Output "PASS: Windows Chrome rendered five equations with local MathJax/fonts, loaded the relative image, followed an in-document heading link, preserved code, and used no remote document resources. Evidence: $BrowserRun"
