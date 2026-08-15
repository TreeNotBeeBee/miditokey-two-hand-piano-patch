$ErrorActionPreference = 'Stop'

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

    throw 'MIDIToKey installation was not found.'
}

if (Get-Process -Name 'SMIDIToKey' -ErrorAction SilentlyContinue) {
    throw 'MIDIToKey is running. Close it completely, then run Restore-Backup.cmd again.'
}

$appDirectory = Find-MidiToKeyDirectory
$target = Join-Path $appDirectory 'SMIDIToKey.exe'
$backups = @(
    Get-ChildItem -LiteralPath $appDirectory -File |
        Where-Object { $_.Name -like 'SMIDIToKey.exe.backup-before-two-hand-piano-*' } |
        Sort-Object LastWriteTime -Descending
)

if ($backups.Count -eq 0) {
    throw 'No backup created by Apply-Patch was found. Use Steam Verify installed files instead.'
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$safetyCopy = Join-Path $appDirectory "SMIDIToKey.exe.backup-before-restore-$stamp"
$temporary = Join-Path $appDirectory 'SMIDIToKey.exe.restore.tmp'

try {
    Copy-Item -LiteralPath $target -Destination $safetyCopy
    Copy-Item -LiteralPath $backups[0].FullName -Destination $temporary
    Move-Item -LiteralPath $temporary -Destination $target -Force
    Write-Host "Restored: $($backups[0].FullName)" -ForegroundColor Green
    Write-Host "Previous patched file kept as: $safetyCopy"
}
finally {
    if (Test-Path -LiteralPath $temporary) {
        Remove-Item -LiteralPath $temporary -Force
    }
}
