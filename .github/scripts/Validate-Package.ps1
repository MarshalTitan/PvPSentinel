param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$feed = Join-Path $root '.packages/SentinelCore/v0.3.1.0'
$uiPath = Join-Path $feed 'MarshalTitan.SentinelCore.UI.0.3.1.nupkg'
$corePath = Join-Path $feed 'MarshalTitan.SentinelCore.0.3.1.nupkg'
$expectedUiHash = 'e1a9ce4e1ce36042c0fcd53f4c23874d918640be10eef16c21f1cd436c6ba747'
$actualUiHash = (Get-FileHash -LiteralPath $uiPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualUiHash -ne $expectedUiHash) { throw "Unexpected SentinelCore.UI package hash: $actualUiHash" }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.ZipFile

function Read-ZipEntry([System.IO.Compression.ZipArchive]$archive, [string]$name) {
    $entry = $archive.GetEntry($name)
    if ($null -eq $entry) { throw "Missing package entry: $name" }
    $stream = $entry.Open()
    $buffer = [System.IO.MemoryStream]::new()
    try {
        $stream.CopyTo($buffer)
        return ,$buffer.ToArray()
    }
    finally {
        $stream.Dispose()
        $buffer.Dispose()
    }
}

function Hash-Bytes([byte[]]$bytes) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
}

$zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $PackagePath).Path)
$ui = [System.IO.Compression.ZipFile]::OpenRead($uiPath)
$core = [System.IO.Compression.ZipFile]::OpenRead($corePath)
try {
    $manifest = [Text.Encoding]::UTF8.GetString((Read-ZipEntry $zip 'PvPSentinel.json')) | ConvertFrom-Json
    if ($manifest.InternalName -ne 'PvPSentinel' -or $manifest.AssemblyVersion -ne $ExpectedVersion) {
        throw "Invalid PvPSentinel ZIP manifest; expected $ExpectedVersion."
    }
    $dependencies = [Text.Encoding]::UTF8.GetString((Read-ZipEntry $zip 'PvPSentinel.deps.json')) | ConvertFrom-Json -AsHashtable
    foreach ($name in @("PvPSentinel/$ExpectedVersion", 'MarshalTitan.SentinelCore/0.3.1', 'MarshalTitan.SentinelCore.UI/0.3.1')) {
        if (-not $dependencies.libraries.ContainsKey($name)) { throw "Missing runtime dependency: $name" }
    }

    $bundledUi = Hash-Bytes (Read-ZipEntry $zip 'SentinelCore.UI.dll')
    $pinnedUi = Hash-Bytes (Read-ZipEntry $ui 'lib/net10.0-windows7.0/SentinelCore.UI.dll')
    $bundledCore = Hash-Bytes (Read-ZipEntry $zip 'SentinelCore.dll')
    $pinnedCore = Hash-Bytes (Read-ZipEntry $core 'lib/net10.0/SentinelCore.dll')
    if ($bundledUi -ne $pinnedUi -or $bundledCore -ne $pinnedCore) {
        throw 'Bundled SentinelCore assemblies differ from the pinned v0.3.1.0 packages.'
    }
    Write-Host "Validated PvPSentinel $ExpectedVersion and exact SentinelCore 0.3.1 assemblies."
}
finally {
    $zip.Dispose()
    $ui.Dispose()
    $core.Dispose()
}
