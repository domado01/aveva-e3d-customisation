# check-types.ps1
# AVEVA(PDMS) 타입의 실제 네임스페이스/어셈블리를 찾는다.
# 의존성 로드 없이 메타데이터만 읽으므로 비트수(x86/x64)와 무관.
# (영문/ASCII 위주로 작성 — 인코딩 문제 회피)
$ErrorActionPreference = "SilentlyContinue"

# 1) AVEVA bin 경로 (Directory.Build.props 우선, 없으면 기본값)
$bin = "C:\AVEVA\Marine\OH12.1.SP5"
$props = Join-Path $PSScriptRoot "Directory.Build.props"
if (Test-Path $props) {
  $m = Select-String -Path $props -Pattern "Condition=[^>]*>([^<>]+)</AvevaBinDir>" | Select-Object -First 1
  if ($m) { $p = $m.Matches[0].Groups[1].Value.Trim().TrimEnd([char]92); if (Test-Path $p) { $bin = $p } }
}
Write-Host ("AVEVA bin: " + $bin)
if (-not (Test-Path $bin)) { Write-Host "ERROR: bin path not found"; return }

$wanted = @("DbElement","DbElementType","DbAttribute","DbAttributeInstance","TypeFilter",
            "DBElementCollection","DbElementTypeInstance","CurrentElement","PdmsMessage",
            "Project","MDB","Standalone")
$out = New-Object System.Collections.Generic.List[string]
$errCount = 0

# 2) System.Reflection.Metadata 로드 시도 (VS / dotnet 에 포함)
$mdLoaded = $false
try {
  $cand = Get-ChildItem "C:\Program Files\dotnet","C:\Program Files (x86)\Microsoft Visual Studio","C:\Program Files\Microsoft Visual Studio" `
            -Recurse -Filter "System.Reflection.Metadata.dll" -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($cand) { Add-Type -Path $cand.FullName -ErrorAction Stop; $mdLoaded = $true; Write-Host ("metadata reader: " + $cand.FullName) }
} catch { $mdLoaded = $false }

foreach ($dll in (Get-ChildItem $bin -Filter "Aveva*.dll" -ErrorAction SilentlyContinue)) {
  $handled = $false
  if ($mdLoaded) {
    try {
      $fs = [IO.File]::OpenRead($dll.FullName)
      $pe = New-Object System.Reflection.PortableExecutable.PEReader($fs)
      if ($pe.HasMetadata) {
        $mr = $pe.GetMetadataReader()
        foreach ($h in $mr.TypeDefinitions) {
          $td = $mr.GetTypeDefinition($h)
          $name = $mr.GetString($td.Name)
          if ($wanted -contains $name) {
            $ns = $mr.GetString($td.Namespace)
            $out.Add(("{0,-22} ns={1,-34} dll={2}" -f $name, $ns, $dll.Name))
          }
        }
      }
      $pe.Dispose(); $fs.Close()
      $handled = $true
    } catch { $errCount++ }
  }
  if (-not $handled) {
    # 폴백: 리플렉션 로드 (비트수 일치 필요 — check-types.cmd 가 32비트로 실행)
    try {
      $asm = [Reflection.Assembly]::LoadFrom($dll.FullName)
      try { $ts = $asm.GetTypes() } catch { $ts = $_.Exception.Types | Where-Object { $_ } }
      foreach ($t in $ts) { if ($t -and ($wanted -contains $t.Name)) { $out.Add(("{0,-22} ns={1,-34} dll={2}" -f $t.Name, $t.Namespace, $dll.Name)) } }
    } catch { $errCount++ }
  }
}

$final = $out | Sort-Object -Unique
$dest1 = Join-Path $env:USERPROFILE "Desktop\aveva_types.txt"
$dest2 = Join-Path $PSScriptRoot "aveva_types.txt"
$final | Set-Content -Path $dest1 -Encoding UTF8
$final | Set-Content -Path $dest2 -Encoding UTF8

Write-Host ""
Write-Host ("==== RESULT (" + $final.Count + " types) ====")
$final | ForEach-Object { Write-Host $_ }
Write-Host ""
Write-Host ("Saved: " + $dest1)
Write-Host ("       " + $dest2)
if ($final.Count -eq 0) {
  Write-Host ""
  Write-Host "NO TYPES FOUND."
  Write-Host " - metadata reader loaded: $mdLoaded, load errors: $errCount"
  Write-Host " - Try: run check-types.cmd (forces 32-bit PowerShell)."
  Write-Host " - Or send the DLL list:"
  Get-ChildItem $bin -Filter "Aveva.Pdms.*.dll" | Select-Object -ExpandProperty Name | Sort-Object | ForEach-Object { Write-Host ("   " + $_) }
}
