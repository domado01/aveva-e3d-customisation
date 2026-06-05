using System;
using System.Collections.Generic;
using Aveva.Pdms.Database;     // DbElement
using Aveva.Pdms.Shared;       // CurrentElement (PDMS는 Shared 에 위치)

namespace E3dLeafCore
{
    /// <summary>② 현재 열린 모델 모드 — 실행 중인 세션의 CE(또는 지정 요소)부터 추출.</summary>
    public class AddinModelProvider : IModelProvider
    {
        public string Host { get { return "addin"; } }
        public string[] Capabilities { get { return new[] { "addin" }; } }

        public ExtractResponse Extract(ExtractRequest req)
        {
            var res = new ExtractResponse { Mode = "addin", Rows = new List<LeafRow>() };
            try
            {
                DbElement start;
                if (!string.IsNullOrEmpty(req.StartElement))
                {
                    start = DbElement.GetElement(req.StartElement);
                    if (start == null || !start.IsValid)
                    {
                        res.Ok = false; res.Error = "요소를 찾을 수 없습니다: " + req.StartElement; return res;
                    }
                }
                else
                {
                    start = CurrentElement.Element;
                    if (start == null || !start.IsValid)
                    {
                        res.Ok = false; res.Error = "현재요소(CE)가 없습니다. 모델에서 요소를 선택하세요."; return res;
                    }
                }

                LeafCollector.Collect(start, res.Rows);
                res.Count = res.Rows.Count;
                res.Text = TextBuilder.Build("addin", "(current session)", "(current MDB)", req.StartElement, res.Rows);
                res.Ok = true;
                return res;
            }
            catch (Exception ex) { res.Ok = false; res.Error = ex.Message; return res; }
        }
    }
}
