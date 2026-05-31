using System;

namespace E3dLeafCore
{
    /// <summary>모델 데이터 소스(standalone / addin)를 추상화.</summary>
    public interface IModelProvider
    {
        string Host { get; }              // "standalone" | "addin"
        string[] Capabilities { get; }    // 이 호스트가 광고하는 모드
        ExtractResponse Extract(ExtractRequest req);
    }

    /// <summary>
    /// AVEVA 호출을 안전한 스레드에서 실행하기 위한 디스패처.
    /// Dabacon DB 는 스레드 안전하지 않으므로 모든 호출을 한 스레드로 직렬화한다.
    /// </summary>
    public interface IDispatcher
    {
        ExtractResponse Run(Func<ExtractResponse> work);
    }
}
