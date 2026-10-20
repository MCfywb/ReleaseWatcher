using System.Runtime.InteropServices;
using System.Text;

using ReleaseWatcher.Application.Abstractions;
using ReleaseWatcher.Application.Exceptions;

namespace ReleaseWatcher.Infrastructure.Security;

/// <summary>
/// 用 Windows 凭据管理器（<c>advapi32!CredWriteW</c> 等）保存 GitHub Token。
/// </summary>
/// <remarks>
/// <para>
/// <b>选型依据见 <c>DECISIONS.md</c> D-053（Q-001 的结论）。</b>
/// 与 DPAPI + 文件相比，两者安全等级相同（底层都由 DPAPI 主密钥派生），
/// 差别在于本实现不引入任何 NuGet 包，且 Token 可在
/// 「控制面板 → 凭据管理器」中被用户查看与删除。
/// </para>
/// <para>
/// <b>凭据名</b>固定为 <see cref="CredentialTarget"/>，全机器唯一；
/// 多实例或换用户登录不会互相覆盖（条目本身按用户隔离）。
/// </para>
/// <para>
/// <b>安全纪律：</b>本类不写日志、不把 Token 放进异常消息，
/// 也不在任何属性里回显它（<c>AGENTS.md</c> §1.4）。
/// </para>
/// </remarks>
public sealed class WindowsCredentialManagerTokenStore : ITokenStore
{
    /// <summary>凭据条目名。凭据管理器按此名索引，勿随意更改——改了等于换了一个条目。</summary>
    private const string CredentialTarget = "ReleaseWatcher/GitHubToken";

    // ---- CredWriteW 的常量（wincred.h）----
    private const uint CRED_TYPE_GENERIC = 1;

    /// <summary>凭据随用户机器持久化（重启后仍在，且不随漫游配置外传）。</summary>
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    private const int ERROR_NOT_FOUND = 1168;

    /// <summary>CRED_MAX_CREDENTIAL_BLOB_SIZE：通用凭据的 blob 上限为 5 × 512 字节。</summary>
    private const int MaxCredentialBlobSize = 5 * 512;

    /// <summary>用默认凭据名构造（生产路径）。</summary>
    public WindowsCredentialManagerTokenStore()
        : this(CredentialTarget)
    {
    }

    /// <summary>用指定凭据名构造。</summary>
    /// <param name="credentialTarget">
    /// 凭据条目名。生产路径必须用默认值；此重载仅供测试以独立条目名运行，
    /// 避免测试写到用户真实的 GitHub Token 上。
    /// </param>
    internal WindowsCredentialManagerTokenStore(string credentialTarget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialTarget);

        _credentialTarget = credentialTarget;
    }

    private readonly string _credentialTarget;

    /// <inheritdoc />
    public bool HasToken => !string.IsNullOrEmpty(Read());

    /// <inheritdoc />
    public string? Read()
    {
        if (!CredReadW(_credentialTarget, CRED_TYPE_GENERIC, 0, out var credentialPtr))
        {
            var error = Marshal.GetLastWin32Error();

            // 从未保存过是正常初始状态，不是错误。
            if (error == ERROR_NOT_FOUND)
            {
                return null;
            }

            throw new TokenStorageException(
                $"读取 Windows 凭据失败（错误码 {error}）。请确认当前账户可访问凭据管理器。");
        }

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);

            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return null;
            }

            var buffer = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, buffer, 0, buffer.Length);

            return Encoding.UTF8.GetString(buffer);
        }
        finally
        {
            // CredRead 分配的内存必须用 CredFree 释放，不能用 FreeHGlobal。
            CredFree(credentialPtr);
        }
    }

    /// <inheritdoc />
    public void Save(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var blob = Encoding.UTF8.GetBytes(token);
        if (blob.Length > MaxCredentialBlobSize)
        {
            throw new TokenStorageException(
                $"Token 长度为 {blob.Length} 字节，超过 Windows 通用凭据上限 {MaxCredentialBlobSize} 字节。");
        }

        // 原生结构体里的字符串与字节数组都是裸指针，必须手工分配并释放。
        var targetPtr = Marshal.StringToHGlobalUni(_credentialTarget);
        var userPtr = Marshal.StringToHGlobalUni(Environment.UserName);
        var blobPtr = Marshal.AllocHGlobal(blob.Length);

        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);

            var credential = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = targetPtr,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPtr,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = userPtr,

                // 其余字段写凭据时必须为 0：原生代码会写入 LastWritten，
                // 传入非零值可能被拒或产生不可预期的条目。
                Flags = 0,
                Comment = IntPtr.Zero,
                LastWritten = default,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                TargetAlias = IntPtr.Zero,
            };

            if (!CredWriteW(ref credential, 0))
            {
                var error = Marshal.GetLastWin32Error();
                throw new TokenStorageException($"写入 Windows 凭据失败（错误码 {error}）。");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
            Marshal.FreeHGlobal(userPtr);
            Marshal.FreeHGlobal(targetPtr);
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        if (CredDeleteW(_credentialTarget, CRED_TYPE_GENERIC, 0))
        {
            return;
        }

        var error = Marshal.GetLastWin32Error();

        // 幂等：本来就没有 Token 时，清除不算失败。
        if (error == ERROR_NOT_FOUND)
        {
            return;
        }

        throw new TokenStorageException($"清除 Windows 凭据失败（错误码 {error}）。");
    }

    // ---- 原生互操作 ----

    /// <summary>
    /// 原生 <c>CREDENTIAL</c> 结构体。
    /// </summary>
    /// <remarks>
    /// 字段顺序、类型与对齐必须与 <c>wincred.h</c> 完全一致：
    /// <c>FILETIME</c> 用 <see cref="System.Runtime.InteropServices.ComTypes.FILETIME"/>
    /// （两个 32 位整数）而非 <c>long</c>，否则在 32 位进程上会因为对齐差异错位。
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string target, uint type, int flags, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string target, uint type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
