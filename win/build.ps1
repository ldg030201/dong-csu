#!/usr/bin/env pwsh
# DongCSU 를 만든다. 맥의 `VARIANT=test ./build.sh` 와 같은 자리다.
#
#   ./build.ps1            정식판   bin/Release/... /DongCSU.exe
#   ./build.ps1 -Test      테스트판 build/test/DongCSU-Test.exe
#   ./build.ps1 -Test -Run 만들고 바로 띄운다
#
# **두 판은 동시에 설치·실행된다.** 어셈블리 이름이 갈리면 설정·기록·토큰 폴더
# (%APPDATA%\DongCSU vs %APPDATA%\DongCSU-Test), 자동 시작 등록 이름, 트레이 문구가
# 함께 갈리고, 테스트판은 자체 업데이트를 걸지 않는다.
[CmdletBinding()]
param(
    [switch]$Test,
    [switch]$Run,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src/DongCSU.App'

if ($Test) {
    $outDir = Join-Path $root 'build/test'
    $exeName = 'DongCSU-Test.exe'
    # **obj 는 갈라 두지 않는다.** BaseIntermediateOutputPath 를 넘기면 참조 프로젝트
    # (DongCSU.Core)까지 따라가고, 옛 obj 안의 AssemblyInfo.cs 가 함께 잡혀서
    # "특성이 중복되었습니다" 로 빌드가 통째로 깨진다. 산출물 폴더만 가르면 충분하다 —
    # 판을 번갈아 만들면 다시 컴파일될 뿐이고, 두 exe 는 서로 덮어쓰지 않는다.
    $args = @('-p:Variant=test', '-o', $outDir)
} else {
    $outDir = Join-Path $root 'src/DongCSU.App/bin' | Join-Path -ChildPath $Configuration | Join-Path -ChildPath 'net10.0-windows/win-x64'
    $exeName = 'DongCSU.exe'
    $args = @()
}

Write-Host "빌드: $(if ($Test) { '테스트판' } else { '정식판' }) ($Configuration)"
& dotnet build $project -c $Configuration @args
if ($LASTEXITCODE -ne 0) { throw "빌드 실패" }

$exe = Join-Path $outDir $exeName
if (-not (Test-Path $exe)) { throw "실행 파일이 없다: $exe" }
Write-Host "만듦: $exe"

if ($Run) {
    # 같은 판이 이미 떠 있으면 옛 바이너리다. 그것만 내린다 — 다른 판은 건드리지 않는다.
    $processName = [System.IO.Path]::GetFileNameWithoutExtension($exeName)
    Get-Process -Name $processName -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 400
    Start-Process $exe
    Write-Host "띄움: $processName"
}
