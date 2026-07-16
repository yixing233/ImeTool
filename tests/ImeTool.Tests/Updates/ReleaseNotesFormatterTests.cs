using ImeTool.Updates;

namespace ImeTool.Tests.Updates;

public sealed class ReleaseNotesFormatterTests
{
    [Fact]
    public void Markdown_Is_Converted_To_Readable_Plain_Text()
    {
        const string markdown = """
            ## 更新内容

            - 修复 **中英状态** 更新
            - 支持 [`Ctrl + Space`](https://example.test)
            """;

        string formatted = ReleaseNotesFormatter.FromMarkdown(markdown);

        Assert.Equal(
            "更新内容\n\n• 修复 中英状态 更新\n• 支持 Ctrl + Space",
            formatted);
    }

    [Fact]
    public void Atom_Html_Is_Converted_To_Readable_Plain_Text()
    {
        const string html = "<h2>更新内容</h2><ul><li>修复状态刷新</li><li>优化窗口记忆</li></ul>";

        string formatted = ReleaseNotesFormatter.FromHtml(html);

        Assert.Equal("更新内容\n\n• 修复状态刷新\n• 优化窗口记忆", formatted);
    }

    [Fact]
    public void Empty_Notes_Use_A_Clear_Placeholder()
    {
        Assert.Equal("此版本未提供更新说明。", ReleaseNotesFormatter.FromMarkdown("  "));
    }
}
