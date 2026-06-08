# find-standalone.ps1
# Aveva.Pdms.Standalone.dll 안의 public 타입과 (Start/Open/Finish) 메서드를 모두 출력.
# 어떤 클래스가 세션을 여는지(=우리가 쓸 클래스) 찾기 위함.
$ErrorActionPreference = "SilentlyContinue"

$bin = "C:\AVEVA\Marine\OH12.1.SP5"
$props = Join-Path $PSScriptRoot "Directory.Build.props"
if (Test-Path $props) {
  $m = Select-String -Path $props -Pattern "Condition=[^>]*>([^<>]+)</AvevaBinDir>" | Select-Object -First 1
  if ($m) { $p = $m.Matches[0].Groups[1].Value.Trim().TrimEnd([char]92); if (Test-Path $p) { $bin = $p } }
}
$dll = Join-Path $bin "Aveva.Pdms.Standalone.dll"
Write-Host ("DLL: " + $dll)
Write-Host ""

$lines = New-Object System.Collections.Generic.List[string]
if (Test-Path $dll) {
  try {
    $a = [Reflection.Assembly]::LoadFrom($dll)
    try { $ts = $a.GetTypes() } catch { $ts = $_.Exception.Types | Where-Object { $_ } }
    foreach ($t in ($ts | Where-Object { $_ -and $_.IsPublic } | Sort-Object FullName)) {
      $mlist = $t.GetMethods("Public,Static,Instance,DeclaredOnly") | ForEach-Object { $_.Name } | Sort-Object -Unique
      $hasSession = ($mlist -contains "Open") -or ($mlist -contains "Start") -or ($mlist -contains "Finish")
      $tag = ""
      if ($hasSession) { $tag = "   <<< 세션 클래스 (Open/Start/Finish 있음)" }
      $lines.Add(("TYPE: " + $t.FullName + $tag))
      $lines.Add(("   methods: " + (($mlist) -join ", ")))
    }
  } catch { $lines.Add("LOAD ERROR: " + $_.Exception.Message) }
} else {
  $lines.Add("NOT FOUND: " + $dll)
}

$dest = Join-Path $env:USERPROFILE "Desktop\standalone_types.txt"
$lines | Set-Content -Path $dest -Encoding UTF8
$lines | ForEach-Object { Write-Host $_ }
Write-Host ""
Write-Host ("==> 위에서 '<<< 세션 클래스' 표시된 줄의 TYPE 전체이름을 보내주세요.")
Write-Host ("(바탕화면 standalone_types.txt 에도 저장됨)")
