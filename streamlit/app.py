# -*- coding: utf-8 -*-
"""
AVEVA Marine - Leaf Export (Streamlit 웹 UI)

E3dLeafCli.exe (.NET) 를 호출해서:
  1) 실행 중인 AM/PDMS 환경 자동 감지 (PROJECT/USER/MDB 후보 + 전체 env)
  2) 지정 시작요소 하위의 leaf(최하위) 요소 Name/Ref 추출 → 표 + TXT 다운로드
PDMS 가 stdout 에 배너를 찍으므로, exe 결과는 JSON 파일로 주고받는다.

실행:  streamlit run app.py      (또는 run-streamlit.cmd 더블클릭)
"""
import json
import os
import subprocess
import tempfile

import streamlit as st

st.set_page_config(page_title="AVEVA Marine Leaf Export", page_icon="🛠️", layout="wide")


def find_cli():
    """E3dLeafCli.exe 위치를 찾는다 (환경변수 > 빌드가 남긴 경로파일 > 후보경로 순)."""
    env = os.environ.get("LEAF_CLI")
    if env and os.path.isfile(env):
        return env
    here = os.path.dirname(os.path.abspath(__file__))
    # build-cli.ps1 이 AVEVA bin 의 exe 경로를 여기에 적어둔다
    ptr = os.path.join(here, "leaf-cli-path.txt")
    if os.path.isfile(ptr):
        try:
            p = open(ptr, "r", encoding="utf-8").read().strip()
            if p and os.path.isfile(p):
                return p
        except OSError:
            pass
    candidates = [
        os.path.join(here, "E3dLeafCli.exe"),
        os.path.join(here, "..", "E3dLeafCli", "E3dLeafCli", "bin", "x86", "Release", "net48", "E3dLeafCli.exe"),
        os.path.join(here, "..", "E3dLeafCli", "E3dLeafCli", "bin", "x86", "Debug", "net48", "E3dLeafCli.exe"),
        r"C:\AVEVA\Marine\OH12.1.SP5\E3dLeafCli.exe",
    ]
    for c in candidates:
        if os.path.isfile(c):
            return os.path.abspath(c)
    return ""


def run_cli(args):
    """exe 를 호출하고 --result JSON 파일을 읽어 dict 반환."""
    exe = find_cli()
    if not exe:
        return {"ok": False, "error": "E3dLeafCli.exe 를 찾을 수 없습니다. LEAF_CLI 환경변수로 경로를 지정하거나 빌드 후 app.py 옆에 두세요."}
    tmp = tempfile.NamedTemporaryFile(suffix=".json", delete=False)
    tmp.close()
    cmd = [exe] + args + ["--result", tmp.name]
    try:
        subprocess.run(cmd, capture_output=True, timeout=600,
                       cwd=os.path.dirname(exe))  # exe 옆 attlib.dat 등 사용
        with open(tmp.name, "r", encoding="utf-8") as f:
            return json.load(f)
    except subprocess.TimeoutExpired:
        return {"ok": False, "error": "시간 초과(10분). PDMS 세션이 응답하지 않습니다."}
    except Exception as e:
        return {"ok": False, "error": "결과 파일을 읽지 못했습니다: %s" % e}
    finally:
        try:
            os.unlink(tmp.name)
        except OSError:
            pass


# ---------- UI ----------
st.title("🛠️ AVEVA Marine — Leaf Export")
st.caption("최하위(leaf) 모델 요소의 Name / Ref 를 추출합니다. AM/PDMS 가 실행·로그인된 상태에서 사용하세요.")

ss = st.session_state
ss.setdefault("projects", [])
ss.setdefault("detected", {})

with st.sidebar:
    st.header("① AM 환경 감지")
    st.write("실행 중인 AM/PDMS 에서 프로젝트·USER·MDB 를 읽어와 자동으로 채웁니다.")
    if st.button("🔍 AM 환경 자동 감지", use_container_width=True):
        with st.spinner("실행 중인 AM/PDMS 환경을 읽는 중..."):
            d = run_cli(["detect-env"])
        if d.get("ok"):
            ss["detected"] = d
            ss["projects"] = d.get("projects", [])
            # 폼 기본값 자동 채움
            if d.get("project"):
                ss["f_project"] = d["project"]
            elif d.get("projects"):
                ss["f_project"] = d["projects"][0]
            if d.get("user"):
                ss["f_user"] = d["user"]
            if d.get("mdb"):
                ss["f_mdb"] = d["mdb"]
            st.success("감지 완료: %s" % (d.get("proc") or "AVEVA process"))
        else:
            st.error(d.get("error", "감지 실패. AM 이 실행 중인지, 권한(같은 사용자/관리자)인지 확인하세요."))

    det = ss.get("detected", {})
    if det:
        st.metric("projects_dir", det.get("projectsDir", "") or "-")
        st.write("**프로젝트 후보:**", ", ".join(det.get("projects", [])) or "-")
        with st.expander("감지된 전체 환경변수 (USER/MDB 확인용)"):
            env = det.get("env", {})
            if env:
                st.dataframe(
                    [{"name": k, "value": v} for k, v in sorted(env.items())],
                    use_container_width=True, height=300)
            else:
                st.write("환경변수를 읽지 못했습니다.")

st.subheader("② 추출 설정")
c1, c2, c3 = st.columns(3)
projects = ss.get("projects", [])
if projects:
    cur = ss.get("f_project", projects[0])
    idx = projects.index(cur) if cur in projects else 0
    project = c1.selectbox("PROJECT", projects, index=idx, key="sel_project")
else:
    project = c1.text_input("PROJECT", key="f_project")
user = c2.text_input("USER", key="f_user")
password = c3.text_input("PASSWORD (없으면 비움)", type="password", key="f_password")

c4, c5, c6 = st.columns(3)
mdb = c4.text_input("MDB", key="f_mdb")
module = c5.text_input("MODULE_NUMBER", value=ss.get("f_module", "78"), key="f_module")
start = c6.text_input("시작 요소 (빈칸/전체 = 모델 전체)", value=ss.get("f_start", "전체"), key="f_start",
                      help="특정 요소만 뽑으려면 /SITE-XXX 또는 =123/456 입력. 비워두거나 '전체'면 모델 전체를 대상으로 합니다.")

proj_val = project if projects else ss.get("f_project", "")

run = st.button("🚀 추출 실행", type="primary", use_container_width=True,
                disabled=not (proj_val and user and mdb))

if run:
    with st.spinner("PDMS 세션 시작 → 로그인 → leaf 추출 중... (수 분 소요될 수 있음)"):
        res = run_cli([
            "extract",
            "--project", proj_val,
            "--user", user,
            "--password", password,
            "--mdb", mdb,
            "--module", module or "78",
            "--start", start,
        ])
    if res.get("ok"):
        rows = res.get("rows", [])
        st.success("✅ leaf %d 개 추출 완료" % res.get("count", len(rows)))
        if rows:
            st.dataframe(rows, use_container_width=True, height=480)
        fpath = res.get("file", "")
        if fpath and os.path.isfile(fpath):
            with open(fpath, "rb") as f:
                st.download_button("⬇️ 결과 TXT 다운로드", f.read(),
                                   file_name=os.path.basename(fpath),
                                   mime="text/plain", use_container_width=True)
        st.caption("저장 경로: %s" % fpath)
    else:
        st.error("추출 실패: %s" % res.get("error", "알 수 없는 오류"))
        st.info("AM 이 실행·로그인된 상태인지, PROJECT/USER/MDB 값이 맞는지 확인하세요. "
                "먼저 사이드바의 'AM 환경 자동 감지'를 눌러 값을 채우면 로그인 성공률이 올라갑니다.")
