param([Parameter(Mandatory)][string]$PublishDirectory,[Parameter(Mandatory)][string]$OutputDirectory,[Parameter(Mandatory)][string]$Tag)
$ErrorActionPreference='Stop'
if($Tag -notmatch '^v\d+\.\d+\.\d+$'){throw 'Use a numeric release tag.'}
$root=Split-Path -Parent $PSScriptRoot
[xml]$project=Get-Content (Join-Path $root 'src/FishNet/FishNet.csproj')
$version=[string]$project.Project.PropertyGroup.Version
if($Tag -ne 'v'+$version){throw 'Release tag does not match source version.'}
$publish=[IO.Path]::GetFullPath($PublishDirectory)
foreach($name in @('FishNet.exe','FishNet.dll','FishNet.deps.json','FishNet.runtimeconfig.json')){if(!(Test-Path -LiteralPath (Join-Path $publish $name))){throw "Published runtime is incomplete: $name"}}
$identity=[Reflection.AssemblyName]::GetAssemblyName((Join-Path $publish 'FishNet.dll'))
if($identity.Name -ne 'FishNet' -or $identity.Version -ne [Version]($version+'.0')){throw 'Published assembly version differs from release version.'}
if(@(Get-ChildItem -LiteralPath $publish -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count){throw 'Release publish folder contains a link.'}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $publish 'README.md')
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath (Join-Path $OutputDirectory 'FishNet-windows-x64.zip')
$archive=Join-Path $OutputDirectory 'FishNet-windows-x64.zip'
((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()+'  FishNet-windows-x64.zip') | Set-Content (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii
@{application='FishNet';version=$version;source=$env:GITHUB_SHA;runtime='Windows x64, self-contained .NET 8 and Windows App SDK';signed=$false} | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'BUILD-INFO.json') -Encoding utf8
