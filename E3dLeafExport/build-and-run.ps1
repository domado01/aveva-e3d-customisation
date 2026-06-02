<#
  build-and-run.ps1  —  E3D 설치 PC에서 원클릭 빌드+실행
  --------------------------------------------------------------------------
  하는 일:
    1) AVEVA 설치 폴더(Aveva.E3D.Standalone.dll 위치) 자동 탐지
    2) 그 dll의 비트수(x86/x64) 자동 감지
    3) 실 프로젝트(E3dLeafExport)를 해당 경로/비트수로 Release 빌드
    4) 빌드된 exe + config 를 AVEVA 폴더로 복사(모든 의존 dll이 거기 있으므로 실행 가능)
    5) 실행

  사용:
    # App.config 에 PROJECT/USER/PASSWORD/MDB/환경변수 를 먼저 채운 뒤:
    powershell -ExecutionPolicy Bypass -File .\build-and-run.ps1
    # 경로를 직접 지정하려면:
    .\build-and-run.ps1 -AvevaBinDir "C:\Program Files (x86)\AVEVA\Everything3D2.10\"
    # 빌드만(실행 안 함):
    .\build-and-run.ps1 -NoRun
#>
param(
    [string]$AvevaBinDir = "",
    [switch]$NoRun
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sln  = Join-Path $root "E3dLeafExport.sln"

# --- 1) AVEVA 설치 폴더 자동 탐지 -------------------------------------------
if ([string]::IsNullOrWhiteSpace($AvevaBinDir)) {
    Write-Host "AVEVA 설치 폴더 탐지 중..." -ForegroundColor Cyan
    $searchRoots = @("C:\Program Files (x86)\AVEVA","C:\Program Files\AVEVA","C:\AVEVA","D:\AVEVA")
    foreach ($sr in $searchRoots) {
        if (Test-Path $sr) {
            $hit = Get-ChildItem $sr -Recurse -Filter "Aveva.E3D.Standalone.dll" -ErrorAction SilentlyContinue |
                   Select-Object -First 1
            if ($hit) { $AvevaBinDir = (Split-Path $hit.FullName -Parent) + "\"; break }
        }
    }
}
if ([string]::IsNullOrWhiteSpace($AvevaBinDir) -or -not (Test-Path (Join-Path $AvevaBinDir "Aveva.E3D.Standalone.dll"))) {
    Write-Host "[오류] AVEVA 설치 폴더를 찾지 못했습니다." -ForegroundColor Red
    Write-Host "       -AvevaBinDir 로 Aveva.E3D.Standalone.dll 가 있는 폴더를 직접 지정하세요." -ForegroundColor Red
    exit 1
}
Write-Host "AVEVA bin: $AvevaBinDir" -ForegroundColor Green

# --- 2) 비트수 자동 감지 (PE 헤더 읽기) -------------------------------------
function Get-DllPlatform([string]$dll) {
    try {
        $fs = [System.IO.File]::OpenRead($dll); $br = New-Object System.IO.BinaryReader($fs)
        $fs.Position = 0x3C; $peOff = $br.ReadInt32()
        $fs.Position = $peOff + 4; $machine = $br.ReadUInt16()
        $br.Close(); $fs.Close()
        if ($machine -eq 0x8664) { return "x64" } elseif ($machine -eq 0x14c) { return "x86" } else { return "AnyCPU" }
    } catch { return "x86" }
}
$platform = Get-DllPlatform (Join-Path $AvevaBinDir "Aveva.E3D.Standalone.dll")
Write-Host "감지된 비트수: $platform" -ForegroundColor Green

# --- 3) MSBuild 찾기 + 빌드 --------------------------------------------------
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = $null
if (Test-Path $vswhere) {
    $msbuild = & $vswhere -latest -prerelease -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
}
if (-not $msbuild) { $msbuild = "MSBuild.exe" }  # PATH 에 있다고 가정
Write-Host "MSBuild: $msbuild" -ForegroundColor Green

Write-Host "`n빌드 중 (Release / $platform)..." -ForegroundColor Cyan
& $msbuild $sln /t:Rebuild /p:Configuration=Release /p:Platform=$platform /p:AvevaBinDir="$AvevaBinDir" /v:minimal
if ($LASTEXITCODE -ne 0) { Write-Host "[오류] 빌드 실패" -ForegroundColor Red; exit 2 }

# --- 4) exe + config 를 AVEVA 폴더로 복사 -----------------------------------
$outDir = Join-Path $root "E3dLeafExport\bin\$platform\Release\net48"
if (-not (Test-Path $outDir)) { $outDir = Join-Path $root "E3dLeafExport\bin\$platform\Release" }
$exe = Join-Path $outDir "E3dLeafExport.exe"
$cfg = Join-Path $outDir "E3dLeafExport.exe.config"
if (-not (Test-Path $exe)) { Write-Host "[오류] 빌드 산출물 없음: $exe" -ForegroundColor Red; exit 3 }

$runExe = Join-Path $AvevaBinDir "E3dLeafExport.exe"
Copy-Item $exe $runExe -Force
if (Test-Path $cfg) { Copy-Item $cfg (Join-Path $AvevaBinDir "E3dLeafExport.exe.config") -Force }
Write-Host "복사 완료 → $runExe" -ForegroundColor Green
Write-Host "  (※ App.config 값은 위 .config 에서 최종 확인/수정하세요)" -ForegroundColor Yellow

# --- 5) 실행 ----------------------------------------------------------------
if ($NoRun) { Write-Host "`n-NoRun 지정: 실행 생략." -ForegroundColor Yellow; exit 0 }
Write-Host "`n실행 중..." -ForegroundColor Cyan
Push-Location $AvevaBinDir
try { & $runExe } finally { Pop-Location }
Write-Host "`n종료 코드: $LASTEXITCODE" -ForegroundColor Cyan
