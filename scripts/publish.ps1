#Requires -Version 5.1
<#
.SYNOPSIS
  发布 LegacyIsland（ClassIsland.exe）：Release + Costura 单文件 → 打 zip → 上传 fuckingfast。
.PARAMETER Arch
  目标架构，默认 x64。
.PARAMETER Test
  测试发布：同样的构建，但输出到 dist-test，包名带实时版本，AES-256 加密且不上传。
.NOTES
  需要 .NET Framework 4.7.2。fuckingfast 的 token 放在 scripts\fuckingfast.token（已被 .gitignore 忽略）。
  测试包密码放在 scripts\zip-password.txt。
#>
param(
    [ValidateSet('x64', 'x86')][string]$Arch = 'x64',
    [switch]$Test
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Root = Resolve-Path (Join-Path $PSScriptRoot '..')
$Proj = Join-Path $Root 'ClassIsland\ClassIsland.csproj'
$Dist = Join-Path $Root $(if ($Test) { 'dist-test' } else { 'dist' })
$Channel = if ($Test) { 'Preview' } else { 'Public' }

function Get-TargetPath {
    param(
        [Parameter(Mandatory)][string]$Project,
        [Parameter(Mandatory)][string]$Configuration,
        [Parameter(Mandatory)][string]$Arch
    )
    $args = @(
        $Project,
        '-getProperty:TargetPath',
        "-p:Configuration=$Configuration",
        "-p:ClassIsland_PlatformTarget=$Arch",
        "-p:PlatformTarget=$Arch"
    )
    $raw = & dotnet msbuild @args | Out-String
    if ($LASTEXITCODE -ne 0) { throw "msbuild -getProperty:TargetPath failed for $Project" }
    $trimmed = $raw.Trim()
    if ($trimmed.StartsWith('{')) {
        $path = ($trimmed | ConvertFrom-Json).Properties.TargetPath
    } else {
        $path = ($trimmed -split "`r?`n" | Where-Object { $_ } | Select-Object -Last 1).Trim()
    }
    if (-not $path) { throw "Could not resolve TargetPath for $Project" }
    return $path
}

function Invoke-WithRetry {
    param(
        [Parameter(Mandatory)][scriptblock]$Action,
        [string]$What = 'request',
        [int]$Attempts = 4,
        [int]$DelaySeconds = 5
    )
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            return & $Action
        }
        catch {
            if ($attempt -ge $Attempts) { throw }
            Write-Host ("  {0} failed (attempt {1}/{2}): {3}" -f $What, $attempt, $Attempts, $_.Exception.Message)
            Start-Sleep -Seconds $DelaySeconds
        }
    }
}

function Get-SevenZip {
    foreach ($name in @('7z', '7za', '7zr')) {
        $cmd = Get-Command $name -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
    }

    $candidates = @(
        (Join-Path $env:ProgramFiles '7-Zip\7z.exe'),
        (Join-Path ${env:ProgramFiles(x86)} '7-Zip\7z.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\7-Zip\7z.exe')
    )
    foreach ($path in $candidates) {
        if ($path -and (Test-Path -LiteralPath $path)) { return $path }
    }

    throw "7-Zip not found; it is needed to encrypt the test package."
}

if (Test-Path $Dist) { Remove-Item $Dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Dist | Out-Null

Write-Host "=== Build (Release, $Arch, Costura) ==="
& dotnet build $Proj -c Release -p:ClassIsland_PlatformTarget=$Arch -p:PlatformTarget=$Arch -p:PublishBuilding=true
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
$exe = Get-TargetPath -Project $Proj -Configuration Release -Arch $Arch
if (-not (Test-Path $exe)) { throw "Missing $exe" }
Copy-Item -LiteralPath $exe -Destination (Join-Path $Dist 'ClassIsland.exe')

# net472 仍然需要 app.config 里的绑定重定向，Costura 会处理程序集解析，但配置文件还是随包带上更稳。
$exeConfig = "$exe.config"
if (Test-Path -LiteralPath $exeConfig) {
    Copy-Item -LiteralPath $exeConfig -Destination (Join-Path $Dist 'ClassIsland.exe.config')
}

$versionSource = Join-Path $Root 'Global.props'
$versionText = Get-Content -Raw -LiteralPath $versionSource
if ($versionText -notmatch '<LegacyIslandBaseVersion[^>]*>([^<]+)</LegacyIslandBaseVersion>') {
    throw "Could not read LegacyIslandBaseVersion from $versionSource"
}
$version = $Matches[1].Trim()
$zipName = "LegacyIsland-$version-SIFWARE.$Channel.zip"
if ($Test) {
    $realtime = (Get-Item -LiteralPath (Join-Path $Dist 'ClassIsland.exe')).VersionInfo.ProductVersion
    if ($realtime) { $zipName = "LegacyIsland-$version-SIFWARE.$Channel.$($realtime -replace '\+', '.').zip" }
}

$zipPath = Join-Path $Dist $zipName
if ($Test) {
    $passwordFile = Join-Path $PSScriptRoot 'zip-password.txt'
    if (-not (Test-Path -LiteralPath $passwordFile)) { throw "Missing $passwordFile" }
    $zipPassword = (Get-Content -Raw -LiteralPath $passwordFile).Trim()
    if (-not $zipPassword) { throw "Empty zip password: $passwordFile" }

    $sevenZip = Get-SevenZip
    $names = @('ClassIsland.exe', 'ClassIsland.exe.config') | Where-Object { Test-Path -LiteralPath (Join-Path $Dist $_) }
    Push-Location $Dist
    try {
        & $sevenZip a -tzip -y "-p$zipPassword" -mem=AES256 "$zipPath" @names
        if ($LASTEXITCODE -ne 0) { throw "7-Zip failed to create the encrypted test package (exit $LASTEXITCODE)" }
    }
    finally {
        Pop-Location
    }
} else {
    $pack = @((Join-Path $Dist 'ClassIsland.exe'))
    if (Test-Path -LiteralPath (Join-Path $Dist 'ClassIsland.exe.config')) {
        $pack += (Join-Path $Dist 'ClassIsland.exe.config')
    }
    Compress-Archive -LiteralPath $pack -DestinationPath $zipPath -Force
}

Write-Host ""
if ($Test) {
    Write-Host "=== Upload skipped (test publish) ==="
} else {
    Write-Host "=== Upload ==="
    $ffTokenFile = Join-Path $PSScriptRoot 'fuckingfast.token'
    $ffRootId = 'g6w2ywy7elxj'
    $ffLocationId = 'g65gmc5rfv2c' # Asia-Pacific
    if (-not (Test-Path -LiteralPath $ffTokenFile)) {
        Write-Host "  skipped: $ffTokenFile not found; zip not uploaded"
    } else {
        $ffToken = (Get-Content -Raw -LiteralPath $ffTokenFile).Trim()
        if (-not $ffToken) { throw "Empty fuckingfast token: $ffTokenFile" }
        $ffHeaders = @{ Authorization = "Bearer $ffToken" }

        # 同名重复上传会被拒绝（400），先删掉旧的。
        $listing = Invoke-WithRetry -What "fuckingfast listing" -Action {
            Invoke-RestMethod -Uri "https://fuckingfast.net/api/fs/$ffRootId" -Headers $ffHeaders -TimeoutSec 60
        }
        foreach ($child in @($listing.data.children)) {
            if ($child -and -not $child.isDirectory -and $child.name -eq $zipName) {
                Invoke-WithRetry -What ("delete old " + $child.name) -Action {
                    Invoke-RestMethod -Method Delete -Uri "https://fuckingfast.net/api/fs/$($child.id)" -Headers $ffHeaders -TimeoutSec 60 | Out-Null
                }
                Write-Host ("  replaced old " + $child.name + " (" + $child.id + ")")
            }
        }

        $uri = "https://w.fuckingfast.net/$ffRootId/${zipName}?locationId=$ffLocationId"
        $upload = Invoke-WithRetry -What "upload" -Attempts 5 -DelaySeconds 8 -Action {
            Invoke-RestMethod -Method Put -Uri $uri -Headers $ffHeaders -InFile $zipPath -ContentType 'application/octet-stream' -TimeoutSec 900
        }
        Write-Host ("  uploaded " + $upload.data.name)
        Write-Host ("  id=" + $upload.data.id + "  location=" + $upload.data.locationId + "  size=" + $upload.data.size)
    }
}

Write-Host ""
Write-Host "Published:"
Get-ChildItem $Dist -File | ForEach-Object {
    Write-Host ("  " + $_.Name + "  (" + $_.Length + " bytes)")
}
