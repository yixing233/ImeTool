using ImeTool.Updates;

namespace ImeTool.Tests.Updates;

public sealed class ReleaseHistoryTests
{
    [Fact]
    public void Parser_Returns_All_Versions_In_Source_Order()
    {
        const string markdown = """
            # 更新日志

            ## [1.2.0] - 2026-07-16

            ### 新增
            - 新功能

            ## [1.1.0] - 2026-07-15

            ### 修复
            - 修复问题
            """;

        IReadOnlyList<ReleaseHistoryEntry> entries = ReleaseHistoryParser.Parse(markdown);

        Assert.Collection(
            entries,
            latest =>
            {
                Assert.Equal("1.2.0", latest.Version);
                Assert.Equal("2026-07-16", latest.Date);
                Assert.Equal("新增\n• 新功能", latest.Notes);
            },
            previous =>
            {
                Assert.Equal("1.1.0", previous.Version);
                Assert.Equal("2026-07-15", previous.Date);
                Assert.Equal("修复\n• 修复问题", previous.Notes);
            });
    }

    [Fact]
    public void Empty_Content_Returns_No_History()
    {
        Assert.Empty(ReleaseHistoryParser.Parse(null));
        Assert.Empty(ReleaseHistoryParser.Parse("# 更新日志"));
    }

    [Fact]
    public void Bundled_Catalog_Contains_Current_Version()
    {
        IReadOnlyList<ReleaseHistoryEntry> entries = ReleaseHistoryCatalog.LoadBundled();

        Assert.Contains(entries, entry => entry.Version == AppVersion.Display);
    }
}
