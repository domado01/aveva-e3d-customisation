<#
  form-build-and-run.ps1  —  AVEVA Marine PC 에서 입력 폼(GUI) 빌드+실행
  AVEVA 경로/비트수 자동 + 빌드 + exe/config/leaf-settings 복사 + 실행.
#>
param([string]$AvevaBinDir = "", [switch]$NoRun)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sln  = Join-Path $root "E3dLeafForm.sln"

$stdNames = @("Aveva.Pdms.Standalone.dll","Aveva.E3D.Standalone.dll","Aveva.Core3D.Standalone.dll")
if ([string]::IsNullOrWhiteSpace($AvevaBinDir)) {
    $propsFile = Join-Path $root "..\Directory.Build.props"
    if (Test-Path $propsFile) {
        $ml = Select-String -Path $propsFile -Pattern "Condition=[^>]*>([^<>]+)</AvevaBinDir>" | Select-Object -First 1
        if ($ml) { $cand = $ml.Matches[0].Groups[1].Value.Trim(); if ($cand -and (Test-Path $cand)) { $AvevaBinDir = $cand } }
    }
}
if ([string]::IsNullOrWhiteSpace($AvevaBinDir)) {
    foreach ($sr in @("C:\AVEVA","C:\Program Files (x86)\AVEVA","C:\Program Files\AVEVA","D:\AVEVA")) {
        if (Test-Path $sr) { $hit = Get-ChildItem $sr -Recurse -Include $stdNames -ErrorAction SilentlyContinue | Select-Object -First 1; if ($hit) { $AvevaBinDir = (Split-Path $hit.FullName -Parent) + "\"; break } }
    }
}
$StandaloneDll = $null
if (-not [string]::IsNullOrWhiteSpace($AvevaBinDir)) { foreach ($n in $stdNames) { $p = Join-Path $AvevaBinDir $n; if (Test-Path $p) { $StandaloneDll = $p; break } } }
if ([string]::IsNullOrWhiteSpace($AvevaBinDir) -or -not $StandaloneDll) { Write-Host "[오류] AVEVA 폴더를 찾지 못함. Directory.Build.props 확인." -ForegroundColor Red; exit 1 }
Write-Host "AVEVA bin: $AvevaBinDir" -ForegroundColor Green

function Get-Plat([string]$dll){ try{ $fs=[IO.File]::OpenRead($dll); $br=New-Object IO.BinaryReader($fs); $fs.Position=0x3C; $pe=$br.ReadInt32(); $fs.Position=$pe+4; $m=$br.ReadUInt16(); $br.Close(); $fs.Close(); if($m -eq 0x8664){"x64"}elseif($m -eq 0x14c){"x86"}else{"x86"} }catch{"x86"} }
$platform = Get-Plat $StandaloneDll
Write-Host "비트수: $platform" -ForegroundColor Green

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = $null
if (Test-Path $vswhere) { $msbuild = & $vswhere -latest -prerelease -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1 }
if (-not $msbuild) { $msbuild = "MSBuild.exe" }
Write-Host "빌드 중 (Release / $platform)..." -ForegroundColor Cyan
& $msbuild $sln /restore /t:Rebuild /p:Configuration=Release /p:Platform=$platform /p:AvevaBinDir="$AvevaBinDir" /v:minimal
if ($LASTEXITCODE -ne 0) { Write-Host "[오류] 빌드 실패" -ForegroundColor Red; exit 2 }

$outDir = Join-Path $root "E3dLeafForm\bin\$platform\Release\net48"
if (-not (Test-Path $outDir)) { $outDir = Join-Path $root "E3dLeafForm\bin\$platform\Release" }
foreach($f in @("E3dLeafForm.exe","E3dLeafForm.exe.config","leaf-settings.config")){
  $src = Join-Path $outDir $f
  if (Test-Path $src) { Copy-Item $src (Join-Path $AvevaBinDir $f) -Force }
}
Write-Host "복사 완료 → $AvevaBinDir" -ForegroundColor Green

if ($NoRun) { exit 0 }
Write-Host "폼 실행 중..." -ForegroundColor Cyan
Push-Location $AvevaBinDir
try { & (Join-Path $AvevaBinDir "E3dLeafForm.exe") } finally { Pop-Location }
