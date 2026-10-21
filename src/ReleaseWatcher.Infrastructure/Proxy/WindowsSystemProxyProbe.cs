using System.Runtime.InteropServices;

using ReleaseWatcher.Application.Abstractions;

namespace ReleaseWatcher.Infrastructure.Proxy;

/// <summary>
/// 通过 WinHTTP 读取当前用户的系统代理设置（含 PAC 与 WPAD）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么走 WinHTTP 而不是注册表：</b>读注册表要用
/// <c>Microsoft.Win32.Registry</c> 包，而 <c>winhttp.dll</c> 是系统自带 DLL，
/// P/Invoke 即可（与 Token 存储同样思路：不为一次读取引入新依赖）。
/// 顺带的好处是读到的是 WinHTTP 实际会用的那份配置，而不是"另一个地方"的副本。
/// </para>
/// <para>
/// <b>读失败一律降级：</b>返回"未配置代理"，绝不抛异常 ——
/// 代理探测只是给用户看的现状，不能因为它把应用带崩。
/// </para>
/// </remarks>
public static class WindowsSystemProxyProbe
{
    /// <summary>读取系统代理现状；失败时返回"未配置"。</summary>
    public static SystemProxyStatus Read()
    {
        try
        {
            if (!WinHttpGetIEProxyConfigForCurrentUser(out var config))
            {
                return new SystemProxyStatus(false, null, null, null);
            }

            try
            {
                return new SystemProxyStatus(
                    config.AutoDetect != 0,
                    Marshal.PtrToStringUni(config.AutoConfigUrl),
                    Marshal.PtrToStringUni(config.Proxy),
                    Marshal.PtrToStringUni(config.ProxyBypass));
            }
            finally
            {
                // WinHttpFreeBuffer 释放的是 WinHTTP 分配的内存，不能用 FreeHGlobal
                WinHttpFreeBuffer(config.AutoConfigUrl);
                WinHttpFreeBuffer(config.Proxy);
                WinHttpFreeBuffer(config.ProxyBypass);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // 含 DllNotFoundException 等：某些精简系统可能没有 winhttp.dll
            return new SystemProxyStatus(false, null, null, null);
        }
    }

    [DllImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinHttpGetIEProxyConfigForCurrentUser(
        out WINHTTP_CURRENT_USER_IE_PROXY_CONFIG pProxyConfig);

    [DllImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinHttpFreeBuffer(IntPtr buffer);

    /// <summary>
    /// 原生 <c>WINHTTP_CURRENT_USER_IE_PROXY_CONFIG</c>。
    /// </summary>
    /// <remarks>
    /// 字段顺序与类型必须与 winhttp.h 一致：先 BOOL，再三个 LPWSTR。
    /// 三个字符串指针由 WinHTTP 分配，必须用 <c>WinHttpFreeBuffer</c> 释放。
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private struct WINHTTP_CURRENT_USER_IE_PROXY_CONFIG
    {
        public int AutoDetect;
        public IntPtr AutoConfigUrl;
        public IntPtr Proxy;
        public IntPtr ProxyBypass;
    }
}
