# AVEVA 설치 경로 진단 (빌드용 DLL 폴더 찾기)

빌드 시 `AVEVA 폴더를 찾지 못함` 오류가 날 때, **빌드에 필요한 DLL이 모여 있는 폴더**를 찾는 방법입니다.
그 폴더 경로를 `-AvevaBinDir` 로 넘기면 빌드됩니다.

> Marine 사용자 주의: `Administration1.5.0` 같은 **관리자 도구 폴더가 아니라**,
> Marine **모델링 런타임** 폴더(핵심 DLL이 모여 있는 곳)를 찾아야 합니다.

---

## 1. 점검 명령 (PowerShell에 그대로 붙여넣기)

```powershell
$root = "C:\Program Files (x86)\AVEVA"
Write-Output "=== AVEVA 제품 폴더 목록 ==="
Get-ChildItem $root -Directory | Select-Object -ExpandProperty Name
Write-Output "`n=== 핵심 DLL 이 어느 폴더에 있는지 ==="
foreach($d in "Aveva.Core.Database.dll","Aveva.E3D.Standalone.dll","Aveva.Core3D.Standalone.dll","Aveva.ApplicationFramework.dll","Aveva.Core.Database.Filters.dll","Aveva.Core.Utilities.dll"){
  Write-Output "--- $d ---"
  $hit = Get-ChildItem $root -Recurse -Filter $d -ErrorAction SilentlyContinue | Select-Object -First 3 -ExpandProperty FullName
  if($hit){ $hit } else { Write-Output "  (없음)" }
}
```

> 64bit 설치라 `C:\Program Files\AVEVA` 에 있을 수도 있습니다. 위에서 안 나오면
> `$root` 를 `"C:\Program Files\AVEVA"` 로 바꿔 다시 실행하세요.

---

## 2. 결과 해석

- **AVEVA 제품 폴더 목록**: 설치된 제품들(예: `Marine12.x`, `Administration1.5.0`, ...). 모델링 런타임 폴더 이름 확인.
- **핵심 DLL 위치**: 아래 6개가 **같은 폴더**에 다 있으면 → 그 폴더가 `AvevaBinDir` 입니다.
  - `Aveva.Core.Database.dll`
  - `Aveva.Core.Database.Filters.dll`
  - `Aveva.Core.Utilities.dll`
  - `Aveva.E3D.Standalone.dll` **또는** `Aveva.Core3D.Standalone.dll` (둘 중 있는 것)
  - `Aveva.ApplicationFramework.dll` (Addin 모드용)

예) `Aveva.Core.Database.dll` 이 `C:\Program Files (x86)\AVEVA\Marine12.1.SP6\` 에 있으면
→ `AvevaBinDir` = `C:\Program Files (x86)\AVEVA\Marine12.1.SP6\`

---

## 3. 그 경로로 빌드 실행

PowerShell에서 (끝에 `\` 포함, 따옴표 필수):

```powershell
# 웹판
powershell -ExecutionPolicy Bypass -File ".\web-build-and-run.ps1" -AvevaBinDir "찾은폴더경로\"

# 콘솔판
powershell -ExecutionPolicy Bypass -File ".\build-and-run.ps1" -AvevaBinDir "찾은폴더경로\"
```

확인용:
```powershell
Test-Path "찾은폴더경로\Aveva.Core.Database.dll"   # True 여야 함
```

---

## 4. 더 자세한 환경 수집 (로그인/환경변수까지)

```powershell
powershell -ExecutionPolicy Bypass -File ".\marine-probe.ps1"
```
→ 설치 경로 · 비트수 · 실행 바로가기 · AVEVA 환경변수 · 프로젝트 코드 목록을 출력합니다.
(App.config 채울 때 사용)

---

## 5. Marine 주의사항

- **Standalone 어셈블리:** Marine 은 `Aveva.E3D.Standalone.dll` 대신
  **`Aveva.Core3D.Standalone.dll`** 을 쓰는 경우가 많습니다. 2번 결과에서 어느 쪽이 있는지 확인하세요.
  `Aveva.Core3D.Standalone.dll` 만 있으면 프로젝트 참조를 그쪽으로 바꿔야 합니다(문의 주세요).
- **모듈 번호:** 78 은 E3D Model 입니다. Marine 모델링 모듈은 번호가 다를 수 있으니
  `marine-probe.ps1` 출력(바로가기 Args 등)에서 확인해 App.config `MODULE_NUMBER` 를 맞추세요.
- **요소 타입:** Outfitting(배관·장비)은 E3D 와 동일(SITE/ZONE). Hull(선체: PANEL/PLATE/STIFFENER 등)은
  시작점이 SITE 가 아닐 수 있어 `START_ELEMENT` 로 선체 루트를 지정.

---

막히면 1번 명령 결과(제품 폴더 + DLL 위치)를 그대로 공유해 주세요.
정확한 `-AvevaBinDir` 확정과 Marine 참조/모듈 조정을 도와드립니다.
원본/최신본: https://github.com/domado01/aveva-e3d-customisation
