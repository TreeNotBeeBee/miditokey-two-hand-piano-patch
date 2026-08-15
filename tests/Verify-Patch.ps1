param(
    [Parameter(Mandatory = $true)]
    [string]$OfficialExe
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$officialPath = [IO.Path]::GetFullPath($OfficialExe)
$expectedHash = 'A23F92819F4C8EC6A42115F355C942259C081312A25E47DDDE97B3D6B1C82EE9'

if (-not (Test-Path -LiteralPath $officialPath)) {
    throw "Official executable not found: $officialPath"
}
if ((Get-FileHash -LiteralPath $officialPath -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'The supplied executable is not the supported official MIDIToKey 2.8 build.'
}

& dotnet build (Join-Path $repositoryRoot 'src\PatchPayload\PatchPayload.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Patch payload build failed.' }
& dotnet build (Join-Path $repositoryRoot 'src\Patcher\Patcher.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Patcher build failed.' }

$artifactDirectory = Join-Path $repositoryRoot 'artifacts'
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
$output = Join-Path $artifactDirectory 'SMIDIToKey.two-hand-test-patched.exe'
$payload = Join-Path $repositoryRoot 'src\PatchPayload\bin\Release\net472\PatchPayload.dll'
$patcher = Join-Path $repositoryRoot 'src\Patcher\bin\Release\net8.0\Patcher.dll'

& dotnet $patcher apply $officialPath $payload $output
if ($LASTEXITCODE -ne 0) { throw 'Patch generation failed.' }
& dotnet $patcher check $output
if ($LASTEXITCODE -ne 0) { throw 'Patch verification failed.' }

& dotnet run --project (Join-Path $repositoryRoot 'tests\BehaviorChecks\BehaviorChecks.csproj') -c Release -- $output
if ($LASTEXITCODE -ne 0) { throw 'Two-hand keyboard behavior checks failed.' }

Write-Host 'Static patch verification passed. The generated executable was not launched.' -ForegroundColor Green
Write-Host "Output: $output"
Write-Host "SHA-256: $((Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash)"
