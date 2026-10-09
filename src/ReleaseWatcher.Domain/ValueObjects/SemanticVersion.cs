namespace ReleaseWatcher.Domain.ValueObjects;

/// <summary>
/// 语义化版本号（<c>major.minor.patch[-prerelease][+build]</c>）的值对象。
/// </summary>
/// <remarks>
/// <para>
/// 存在的理由：GitHub Release 的 tag 命名千差万别（<c>v1.2.3</c>、<c>1.2.3</c>、
/// <c>release-1.2.3</c>、<c>1.2.3-beta.1</c>），而"是否需要提示更新"这个判断
/// 必须建立在可比较的版本号之上。把解析与比较收敛在一个值对象里，
/// 可以避免同一套比较逻辑在检测、列表排序、展示三处各写一遍。
/// </para>
/// <para>
/// <b>本类型只做"版本号"语义，不做"项目发布策略"语义。</b>
/// 例如 <c>1.0.0-alpha</c> 是否算更新，由 <see cref="Rules.ReleaseDetectionRules"/>
/// 结合预发布通道策略决定，而不是在这里决定。
/// </para>
/// <para>
/// 不可变，且重写值相等 —— 便于在集合与字典中按值使用。
/// 依据：<c>AGENTS.md</c> 第 1.2 节 Domain 层零引用（此处不引入任何外部依赖）。
/// </para>
/// </remarks>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private SemanticVersion(int major, int minor, int patch, string? prerelease, string? build)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        Build = build;
    }

    /// <summary>主版本号。</summary>
    public int Major { get; }

    /// <summary>次版本号。</summary>
    public int Minor { get; }

    /// <summary>修订号。</summary>
    public int Patch { get; }

    /// <summary>预发布标识（如 <c>beta.1</c>），无则为空。</summary>
    public string? Prerelease { get; }

    /// <summary>构建元数据（如 <c>sha.abc123</c>），无则为空。</summary>
    public string? Build { get; }

    /// <summary>是否为预发布版本。</summary>
    public bool IsPrerelease => !string.IsNullOrEmpty(Prerelease);

    /// <summary>
    /// 宽松解析形如 <c>v1.2.3-beta.1+build</c> 的字符串。
    /// </summary>
    /// <param name="text">
    /// 待解析文本；允许带 <c>v</c> 前缀，以及 <c>release-</c>、<c>Release/</c> 等常见包装前缀。
    /// </param>
    /// <param name="version">解析成功时输出版本对象。</param>
    /// <returns>能否解析出一个合法版本号。</returns>
    /// <remarks>
    /// <b>刻意不抛异常。</b>真实 tag 里存在 <c>nightly</c>、<c>latest</c>、日期串等非版本命名，
    /// 解析失败是正常业务情况而非异常，调用方据此走"无法比较版本"的降级路径。
    /// </remarks>
    public static bool TryParse(string? text, out SemanticVersion? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.Trim();

        // 剥离已知包装前缀：v / V / release- / Release/ / releases/
        foreach (var prefix in KnownPrefixes)
        {
            if (span.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                span = span[prefix.Length..];
                break;
            }
        }

        if (span.Length == 0)
        {
            return false;
        }

        // 构建元数据（+ 之后）不参与优先级比较，先剥离。
        string? build = null;
        var plusIndex = span.IndexOf('+');
        if (plusIndex >= 0)
        {
            build = span[(plusIndex + 1)..];
            span = span[..plusIndex];

            if (build.Length == 0)
            {
                return false;
            }
        }

        // 预发布标识（- 之后）。
        string? prerelease = null;
        var dashIndex = span.IndexOf('-');
        if (dashIndex >= 0)
        {
            prerelease = span[(dashIndex + 1)..];
            span = span[..dashIndex];

            if (prerelease.Length == 0)
            {
                return false;
            }
        }

        var parts = span.Split('.');
        if (parts.Length is not (1 or 2 or 3))
        {
            return false;
        }

        if (!TryParseSegment(parts[0], out var major))
        {
            return false;
        }

        var minor = 0;
        var patch = 0;

        if (parts.Length >= 2 && !TryParseSegment(parts[1], out minor))
        {
            return false;
        }

        if (parts.Length == 3 && !TryParseSegment(parts[2], out patch))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, prerelease, build);
        return true;
    }

    private static readonly string[] KnownPrefixes = ["v", "release-", "release/", "releases/", "rel-"];

    /// <summary>
    /// 单段纯数字且长度达到该值时，判定为日期串而非版本号。
    /// </summary>
    /// <remarks>
    /// <b>为什么需要这道防护：</b><c>20241015</c> 这类日期 tag 在形式上是合法的
    /// 单段数字（major = 20241015），若直接放行，它会大于任何正常版本号
    /// （如 <c>1.2.3</c>），导致检测永远判定"有更新"且反复提示。
    /// 真实的语义化版本 major 不会达到 8 位，因此用长度作为判据：
    /// 既拦下常见日期格式，又不会误伤 <c>99999999</c> 之类虽不常见但合法的版本号。
    /// </remarks>
    private const int DateLikeSegmentLength = 8;

    private static bool TryParseSegment(string segment, out int value)
    {
        if (!int.TryParse(segment, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out value)
            || value < 0)
        {
            value = 0;
            return false;
        }

        // 单一数字段且长到像日期 → 拒绝
        if (segment.Length >= DateLikeSegmentLength && segment.All(char.IsAsciiDigit))
        {
            value = 0;
            return false;
        }

        return true;
    }

    /// <summary>
    /// 按语义化版本的优先级规则比较两个版本。
    /// </summary>
    /// <remarks>
    /// 规则：先比 <c>major.minor.patch</c>；相同则<b>有预发布标识的版本更低</b>
    /// （<c>1.0.0-alpha &lt; 1.0.0</c>）；两者都有预发布标识时按点分段逐段比较，
    /// 数字段按数值比较，数字优先于非数字段。
    /// 构建元数据（<c>+</c>）<b>不参与</b>比较。
    /// </remarks>
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var coreComparison =
            Major.CompareTo(other.Major) is var byMajor and not 0
                ? byMajor
                : Minor.CompareTo(other.Minor) is var byMinor and not 0
                    ? byMinor
                    : Patch.CompareTo(other.Patch);

        if (coreComparison != 0)
        {
            return coreComparison;
        }

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    private static int ComparePrerelease(string? left, string? right)
    {
        // 无预发布标识视为正式版，优先级高于任何预发布版本。
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return 1;
        }

        if (right is null)
        {
            return -1;
        }

        var leftSegments = left.Split('.');
        var rightSegments = right.Split('.');
        var shared = Math.Min(leftSegments.Length, rightSegments.Length);

        for (var i = 0; i < shared; i++)
        {
            var comparison = ComparePrereleaseSegment(leftSegments[i], rightSegments[i]);

            if (comparison != 0)
            {
                return comparison;
            }
        }

        // 共同前缀相同：段数多者更高（alpha.1 < alpha.1.1）
        return leftSegments.Length.CompareTo(rightSegments.Length);
    }

    private static int ComparePrereleaseSegment(string left, string right)
    {
        var leftIsNumeric = int.TryParse(left, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var leftNumber);
        var rightIsNumeric = int.TryParse(right, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var rightNumber);

        // 纯数字段优先于非数字段
        return (leftIsNumeric, rightIsNumeric) switch
        {
            (true, true) => leftNumber.CompareTo(rightNumber),
            (true, false) => -1,
            (false, true) => 1,
            _ => string.CompareOrdinal(left, right),
        };
    }

    /// <inheritdoc />
    public bool Equals(SemanticVersion? other)
        => other is not null
           && Major == other.Major
           && Minor == other.Minor
           && Patch == other.Patch
           && string.Equals(Prerelease, other.Prerelease, StringComparison.Ordinal)
           && string.Equals(Build, other.Build, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SemanticVersion);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(Major, Minor, Patch, Prerelease, Build);

    public static bool operator >(SemanticVersion? left, SemanticVersion? right)
        => Compare(left, right) > 0;

    public static bool operator <(SemanticVersion? left, SemanticVersion? right)
        => Compare(left, right) < 0;

    public static bool operator >=(SemanticVersion? left, SemanticVersion? right)
        => Compare(left, right) >= 0;

    public static bool operator <=(SemanticVersion? left, SemanticVersion? right)
        => Compare(left, right) <= 0;

    private static int Compare(SemanticVersion? left, SemanticVersion? right)
        => left is null ? (right is null ? 0 : -1) : left.CompareTo(right);

    /// <summary>还原为标准版本号文本（不含原始 tag 的包装前缀）。</summary>
    public override string ToString()
    {
        var core = $"{Major}.{Minor}.{Patch}";
        var withPrerelease = Prerelease is null ? core : $"{core}-{Prerelease}";
        return Build is null ? withPrerelease : $"{withPrerelease}+{Build}";
    }
}