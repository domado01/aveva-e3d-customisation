<#
  web-build-and-run.ps1  —  E3D/Marine 설치 PC에서 웹(Standalone) 호스트 원클릭 빌드+실행
  --------------------------------------------------------------------------
  1) AVEVA 설치 폴더 자동 탐지  2) 비트수 감지  3) Release 빌드
  4) E3dLeafWeb.Standalone.exe + E3dLeafCore.dll + config 를 AVEVA 폴더로 복사
  5) 실행 → 브라우저에서 http://127.0.0.1:8731/ 접속

  사용:
    .\web-build-and-run.ps1
    .\web-build-and-run.ps1 -AvevaBinDir "C:\Program Files (x86)\AVEVA\Everything3D2.10\"
    .\web-build-and-run.ps1 -NoRun        # 빌드/복사만
#>
param([string]$AvevaBinDir = "", [switch]$NoRun)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sln  = Join-Path $root "E3dLeafWeb.sln"

# 1) AVEVA 폴더 결정
#    Standalone DLL 후보: Marine(PDMS) / E3D / Core3D
$stdNames = @("Aveva.Pdms.Standalone.dll","Aveva.E3D.Standalone.dll","Aveva.Core3D.Standalone.dll")
# (a) Directory.Build.props 의 AvevaBinDir 우선 사용
if ([string]::IsNullOrWhiteSpace($AvevaBinDir)) {
    $propsFile = Join-Path $root "..\Directory.Build.props"
    if (Test-Path $propsFile) {
        $ml = Select-String -Path $propsFile -Pattern "Condition=[^>]*>([^<>]+)</AvevaBinDir>" | Select-Object -First 1
        if ($ml) { $cand = $ml.Matches[0].Groups[1].Value.Trim(); if ($cand -and (Test-Path $cand)) { $AvevaBinDir = $cand } }
    }
}
# (b) 표준 위치에서 Standalone DLL 자동 탐지
if ([string]::IsNullOrWhiteSpace($AvevaBinDir)) {
    foreach ($sr in @("C:\AVEVA","C:\Program Files (x86)\AVEVA","C:\Program Files\AVEVA","D:\AVEVA")) {
        if (Test-Path $sr) {
            $hit = Get-ChildItem $sr -Recurse -Include $stdNames -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($hit) { $AvevaBinDir = (Split-Path $hit.FullName -Parent) + "\"; break }
        }
    }
}
# 존재하는 Standalone DLL 찾기
$StandaloneDll = $null
if (-not [string]::IsNullOrWhiteSpace($AvevaBinDir)) {
    foreach ($n in $stdNames) { $p = Join-Path $AvevaBinDir $n; if (Test-Path $p) { $StandaloneDll = $p; break } }
}
if ([string]::IsNullOrWhiteSpace($AvevaBinDir) -or -not $StandaloneDll) {
    Write-Host "[오류] AVEVA 폴더를 찾지 못함. -AvevaBinDir 로 지정하거나 Directory.Build.props 의 AvevaBinDir 를 확인하세요." -ForegroundColor Red; exit 1
}
Write-Host "AVEVA bin: $AvevaBinDir" -ForegroundColor Green
Write-Host "Standalone: $(Split-Path $StandaloneDll -Leaf)" -ForegroundColor Green

# 2) 비트수
function Get-Plat([string]$dll){
  try{ $fs=[IO.File]::OpenRead($dll); $br=New-Object IO.BinaryReader($fs)
    $fs.Position=0x3C; $pe=$br.ReadInt32(); $fs.Position=$pe+4; $m=$br.ReadUInt16(); $br.Close(); $fs.Close()
    if($m -eq 0x8664){"x64"}elseif($m -eq 0x14c){"x86"}else{"x86"} }catch{"x86"}
}
$platform = Get-Plat $StandaloneDll
Write-Host "비트수: $platform" -ForegroundColor Green

# 3) MSBuild 빌드
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = $null
if (Test-Path $vswhere) { $msbuild = & $vswhere -latest -prerelease -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1 }
if (-not $msbuild) { $msbuild = "MSBuild.exe" }
Write-Host "빌드 중 (Release / $platform)..." -ForegroundColor Cyan
& $msbuild $sln /t:Rebuild /p:Configuration=Release /p:Platform=$platform /p:AvevaBinDir="$AvevaBinDir" /v:minimal
if ($LASTEXITCODE -ne 0) { Write-Host "[오류] 빌드 실패" -ForegroundColor Red; exit 2 }

# 4) 산출물 복사 (모든 AVEVA dll 이 있는 폴더에서 실행해야 의존성 해결됨)
$outDir = Join-Path $root "E3dLeafWeb.Standalone\bin\$platform\Release\net48"
if (-not (Test-Path $outDir)) { $outDir = Join-Path $root "E3dLeafWeb.Standalone\bin\$platform\Release" }
foreach($f in @("E3dLeafWeb.Standalone.exe","E3dLeafWeb.Standalone.exe.config","E3dLeafCore.dll")){
  $src = Join-Path $outDir $f
  if (Test-Path $src) { Copy-Item $src (Join-Path $AvevaBinDir $f) -Force }
}
Write-Host "복사 완료 → $AvevaBinDir" -ForegroundColor Green
Write-Host "  (포트/LAN 설정은 $AvevaBinDir\E3dLeafWeb.Standalone.exe.config 에서)" -ForegroundColor Yellow

# 5) 실행
if ($NoRun) { Write-Host "-NoRun: 실행 생략" -ForegroundColor Yellow; exit 0 }
Write-Host "`n실행 → 브라우저에서 http://127.0.0.1:8731/ 접속" -ForegroundColor Cyan
Push-Location $AvevaBinDir
try { & (Join-Path $AvevaBinDir "E3dLeafWeb.Standalone.exe") } finally { Pop-Location }
