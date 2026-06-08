<#
  build-cli.ps1  —  AVEVA Marine PC 에서 E3dLeafCli.exe 빌드 + AVEVA bin 으로 복사
  Streamlit(app.py) 가 호출하는 .NET 추출 엔진을 만든다.
  AVEVA 경로/비트수 자동 감지 → msbuild → exe 를 AVEVA bin 폴더로 복사.
#>
param([string]$AvevaBinDir = "", [switch]$NoCopy)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root "E3dLeafCli\E3dLeafCli.csproj"

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
& $msbuild $proj /restore /t:Rebuild /p:Configuration=Release /p:Platform=$platform /p:AvevaBinDir="$AvevaBinDir" /v:minimal
if ($LASTEXITCODE -ne 0) { Write-Host "[오류] 빌드 실패" -ForegroundColor Red; exit 2 }

$outDir = Join-Path $root "E3dLeafCli\bin\$platform\Release\net48"
if (-not (Test-Path $outDir)) { $outDir = Join-Path $root "E3dLeafCli\bin\$platform\Release" }
$exe = Join-Path $outDir "E3dLeafCli.exe"
if (-not (Test-Path $exe)) { Write-Host "[오류] 빌드 산출물 없음: $exe" -ForegroundColor Red; exit 3 }
Write-Host "빌드 완료: $exe" -ForegroundColor Green

if ($NoCopy) { exit 0 }
foreach($f in @("E3dLeafCli.exe","E3dLeafCli.exe.config")){
  $src = Join-Path $outDir $f
  if (Test-Path $src) { Copy-Item $src (Join-Path $AvevaBinDir $f) -Force }
}
Write-Host "복사 완료 → $AvevaBinDir (app.py 가 자동으로 찾습니다)" -ForegroundColor Green
