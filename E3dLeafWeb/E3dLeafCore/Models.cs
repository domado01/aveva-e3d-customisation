using System.Collections.Generic;
using System.Runtime.Serialization;

namespace E3dLeafCore
{
    // 웹 API 요청/응답 DTO. JSON 키는 데모 UI(JS)와 동일하게 소문자로 맞춘다.
    [DataContract]
    public class ExtractRequest
    {
        [DataMember(Name = "mode")] public string Mode { get; set; }
        [DataMember(Name = "project")] public string Project { get; set; }
        [DataMember(Name = "user")] public string User { get; set; }
        [DataMember(Name = "password")] public string Password { get; set; }
        [DataMember(Name = "mdb")] public string Mdb { get; set; }
        [DataMember(Name = "moduleNumber")] public int ModuleNumber { get; set; }
        [DataMember(Name = "startElement")] public string StartElement { get; set; }
    }

    [DataContract]
    public class LeafRow
    {
        [DataMember(Name = "type")] public string Type { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "reference")] public string Reference { get; set; }
    }

    [DataContract]
    public class ExtractResponse
    {
        [DataMember(Name = "ok")] public bool Ok { get; set; }
        [DataMember(Name = "mode")] public string Mode { get; set; }
        [DataMember(Name = "count")] public int Count { get; set; }
        [DataMember(Name = "rows")] public List<LeafRow> Rows { get; set; }
        [DataMember(Name = "text")] public string Text { get; set; }
        [DataMember(Name = "error")] public string Error { get; set; }
    }

    [DataContract]
    public class Capabilities
    {
        [DataMember(Name = "host")] public string Host { get; set; }
        [DataMember(Name = "modes")] public string[] Modes { get; set; }
    }

    public enum BindScope { Localhost, Lan }

    public class WebOptions
    {
        public int Port = 8731;
        public BindScope Bind = BindScope.Localhost;
    }
}
