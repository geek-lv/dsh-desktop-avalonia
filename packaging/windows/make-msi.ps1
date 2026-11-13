# Build dist\DeepSeek-Harness-Desktop-<version>-win-x64.msi
#   pwsh packaging/windows/make-msi.ps1            # version from DshDesktop.csproj
#   $env:VERSION='2.1.0'; pwsh packaging/windows/make-msi.ps1
# Needs the .NET 10 SDK and the MSVC build tools (Native AOT links with link.exe).
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$csproj = Join-Path $root 'DshDesktop.csproj'
# VERSION (e.g. from a release tag) overrides the csproj <Version>.
$version = if ($env:VERSION) { $env:VERSION } else { ([xml](Get-Content $csproj)).SelectSingleNode('/Project/PropertyGroup/Version').InnerText }
$rid = 'win-x64'
$publish = Join-Path $root "dist\$rid\app"
$out = Join-Path $root 'dist'

# The AOT toolchain locates MSVC through vswhere, which is not on PATH by default outside CI.
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if ((Test-Path (Join-Path $installer 'vswhere.exe')) -and ($env:PATH -notlike "*$installer*")) {
    $env:PATH = "$env:PATH;$installer"
}

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $csproj -c Release -r $rid -o $publish -p:Version=$version
if ($LASTEXITCODE) { throw "dotnet publish failed ($LASTEXITCODE)" }

$wixproj = Join-Path $PSScriptRoot 'DshDesktop.Installer.wixproj'
$wixOut = Join-Path $root "dist\$rid\msi"
dotnet build $wixproj -c Release -p:Version=$version -p:PublishDir=$publish -o $wixOut
if ($LASTEXITCODE) { throw "wix build failed ($LASTEXITCODE)" }

$msi = Join-Path $out "DeepSeek-Harness-Desktop-$version-$rid.msi"
Move-Item (Join-Path $wixOut 'DshDesktop.msi') $msi -Force
Write-Host "built: $msi ($([math]::Round((Get-Item $msi).Length / 1MB, 1)) MB)"
