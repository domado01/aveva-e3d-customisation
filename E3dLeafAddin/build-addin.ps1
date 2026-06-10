<#
  build-addin.ps1  —  AVEVA Marine PC 에서 E3dLeafAddin.dll 빌드 + AVEVA bin 복사
  AM 에 로드할 애드인 DLL 을 만든다. (등록 방법은 ADDIN_README.md 참고)
#>
param([string]$AvevaBinDir = "", [switch]$NoCopy)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root "E3dLeafAddin\E3dLeafAddin.csproj"

$stdNames = @("Aveva.Pdms.Standalone.dll","Aveva.E3D.Standalone.dll","Aveva.Pdms.Database.dll")
if ([string]::IsNullOrWhiteSpace($AvevaBinDir)) {
    $propsFile = Join-Path $root "..\Directory.Build.props"
    if (Test-Path $propsFile) {
        $ml = Select-String -Path $propsFile -Pattern "Condition=[^>]*>([^<>]+)</AvevaBinDir>" | Select-Object -First 1
        if ($ml) { $cand = $ml.Matches[0].Groups[1].Value.Trim(); if ($cand -and (Test-Path $cand)) { $AvevaBinDir = $cand } }
    }
}
if ([string]::IsNullOrWhiteSpace($AvevaBinDir)) {
    foreach ($sr in @("C:\AVEVA","C:\Program Files (x86)\AVEVA","D:\AVEVA")) {
        if (Test-Path $sr) { $hit = Get-ChildItem $sr -Recurse -Include "Aveva.Pdms.Database.dll" -ErrorAction SilentlyContinue | Select-Object -First 1; if ($hit) { $AvevaBinDir = (Split-Path $hit.FullName -Parent) + "\"; break } }
    }
}
if ([string]::IsNullOrWhiteSpace($AvevaBinDir) -or -not (Test-Path (Join-Path $AvevaBinDir "Aveva.Pdms.Database.dll"))) {
    Write-Host "[오류] AVEVA bin 을 찾지 못함. Directory.Build.props 확인." -ForegroundColor Red; exit 1
}
Write-Host "AVEVA bin: $AvevaBinDir" -ForegroundColor Green
if (-not (Test-Path (Join-Path $AvevaBinDir "Aveva.ApplicationFramework.dll"))) {
    Write-Host "[주의] Aveva.ApplicationFramework.dll 이 그 폴더에 없습니다. 애드인 인터페이스 어셈블리 이름이 다를 수 있어요." -ForegroundColor Yellow
    Write-Host "       빌드 오류가 나면 그 폴더의 Aveva.*.dll 목록을 알려주세요." -ForegroundColor Yellow
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = $null
if (Test-Path $vswhere) { $msbuild = & $vswhere -latest -prerelease -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1 }
if (-not $msbuild) { $msbuild = "MSBuild.exe" }
Write-Host "빌드 중 (Release / x86)..." -ForegroundColor Cyan
& $msbuild $proj /restore /t:Rebuild /p:Configuration=Release /p:Platform=x86 /p:AvevaBinDir="$AvevaBinDir" /v:minimal
if ($LASTEXITCODE -ne 0) { Write-Host "[오류] 빌드 실패" -ForegroundColor Red; exit 2 }

$outDir = Join-Path $root "E3dLeafAddin\bin\x86\Release\net48"
if (-not (Test-Path $outDir)) { $outDir = Join-Path $root "E3dLeafAddin\bin\x86\Release" }
$dll = Join-Path $outDir "E3dLeafAddin.dll"
if (-not (Test-Path $dll)) { Write-Host "[오류] 산출물 없음: $dll" -ForegroundColor Red; exit 3 }
Write-Host "빌드 완료: $dll" -ForegroundColor Green

if ($NoCopy) { exit 0 }
Copy-Item $dll (Join-Path $AvevaBinDir "E3dLeafAddin.dll") -Force
Write-Host "복사 완료 → $AvevaBinDir" -ForegroundColor Green
Write-Host "다음: ADDIN_README.md 의 '등록' 절차대로 AM 에 애드인을 등록하고 AM 재시작." -ForegroundColor Cyan
Write-Host "로드 확인: C:\Users\Public\Documents\leaf_addin_status.txt 가 생기면 성공." -ForegroundColor Cyan
