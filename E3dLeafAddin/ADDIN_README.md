# E3dLeafAddin — AM 내부 API 애드인

외부에서 키 입력/창 제어로 AM 을 다루는 대신, **AM 안에서 도는 .NET 애드인**이 살아있는
세션 API 로 직접 처리합니다. 키 입력/포커스/standalone 로그인 문제가 모두 사라집니다.

- `session` : 현재 PROJECT/USER/MDB/현재요소(CE) 읽기
- `extract` : 현재요소(CE) 하위 leaf 추출 → 표 + txt
- `add`     : ADD CE / ADD &lt;ref&gt; 실행 (3D 뷰에 추가)

Streamlit ↔ 애드인은 **파일 브리지**로 통신합니다.
- 요청:  `C:\Users\Public\Documents\leaf_req.txt`
- 응답:  `C:\Users\Public\Documents\leaf_resp.json`
- 로드확인: `C:\Users\Public\Documents\leaf_addin_status.txt` (애드인이 뜨면 생성)

## 1) 빌드 (AM PC, 최초 1회)

```powershell
powershell -ExecutionPolicy Bypass -File E3dLeafAddin\build-addin.ps1
```
→ `E3dLeafAddin.dll` 생성 + AVEVA bin 으로 복사.
빌드 오류가 나면(특히 `Aveva.ApplicationFramework` 못 찾음): AVEVA bin 폴더의 `Aveva.*.dll`
목록을 알려주세요. 애드인 인터페이스 어셈블리 이름을 그 버전에 맞추겠습니다.

## 2) 등록 (AM 이 애드인을 로드하게)

AVEVA 버전/사이트마다 방식이 다릅니다. 보통 아래 중 하나입니다 — 사내 표준이 있으면 그걸 따르세요.

- **(A) 애드인 목록 설정파일에 추가**: AM 실행 폴더의 addins 설정(예: `*.addins`,
  `CustomisationAddins.xml`, 또는 `Aveva...config` 의 addins 항목)에
  `E3dLeafAddin.dll` 의 `E3dLeafAddin.LeafAddin` 을 등록.
- **(B) 애드인 폴더에 DLL 배치**: AM 이 스캔하는 addins 디렉터리(예: `%CAF_ADDINS_PATH%`
  또는 설치본의 `Addins` 폴더)에 `E3dLeafAddin.dll` 복사.
- **(C) Customisation/PML 로 로드**: 사내 커스터마이즈 로더가 있으면 거기에 등록.

> 어느 방식인지 모르면, AM 설치 폴더에서 다른 애드인이 어떻게 등록돼 있는지
> (또는 `*.addins` / `addins` 폴더 유무)를 알려주세요. 정확한 등록 절차를 맞춰드립니다.

## 3) 확인

AM 재시작(또는 애드인 재로드) 후:
`C:\Users\Public\Documents\leaf_addin_status.txt` 파일이 생기면 로드 성공입니다.
그 뒤 Streamlit 상단에 "🔌 E3dLeafAddin 연결됨" 이 뜨고, 애드인 기능 버튼이 동작합니다.

## 참고 (불확실 항목 — 빌드/실행으로 교정)

- 현재요소(CE)·세션정보·명령실행(ADD) API 는 버전별로 이름이 달라
  리플렉션으로 여러 후보를 시도합니다. ADD 가 "command runner 못 찾음" 으로 나오면
  그 메시지를 알려주세요 — 그 버전의 정확한 명령 실행 API 로 교정합니다.
