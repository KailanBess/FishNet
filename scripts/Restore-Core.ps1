$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$feed = Join-Path $root '.core-feed'
$package = Join-Path $feed 'Aquarium.Core.0.2.0.nupkg'
$expected = 'aa5a7a96513a11feef7b2f7fc2306b19086e50ecb6b96b6f9cfdc723c273b695'
foreach ($path in @($feed, $package)) {
    if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked dependency paths are not supported.' }
}
if ((Test-Path -LiteralPath $package) -and (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant() -eq $expected) { return }
New-Item -ItemType Directory -Path $feed -Force | Out-Null
$temporary = Join-Path $feed ([Guid]::NewGuid().ToString('N') + '.tmp')
try {
    Invoke-WebRequest 'https://github.com/KailanBess/Aquarium/releases/download/v0.6.0/Aquarium.Core.0.2.0.nupkg' -UseBasicParsing -OutFile $temporary
    if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Aquarium Core package checksum mismatch.' }
    Move-Item -LiteralPath $temporary -Destination $package -Force
} finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
