#requires -Version 7.4
[CmdletBinding()]
param([string]$PublishDirectory = '')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PackageLinuxRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$PackageTemporary = Join-Path $PackageLinuxRoot '.codex-tmp'
$Utf8 = [Text.UTF8Encoding]::new($false)
$VersionFile = Join-Path $PackageLinuxRoot 'release/version.env'
$VersionLines = @([IO.File]::ReadAllLines($VersionFile) | Where-Object { $_ -match '^TEXBB_VERSION=' })
if ($VersionLines.Count -ne 1 -or $VersionLines[0] -notmatch '^TEXBB_VERSION=([0-9]+\.[0-9]+\.[0-9]+~alpha[0-9]+)$') { throw 'Invalid Linux alpha package version.' }
$PackageVersion = $Matches[1]

function Assert-NoLinks([string]$Path) {
    $Current = [IO.Path]::GetFullPath($Path)
    while ($Current) {
        $Item = Get-Item -LiteralPath $Current -Force -ErrorAction SilentlyContinue
        if ($null -ne $Item) {
            if (($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Symbolic links and reparse points are not accepted: $Current"
            }
        }
        $Parent = [IO.Path]::GetDirectoryName($Current)
        if ($Parent -eq $Current) { break }
        $Current = $Parent
    }
}

function Assert-TemporaryChild([string]$Path) {
    $FullPath = [IO.Path]::GetFullPath($Path)
    $Relative = [IO.Path]::GetRelativePath($PackageTemporary, $FullPath)
    if ([IO.Path]::IsPathRooted($Relative) -or $Relative -eq '.' -or $Relative -eq '..' -or
        $Relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar)) {
        throw 'Package inputs and outputs must be children of linux/.codex-tmp.'
    }
    Assert-NoLinks $FullPath
    return $FullPath
}

function Assert-LinuxAppHost([IO.Stream]$Stream) {
    $Header = [byte[]]::new(20)
    $Stream.ReadExactly($Header, 0, $Header.Length)
    if ([Convert]::ToHexString($Header[0..5]) -ne '7F454C460201' -or
        $Header[18] -ne 62 -or $Header[19] -ne 0) {
        throw 'TeXBB must be a little-endian ELF64 x86-64 Linux apphost.'
    }
}

function Write-TarGzip([string]$Path, [object[]]$Files, [string[]]$Directories) {
    $Output = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try {
        $Compressed = [IO.Compression.GZipStream]::new($Output, [IO.Compression.CompressionLevel]::Optimal, $true)
        try {
            $Writer = [System.Formats.Tar.TarWriter]::new($Compressed, [System.Formats.Tar.TarEntryFormat]::Ustar, $true)
            try {
                foreach ($Directory in $Directories) {
                    $Entry = [System.Formats.Tar.UstarTarEntry]::new([System.Formats.Tar.TarEntryType]::Directory, './' + $Directory + '/')
                    $Entry.Mode = [IO.UnixFileMode]493
                    $Entry.Uid = 0; $Entry.Gid = 0; $Entry.UserName = 'root'; $Entry.GroupName = 'root'
                    $Entry.ModificationTime = $PackageTimestamp
                    $Writer.WriteEntry($Entry)
                }
                foreach ($File in $Files) {
                    Assert-NoLinks $File.Source
                    $InputFile = [IO.File]::OpenRead($File.Source)
                    try {
                        $Entry = [System.Formats.Tar.UstarTarEntry]::new([System.Formats.Tar.TarEntryType]::RegularFile, './' + $File.Name)
                        $Entry.Mode = [IO.UnixFileMode]$File.Mode
                        $Entry.Uid = 0; $Entry.Gid = 0; $Entry.UserName = 'root'; $Entry.GroupName = 'root'
                        $Entry.ModificationTime = $PackageTimestamp
                        $Entry.DataStream = $InputFile
                        $Writer.WriteEntry($Entry)
                    }
                    finally { $InputFile.Dispose() }
                }
            }
            finally { $Writer.Dispose() }
        }
        finally { $Compressed.Dispose() }
    }
    finally { $Output.Dispose() }
}

function New-PayloadFile([string]$Name, [string]$Source, [int]$Mode = 420) {
    Assert-NoLinks $Source
    [pscustomobject]@{
        Name = $Name; Source = $Source; Mode = $Mode
        Length = (Get-Item -LiteralPath $Source).Length
        SHA256 = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
    }
}

# The ar reader below is deliberately separate from the writing loop. It rereads
# the completed .deb, decompresses each tar, and checks every payload hash/mode.
function Test-DebArchive([string]$Path, [object[]]$Payload, [string]$ExpectedControl, [string]$EvidenceDirectory) {
    $Archive = [IO.File]::OpenRead($Path)
    $MemberNames = @('debian-binary', 'control.tar.gz', 'data.tar.gz')
    $VerifiedFiles = 0
    try {
        $Magic = [byte[]]::new(8); $Archive.ReadExactly($Magic, 0, 8)
        if ([Text.Encoding]::ASCII.GetString($Magic) -cne "!<arch>`n") { throw 'Invalid ar magic.' }
        foreach ($MemberName in $MemberNames) {
            $Header = [byte[]]::new(60); $Archive.ReadExactly($Header, 0, 60)
            $HeaderText = [Text.Encoding]::ASCII.GetString($Header)
            if ($HeaderText.Substring(0, 16).TrimEnd().TrimEnd('/') -cne $MemberName -or
                $HeaderText.Substring(58, 2) -cne ('`' + "`n")) { throw "Invalid ar member header: $MemberName" }
            $Length = [long]::Parse($HeaderText.Substring(48, 10).Trim(), [Globalization.CultureInfo]::InvariantCulture)
            if ($Length -lt 0 -or $Length -gt $Archive.Length - $Archive.Position) { throw 'Invalid ar member length.' }
            # Members are copied into a fresh verification directory, never extracted as paths from the archive.
            $MemberPath = Join-Path $EvidenceDirectory $MemberName
            $MemberStream = [IO.File]::Open($MemberPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
            try {
                $Buffer = [byte[]]::new(65536); $Remaining = $Length
                while ($Remaining -gt 0) {
                    $Count = $Archive.Read($Buffer, 0, [int][Math]::Min($Remaining, $Buffer.Length))
                    if ($Count -eq 0) { throw 'Truncated ar member.' }
                    $MemberStream.Write($Buffer, 0, $Count); $Remaining -= $Count
                }
            }
            finally { $MemberStream.Dispose() }
            if ($Length % 2 -ne 0 -and $Archive.ReadByte() -ne 10) { throw 'Invalid ar padding.' }
            if ($MemberName -eq 'debian-binary') {
                if ([IO.File]::ReadAllText($MemberPath) -cne "2.0`n") { throw 'Invalid Debian format version.' }
                continue
            }
            $CompressedInput = [IO.File]::OpenRead($MemberPath)
            try {
                $Uncompressed = [IO.Compression.GZipStream]::new($CompressedInput, [IO.Compression.CompressionMode]::Decompress)
                try {
                    $Reader = [System.Formats.Tar.TarReader]::new($Uncompressed)
                    try {
                        $Seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                        while ($null -ne ($Entry = $Reader.GetNextEntry())) {
                            $Name = $Entry.Name
                            if ($Name.StartsWith('./')) { $Name = $Name.Substring(2) }
                            $Name = $Name.TrimEnd('/')
                            if (!$Seen.Add($Name) -or $Name.StartsWith('/') -or $Name.Split('/') -contains '..' -or $Name.Contains('\')) { throw "Unsafe/duplicate tar path: $Name" }
                            if ($Entry.Uid -ne 0 -or $Entry.Gid -ne 0) { throw "Non-root package ownership: $Name" }
                            if ($Entry.EntryType -eq [System.Formats.Tar.TarEntryType]::Directory) {
                                if ([int]$Entry.Mode -ne 493) { throw "Wrong directory mode: $Name" }
                                continue
                            }
                            if ($Entry.EntryType -ne [System.Formats.Tar.TarEntryType]::RegularFile) { throw "Unsupported tar entry: $Name" }
                            if ($MemberName -eq 'control.tar.gz') {
                                if ($Name -cne 'control' -or [int]$Entry.Mode -ne 420) { throw 'Unexpected control entry.' }
                                $TextReader = [IO.StreamReader]::new($Entry.DataStream, $Utf8, $false, 4096, $true)
                                try { $Control = $TextReader.ReadToEnd() } finally { $TextReader.Dispose() }
                                if ($Control -cne $ExpectedControl) { throw 'Control metadata mismatch.' }
                            }
                            else {
                                $Expected = @($Payload | Where-Object Name -CEQ $Name)
                                if ($Expected.Count -ne 1 -or [int]$Entry.Mode -ne $Expected[0].Mode -or $Entry.Length -ne $Expected[0].Length) { throw "Payload metadata mismatch: $Name" }
                                $ActualHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Entry.DataStream))
                                if ($ActualHash -cne $Expected[0].SHA256) { throw "Payload hash mismatch: $Name" }
                                $VerifiedFiles++
                            }
                        }
                        if ($MemberName -eq 'control.tar.gz' -and !$Seen.Contains('control')) { throw 'Missing control file.' }
                        if ($MemberName -eq 'data.tar.gz') {
                            foreach ($File in $Payload) { if (!$Seen.Contains($File.Name)) { throw "Missing payload file: $($File.Name)" } }
                        }
                    }
                    finally { $Reader.Dispose() }
                }
                finally { $Uncompressed.Dispose() }
            }
            finally { $CompressedInput.Dispose() }
        }
        if ($Archive.Position -ne $Archive.Length) { throw 'Unexpected trailing ar data.' }
    }
    finally { $Archive.Dispose() }
    return $VerifiedFiles
}

& git -C $PackageLinuxRoot check-ignore --quiet .codex-tmp/package-deb-cross
if ($LASTEXITCODE -ne 0) { throw 'linux/.codex-tmp must be ignored before packaging.' }
if ([string]::IsNullOrWhiteSpace($PublishDirectory)) { $PublishDirectory = Join-Path $PackageTemporary 'publish/linux-x64' }
$PublishDirectory = Assert-TemporaryChild $PublishDirectory
if (!(Test-Path -LiteralPath $PublishDirectory -PathType Container)) { throw 'Publish directory was not found.' }
$PublishItems = @(Get-ChildItem -LiteralPath $PublishDirectory -Recurse -Force)
foreach ($Item in $PublishItems) { Assert-NoLinks $Item.FullName }
$Application = Join-Path $PublishDirectory 'TeXBB'
$Runtime = Join-Path $PublishDirectory 'libcoreclr.so'
if (!(Test-Path -LiteralPath $Application -PathType Leaf) -or !(Test-Path -LiteralPath $Runtime -PathType Leaf)) { throw 'A self-contained Linux publish containing TeXBB and libcoreclr.so is required.' }
$InputHost = [IO.File]::OpenRead($Application)
try { Assert-LinuxAppHost $InputHost } finally { $InputHost.Dispose() }

$Epoch = if ([string]::IsNullOrWhiteSpace($env:SOURCE_DATE_EPOCH)) { 0L } else { [long]::Parse($env:SOURCE_DATE_EPOCH, [Globalization.CultureInfo]::InvariantCulture) }
if ($Epoch -lt 0) { throw 'SOURCE_DATE_EPOCH must not be negative.' }
$PackageTimestamp = [DateTimeOffset]::FromUnixTimeSeconds($Epoch)
$RunDirectory = Assert-TemporaryChild (Join-Path $PackageTemporary ('packages/deb-cross-' + [Guid]::NewGuid().ToString('N')))
[void][IO.Directory]::CreateDirectory($RunDirectory)
$Generated = Join-Path $RunDirectory 'generated'; [void][IO.Directory]::CreateDirectory($Generated)
$Evidence = Join-Path $RunDirectory 'verified-members'; [void][IO.Directory]::CreateDirectory($Evidence)
$Payload = [Collections.Generic.List[object]]::new()
foreach ($File in ($PublishItems | Where-Object { !$_.PSIsContainer } | Sort-Object FullName)) {
    $Name = 'opt/texbb/' + [IO.Path]::GetRelativePath($PublishDirectory, $File.FullName).Replace('\', '/')
    $Mode = if ($Name -ceq 'opt/texbb/TeXBB' -or $Name.EndsWith('.so', [StringComparison]::Ordinal)) { 493 } else { 420 }
    $Payload.Add((New-PayloadFile $Name $File.FullName $Mode))
}
$Launcher = "#!/bin/sh`nexec /opt/texbb/TeXBB " + '"$@"' + "`n"
[IO.File]::WriteAllText((Join-Path $Generated 'texbb'), $Launcher, $Utf8)
$Payload.Add((New-PayloadFile 'usr/bin/texbb' (Join-Path $Generated 'texbb') 493))
$DesktopEntry = @'
[Desktop Entry]
Type=Application
Name=TeXBB
Comment=Markdown, LaTeX, Typst and PDF workspace
Exec=/usr/bin/texbb
TryExec=/usr/bin/texbb
Icon=texbb
Terminal=false
Categories=Office;Education;
StartupWMClass=TeXBB
'@ -replace "`r`n", "`n"
[IO.File]::WriteAllText((Join-Path $Generated 'texbb.desktop'), $DesktopEntry + "`n", $Utf8)
$Payload.Add((New-PayloadFile 'usr/share/applications/texbb.desktop' (Join-Path $Generated 'texbb.desktop')))
$Payload.Add((New-PayloadFile 'usr/share/icons/hicolor/256x256/apps/texbb.png' (Join-Path $PackageLinuxRoot 'assets/texbb-logo.png')))
$InstalledSize = [long][Math]::Ceiling(($Payload | Measure-Object Length -Sum).Sum / 1024.0)
$Control = @"
Package: texbb
Version: $PackageVersion
Architecture: amd64
Maintainer: TeXBB contributors
Section: editors
Priority: optional
Installed-Size: $InstalledSize
Depends: ca-certificates, tzdata, libc6 (>= 2.35), libgcc-s1, libgssapi-krb5-2, libstdc++6, zlib1g, libicu74 | libicu76 | libicu78 | libicu72 | libicu70, libssl3t64 | libssl3, libx11-6, libice6, libsm6, libfontconfig1, libgtk-3-0t64 | libgtk-3-0, libwebkit2gtk-4.1-0, libsoup-3.0-0, poppler-utils, xdg-utils
Suggests: texlive-latex-base, latexmk, pandoc
Description: TeXBB Linux desktop alpha
 Markdown, LaTeX, Typst and PDF editing workspace with optional external
 compilers. Includes the .NET runtime; native desktop libraries are required.
"@ -replace "`r`n", "`n"
$Control += "`n"
$ControlPath = Join-Path $Generated 'control'; [IO.File]::WriteAllText($ControlPath, $Control, $Utf8)
$Directories = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
foreach ($File in $Payload) {
    $Directory = $File.Name
    while ($Directory.Contains('/')) { $Directory = $Directory.Substring(0, $Directory.LastIndexOf('/')); [void]$Directories.Add($Directory) }
}
Write-TarGzip (Join-Path $RunDirectory 'control.tar.gz') @((New-PayloadFile 'control' $ControlPath)) @()
Write-TarGzip (Join-Path $RunDirectory 'data.tar.gz') $Payload.ToArray() @($Directories)
[IO.File]::WriteAllText((Join-Path $RunDirectory 'debian-binary'), "2.0`n", $Utf8)
$DebPath = Join-Path $RunDirectory ("texbb_" + $PackageVersion + "_amd64.deb")
$Deb = [IO.File]::Open($DebPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
try {
    $Deb.Write([Text.Encoding]::ASCII.GetBytes("!<arch>`n"))
    foreach ($Name in @('debian-binary', 'control.tar.gz', 'data.tar.gz')) {
        $Member = Get-Item -LiteralPath (Join-Path $RunDirectory $Name)
        if ($Member.Length -gt 9999999999) { throw 'ar member exceeds supported size.' }
        $Header = ($Name + '/').PadRight(16) + $Epoch.ToString().PadRight(12) + '0'.PadRight(6) + '0'.PadRight(6) +
            '100644'.PadRight(8) + $Member.Length.ToString().PadRight(10) + '`' + "`n"
        if ($Header.Length -ne 60) { throw 'Invalid ar header field length.' }
        $Deb.Write([Text.Encoding]::ASCII.GetBytes($Header))
        $MemberInput = [IO.File]::OpenRead($Member.FullName)
        try { $MemberInput.CopyTo($Deb) } finally { $MemberInput.Dispose() }
        if ($Member.Length % 2 -ne 0) { $Deb.WriteByte(10) }
    }
}
finally { $Deb.Dispose() }
$VerifiedCount = Test-DebArchive $DebPath $Payload.ToArray() $Control $Evidence
$PackageHash = (Get-FileHash -LiteralPath $DebPath -Algorithm SHA256).Hash
$Payload | Select-Object Name, Mode, Length, SHA256 | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $RunDirectory 'payload-manifest.json') -Encoding utf8NoBOM
$Report = @"
Verified Debian ar member order/version, control metadata, root ownership, safe paths,
directory/file modes, and SHA-256 hashes of all $VerifiedCount payload files.
Input apphost is Linux x86-64 ELF; its archived bytes match the input hash.
Package SHA256: $PackageHash
Package: $DebPath
This is archive verification only. No Linux execution or dpkg installation was performed.
"@
$Report | Set-Content -LiteralPath (Join-Path $RunDirectory 'verification.txt') -Encoding utf8NoBOM
Write-Output $Report
