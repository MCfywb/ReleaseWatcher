using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

// Markdig 与 WPF 文档模型都有 Block / Inline，且本类要同时用到两者：
// 参数是 Markdig 的语法树，操作对象是 WPF 的流文档节点。故两侧都显式别名。
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using WpfBlock = System.Windows.Documents.Block;

namespace ReleaseWatcher.App.Rendering;

/// <summary>
/// 把 Markdown 文本渲染为 WPF <see cref="FlowDocument"/>。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不走 HTML：</b>中间加一层 HTML 就得多一套转换器和一个解析器依赖，
/// 而 Release 的发布说明结构有限（标题、列表、代码、引用、表格、粗斜体、链接），
/// 直接产出 FlowDocument 更短也更可控。
/// </para>
/// <para>
/// <b>颜色一律 <c>SetResourceReference</c>（即 DynamicResource）：</b>
/// FlowDocument 宿主在 <c>RichTextBox</c> 内，会沿元素树向上解析资源，
/// 切换深浅主题时已渲染的说明也会跟着变色，与项目其他界面一致
/// （见 <c>ThemeManager</c> 与 D-046）。
/// </para>
/// <para>
/// <b>不使用 BlockContainer 之类的容器分组：</b>那些类型要么不是公开 API
/// （<c>BlockContainer</c> 即是），要么不支持背景与边框。代码块与引用块
/// 改为直接用 <see cref="Paragraph"/> 自带的 <c>Background</c> /
/// <c>BorderThickness</c> / <c>Padding</c> 表达 —— 少一层嵌套，
/// 也少一类"类型根本不存在"的编译期意外。
/// </para>
/// <para>
/// <b>支持范围：</b>标题、段落、引用、有序/无序列表、代码块、分隔线、
/// 表格（按等宽文本呈现）、行内代码、粗体、斜体、链接、图片（显示为链接文字）。
/// 原样 HTML 片段按纯文本降级 —— WPF 无法渲染 HTML，静默丢弃会让用户
/// 以为内容缺失。
/// </para>
/// </remarks>
public static class MarkdownFlowRenderer
{
    private const string TextPrimary = "Brush.Text.Primary";
    private const string TextSecondary = "Brush.Text.Secondary";
    private const string Accent = "Brush.Accent";

    private const double BodySize = 14;
    private const double CodeSize = 13;

    /// <summary>Markdown 解析管线；显式开启管道表格与强调扩展（GitHub 发布说明常见）。</summary>
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras()
        .Build();

    /// <summary>渲染为完整文档。</summary>
    /// <param name="markdown">Markdown 源文本，可为空。</param>
    /// <returns>可直接交给 <c>RichTextBox.Document</c> 的文档。</returns>
    public static FlowDocument Render(string? markdown)
        => Render(markdown, maxBlocks: null);

    /// <summary>渲染文档，可只取前若干个顶层块。</summary>
    /// <param name="markdown">Markdown 源文本，可为空。</param>
    /// <param name="maxBlocks">最多渲染多少个顶层块；<c>null</c> 表示不限制。</param>
    /// <returns>文档。</returns>
    public static FlowDocument Render(string? markdown, int? maxBlocks)
    {
        var document = new FlowDocument
        {
            IsOptimalParagraphEnabled = false,
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
        };

        if (string.IsNullOrWhiteSpace(markdown))
        {
            return document;
        }

        try
        {
            var count = 0;

            foreach (var block in Markdown.Parse(markdown, Pipeline))
            {
                if (maxBlocks is int limit && count >= limit)
                {
                    break;
                }

                foreach (var rendered in RenderBlock(block, indent: 0))
                {
                    document.Blocks.Add(rendered);
                }

                count++;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // 解析器不该把界面带崩：出错就整体按纯文本显示，内容仍然可读。
            document.Blocks.Clear();
            document.Blocks.Add(CreateTextParagraph(markdown, BodySize, TextPrimary, 0));
        }

        return document;
    }

    /// <summary>判断源文本是否长到需要折叠。</summary>
    /// <param name="markdown">Markdown 源文本。</param>
    /// <param name="maxBlocks">折叠时最多显示的顶层块数。</param>
    /// <returns>超出则返回 <c>true</c>。</returns>
    public static bool NeedsCollapse(string? markdown, int maxBlocks)
    {
        if (string.IsNullOrWhiteSpace(markdown) || maxBlocks <= 0)
        {
            return false;
        }

        try
        {
            return Markdown.Parse(markdown, Pipeline).Count() > maxBlocks;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    // ==================== 块级元素 ====================

    /// <summary>把一个 Markdown 块渲染成零到多个 WPF 块。</summary>
    private static List<WpfBlock> RenderBlock(MdBlock block, double indent)
    {
        switch (block)
        {
            case HeadingBlock heading:
            {
                // 层级压到 1~5，避免长文里 H1 巨大、H6 反而比正文小
                var level = Math.Clamp(heading.Level, 1, 5);
                var size = level switch { 1 => 20d, 2 => 17d, 3 => 15d, _ => BodySize };

                var result = new Paragraph { Margin = new Thickness(indent, level <= 2 ? 10 : 4, 0, 6) };
                AppendInlines(result, heading.Inline, size, TextPrimary, FontWeights.SemiBold);
                return [result];
            }

            case ParagraphBlock paragraphBlock:
            {
                var paragraph = new Paragraph { Margin = new Thickness(indent, 0, 0, 8) };
                AppendInlines(paragraph, paragraphBlock.Inline, BodySize, TextPrimary, FontWeights.Normal);
                return [paragraph];
            }

            case QuoteBlock quote:
                return RenderQuote(quote, indent);

            case ListBlock list:
                return RenderList(list, indent);

            case CodeBlock code:
                return [CreateCodeBlock(code.Lines.ToString(), indent)];

            case ThematicBreakBlock:
            {
                var run = new Run(new string('─', 24)) { FontSize = 12 };
                run.SetResourceReference(TextElement.ForegroundProperty, "Brush.Border");
                return [new Paragraph(run) { Margin = new Thickness(indent, 8, 0, 8) }];
            }

            case LinkReferenceDefinitionGroup:
                return []; // 纯定义（[ref]: url），不产生可见内容

            case HtmlBlock html:
                return [CreateTextParagraph(Fallback(html.Lines.ToString()), CodeSize, TextSecondary, indent)];

            case ContainerBlock container:
            {
                // 表格等容器按等宽文本逐行呈现。做成真正的 WPF 表格需要处理
                // 跨列与合并单元格，而发布说明里的表格通常只是对齐的短文本，
                // 等宽呈现已足够可读，也不会错位。
                var text = Flatten(container).Trim();
                return text.Length == 0 ? [] : [CreateCodeBlock(text, indent)];
            }

            case LeafBlock leaf:
            {
                var text = LeafText(leaf);
                return text.Length == 0 ? [] : [CreateTextParagraph(text, BodySize, TextPrimary, indent)];
            }

            default:
                return [];
        }
    }

    /// <summary>引用块：整体次级色 + 左侧竖线。</summary>
    private static List<WpfBlock> RenderQuote(QuoteBlock quote, double indent)
    {
        var result = new List<WpfBlock>();

        foreach (var child in quote)
        {
            foreach (var rendered in RenderBlock(child, indent))
            {
                if (rendered is Paragraph paragraph && paragraph.BorderThickness.Left == 0)
                {
                    paragraph.BorderThickness = new Thickness(3, 0, 0, 0);
                    paragraph.Padding = new Thickness(indent + 12, 2, 0, 2);
                    paragraph.SetResourceReference(Paragraph.BorderBrushProperty, "Brush.Border");

                    foreach (var run in EnumerateRuns(paragraph))
                    {
                        run.SetResourceReference(TextElement.ForegroundProperty, TextSecondary);
                    }
                }

                result.Add(rendered);
            }
        }

        return result;
    }

    private static List<WpfBlock> RenderList(ListBlock list, double indent)
    {
        var result = new List<WpfBlock>();
        var number = 1;

        foreach (var item in list.OfType<ListItemBlock>())
        {
            // 列表项用"前缀 + 缩进"表达：项目符号单独成 Run，
            // 续行靠 Paragraph 的 Padding 与首行文字左边界对齐，
            // 比用 ListItem 容器在不同宿主下的行为更可预测。
            var line = new Paragraph
            {
                Margin = new Thickness(indent, 1, 0, 1),
                TextIndent = 0,
                Padding = new Thickness(indent + 18, 0, 0, 0),
            };

            var bullet = new Run(list.IsOrdered
                ? number++ .ToString(CultureInfo.InvariantCulture) + ". "
                : "• ")
            {
                FontSize = BodySize,
            };
            bullet.SetResourceReference(TextElement.ForegroundProperty, TextSecondary);
            line.Inlines.Add(bullet);

            // 列表项的首子块通常是段落，取它的 Inline 拼到项目符号所在行；
            // 若首个子块本身就是嵌套块（如子列表），交给下面的嵌套分支处理。
            if (item.FirstOrDefault() is ParagraphBlock firstParagraph)
            {
                AppendInlines(line, firstParagraph.Inline, BodySize, TextPrimary, FontWeights.Normal);
            }

            result.Add(line);

            // 列表项内的嵌套块（子列表、代码块）接在项目符号所在段之后
            foreach (var nested in item.Skip(1))
            {
                result.AddRange(RenderBlock(nested, indent + 18));
            }
        }

        return result;
    }

    // ==================== 叶子构造 ====================

    private static Paragraph CreateTextParagraph(string text, double size, string brushKey, double indent)
    {
        var run = new Run(text) { FontSize = size };
        run.SetResourceReference(TextElement.ForegroundProperty, brushKey);

        return new Paragraph(run) { Margin = new Thickness(indent, 0, 0, 8) };
    }

    /// <summary>代码块：等宽字体 + 浅底 + 描边；多行之间用换行符分隔。</summary>
    private static Paragraph CreateCodeBlock(string code, double indent)
    {
        var lines = code.TrimEnd().Replace("\r\n", "\n").Split('\n');

        var paragraph = new Paragraph
        {
            Margin = new Thickness(indent, 4, 0, 8),
            Padding = new Thickness(10, 8, 10, 8),
            BorderThickness = new Thickness(1),
        };
        paragraph.SetResourceReference(Paragraph.BackgroundProperty, "Brush.Background.Secondary");
        paragraph.SetResourceReference(Paragraph.BorderBrushProperty, "Brush.Border.Subtle");

        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                paragraph.Inlines.Add(new LineBreak());
            }

            var run = new Run(lines[i])
            {
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = CodeSize,
            };
            run.SetResourceReference(TextElement.ForegroundProperty, TextPrimary);
            paragraph.Inlines.Add(run);
        }

        return paragraph;
    }

    /// <summary>递归取出块内的所有文本 Run（WPF 文档树没有 Descendants 扩展）。</summary>
    private static IEnumerable<Run> EnumerateRuns(WpfBlock block)
    {
        if (block is not Paragraph paragraph)
        {
            yield break;
        }

        foreach (var inline in paragraph.Inlines)
        {
            if (inline is Run run)
            {
                yield return run;
            }
        }
    }

    /// <summary>
    /// 取叶块的纯文本。
    /// </summary>
    /// <remarks>
    /// <b>不能一律读 <c>Lines</c>：</b>Markdig 里段落类块（表格单元格、普通段落）
    /// 的文本挂在 <c>Inline</c> 上，<c>Lines</c> 是空的；只有代码块、
    /// HTML 块这类"原样文本"才写进 Lines。读错位置会让表格整块消失。
    /// </remarks>
    private static string LeafText(LeafBlock leaf)
        => leaf is ParagraphBlock paragraph && paragraph.Inline is not null
            ? FlattenInlines(paragraph.Inline).Trim()
            : leaf.Lines.ToString().Trim();

    /// <summary>把容器内的文本按行拍平（表格用）。</summary>
    private static string Flatten(ContainerBlock container)
    {
        var builder = new System.Text.StringBuilder();

        foreach (var child in container)
        {
            switch (child)
            {
                case LeafBlock leaf:
                    builder.AppendLine(LeafText(leaf).TrimEnd());
                    break;
                case ContainerBlock nested:
                    builder.AppendLine(Flatten(nested));
                    break;
            }
        }

        return builder.ToString();
    }

    // ==================== 行内元素 ====================

    /// <summary>把行内元素追加到段落，并统一上色与字号。</summary>
    private static void AppendInlines(
        Paragraph paragraph,
        MdInline? inline,
        double size,
        string brushKey,
        FontWeight weight)
    {
        switch (inline)
        {
            case null:
                return;

            case LiteralInline literal:
                AppendRun(paragraph, literal.Content.ToString(), size, brushKey, weight);
                return;

            case EmphasisInline emphasis:
            {
                // DelimiterCount ≥ 2 视为粗体（***x*** 与 **x** 都落在这里）
                var bold = emphasis.DelimiterCount >= 2;
                AppendInlines(
                    paragraph,
                    emphasis.FirstChild,
                    size,
                    brushKey,
                    bold ? FontWeights.Bold : weight);
                return;
            }

            case CodeInline code:
            {
                var run = new Run(code.Content.ToString())
                {
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = CodeSize,
                    FontWeight = weight,
                };
                run.SetResourceReference(TextElement.ForegroundProperty, Accent);
                paragraph.Inlines.Add(run);
                return;
            }

            case LinkInline link:
            {
                var text = FlattenInlines(link).Trim();
                if (text.Length == 0)
                {
                    text = link.Url;
                }

                var run = new Run((link.IsImage ? "[图片] " : string.Empty) + text)
                {
                    FontSize = size,
                    TextDecorations = TextDecorations.Underline,
                };
                run.SetResourceReference(TextElement.ForegroundProperty, Accent);
                paragraph.Inlines.Add(run);
                return;
            }

            case AutolinkInline autolink:
            {
                var run = new Run(autolink.Url)
                {
                    FontSize = size,
                    TextDecorations = TextDecorations.Underline,
                };
                run.SetResourceReference(TextElement.ForegroundProperty, Accent);
                paragraph.Inlines.Add(run);
                return;
            }

            case LineBreakInline:
                paragraph.Inlines.Add(new LineBreak());
                return;

            case HtmlInline html:
                // HTML 片段在 WPF 里无法渲染，按纯文本降级而不是丢弃
                AppendRun(paragraph, Fallback(html.Tag), size, TextSecondary, weight);
                return;

            case HtmlEntityInline entity:
                AppendRun(paragraph, entity.Transcoded.ToString(), size, brushKey, weight);
                return;

            // 必须放在最后：Markdig 的 StringInline（LiteralInline / CodeInline /
            // HtmlInline 的基类）本身也派生自 ContainerInline，
            // 放前面会让后面所有 case 变成不可达（CS8120）。
            case ContainerInline container:
                foreach (var child in container)
                {
                    AppendInlines(paragraph, child, size, brushKey, weight);
                }

                return;
        }
    }

    private static void AppendRun(
        Paragraph paragraph,
        string text,
        double size,
        string brushKey,
        FontWeight weight)
    {
        var run = new Run(text) { FontSize = size, FontWeight = weight };
        run.SetResourceReference(TextElement.ForegroundProperty, brushKey);
        paragraph.Inlines.Add(run);
    }

    private static string FlattenInlines(ContainerInline container)
    {
        var builder = new System.Text.StringBuilder();

        foreach (var child in container)
        {
            switch (child)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.ToString());
                    break;
                case ContainerInline nested:
                    builder.Append(FlattenInlines(nested));
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>把可能被 WPF 丢弃的内容转成可显示的纯文本。</summary>
    private static string Fallback(string? raw)
        => string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim();
}
