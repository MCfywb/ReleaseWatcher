using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReleaseWatcher.App.Services;

/// <summary>
/// 布局偏好持久化（config.json 的 <c>layout</c> 段）。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="ThemeManager"/> 的主题持久化共用同一份 <c>config.json</c>，
/// 但读写走 <see cref="JsonNode"/> 对象模型而非字符串手术 ——
/// 数组值的读写用字符串替换极易写坏文件；System.Text.Json 属于 BCL，
/// 不新增 NuGet 包（与 AGENTS §1.8 / D-052 少引库原则一致）。
/// 两种写入方式产出的都是合法 JSON，可互相解析。
/// </para>
/// <para>
/// 所有失败（文件损坏、只读、权限）一律返回缺省 / false：
/// 布局偏好不是核心功能，不应影响启动与使用。
/// </para>
/// </remarks>
public static class LayoutPreferences
{
    /// <summary>检测列表三栏宽度比例在 config.json 中的键（位于 <c>layout</c> 段）。</summary>
    private const string ReposColumnsKey = "reposColumns";

    /// <summary>
    /// 读取检测列表三栏的宽度比例（三者和为 1）。
    /// </summary>
    /// <returns>
    /// 保存的比例（已归一化）；未保存过、数据非法（数量不符 / 含非正数 /
    /// 总和偏离 1 过多）时返回 <c>null</c>，由调用方使用默认值。
    /// </returns>
    public static double[]? LoadReposColumnRatios(string? configFilePath)
    {
        if (string.IsNullOrWhiteSpace(configFilePath) || !File.Exists(configFilePath))
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(File.ReadAllText(configFilePath)) is not JsonObject root
                || root["layout"] is not JsonObject layout
                || layout[ReposColumnsKey] is not JsonArray array
                || array.Count != 3)
            {
                return null;
            }

            var ratios = new double[3];
            for (var i = 0; i < 3; i++)
            {
                if (array[i] is not JsonValue value
                    || !value.TryGetValue<double>(out ratios[i])
                    || !double.IsFinite(ratios[i])
                    || ratios[i] < 0.05)
                {
                    // 单栏比例小于 5% 基本等于误存，整组弃用走默认。
                    return null;
                }
            }

            var total = ratios[0] + ratios[1] + ratios[2];
            if (total is < 0.9 or > 1.1)
            {
                return null;
            }

            // 归一化，抵消存储舍入误差，保证三者和恰好为 1。
            return [ratios[0] / total, ratios[1] / total, ratios[2] / total];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 保存检测列表三栏的宽度比例，保留 config.json 的其余内容。
    /// </summary>
    /// <remarks>
    /// 在拖拽结束时调用（见 MainWindow 的 GridSplitter.DragCompleted），
    /// 保存的是三栏星型宽度归一化后的比例；窗口缩放时星型宽度天然等比，
    /// 因此只需记住比例本身。
    /// </remarks>
    public static bool SaveReposColumnRatios(string? configFilePath, IReadOnlyList<double> ratios)
    {
        if (string.IsNullOrWhiteSpace(configFilePath)
            || ratios.Count != 3
            || ratios.Any(r => !double.IsFinite(r) || r <= 0))
        {
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(configFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var root = File.Exists(configFilePath)
                && JsonNode.Parse(File.ReadAllText(configFilePath)) is JsonObject parsed
                    ? parsed
                    : new JsonObject();

            var layout = root["layout"] as JsonObject ?? new JsonObject();
            layout[ReposColumnsKey] = new JsonArray(
                ratios.Select(r => (JsonNode)Math.Round(r, 4)).ToArray());
            root["layout"] = layout;

            File.WriteAllText(configFilePath, root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
            }));
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
