using ReleaseWatcher.Infrastructure.Security;

namespace ReleaseWatcher.Infrastructure.Tests;

/// <summary>
/// <see cref="WindowsCredentialManagerTokenStore"/> 对真实凭据 API 的读写验证。
/// </summary>
/// <remarks>
/// <para>
/// 这些用例会真的调用 <c>advapi32</c> 的凭据函数 —— 值得如此：
/// <c>CREDENTIAL</c> 结构体的 marshalling 一旦出错，编译期与普通单元测试
/// 都发现不了，只会在用户保存 Token 时静默失败。
/// </para>
/// <para>
/// 使用每次运行都不同的条目名并在结束时清除，绝不触碰用户真实的
/// GitHub Token 条目。
/// </para>
/// </remarks>
public class WindowsCredentialManagerTokenStoreTests : IDisposable
{
    private readonly string _target;
    private readonly WindowsCredentialManagerTokenStore _store;

    public WindowsCredentialManagerTokenStoreTests()
    {
        _target = "ReleaseWatcher.Tests/" + Guid.NewGuid().ToString("N");
        _store = new WindowsCredentialManagerTokenStore(_target);
    }

    public void Dispose()
    {
        try
        {
            _store.Clear();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // 清理失败不应让测试变成"红"，但也不该静默：交由断言失败暴露。
            throw new InvalidOperationException($"测试凭据 {_target} 未能清除。", ex);
        }
    }

    [Fact]
    public void Read_BeforeSave_ReturnsNull()
    {
        Assert.Null(_store.Read());
        Assert.False(_store.HasToken);
    }

    [Fact]
    public void SaveThenRead_RoundTripsToken()
    {
        _store.Save("ghp_roundtrip_123");

        Assert.Equal("ghp_roundtrip_123", _store.Read());
        Assert.True(_store.HasToken);
    }

    [Fact]
    public void Save_OverwritesPreviousToken()
    {
        _store.Save("first");
        _store.Save("second");

        Assert.Equal("second", _store.Read());
    }

    [Fact]
    public void Clear_RemovesToken()
    {
        _store.Save("ghp_to_remove");

        _store.Clear();

        Assert.Null(_store.Read());
        Assert.False(_store.HasToken);
    }

    [Fact]
    public void Clear_WhenNothingSaved_DoesNotThrow()
    {
        // 幂等：设置页的"清除"按钮在无 Token 时也必须可用。
        _store.Clear();
        _store.Clear();

        Assert.Null(_store.Read());
    }

    [Fact]
    public void Save_RejectsBlankToken()
    {
        Assert.Throws<ArgumentException>(() => _store.Save("   "));
    }
}
