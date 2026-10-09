namespace ReleaseWatcher.Domain.Exceptions;

/// <summary>
/// Release 数据来源不可达或返回了无法解析的内容。
/// </summary>
/// <remarks>
/// <para>
/// <b>为何需要这个异常而不是返回空集合：</b>
/// "取不到数据"与"该仓库确实没有 Release"是两种不同的业务状态，
/// 界面应给出不同提示（前者是错误，后者是空态）。
/// 若统一返回空集合，调用方无法区分，会把网络故障显示成"暂无更新" ——
/// 这是最容易让人误判应用是否正常的错误。
/// </para>
/// <para>
/// 放在 Domain 层而非 Application：它是"检测所需数据缺失"这一领域概念的表达，
/// 不依赖任何具体来源。
/// </para>
/// </remarks>
public sealed class ReleaseFeedException : Exception
{
    /// <summary>构造异常。</summary>
    /// <param name="message">面向用户的说明。</param>
    public ReleaseFeedException(string message)
        : base(message)
    {
    }

    /// <summary>构造异常并保留内部原因。</summary>
    /// <param name="message">面向用户的说明。</param>
    /// <param name="innerException">底层原因，通常是 HTTP 或解析错误。</param>
    public ReleaseFeedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}