# 사용 설명서 — AVEVA E3D/Marine .NET Leaf Export

> **GitHub:** https://github.com/domado01/aveva-e3d-customisation

이 저장소는 AVEVA™ E3D Design / Marine 모델에서 **제일 하위 단위(leaf) 요소의 이름과 참조값(Ref)** 을
뽑아내는 .NET 커스터마이징 도구 모음입니다. 두 가지 형태가 있습니다.

| 형태 | 폴더 | 무엇 |
|------|------|------|
| **콘솔판** | `E3dLeafExport/` | 명령줄 EXE 로 추출 → TXT 저장 |
| **웹판** | `E3dLeafWeb/` | 브라우저 UI 에서 두 모드 선택 → 표 + TXT 다운로드 |

추가로 **AVEVA 없이 로직만 확인하는 데모**(`E3dLeafExport/E3dLeafExport.Demo`, `E3dLeafExport/WebDemo`)가 들어 있습니다.

---

## 목차
1. [먼저 알아둘 점 (중요)](#1-먼저-알아둘-점-중요)
2. [내려받기 (clone)](#2-내려받기-clone)
3. [사전 준비물](#3-사전-준비물)
4. [지금 바로 보기 — 데모 (AVEVA 불필요)](#4-지금-바로-보기--데모-aveva-불필요)
5. [콘솔판 사용법](#5-콘솔판-사용법)
6. [웹판 사용법](#6-웹판-사용법)
7. [출력 형식](#7-출력-형식)
8. [환경 정보 자동 수집 (marine-probe)](#8-환경-정보-자동-수집-marine-probe)
9. [문제 해결](#9-문제-해결)
10. [동작 원리 / API 메모](#10-동작-원리--api-메모)

---

## 1. 먼저 알아둘 점 (중요)

- **빌드·실행에는 AVEVA E3D/Marine 설치가 필요합니다.** 참조하는 `Aveva.Core.Database.dll`,
  `Aveva.E3D.Standalone.dll` 등은 제품에 포함된 **라이선스 어셈블리**라 이 저장소에 들어있지 않습니다.
  (AVEVA 홈페이지에서 따로 다운로드 불가 — CONNECT 포털의 정식 계정 필요)
- 따라서 **소스 작성/검토는 아무 PC에서나** 가능하지만, **실제 빌드와 실행은 AVEVA 설치 PC**에서 합니다.
- **API 키 같은 건 없습니다.** 필요한 건 ① 프로젝트 로그인 정보(PROJECT/USER/PASSWORD/MDB) ②
  AVEVA 환경변수(projects_dir 등) 뿐입니다.

---

## 2. 내려받기 (clone)

```bash
git clone https://github.com/domado01/aveva-e3d-customisation.git
cd aveva-e3d-customisation
```

폴더 구조:
```
aveva-e3d-customisation/
├─ README.md
├─ USAGE_사용법.md                ← 이 문서
├─ E3dLeafExport/                 ← 콘솔판
│   ├─ E3dLeafExport/             (실제 프로그램: Program.cs, App.config, csproj)
│   ├─ E3dLeafExport.Demo/        (net10 콘솔 데모 — AVEVA 불필요)
│   ├─ WebDemo/                   (net10 웹 데모 — AVEVA 불필요)
│   ├─ build-and-run.ps1          (E3D PC 원클릭 빌드+실행)
│   ├─ marine-probe.ps1           (환경 정보 자동 수집)
│   └─ README_빌드및실행.md
└─ E3dLeafWeb/                    ← 웹판 (.NET 4.8)
    ├─ E3dLeafCore/               (공용: 웹서버+UI+추출+AVEVA연결)
    ├─ E3dLeafWeb.Standalone/     (①모드 콘솔 호스트)
    ├─ E3dLeafWeb.Addin/          (②모드 E3D Addin)
    ├─ web-build-and-run.ps1
    └─ README_웹버전.md
```

---

## 3. 사전 준비물

| 항목 | 데모용 | 실제 빌드/실행용 |
|------|--------|------------------|
| .NET SDK 8/9/10 | ✅ 필요 (`dotnet run`) | — |
| .NET Framework 4.8 | — | ✅ 필요 |
| Visual Studio 2022 또는 Build Tools | — | ✅ 필요 (MSBuild) |
| AVEVA E3D/Marine 설치 | — | ✅ 필요 |
| AVEVA 프로젝트 + 로그인 정보 | — | ✅ 필요 |

> 비트수 주의: 빌드 산출물의 비트수(x86/x64)를 **설치된 E3D 비트수와 일치**시켜야 합니다.
> 교육 매뉴얼(E3D 3.1)은 x86 기준입니다. 제공된 스크립트가 자동 감지합니다.

---

## 4. 지금 바로 보기 — 데모 (AVEVA 불필요)

AVEVA가 없어도 추출 로직과 화면을 확인할 수 있습니다. .NET SDK 만 있으면 됩니다.

### 콘솔 데모
```powershell
cd E3dLeafExport/E3dLeafExport.Demo
dotnet run
```
가짜 모델 트리에서 leaf 를 뽑아 콘솔에 표시하고 `E3D_Leaf_Export_DEMO_*.txt` 로 저장합니다.

### 웹 데모
```powershell
cd E3dLeafExport/WebDemo
dotnet run
```
콘솔에 `http://127.0.0.1:5099/` 가 뜨면 브라우저로 접속 → 두 모드(① 프로젝트 입력 / ② 현재 모델)를
전환하며 **추출** → 표 + **TXT 다운로드** 를 확인할 수 있습니다.

---

## 5. 콘솔판 사용법

`E3dLeafExport/` — 프로젝트를 직접 열어 leaf 를 TXT 로 뽑는 Standalone 콘솔 EXE.

### 5-1. 원클릭 (권장)
E3D 설치 PC에서:
```powershell
cd E3dLeafExport
.\build-and-run.ps1
```
→ AVEVA 경로·비트수 자동 감지 → 빌드 → AVEVA 폴더로 복사 → 실행.
경로를 직접 지정하려면: `.\build-and-run.ps1 -AvevaBinDir "C:\Program Files (x86)\AVEVA\Everything3D2.10\"`

### 5-2. 수동 빌드
1. `E3dLeafExport/E3dLeafExport.csproj` 의 `AvevaBinDir` 를 설치 폴더로 수정 (끝에 `\`)
2. Visual Studio 에서 `Release` / `x86`(또는 x64) 로 빌드, 또는:
   ```powershell
   msbuild E3dLeafExport.sln /p:Configuration=Release /p:Platform=x86 /p:AvevaBinDir="설치경로\"
   ```
3. 산출 exe + config 를 **모든 AVEVA dll 이 있는 폴더**(또는 제품 폴더)에 두고 실행

### 5-3. App.config 채우기
실행 폴더의 `E3dLeafExport.exe.config`:

| 키 | 설명 | 예 |
|----|------|----|
| `PROJECT` | 프로젝트 코드 | `TRA` |
| `USER` / `PASSWORD` | 로그인 | `SYSTEM` / `****` |
| `MDB` | MDB 이름 | `MAC-MDB` |
| `MODULE_NUMBER` | 모듈(78=Model) | `78` |
| `projects_dir`, `AVEVA_DESIGN_USER`, `PMLLIB`, `프로젝트코드000` | AVEVA 환경변수 | 환경에 맞게 |
| `START_ELEMENT` | 비우면 모든 SITE, 지정 시 그 요소부터 | `/SITE-EQUIPMENT-AREA01` |
| `OUTPUT_FILE` | 비우면 exe 옆에 자동 생성 | (빈 값) |

> 가장 확실한 방법: AVEVA 교육용 `TrainingStandalone` 의 app.config 환경변수 키들을 그대로 복사.

### 5-4. 실행
```powershell
E3dLeafExport.exe                                  # config 대로
E3dLeafExport.exe "/SITE-EQUIPMENT-AREA01"         # 시작요소 지정
E3dLeafExport.exe "" "D:\export\leaves.txt"        # 출력파일 지정
```
(커맨드라인 인자: `[시작요소] [출력파일]` — config 보다 우선)

---

## 6. 웹판 사용법

`E3dLeafWeb/` — 브라우저에서 **두 모드 선택**. 백엔드는 AVEVA PC에서 동작.

### 6-1. ①모드 — 프로젝트 정보 입력 (Standalone 웹)
E3D 설치 PC에서:
```powershell
cd E3dLeafWeb
.\web-build-and-run.ps1
```
→ 콘솔에 `http://127.0.0.1:8731/` 출력. 브라우저 접속 → ① 탭에서
PROJECT/USER/PASSWORD/MDB 입력 → **추출** → 표 + **TXT 다운로드**.

설정 파일 `E3dLeafWeb.Standalone.exe.config`:
- `PORT` (기본 8731)
- `BIND` = `localhost`(이 PC만) / `lan`(사내망 다른 PC도)
- `MODULE_NUMBER`, `projects_dir`, `AVEVA_DESIGN_USER`, `PMLLIB`, `프로젝트코드000`
- (로그인 값은 웹 화면에서 입력하므로 config 에 둘 필요 없음)

#### LAN 으로 다른 PC에서 접속하려면 (BIND=lan)
관리자 PowerShell 에서 한 번:
```powershell
netsh http add urlacl url=http://+:8731/ user=Everyone
New-NetFirewallRule -DisplayName "E3dLeafWeb 8731" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8731
```
다른 PC 브라우저에서 `http://<AVEVA PC IP>:8731/` 접속.

### 6-2. ②모드 — 현재 열린 모델 (Addin 웹)
E3D/Marine 을 켜둔 채, 현재요소(CE) 기준으로 추출. 로그인 입력 불필요.

1. 빌드: `.\web-build-and-run.ps1 -NoRun` (또는 VS 에서 Release/x86)
2. `E3dLeafWeb.Addin.dll` + `E3dLeafCore.dll` 을 **CAF_ADDINS_PATH** 폴더(예: `...\Data\Addins`)로 복사
3. 해당 모듈의 `<module>Addins.xml`(예: `DesignAddins.xml`)에 Addin 등록 (매뉴얼 6장)
4. E3D/Marine 시작 → Addin 로드 시 웹서버 기동 → 브라우저 `http://127.0.0.1:8731/` → ② 탭 → **추출**
   - 포트/LAN 은 환경변수 `LEAFWEB_PORT`, `LEAFWEB_BIND=lan` 로 조정

> 화면 상단 배지에 현재 호스트가 지원하는 모드가 표시되고, 미지원 모드 탭은 자동 비활성화됩니다.

---

## 7. 출력 형식

탭(Tab) 구분 텍스트. 이름 없는 프리미티브(BOX, CYLI 등)는 Name 이 비어 있고 Reference 로 식별합니다.

```
# AVEVA Leaf Export (Web)
# Generated : 2026-06-01 10:20:01
# Mode      : standalone
# Project   : TRA
# MDB       : MAC-MDB
# Start     : (all)
# Count     : 1234
#
Type	Name	Reference
BOX		=12345/678
CYLI	/PUMP-101-NOZZLE-SEAT	=12345/690
NOZZ	/TANK-201-N1	=12345/702
```

---

## 8. 환경 정보 자동 수집 (marine-probe)

App.config 에 넣을 값을 못 찾겠을 때, AVEVA PC 에서:
```powershell
cd E3dLeafExport
.\marine-probe.ps1
```
출력: AVEVA 설치 bin 경로 / 비트수 / 실행 바로가기·evars / AVEVA 환경변수 / 프로젝트 코드 목록.
이 출력을 그대로 App.config 채우는 데 사용하면 됩니다.

---

## 9. 문제 해결

| 증상 | 원인 / 해결 |
|------|------------|
| `Aveva.* 를 찾을 수 없음` (CS0246) | `AvevaBinDir` 경로/비트수 확인. `.csproj` 또는 `/p:AvevaBinDir` |
| 실행 시 의존성 예외 (e3d.dll 등) | exe 를 **모든 AVEVA dll 이 있는 폴더**에서 실행 (매뉴얼 7.6.1 dumpbin) |
| 로그인 실패 (메시지 번호 출력) | PROJECT/USER/PASSWORD/MDB, 환경변수(projects_dir 등) 확인 |
| 웹 LAN 접속 안 됨 | `BIND=lan` + `netsh http add urlacl` + 방화벽 규칙 (6-1 참고) |
| `Standalone`/`PdmsMessage`/`CurrentElement`/`IAddin` 네임스페이스 오류 | 설치 SDK 버전 차이. VS 에서 해당 타입에 `Ctrl+.` → using 추가 |
| 미서명 exe 실행 차단 (Smart App Control/WDAC) | 일부 보안 정책 PC. E3D 워크스테이션은 보통 해당 없음 |
| 데모 exe 더블클릭이 막힘 | 데모는 `dotnet run` 으로 실행 (서명된 호스트) |

---

## 10. 동작 원리 / API 메모

- **leaf 판정:** `DbElement.Members()` 가 0개면 최하위(leaf). 재귀로 트리를 내려간다.
- **값 취득:** 이름 `GetString(DbAttributeInstance.NAME)`, 참조값 `DbElement.ToString()`(`=12345/678`),
  타입 `GetElementType().Name`.
- **시작점:** 콘솔/①모드 = 모든 SITE(`DBElementCollection(new TypeFilter(DbElementTypeInstance.SITE))`)
  또는 지정 요소. ②모드 = `CurrentElement.Element`(현재요소) 또는 지정 요소.
- **세션:** `Standalone.Start(78, env)` → `Standalone.Open(proj,user,pass,mdb, out PdmsMessage)` →
  추출 → `Project.CurrentProject.Close()` → `Standalone.Finish()`.
- **웹 스레드 안전:** Dabacon DB 는 스레드 안전하지 않아, 모든 AVEVA 호출을 한 스레드로 직렬화한다.
  ①모드는 전용 STA 워커, ②모드는 E3D UI 스레드로 마샬링(WinForms `Control.Invoke`).
- **참고 매뉴얼:** AVEVA TG1031-CC3-C01 ".Net Customisation" (8장 DB 접근, 7/9장 Standalone, 6장 Addin).

### Marine 사용 시
- **Outfitting**(배관·장비)은 E3D 와 동일한 SITE/ZONE 구조라 그대로 동작.
- **Hull**(선체: PANEL/PLATE/STIFFENER 등)은 시작점이 SITE 가 아닐 수 있으니
  `START_ELEMENT` 에 선체 모델 루트를 지정. 모듈 번호도 Marine 작업 모듈로 조정.

---

문의/개선 필요 시 이슈로 남기거나, 환경 정보(`marine-probe.ps1` 출력)를 공유하면 App.config 를 맞춰드립니다.
