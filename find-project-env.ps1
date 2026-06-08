# find-project-env.ps1
# PDMS 가 프로젝트를 찾는 데 필요한 환경(projects_dir, 프로젝트코드 경로 등)을 수집.
# AM Launcher 가 세팅하는 값을 역추적하기 위함.
$ErrorActionPreference = "SilentlyContinue"
$proj = "SN2661"   # 화면에서 본 프로젝트 코드. 다르면 이 줄만 바꾸세요.

Write-Host "===== 1) 현재 AVEVA/PDMS 관련 환경변수 ====="
Get-ChildItem Env: | Where-Object { $_.Name -match "PDMS|AVEVA|PROJ|MDB|DESIGN|MARINE|$proj|^[A-Z0-9]{2,6}00[0-9]$" } |
  Sort-Object Name | ForEach-Object { Write-Host ("  {0} = {1}" -f $_.Name, $_.Value) }

Write-Host ""
Write-Host "===== 2) 프로젝트 폴더 검색 ($proj) ====="
$roots = @("C:\AVEVA","C:\","D:\","E:\","\\")
foreach ($d in @("C:\AVEVA","D:\","C:\")) {
  if (Test-Path $d) {
    Get-ChildItem $d -Directory -Recurse -Depth 3 -Filter "$proj*" -ErrorAction SilentlyContinue |
      Select-Object -First 6 -ExpandProperty FullName | ForEach-Object { Write-Host "  $_" }
  }
}

Write-Host ""
Write-Host "===== 3) evars / 환경 배치 파일 ====="
foreach ($r in @("C:\AVEVA","C:\Program Files (x86)\AVEVA","C:\Program Files\AVEVA","D:\AVEVA")) {
  if (Test-Path $r) {
    Get-ChildItem $r -Recurse -Include "evars*.bat","*evar*.bat","*.bat" -ErrorAction SilentlyContinue |
      Where-Object { $_.Length -lt 50000 } | Select-Object -First 12 -ExpandProperty FullName | ForEach-Object { Write-Host "  $_" }
  }
}

Write-Host ""
Write-Host "===== 4) AM Launcher / PDMS 바로가기 대상 ====="
try {
  $sh = New-Object -ComObject WScript.Shell
  Get-ChildItem "$env:USERPROFILE\Desktop","$env:ProgramData\Microsoft\Windows\Start Menu\Programs","$env:APPDATA\Microsoft\Windows\Start Menu\Programs" -Recurse -Filter "*.lnk" -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match "AM|PDMS|Marine|Launcher|Design|Outfit" } | Select-Object -First 10 |
    ForEach-Object { $t = $sh.CreateShortcut($_.FullName); Write-Host ("  [{0}]" -f $_.Name); Write-Host ("     target: {0}" -f $t.TargetPath); Write-Host ("     args  : {0}" -f $t.Arguments) }
} catch {}

$dest = Join-Path $env:USERPROFILE "Desktop\project_env.txt"
Write-Host ""
Write-Host "(이 화면을 사진 찍어 보내주세요. 위 1~4 의 결과로 leaf-settings.config 를 채웁니다.)"
