[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$version = '0.1.0-beta.1'
$staging = Join-Path $repo "dist\Auraline-$version-$Runtime"
$archive = "$staging.zip"
$hostDir = Join-Path $staging 'Host'
$pluginDir = Join-Path $staging 'InfoPanel.Plugin\InfoPanel.Auraline'

if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
New-Item -ItemType Directory -Path $hostDir,$pluginDir | Out-Null

dotnet restore (Join-Path $repo 'src\Auraline.Host\Auraline.Host.csproj') --runtime $Runtime --configfile (Join-Path $repo 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'Host restore failed.' }
dotnet publish (Join-Path $repo 'src\Auraline.Host\Auraline.Host.csproj') --configuration $Configuration --runtime $Runtime --self-contained false --no-restore --output $hostDir
if ($LASTEXITCODE -ne 0) { throw 'Host publish failed.' }
dotnet build (Join-Path $repo 'src\InfoPanel.Auraline\InfoPanel.Auraline.csproj') --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }

$pluginSource = Join-Path $repo 'src\InfoPanel.Auraline\artifacts\InfoPanel.Auraline'
$expectedPluginFiles = @('Auraline.Contracts.dll','InfoPanel.Auraline.deps.json','InfoPanel.Auraline.dll','PluginInfo.ini')
$actualPluginFiles = @(Get-ChildItem -LiteralPath $pluginSource -File | Sort-Object Name | ForEach-Object Name)
if (Compare-Object $expectedPluginFiles $actualPluginFiles) { throw "Plugin package contents do not match the four-file contract: $($actualPluginFiles -join ', ')" }
Copy-Item -Path (Join-Path $pluginSource '*') -Destination $pluginDir
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Beta-README.md') -Destination (Join-Path $staging 'README.md')

$manifest = Get-ChildItem -LiteralPath $staging -Recurse -File |
    Where-Object Name -ne 'checksums.txt' |
    Sort-Object FullName |
    ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($staging, $_.FullName).Replace('\','/')
        '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
    }
Set-Content -LiteralPath (Join-Path $staging 'checksums.txt') -Value $manifest -Encoding utf8NoBOM
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $archive -CompressionLevel Optimal
Get-FileHash -LiteralPath $archive -Algorithm SHA256 | Format-List Algorithm,Hash,Path
