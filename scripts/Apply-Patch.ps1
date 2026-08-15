$ErrorActionPreference = 'Stop'

$officialHash = 'A23F92819F4C8EC6A42115F355C942259C081312A25E47DDDE97B3D6B1C82EE9'

function Find-MidiToKeyDirectory {
    $candidates = [System.Collections.Generic.List[string]]::new()
    $candidates.Add('D:\SteamLibrary\steamapps\common\MIDIToKey')
    $candidates.Add('C:\Program Files (x86)\Steam\steamapps\common\MIDIToKey')
    $candidates.Add('C:\Program Files\Steam\steamapps\common\MIDIToKey')

    $steam = Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue
    if ($steam.SteamPath) {
        $steamPath = $steam.SteamPath -replace '/', '\'
        $candidates.Add((Join-Path $steamPath 'steamapps\common\MIDIToKey'))
        $libraryFile = Join-Path $steamPath 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $libraryFile) {
            $content = Get-Content -LiteralPath $libraryFile -Raw
            foreach ($match in [regex]::Matches($content, '"path"\s+"([^"]+)"')) {
                $libraryPath = $match.Groups[1].Value -replace '\\\\', '\'
                $candidates.Add((Join-Path $libraryPath 'steamapps\common\MIDIToKey'))
            }
        }
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path -LiteralPath (Join-Path $candidate 'SMIDIToKey.exe')) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }

    throw 'MIDIToKey installation was not found. Install MIDIToKey 2.8 from Steam first.'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET 8 SDK or newer is required. Install it from https://dotnet.microsoft.com/download/dotnet/8.0'
}

if (Get-Process -Name 'SMIDIToKey' -ErrorAction SilentlyContinue) {
    throw 'MIDIToKey is running. Close it completely, then run Apply-Patch.cmd again.'
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$payloadProject = Join-Path $repositoryRoot 'src\PatchPayload\PatchPayload.csproj'
$patcherProject = Join-Path $repositoryRoot 'src\Patcher\Patcher.csproj'

& dotnet build $payloadProject -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Patch payload build failed.' }
& dotnet build $patcherProject -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Patcher build failed.' }

$payload = Join-Path $repositoryRoot 'src\PatchPayload\bin\Release\net472\PatchPayload.dll'
$patcher = Join-Path $repositoryRoot 'src\Patcher\bin\Release\net8.0\Patcher.dll'
$appDirectory = Find-MidiToKeyDirectory
$target = Join-Path $appDirectory 'SMIDIToKey.exe'

& dotnet $patcher check $target
if ($LASTEXITCODE -eq 0) {
    Write-Host 'The two-hand piano patch is already installed.' -ForegroundColor Green
    exit 0
}
if ($LASTEXITCODE -ne 1) {
    throw 'Could not inspect the installed executable.'
}

$currentHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
if ($currentHash -ne $officialHash) {
    throw "Unsupported MIDIToKey executable ($currentHash). Restore the official 2.8 EXE before installing this separate patch; nothing was changed."
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $appDirectory "SMIDIToKey.exe.backup-before-two-hand-piano-$stamp"
$temporary = Join-Path $appDirectory 'SMIDIToKey.exe.two-hand-piano.tmp'

try {
    & dotnet $patcher apply $target $payload $temporary
    if ($LASTEXITCODE -ne 0) { throw 'Patch generation failed.' }

    & dotnet $patcher check $temporary
    if ($LASTEXITCODE -ne 0) { throw 'Generated executable verification failed.' }

    Copy-Item -LiteralPath $target -Destination $backup
    Move-Item -LiteralPath $temporary -Destination $target -Force

    Write-Host 'MIDIToKey two-hand piano patch installed successfully.' -ForegroundColor Green
    Write-Host "Backup: $backup"
    Write-Host "Patched SHA-256: $((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash)"
}
finally {
    if (Test-Path -LiteralPath $temporary) {
        Remove-Item -LiteralPath $temporary -Force
    }
}
