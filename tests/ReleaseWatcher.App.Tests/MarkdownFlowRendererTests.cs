using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

using ReleaseWatcher.App.Rendering;

namespace ReleaseWatcher.App.Tests;

/// <summary>
/// <see cref="MarkdownFlowRenderer"/> 的渲染与折叠行为验证。
/// </summary>
/// <remarks>
/// 渲染器是"看得见的逻辑"：结构错了用户立刻会发现，但编译期一无所知。
/// 这里锁住三件事：块结构、文本不丢、以及折叠按块切分。
/// </remarks>
public class MarkdownFlowRendererTests
{
    /// <summary>
    /// 取块内的文本 Run。WPF 文档树没有 Descendants 扩展，
    /// 渲染器产出的块又都是段落，故直接遍历 Inlines 即可。
    /// </summary>
    private static IEnumerable<Run> AllRuns(Block block)
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

    private static string TextOf(Block block)
        => string.Concat(AllRuns(block).Select(r => r.Text));

    /// <summary>取第 n 个块（BlockCollection 不支持索引）。</summary>
    private static Block At(FlowDocument document, int index)
        => document.Blocks.Skip(index).First();

    [Fact]
    public void Render_EmptyInput_YieldsEmptyDocument()
    {
        var document = MarkdownFlowRenderer.Render(null);
        var alsoEmpty = MarkdownFlowRenderer.Render("   ");

        Assert.Empty(document.Blocks);
        Assert.Empty(alsoEmpty.Blocks);
    }

    [Fact]
    public void Render_HeadingAndParagraph_KeepsText()
    {
        var document = MarkdownFlowRenderer.Render("# 标题\n\n正文一段。");

        Assert.Equal(2, document.Blocks.Count);

        var heading = Assert.IsType<Paragraph>(At(document, 0));
        Assert.Contains("标题", TextOf(heading));
        Assert.True(AllRuns(heading).Any(r => r.FontSize >= 20), "一级标题应比正文大");

        var paragraph = Assert.IsType<Paragraph>(At(document, 1));
        Assert.Contains("正文一段。", TextOf(paragraph));
    }

    [Fact]
    public void Render_BoldAndItalic_ProduceWeightedRuns()
    {
        var document = MarkdownFlowRenderer.Render("普通**加粗**和*斜体*文本");

        var runs = AllRuns(At(document, 0)).ToList();

        Assert.Contains(runs, r => r.Text == "加粗" && r.FontWeight == FontWeights.Bold);
        Assert.Contains(runs, r => r.Text == "斜体");
    }

    [Fact]
    public void Render_Link_MarksRunAsAccent()
    {
        var document = MarkdownFlowRenderer.Render("见 [文档](https://example.com)。");

        var link = AllRuns(At(document, 0)).Single(r => r.Text.Contains("文档"));

        // 链接靠下划线 + Accent 色标识；DynamicResource 不落地为具体画刷，
        // 因此只断言下划线存在。
        Assert.Equal(TextDecorations.Underline, link.TextDecorations);
    }

    [Fact]
    public void Render_UnorderedList_ProducesOneParagraphPerItem()
    {
        var document = MarkdownFlowRenderer.Render("- 第一项\n- 第二项\n- 第三项");

        Assert.Equal(3, document.Blocks.Count);

        var texts = document.Blocks.OfType<Paragraph>().Select(TextOf).ToList();
        Assert.Contains(texts, t => t.Contains("第一项"));
        Assert.Contains(texts, t => t.Contains("第三项"));
        Assert.All(texts, t => Assert.StartsWith("•", t));
    }

    [Fact]
    public void Render_OrderedList_NumbersItems()
    {
        var document = MarkdownFlowRenderer.Render("1. 一\n2. 二");

        var texts = document.Blocks.OfType<Paragraph>().Select(TextOf).ToList();

        Assert.Contains(texts, t => t.StartsWith("1.", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.StartsWith("2.", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_FencedCode_KeepsCodeText()
    {
        var document = MarkdownFlowRenderer.Render("```csharp\nvar x = 1;\nvar y = 2;\n```");

        var paragraph = Assert.IsType<Paragraph>(At(document, 0));

        // 多行之间用 LineBreak 分隔，文本不能丢
        Assert.Contains("var x = 1;", TextOf(paragraph));
        Assert.Contains("var y = 2;", TextOf(paragraph));
        Assert.Contains(paragraph.Inlines, i => i is LineBreak);
    }

    [Fact]
    public void Render_Quote_MarksParagraphWithLeftBorder()
    {
        var document = MarkdownFlowRenderer.Render("> 引用内容");

        var paragraph = Assert.IsType<Paragraph>(At(document, 0));

        Assert.Equal(3, paragraph.BorderThickness.Left);
        Assert.Contains("引用内容", TextOf(paragraph));
    }

    [Fact]
    public void Render_Table_FallsBackToMonospaceText()
    {
        var document = MarkdownFlowRenderer.Render("| 名称 | 说明 |\n| --- | --- |\n| A | 甲 |");

        Assert.NotEmpty(document.Blocks);

        // 表格按等宽文本呈现：内容不能丢
        var text = string.Join("\n", document.Blocks.Select(TextOf));
        Assert.Contains("名称", text);
        Assert.Contains("甲", text);
    }

    [Fact]
    public void Render_RawHtml_FallsBackToPlainTextInsteadOfDropping()
    {
        var document = MarkdownFlowRenderer.Render("前<div>中</div>后");

        var text = string.Join(string.Empty, document.Blocks.Select(TextOf));

        // 静默丢弃会让用户以为内容缺失
        Assert.Contains("中", text);
    }

    [Fact]
    public void NeedsCollapse_ShortText_ReturnsFalse()
    {
        Assert.False(MarkdownFlowRenderer.NeedsCollapse("就一段话。", 4));
        Assert.False(MarkdownFlowRenderer.NeedsCollapse(string.Empty, 4));
    }

    [Fact]
    public void NeedsCollapse_ManyBlocks_ReturnsTrue()
    {
        var markdown = string.Join("\n\n", Enumerable.Range(1, 6).Select(i => $"第 {i} 段"));

        Assert.True(MarkdownFlowRenderer.NeedsCollapse(markdown, 4));
    }

    [Fact]
    public void Render_WithMaxBlocks_TruncatesToLimit()
    {
        var markdown = string.Join("\n\n", Enumerable.Range(1, 8).Select(i => $"第 {i} 段"));

        var full = MarkdownFlowRenderer.Render(markdown);
        var collapsed = MarkdownFlowRenderer.Render(markdown, maxBlocks: 4);

        Assert.True(full.Blocks.Count > collapsed.Blocks.Count);
        Assert.Equal(4, collapsed.Blocks.Count);

        // 截断后的内容必须是开头那几段，不能随机取
        Assert.Contains("第 1 段", string.Join(string.Empty, collapsed.Blocks.Select(TextOf)));
        Assert.DoesNotContain("第 8 段", string.Join(string.Empty, collapsed.Blocks.Select(TextOf)));
    }

    [Fact]
    public void Render_MalformedMarkdown_DoesNotThrow()
    {
        // 解析器出错时按纯文本兜底：宁可丑，不可崩
        var document = MarkdownFlowRenderer.Render("**未闭合的加粗\n\n```未闭合的代码块");

        Assert.NotEmpty(document.Blocks);
    }
}
