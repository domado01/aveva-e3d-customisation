using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// ============================================================================
//  E3dLeafExport.Demo  —  E3D 없이 도는 데모
//
//  실 프로그램(E3dLeafExport)의 핵심 로직(멤버 없는 leaf 재귀 수집 + 텍스트 출력)을
//  그대로 옮기되, AVEVA DbElement 대신 가짜 트리(FakeElement)로 대체했다.
//  → 이 PC에서 'dotnet run' 으로 즉시 실행해 실제 출력 형식을 확인하는 용도.
//
//  실 프로그램과 1:1 대응:
//    FakeElement.Members()      ↔  DbElement.Members()
//    FakeElement.Name           ↔  DbElement.GetString(DbAttributeInstance.NAME)
//    FakeElement.Reference      ↔  DbElement.ToString()
//    FakeElement.Type           ↔  DbElement.GetElementType().Name
// ============================================================================
namespace E3dLeafExport.Demo
{
    /// <summary>AVEVA DbElement 를 흉내낸 가짜 요소.</summary>
    internal sealed class FakeElement
    {
        public string Type;
        public string Name;            // 이름 없는 프리미티브는 ""
        public string Reference;       // 예: =12345/678
        public List<FakeElement> Children = new List<FakeElement>();

        public FakeElement(string type, string name, string reference)
        {
            Type = type; Name = name ?? ""; Reference = reference;
        }
        public FakeElement Add(FakeElement child) { Children.Add(child); return this; }

        // 실 프로그램의 el.Members() 에 대응
        public FakeElement[] Members() { return Children.ToArray(); }
    }

    internal sealed class LeafRow
    {
        public string Type;
        public string Name;
        public string Reference;
    }

    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("=== AVEVA E3D Standalone Leaf Export [DEMO / E3D 불필요] ===");

            // 출력 경로 (인자로 덮어쓰기 가능)
            string outputFile = (args.Length >= 1 && !string.IsNullOrEmpty(args[0]))
                ? args[0]
                : Path.Combine(AppContext.BaseDirectory,
                    "E3D_Leaf_Export_DEMO_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");

            // ----------------------------------------------------------------
            // 1) 가짜 모델 트리 구성 (교육용 TRA 프로젝트 형태를 모사)
            //    SITE > ZONE > EQUIPMENT > 프리미티브(BOX/CYLI ...)
            //    SITE > ZONE > PIPE > BRANCH > 컴포넌트(ELBO/TEE/FLAN ...)
            // ----------------------------------------------------------------
            List<FakeElement> roots = BuildSampleModel();

            // ----------------------------------------------------------------
            // 2) 실 프로그램과 동일: 재귀로 멤버 0개(leaf)만 수집
            // ----------------------------------------------------------------
            List<LeafRow> leaves = new List<LeafRow>();
            foreach (FakeElement root in roots) CollectLeaves(root, leaves);
            Console.WriteLine("최하위(leaf) 요소 {0} 개 수집 완료.", leaves.Count);

            // ----------------------------------------------------------------
            // 3) 텍스트 파일 저장 (실 프로그램과 동일 형식)
            // ----------------------------------------------------------------
            WriteOutput(outputFile, "TRA(DEMO)", "MAC-MDB(DEMO)", "(all SITEs)", leaves);
            Console.WriteLine("저장 완료: {0}", outputFile);

            // 콘솔에도 미리보기
            Console.WriteLine();
            Console.WriteLine("----- 출력 미리보기 -----");
            Console.WriteLine("Type\tName\tReference");
            foreach (LeafRow r in leaves)
                Console.WriteLine(r.Type + "\t" + (r.Name == "" ? "(이름없음)" : r.Name) + "\t" + r.Reference);

            return 0;
        }

        // 실 프로그램의 CollectLeaves 와 동일한 알고리즘
        private static void CollectLeaves(FakeElement element, List<LeafRow> sink)
        {
            if (element == null) return;
            FakeElement[] members = element.Members();
            if (members == null || members.Length == 0)
            {
                sink.Add(new LeafRow { Type = element.Type, Name = element.Name, Reference = element.Reference });
                return;
            }
            foreach (FakeElement child in members) CollectLeaves(child, sink);
        }

        private static void WriteOutput(string path, string project, string mdb,
                                        string startElement, List<LeafRow> leaves)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            using (StreamWriter sw = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                sw.WriteLine("# AVEVA E3D Standalone Leaf Export  [DEMO]");
                sw.WriteLine("# Generated : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sw.WriteLine("# Project   : " + project);
                sw.WriteLine("# MDB       : " + mdb);
                sw.WriteLine("# Start     : " + startElement);
                sw.WriteLine("# Count     : " + leaves.Count);
                sw.WriteLine("#");
                sw.WriteLine("Type\tName\tReference");
                foreach (LeafRow r in leaves)
                    sw.WriteLine(r.Type + "\t" + r.Name + "\t" + r.Reference);
            }
        }

        // ---- 샘플 모델 (교육용 TRA 형태 모사) --------------------------------
        private static List<FakeElement> BuildSampleModel()
        {
            int refCounter = 12345600;
            Func<string, string, FakeElement> mk = (type, name) =>
                new FakeElement(type, name, "=" + (refCounter++) + "/" + (refCounter++ % 1000));

            // SITE 1: 장비
            FakeElement site1 = mk("SITE", "/SITE-EQUIPMENT-AREA01");
            FakeElement zone1 = mk("ZONE", "/ZONE-EQUIPMENT-AREA01");
            FakeElement equi1 = mk("EQUI", "/PUMP-101");
            equi1.Add(mk("BOX", ""))          // 이름 없는 프리미티브
                 .Add(mk("CYLI", ""))
                 .Add(mk("CYLI", "/PUMP-101-NOZZLE-SEAT"));
            FakeElement equi2 = mk("EQUI", "/TANK-201");
            equi2.Add(mk("CYLI", ""))
                 .Add(mk("DISH", ""))
                 .Add(mk("NOZZ", "/TANK-201-N1"));
            zone1.Add(equi1).Add(equi2);
            site1.Add(zone1);

            // SITE 2: 배관
            FakeElement site2 = mk("SITE", "/SITE-PIPING-AREA01");
            FakeElement zone2 = mk("ZONE", "/ZONE-PIPING-AREA01");
            FakeElement pipe1 = mk("PIPE", "/100-B-1");
            FakeElement bran1 = mk("BRAN", "/100-B-1/B1");
            bran1.Add(mk("ELBO", ""))
                 .Add(mk("TEE", ""))
                 .Add(mk("FLAN", ""))
                 .Add(mk("GASK", ""));
            pipe1.Add(bran1);
            zone2.Add(pipe1);
            site2.Add(zone2);

            return new List<FakeElement> { site1, site2 };
        }
    }
}
