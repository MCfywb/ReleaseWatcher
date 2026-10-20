namespace ReleaseWatcher.Application.Exceptions;

/// <summary>
/// Token 存取失败。
/// </summary>
/// <remarks>
/// 单独成类型而不复用 <c>InvalidOperationException</c>：调用方需要区分
/// "凭据系统不可用"（提示用户去检查系统设置、重试）与普通程序错误，
/// 而凭据失败往往不该让整次检测崩掉 —— 应当降级为"无 Token 继续"或
/// 在界面上给出明确提示。
/// </remarks>
public sealed class TokenStorageException : Exception
{
    /// <summary>构造异常。</summary>
    /// <param name="message">面向用户的原因说明。</param>
    /// <param name="innerException">底层异常，可为空。</param>
    public TokenStorageException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
