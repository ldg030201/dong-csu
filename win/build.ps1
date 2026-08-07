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
    # 시작 메뉴에 바로가기를 만든다. 테스트판은 설치본이 아니라 검색해도 안 나오는데,
    # 개발하면서 띄울 때마다 폴더를 찾아 들어가는 것은 번거롭다.
    [switch]$Shortcut,
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

if ($Shortcut) {
    # 시작 메뉴의 사용자 영역. 관리자 권한이 필요 없다.
    $programs = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
    $linkPath = Join-Path $programs "$([System.IO.Path]::GetFileNameWithoutExtension($exeName)).lnk"

    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($linkPath)
    $link.TargetPath = $exe
    # 앱이 자기 폴더의 파일(아이콘 등)을 찾을 수 있게 해 둔다.
    $link.WorkingDirectory = $outDir
    $link.IconLocation = $exe
    $link.Description = if ($Test) { 'DongCSU 테스트 빌드 (설치본 아님)' } else { 'DongCSU' }
    $link.Save()

    Write-Host "바로가기: $linkPath"
    Write-Host "  시작 메뉴에서 검색해서 띄울 수 있다. 색인에 잡히는 데 잠깐 걸릴 수 있다."
}

if ($Run) {
    # 같은 판이 이미 떠 있으면 옛 바이너리다. 그것만 내린다 — 다른 판은 건드리지 않는다.
    $processName = [System.IO.Path]::GetFileNameWithoutExtension($exeName)
    Get-Process -Name $processName -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 400
    Start-Process $exe
    Write-Host "띄움: $processName"
}
