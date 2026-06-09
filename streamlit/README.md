# AVEVA Marine — Leaf Export (Streamlit 웹 UI)

올드한 WinForms 대신 **브라우저 기반 모던 UI**. 파이썬 Streamlit 으로 호스팅하고,
실제 추출은 .NET 엔진 **`E3dLeafCli.exe`** 가 담당합니다.

```
[브라우저] ──HTTP──> [streamlit app.py] ──subprocess──> [E3dLeafCli.exe (.NET)]
                                                          │
                                          AM/PDMS 환경 자동감지 + 세션 로그인 + leaf 추출
                                                          │
                                          결과 JSON 파일  <──┘   (PDMS 가 콘솔에 배너를
                                                                  찍어서 stdout 대신 파일로 주고받음)
```

## 필요 조건 (AVEVA Marine PC)

1. **AVEVA Marine OH12.1.SP5** 설치 + **AM 실행·프로젝트 로그인 상태**
   (환경 자동감지가 실행 중인 AM 프로세스에서 projects_dir·USER·MDB 를 읽습니다)
2. **Visual Studio / MSBuild** (E3dLeafCli.exe 빌드용) — 최초 1회
3. **Python 3** (python.org 또는 MS Store) — streamlit 호스팅용
   - ⚠ 회사 정책상 파이썬/모듈 설치가 막혀 있으면 IT 승인이 필요할 수 있습니다.

## 실행 (가장 쉬운 방법)

`streamlit\run-streamlit.cmd` **더블클릭**. 자동으로:
1. `E3dLeafCli.exe` 빌드 → AVEVA bin 폴더로 복사
2. streamlit 미설치 시 설치
3. 브라우저에서 웹앱 열기

## 수동 실행

```powershell
# 1) .NET 엔진 빌드 (AVEVA bin 으로 복사됨)
powershell -ExecutionPolicy Bypass -File ..\E3dLeafCli\build-cli.ps1

# 2) streamlit 설치 (최초 1회)
python -m pip install --user streamlit

# 3) 웹앱 실행
python -m streamlit run app.py
```

## 사용 순서 (웹 화면)

1. 왼쪽 사이드바 **[🔍 AM 환경 자동 감지]** 클릭
   → 실행 중인 AM 에서 **PROJECT / USER / MDB** 후보를 읽어 폼에 자동 입력
   → "감지된 전체 환경변수" 펼치면 AM 이 설정한 모든 변수 확인 가능
2. PROJECT(드롭다운) / USER / PASSWORD / MDB / MODULE / **시작 요소** 확인·수정
3. **[🚀 추출 실행]** → leaf 표 표시 + **TXT 다운로드**

## E3dLeafCli.exe 위치를 못 찾을 때

`app.py` 는 아래 순서로 exe 를 찾습니다:
1. 환경변수 `LEAF_CLI`
2. `app.py` 옆 `E3dLeafCli.exe`
3. 빌드 출력 폴더(`E3dLeafCli\...\bin\...`)
4. `C:\AVEVA\Marine\OH12.1.SP5\E3dLeafCli.exe`

직접 지정하려면:
```powershell
$env:LEAF_CLI = "C:\AVEVA\Marine\OH12.1.SP5\E3dLeafCli.exe"
python -m streamlit run app.py
```

## CLI 단독 사용 (디버그용)

```bat
REM 환경 감지 (결과를 detect.json 으로)
E3dLeafCli.exe detect-env --result detect.json

REM 추출
E3dLeafCli.exe extract --project SN2661 --user 02564 --password "" ^
  --mdb MYMDB --module 78 --start /SITE-XXX --result out.json
```

## ③ AM 현재 선택 요소 하위 추출 (마지막 활성 AM)

AM Explorer 에서 선택한 요소(CE) 하위의 모든 부재 이름을 뽑습니다.
선택 상태는 AM 내부 UI 정보라 외부 프로그램이 직접 읽을 수 없어, AM 이 파일로 한 번 내보내는 방식입니다.

1. AM 에서 원하는 요소를 Explorer 에서 선택(클릭)
2. AM 명령창에 매크로 실행 (경로는 압축 푼 위치에 맞게):
   ```
   $m /C:/AVEVA/AVEVA_Streamlit_LeafExport/streamlit/am-export-ce.pmlmac
   ```
   → `C:\Users\Public\Documents\am_current_element.txt` 에 현재 요소 Ref 저장
3. 웹의 ③ 섹션에서 **[🔄 AM 현재요소 불러오기]** → 자동 입력
4. **[📌 선택 요소 하위 모든 부재 이름 추출]** 클릭

직접 입력도 가능: AM Explorer 에서 본 Name/Ref 를 ③의 입력칸에 넣고 버튼 클릭.
(매크로가 PML 버전차로 에러나면 에러 줄을 알려주세요 — 맞춰 수정합니다.)

### 실행 중인 AM 선택 (여러 AM 호환)

상단 **[🖥️ 실행 중인 AM 선택]** → [🔄 AM 목록 새로고침] → 작업할 AM 선택.
- 선택한 AM 의 환경(프로젝트/USER/MDB)을 읽어 ② 폼을 자동으로 채웁니다.
- 이후 추출·CE·ADD 가 모두 **그 AM** 을 통해 동작합니다(pid 지정).
- PROJECT 는 명령줄/환경 기반으로 추정해 채워, 템플릿(AAA) 이 잘못 먼저 뜨던 문제를 보정합니다.

### 선택 요소를 3D 뷰에 ADD (실제 실행)

웹 ③의 **[🖼️ 선택 요소를 3D 뷰에 ADD (실행)]** → 선택한 AM 창에 `ADD CE`(또는 `ADD <ref>`)
명령을 실제로 전송해 3D 뷰(드로우리스트)에 추가합니다.
- 동작 원리: 외부 프로그램은 AM 3D 뷰를 직접 못 만지므로, 선택한 AM 창을 전면으로 가져와 명령을 키 입력으로 보냅니다.
- 전송 실패 시(전면 전환 차단 등): AM 명령창을 클릭해 포커스를 둔 뒤 다시 누르거나, 표시되는 한 줄을 직접 입력. 또는 `am-add-ce.pmlmac` 실행.

## USER/MDB 가 자동으로 안 채워질 때

USER·MDB 는 AM 이 환경변수로 노출하지 않을 수도 있습니다.
이 경우 사이드바 **"감지된 전체 환경변수"** 표를 열어 값을 확인하고
직접 입력하세요. (자동감지는 `PDMSUSER/USER/USERNAME`, `*MDB*` 등 흔한 키를 추적합니다.)
어떤 키에 들어있는지 알려주시면 자동 채움 규칙을 추가하겠습니다.
