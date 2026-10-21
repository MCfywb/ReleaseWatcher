using System.Text.Json;

using ReleaseWatcher.Application.Abstractions;
using ReleaseWatcher.Infrastructure.Proxy;

namespace ReleaseWatcher.Infrastructure.Configuration;

/// <summary>
/// 代理配置的实现：内存持有当前值，持久化到 <c>config.json</c> 的 <c>proxy</c> 段。
/// </summary>
/// <remarks>
/// <para>
/// 文件形状（其余键由其他组件各管各的）：
/// <code>
/// { "proxy": { "mode": "System", "server": "127.0.0.1:7890" } }
/// </code>
/// </para>
/// <para>
/// 与主题写入一样，<b>只替换自己的键</b>，不动文件里其他内容
/// （见 <c>ThemeManager.TrySaveTheme</c> 的做法）。写入失败不影响本次生效 ——
/// 内存里的值已经改了，只是下次启动未必保留。
/// </para>
/// </remarks>
public sealed class JsonProxySettings : IProxySettings
{
    private const string SectionKey = "proxy";

    private readonly string _configFilePath;
    private readonly Lock _writeGate = new();
    private ProxySettings _current;

    /// <summary>构造并从配置文件载入。</summary>
    /// <param name="configFilePath"><c>config.json</c> 完整路径。</param>
    public JsonProxySettings(string configFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFilePath);

        _configFilePath = configFilePath;
        _current = Load(configFilePath);
    }

    /// <inheritdoc />
    public ProxySettings Current => _current;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public SystemProxyStatus ReadSystemStatus() => WindowsSystemProxyProbe.Read();

    /// <inheritdoc />
    public void Update(ProxySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsUsable)
        {
            throw new ArgumentException("自定义代理模式下必须填写代理服务器地址。", nameof(settings));
        }

        _current = settings;

        TryPersist(settings);

        // HTTP 客户端据此重建；界面据此刷新状态文案
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>从配置文件读取；缺失或非法时回到默认（跟随系统）。</summary>
    private static ProxySettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return ProxySettings.Default;
            }

            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);

            if (!document.RootElement.TryGetProperty(SectionKey, out var section)
                || section.ValueKind != JsonValueKind.Object)
            {
                return ProxySettings.Default;
            }

            var mode = section.TryGetProperty("mode", out var modeElement)
                       && Enum.TryParse<ProxyMode>(modeElement.GetString(), ignoreCase: true, out var parsed)
                ? parsed
                : ProxyMode.System;

            var server = section.TryGetProperty("server", out var serverElement)
                         && serverElement.ValueKind == JsonValueKind.String
                ? serverElement.GetString()
                : null;

            var settings = new ProxySettings(mode, server);

            // 配置写坏了不该让应用带着"手工模式但没地址"的非法状态启动
            return settings.IsUsable ? settings : ProxySettings.Default;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return ProxySettings.Default;
        }
    }

    /// <summary>只替换 proxy 段，其余内容原样保留；失败静默（内存值已生效）。</summary>
    private void TryPersist(ProxySettings settings)
    {
        lock (_writeGate)
        {
            try
            {
                var existing = ReadExisting();
                var updated = Upsert(
                    string.IsNullOrWhiteSpace(existing) ? "{}" : existing,
                    settings);

                var directory = Path.GetDirectoryName(_configFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_configFilePath, updated);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // 磁盘只读等情况下不打断使用：本次设置已生效，只是无法持久化
            }
        }
    }

    private string? ReadExisting()
    {
        try
        {
            return File.Exists(_configFilePath) ? File.ReadAllText(_configFilePath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>在 JSON 文本中写入 proxy 段，保留其余内容。</summary>
    private static string Upsert(string json, ProxySettings settings)
    {
        var server = settings.ProxyServer is null
            ? string.Empty
            : $",\n    \"server\": {JsonSerializer.Serialize(settings.ProxyServer)}";

        // 分成两段：插入顶层时用"键: 值"，替换已有段时只能用"值" ——
        // 早前把带键名的整段又插了一次，结果写成 "proxy": { "proxy": {...} } 嵌套一层。
        var objectBody = $"{{\n    \"mode\": \"{settings.Mode}\"{server}\n  }}";
        var entry = $"\"{SectionKey}\": {objectBody}";

        // 顶层键名后需跟随 : 或 , 或空白，否则会误替换嵌套的同名键
        var searchFrom = 0;

        while (true)
        {
            var keyIndex = json.IndexOf('"' + SectionKey + '"', searchFrom, StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                break;
            }

            var afterKey = keyIndex + SectionKey.Length + 2;
            var next = afterKey;

            while (next < json.Length && char.IsWhiteSpace(json[next]))
            {
                next++;
            }

            if (next >= json.Length || json[next] != ':')
            {
                // 命中的是字符串值里的同名文本，不是键，跳过
                searchFrom = afterKey;
                continue;
            }

            // 找到该键的值（对象）并整体替换
            var valueStart = next + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
            {
                valueStart++;
            }

            if (valueStart >= json.Length || json[valueStart] != '{')
            {
                searchFrom = afterKey;
                continue;
            }

            var valueEnd = FindObjectEnd(json, valueStart);
            return json[..valueStart] + objectBody + json[valueEnd..];
        }

        return InsertTopLevel(json, entry);
    }

    /// <summary>找到从 <paramref name="objectStart"/> 起的对象右花括号位置。</summary>
    private static int FindObjectEnd(string json, int objectStart)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = objectStart; i < json.Length; i++)
        {
            var c = json[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return i + 1;
                    }

                    break;
            }
        }

        return json.Length;
    }

    /// <summary>顶层没有同名键时追加一个。</summary>
    private static string InsertTopLevel(string json, string section)
    {
        var trimmed = json.TrimEnd();

        if (trimmed == "{}")
        {
            return $"{{\n  {section}\n}}";
        }

        var lastBrace = trimmed.LastIndexOf('}');
        if (lastBrace <= 0)
        {
            return $"{{\n  {section}\n}}";
        }

        var body = trimmed[..lastBrace].TrimEnd();

        // 顶层已有其他键，补逗号
        return body.EndsWith('{')
            ? $"{{{Environment.NewLine}  {section}{Environment.NewLine}}}"
            : $"{body},{Environment.NewLine}  {section}{Environment.NewLine}}}";
    }
}
