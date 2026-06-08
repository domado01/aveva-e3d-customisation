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
      $methods = $t.GetMethods("Public,Static,Instance,DeclaredOnly")
      $names = $methods | ForEach-Object { $_.Name } | Sort-Object -Unique
      $hasSession = ($names -contains "Open") -or ($names -contains "Start") -or ($names -contains "Finish")
      if ($hasSession) {
        $lines.Add(("TYPE: " + $t.FullName + "   <<< 세션 클래스"))
        foreach ($m in ($methods | Where-Object { @("Start","Open","Finish","Close","Quit") -contains $_.Name } | Sort-Object Name)) {
          $pstr = ($m.GetParameters() | ForEach-Object {
            $pre = ""
            if ($_.IsOut) { $pre = "out " } elseif ($_.ParameterType.IsByRef) { $pre = "ref " }
            $pre + $_.ParameterType.Name + " " + $_.Name
          }) -join ", "
          $st = ""
          if ($m.IsStatic) { $st = "static " }
          $lines.Add(("   " + $st + $m.ReturnType.Name + " " + $m.Name + "(" + $pstr + ")"))
        }
      } else {
        $lines.Add(("TYPE: " + $t.FullName))
      }
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
