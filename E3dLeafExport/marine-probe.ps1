<#
  marine-probe.ps1  —  AVEVA Marine/E3D 설치 PC에서 실행해 필요한 정보를 자동 수집
  --------------------------------------------------------------------------
  목적: App.config / build-and-run.ps1 에 넣을 값을 한 번에 찾아준다.
        (API 키는 없음. 설치경로·비트수·환경변수·프로젝트 목록을 출력)
  사용: 그 PC에서  powershell -ExecutionPolicy Bypass -File .\marine-probe.ps1
        출력 내용을 복사해 전달하면 App.config 를 채워드립니다.
#>
$ErrorActionPreference = "SilentlyContinue"
function H($t){ Write-Host "`n=== $t ===" -ForegroundColor Cyan }

H "1) AVEVA 설치 폴더 / 핵심 DLL"
$roots = @("C:\Program Files (x86)\AVEVA","C:\Program Files\AVEVA","C:\AVEVA","D:\AVEVA","D:\Program Files\AVEVA")
$dll = $null
foreach($r in $roots){
  if(Test-Path $r){
    $dll = Get-ChildItem $r -Recurse -Include "Aveva.E3D.Standalone.dll","Aveva.Core.Database.dll" -ErrorAction SilentlyContinue | Select-Object -First 1
    if($dll){ break }
  }
}
if($dll){
  $bin = Split-Path $dll.FullName -Parent
  Write-Host "AVEVA bin (이 경로를 -AvevaBinDir 로 사용):"
  Write-Host "  $bin\" -ForegroundColor Green
  # 어떤 핵심 DLL 이 있는지
  foreach($n in @("Aveva.Core.Database.dll","Aveva.Core.Database.Filters.dll","Aveva.Core.Utilities.dll","Aveva.E3D.Standalone.dll","Aveva.Core3D.Standalone.dll","PMLNet.dll")){
    $p = Join-Path $bin $n
    Write-Host ("  [{0}] {1}" -f $(if(Test-Path $p){"있음"}else{"없음"}), $n)
  }
} else {
  Write-Host "AVEVA 설치 폴더를 표준 위치에서 못 찾음. 설치 경로를 직접 알려주세요." -ForegroundColor Yellow
}

H "2) 비트수 (x86/x64)"
if($dll){
  try{
    $fs=[IO.File]::OpenRead($dll.FullName); $br=New-Object IO.BinaryReader($fs)
    $fs.Position=0x3C; $pe=$br.ReadInt32(); $fs.Position=$pe+4; $m=$br.ReadUInt16(); $br.Close(); $fs.Close()
    $plat = if($m -eq 0x8664){"x64"} elseif($m -eq 0x14c){"x86"} else {"AnyCPU($m)"}
    Write-Host "  $plat  (csproj PlatformTarget 와 일치시킬 것)" -ForegroundColor Green
  }catch{ Write-Host "  감지 실패" -ForegroundColor Yellow }
}

H "3) AVEVA 실행 바로가기 / evars 배치 (환경변수 출처)"
$lnks = Get-ChildItem "$env:USERPROFILE\Desktop","$env:ProgramData\Microsoft\Windows\Start Menu\Programs" -Recurse -Filter "*.lnk" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match "E3D|Marine|Everything3D|Design|Hull|Outfit" }
$sh = New-Object -ComObject WScript.Shell
foreach($l in $lnks){
  $t = $sh.CreateShortcut($l.FullName)
  Write-Host ("  바로가기: {0}" -f $l.Name)
  Write-Host ("    Target : {0}" -f $t.TargetPath)
  Write-Host ("    Args   : {0}" -f $t.Arguments)
}
Get-ChildItem $roots -Recurse -Filter "evars*.bat" -ErrorAction SilentlyContinue | Select-Object -First 5 | ForEach-Object {
  Write-Host ("  evars 배치: {0}" -f $_.FullName) -ForegroundColor Green
}

H "4) 현재 세션의 AVEVA 관련 환경변수"
Get-ChildItem Env: | Where-Object { $_.Name -match "AVEVA|PML|PROJ|DESIGN|^[A-Z]{3}000$|MARINE|HULL" } |
  Sort-Object Name | ForEach-Object { Write-Host ("  {0} = {1}" -f $_.Name, $_.Value) }

H "5) projects_dir 안의 프로젝트 코드 목록 (xxx000 형태)"
$pd = $env:projects_dir
if(-not $pd -and $bin){ $pd = "" }
if($pd -and (Test-Path $pd)){
  Get-ChildItem $pd -Directory | Select-Object -First 30 | ForEach-Object { Write-Host "  $($_.Name)" }
} else {
  Write-Host "  projects_dir 환경변수 미설정(또는 세션 밖). 4번/3번의 evars 에서 확인하세요." -ForegroundColor Yellow
}

Write-Host "`n위 1~5 출력을 복사해 전달하면 App.config 와 빌드설정을 채워드립니다." -ForegroundColor Cyan
Write-Host "특히 필요한 값: AVEVA bin 경로 / 비트수 / projects_dir / 프로젝트코드 / MDB 이름" -ForegroundColor Cyan
