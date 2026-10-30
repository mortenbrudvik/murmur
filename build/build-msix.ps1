# Builds the Store package. Run with the 64-bit PowerShell:
#   & 'C:\Program Files\PowerShell\7\pwsh.exe' -File build/build-msix.ps1
# Publishes self-contained (frontend included), stages the manifest and assets beside it, builds resources.pri, and
# packs. The result is UNSIGNED on purpose: the Store re-signs, and signing with a certificate whose subject is not
# byte-identical to the Partner Center publisher fails validation at upload. Pass -SignWith to produce a sideloadable
# copy for local testing instead. For a quick local run without signing at all, register the staged layout:
#   Add-AppxPackage -Register packaging\msix\staging\AppxManifest.xml   (needs Developer Mode)
param(
    [string]$Configuration = 'Release',
    [string]$SignWith
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$version = (Get-Content (Join-Path $root 'version.json') -Raw | ConvertFrom-Json).version
# An empty $version would name the package Murmur--x64.msix and still satisfy the postcondition that checks the file
# exists, so catch it here.
if ([string]::IsNullOrWhiteSpace($version)) { throw "version.json has no 'version' value" }
$stage = Join-Path $root 'packaging/msix/staging'
$output = Join-Path $root 'installer-output'
$package = Join-Path $output "Murmur-$version-x64.msix"
$manifestPath = Join-Path $root 'packaging/msix/AppxManifest.xml'

# There is no version-manager script: the manifest's Identity Version is kept in step with version.json by hand. A
# mismatch would name the file after one version and submit another, so refuse it before spending a publish on it.
$manifestVersion = ([xml](Get-Content $manifestPath -Raw)).Package.Identity.Version
if ($manifestVersion -ne "$version.0") {
    throw "AppxManifest.xml has Version=""$manifestVersion"" but version.json says $version; set the manifest to $version.0 (the fourth field is the Store's and stays 0)"
}

# The csproj is the one place the BuildTools version is pinned; reading it here instead of hard-coding the number a
# second time means a future version bump cannot leave the two disagreeing.
$csprojPath = Join-Path $root 'src/Murmur.UI/Murmur.UI.csproj'
$csprojText = Get-Content $csprojPath -Raw
if ($csprojText -match 'Microsoft\.Windows\.SDK\.BuildTools"\s+Version="([^"]+)"') {
    $toolsVersion = $Matches[1]
} else {
    throw "no Microsoft.Windows.SDK.BuildTools package reference found in $csprojPath"
}

# NUGET_PACKAGES overrides the default global-packages folder (common on CI runners); honouring it means this resolves
# to wherever dotnet restore actually put the tools.
$nugetPackages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES.TrimEnd('\') } else { "$env:USERPROFILE\.nuget\packages" }
$toolsRoot = "$nugetPackages\microsoft.windows.sdk.buildtools\$toolsVersion\bin"

# Resolved to the pinned version specifically - not "whatever else is cached and sorts last". Sort-Object over every
# cached version is a string sort, so e.g. "10.0.22621.2428" sorts before "10.0.22621.755"; that can silently hand the
# build a toolset nobody vetted for this package.
$tools = Get-ChildItem "$toolsRoot\*\x64" -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $tools) { throw "Microsoft.Windows.SDK.BuildTools $toolsVersion is not restored under $toolsRoot; run dotnet restore first" }

Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
# A delete that stops partway (a locked file, a handle left over from a previous run, or a staged layout still
# registered with Add-AppxPackage -Register and running) must not be accepted silently: New-Item -Force is a no-op on a
# directory that still exists, and every step after it only adds or overwrites, so a stale file would ride along into
# the package unnoticed.
if (Test-Path $stage) { throw "could not remove the stale staging directory $stage; check for a locked file or a running registered copy" }
New-Item -ItemType Directory -Force $stage, $output | Out-Null

# Self-contained because there is no MSIX framework package for the .NET desktop runtime.
# PublishSingleFile is off on purpose: one bundled file defeats MSIX block-level differential updates, and Whisper.net
# loads its native runtimes from runtimes\ folders beside the exe.
# PublishTrimmed is not an option at all - it is a hard SDK error for WPF (NETSDK1168).
# The UI project's BuildFrontend/PublishFrontend targets build the React app and publish it into Assets\.
dotnet publish (Join-Path $root 'src/Murmur.UI') -c $Configuration -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:PublishSingleFile=false `
    -p:SatelliteResourceLanguages=en -p:DebugType=none -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with $LASTEXITCODE" }

# Postconditions on the publish: each of these failing produces a package that installs and then cannot work.
$expected = @(
    'Murmur.UI.exe',
    'Assets\index.html',
    'runtimes\win-x64\whisper.dll',
    'runtimes\cuda\win-x64\ggml-cuda-whisper.dll',
    'runtimes\vulkan\win-x64\ggml-vulkan-whisper.dll'
)
foreach ($file in $expected) {
    if (-not (Test-Path (Join-Path $stage $file))) { throw "publish produced no $file in $stage" }
}
# And the other way round: the UI project's RemoveForeignNativeRuntimes target keeps every other platform's natives out
# (hundreds of MB of Linux .so files); a package layout change in Whisper.net must not quietly bring them back.
$foreign = Get-ChildItem (Join-Path $stage 'runtimes') -Recurse -File | Where-Object { $_.FullName -notmatch '\\win-x64\\' }
if ($foreign) { throw "publish staged native libraries for other platforms: $($foreign.FullName -join ', ')" }

Copy-Item $manifestPath $stage

# The manifest ships an Identity placeholder until the Partner Center reservation exists. It is schema-valid ('Name'
# allows [-.A-Za-z0-9]), so makeappx and this script report success for a package Partner Center rejects on identity
# mismatch. A warning and deliberately not a throw: a hard failure would block the local packaging path.
if ((Get-Content (Join-Path $stage 'AppxManifest.xml') -Raw) -match 'PLACEHOLDER') {
    Write-Warning "the manifest still holds the identity placeholder; this package can be installed locally but cannot be submitted to the Store"
}
Copy-Item (Join-Path $root 'packaging/msix/Assets') $stage -Recurse -Force

# resources.pri is required: a manifest naming Square44x44Logo.png while only scale-qualified files exist fails
# makeappx with 0x80080204.
& "$($tools.FullName)\makepri.exe" new /pr $stage /cf (Join-Path $root 'packaging/msix/priconfig.xml') /of (Join-Path $stage 'resources.pri') /o
if ($LASTEXITCODE -ne 0) { throw "makepri failed with $LASTEXITCODE" }

& "$($tools.FullName)\makeappx.exe" pack /o /d $stage /p $package
if ($LASTEXITCODE -ne 0) { throw "makeappx failed with $LASTEXITCODE" }

if (-not (Test-Path $package)) { throw "makeappx reported success but wrote no package at $package" }

if ($SignWith) {
    # SignTool defaults to SHA1, which is rejected; /fd SHA256 is not optional.
    & "$($tools.FullName)\signtool.exe" sign /fd SHA256 /a /f $SignWith $package
    if ($LASTEXITCODE -ne 0) { throw "signtool failed with $LASTEXITCODE" }
}

"wrote $package ($([math]::Round((Get-Item $package).Length / 1MB, 1)) MB)"
exit 0
