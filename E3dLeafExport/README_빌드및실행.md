# E3dLeafExport — AVEVA E3D 최하위(leaf) 요소 추출 Standalone

샘플 모델을 순회하여 **멤버(자식)가 없는 제일 하위 단위 요소**의
**이름(NAME)** 과 **참조값(REF)** 을 텍스트 파일로 뽑는 .NET Framework 4.8 콘솔 프로그램입니다.
AVEVA 교육 매뉴얼 `TG1031-CC3-C01 AVEVA E3D Design (3.1) .Net Customisation` 의
8장(Accessing the Database) · 7/9장(Standalone) 내용을 기반으로 작성했습니다.

---

## 0. 중요 — 이 프로그램은 E3D가 설치된 PC에서 빌드·실행해야 합니다

참조하는 `Aveva.Core.Database.dll`, `Aveva.E3D.Standalone.dll` 등은 **E3D 제품에 포함**되어
설치되며 일반 다운로드가 불가합니다. 따라서:

- **빌드**: E3D 설치본이 있는 PC에서 (DLL 참조 필요)
- **실행**: 모든 AVEVA dll 이 있는 폴더에서 (런타임 의존성 필요)

작성/편집은 어느 PC에서나 가능하지만, **컴파일과 실행은 E3D PC에서** 하세요.

---

## 1. 구성 파일

```
E3dLeafExport/
├─ E3dLeafExport.sln              ← Visual Studio 솔루션
└─ E3dLeafExport/
   ├─ E3dLeafExport.csproj        ← 프로젝트 (net48, x86, AVEVA 참조)
   ├─ Program.cs                  ← 본체 (순회 + 추출 + 저장)
   └─ App.config                  ← 로그인/환경변수/옵션
```

---

## 2. 빌드 전 설정 2가지

### (1) AVEVA DLL 경로 — `E3dLeafExport.csproj`
`AvevaBinDir` 의 기본값을 **설치된 E3D bin 폴더**로 바꾸세요 (끝에 `\` 포함):

```xml
<AvevaBinDir Condition="'$(AvevaBinDir)' == ''">C:\Program Files (x86)\AVEVA\Everything3D2.10\</AvevaBinDir>
```

또는 빌드 시 인자로 덮어쓰기:
```
msbuild E3dLeafExport.sln /p:Configuration=Release /p:Platform=x86 /p:AvevaBinDir="D:\AVEVA\E3D3.1\"
```

> 참조 4개: `Aveva.Core.Database`, `Aveva.Core.Database.Filters`,
> `Aveva.Core.Utilities`(PdmsMessage), `Aveva.E3D.Standalone`.
> 만약 설치 SDK의 네임스페이스가 약간 다르면 VS에서 해당 타입에 `Ctrl+.` → using 추가로 해결됩니다.

### (2) 비트수(x86/x64) — `E3dLeafExport.csproj`
매뉴얼(E3D 3.1)은 **x86**을 사용합니다. **설치된 E3D의 비트수와 반드시 일치**시켜야 합니다.
64bit E3D면 `<PlatformTarget>x86</PlatformTarget>` 를 `x64` 로 바꾸고, 솔루션 구성도 x64로 변경하세요.

---

## 3. 빌드

**Visual Studio**: 솔루션 열기 → 구성 `Release` / 플랫폼 `x86` → 빌드.

**명령줄 (이 PC의 Build Tools 기준)**:
```
& "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe" `
    "C:\...\E3dLeafExport.sln" /p:Configuration=Release /p:Platform=x86 /p:AvevaBinDir="설치경로\"
```
산출물: `E3dLeafExport\bin\x86\Release\net48\E3dLeafExport.exe` (+ `E3dLeafExport.exe.config`)

---

## 4. 실행 (런타임 의존성)

Standalone exe는 참조 dll뿐 아니라 **네이티브 의존성**(e3d.dll, core.dll, core3d.dll,
libgeom.dll, libmmd.dll 등)까지 모두 필요합니다(매뉴얼 7.6). 두 방법 중 하나:

- **(권장)** `E3dLeafExport.exe` 와 `E3dLeafExport.exe.config` 를
  **모든 AVEVA dll 이 모인 폴더**(교육용 `StandAlone Dependencies` 모음 또는 제품 폴더 복사본)에 두고 실행.
- 또는 제품 설치 폴더 안에서 직접 실행(제품 업데이트 시 깨질 수 있어 비권장).

### App.config 채우기
실행 폴더의 `E3dLeafExport.exe.config` 에서:

| 키 | 설명 | 예 |
|----|------|----|
| `PROJECT` | 프로젝트 코드 | `TRA` |
| `USER` / `PASSWORD` | 로그인 | `SYSTEM` / `XXXX` |
| `MDB` | MDB 이름 | `MAC-MDB` |
| `MODULE_NUMBER` | 모듈(78=Model) | `78` |
| `projects_dir`, `AVEVA_DESIGN_USER`, `PMLLIB`, `프로젝트코드000` | AVEVA 환경변수 | 환경에 맞게 |
| `START_ELEMENT` | 비우면 **모든 SITE**부터, 지정 시 그 요소부터 | `/SITE-EQUIPMENT-AREA01` |
| `OUTPUT_FILE` | 비우면 exe 옆에 자동 생성 | (빈 값) |

> 💡 가장 확실한 방법: AVEVA 교육용 `TrainingStandalone` 프로젝트의 `app.config` 에 들어있는
> 환경변수 키들을 그대로 이 `<appSettings>` 에 복사해 넣으세요.

### 실행
```
E3dLeafExport.exe
E3dLeafExport.exe "/SITE-EQUIPMENT-AREA01"
E3dLeafExport.exe "" "D:\export\leaves.txt"
```
(인자: `[시작요소] [출력파일]` — config보다 우선)

---

## 5. 출력 형식

```
# AVEVA E3D Standalone Leaf Export
# Generated : 2026-05-30 14:20:01
# Project   : TRA
# MDB       : MAC-MDB
# Start     : (all SITEs)
# Count     : 1234
#
Type	Name	Reference
BOX	(빈값=이름없음)	=12345/678
CYLI	/EQUIP-01-CYL	=12345/690
...
```
- 탭(Tab) 구분: **Type / Name / Reference**
- 이름이 없는 하위 요소(BOX, CYLI 등 프리미티브)는 Name이 빈 값이고 Reference로 식별합니다.

---

## 6. 동작 원리 (Program.cs)

1. `Standalone.Start(78, env)` — Model 모듈로 Standalone 세션 시작
2. `Standalone.Open(project, user, pass, mdb, out error)` — 프로젝트/MDB 로그인
3. 시작점 결정: `START_ELEMENT` 지정 시 `DbElement.GetElement(name)`,
   아니면 `DBElementCollection(TypeFilter(DbElementTypeInstance.SITE))` 로 모든 SITE 수집
4. 각 시작점에서 `element.Members()` 재귀 — **멤버가 0개면 leaf**로 판정해 수집
5. leaf마다 `GetString(NAME)`(이름), `ToString()`(참조값), `GetElementType().Name`(타입) 취득
6. 텍스트 파일로 저장
7. 정리: `Project.CurrentProject.Close()` → `Standalone.Finish()`

---

## 7. 문제 해결

- **`Aveva.* 를 찾을 수 없음`(CS0246)** → `AvevaBinDir` 경로/비트수 확인.
- **실행 시 의존성 예외** → 4번의 네이티브 dll 폴더 구성 확인(매뉴얼 7.6.1 dumpbin으로 누락 dll 추적).
- **로그인 실패(메시지 번호 출력)** → PROJECT/USER/PASSWORD/MDB, 환경변수(projects_dir 등) 확인.
- **빌드 PC에서 미서명 exe 실행 차단** → 이 개발 PC처럼 Smart App Control/WDAC가 켜진 환경에서는
  갓 빌드한 exe가 차단될 수 있습니다. E3D 워크스테이션은 보통 해당 정책이 꺼져 있습니다.

---

## 8. 검증 상태

- ✅ AVEVA API 스텁(shim)으로 **컴파일 통과** — 타입·메서드 시그니처 사용이 일관됨 확인.
- ⏳ 실제 빌드/실행 검증은 **E3D 설치 PC에서** 진행 필요(이 PC엔 E3D 미설치).
