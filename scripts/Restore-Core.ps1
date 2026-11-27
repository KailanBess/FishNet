$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$feed = Join-Path $root '.core-feed'
$package = Join-Path $feed 'Aquarium.Core.0.4.0.nupkg'
$expected = 'dfb43dfd7f701d08e94efc79864535a6314bb8fce2f0fbfa66b7878626ba3611'
foreach ($path in @($feed, $package)) {
    if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked dependency paths are not supported.' }
}
if ((Test-Path -LiteralPath $package) -and (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant() -eq $expected) { return }
New-Item -ItemType Directory -Path $feed -Force | Out-Null
$temporary = Join-Path $feed ([Guid]::NewGuid().ToString('N') + '.tmp')
try {
    Invoke-WebRequest 'https://github.com/KailanBess/Aquarium/releases/download/v0.11.0/Aquarium.Core.0.4.0.nupkg' -UseBasicParsing -OutFile $temporary
    if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Aquarium Core package checksum mismatch.' }
    Move-Item -LiteralPath $temporary -Destination $package -Force
} finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
