namespace ReleaseWatcher.Application.Abstractions;

/// <summary>
/// GitHub Token 的存储位置。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是端口而非具体实现：</b>凭据的落地方式属于基础设施
/// （当前决策为 Windows 凭据管理器，见 <c>DECISIONS.md</c> D-053），
/// 而"有没有 Token""能不能拿到 Token"是检测链路自身要回答的问题。
/// 端口定在应用层，用例与界面因此不必知道凭据存在Windows 的哪里。
/// </para>
/// <para>
/// <b>为何定义成同步方法：</b>凭据存取是一次本机进程内的调用，
/// 不含可等待的 I/O —— 用异步包装只会让调用点出现没有意义的
/// <c>await</c>。真正的耗时在随后的网络请求上，那里已有异步。
/// </para>
/// <para>
/// <b>安全约束：</b>Token 不得写入日志、不得出现在界面上
/// （见 <c>AGENTS.md</c> §1.4）。实现方也不得把它并入
/// 明文 <c>config.json</c>。
/// </para>
/// </remarks>
public interface ITokenStore
{
    /// <summary>读取已保存的 Token。</summary>
    /// <returns>Token 文本；未设置时返回 <c>null</c>。</returns>
    /// <exception cref="Application.Exceptions.TokenStorageException">读取失败。</exception>
    string? Read();

    /// <summary>保存（覆盖）Token。</summary>
    /// <param name="token">Token 文本；空白视为无效输入。</param>
    /// <exception cref="ArgumentException"><paramref name="token"/> 为空或全空白。</exception>
    /// <exception cref="Application.Exceptions.TokenStorageException">写入失败。</exception>
    void Save(string token);

    /// <summary>清除已保存的 Token。</summary>
    /// <remarks>幂等：本来就没有 Token 时不应视为失败。</remarks>
    /// <exception cref="Application.Exceptions.TokenStorageException">清除失败。</exception>
    void Clear();

    /// <summary>是否已保存过 Token。</summary>
    /// <remarks>只判断存在性，不把 Token 本身暴露给调用方做展示。</remarks>
    bool HasToken { get; }
}
