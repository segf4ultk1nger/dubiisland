param($is_trim, $arch)

$ErrorActionPreference = "Stop"

if ($arch -eq "arm64") {
    throw "arm64 is not a .NET Framework 4.7.2 target. Publish x86 or x64. The package is framework-dependent and requires .NET Framework 4.7.2 or higher installed on the machine."
}

$PUBLISH_TARGET = "..\out\ClassIsland"

if ($(Test-Path ./out) -eq $false) {
    mkdir out
} else {
    rm out/* -Recurse -Force
}
#dotnet clean

./tools/release-gen/generate-secrets.ps1

Write-Host "Publish parameters: TrimAssets=$is_trim, Platform=$arch" 

$publishArgs = @(
    "publish"
    ".\ClassIsland\ClassIsland.csproj"
    "-c", "Release"
    "-p:PublishDir=$PUBLISH_TARGET"
    "-property:DebugType=embedded"
    "-p:TrimAssets=$is_trim"
    "-p:ClassIsland_PlatformTarget=$arch"
    "-p:PlatformTarget=$arch"
    "-p:PublishBuilding=true"
)

$folderProfile = Join-Path (Resolve-Path ".\ClassIsland").Path "Properties\PublishProfiles\FolderProfile.pubxml"
if (Test-Path -LiteralPath $folderProfile) {
    $publishArgs += "-p:PublishProfile=FolderProfile"
}

dotnet @publishArgs

Write-Host "Packaging..." -ForegroundColor Cyan

rm ./out/ClassIsland/*.xml -ErrorAction Continue
7z a ./out/${env:artifact_name}.zip ./out/ClassIsland/* -r

Write-Host "Successfully published to $PUBLISH_TARGET" -ForegroundColor Green

