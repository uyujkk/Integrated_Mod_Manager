using IntegratedModManager.Core;

namespace IntegratedModManager.Core.Tests;

public sealed class EndfieldOperatorCatalogParserTests
{
    [Fact]
    public void Parse_ReadsLocalizedNamesImagesAndDisplayOrder()
    {
        const string html = """
            <main>
              <div class="OperatorItem_operatorItem__gPezu"><div class="OperatorItem_image__fyd3C" data-key="typhon" style="background-image:url(https://cdn.example/typhon.png)"></div><div class="OperatorItem_contentBlock__I_0_3"><span class="OperatorItem_nameText__ibYGO">提弗洛斯</span><div class="OperatorItem_codename__U3_VI">// Typhoeus</div><div class="OperatorItem_index__ivv9h">01<!-- --> / <!-- -->33</div></div></div>
              <div class="OperatorItem_operatorItem__gPezu"><div class="OperatorItem_image__fyd3C" data-key="purrche" style="background-image:url(https://cdn.example/purrche.png)"></div><div class="OperatorItem_contentBlock__I_0_3"><span class="OperatorItem_nameText__ibYGO">噗切娜</span><div class="OperatorItem_codename__U3_VI">// Purrchena</div><div class="OperatorItem_index__ivv9h">02<!-- --> / <!-- -->33</div></div></div>
            </main>
            """;

        IReadOnlyList<EndfieldOperatorCatalogEntry> entries = EndfieldOperatorCatalogParser.Parse(html);

        Assert.Collection(
            entries,
            first =>
            {
                Assert.Equal("typhon", first.Slug);
                Assert.Equal("提弗洛斯", first.ChineseName);
                Assert.Equal("Typhoeus", first.EnglishName);
                Assert.Equal("https://cdn.example/typhon.png", first.AvatarUrl);
                Assert.Equal(1, first.Order);
            },
            second =>
            {
                Assert.Equal("purrche", second.Slug);
                Assert.Equal("噗切娜", second.ChineseName);
                Assert.Equal("Purrchena", second.EnglishName);
                Assert.Equal(2, second.Order);
            });
    }

    [Fact]
    public void Parse_KeepsBothEndministratorVariants()
    {
        const string html = """
            <section>
              <div class="OperatorItem_operatorItem__hash"><div class="OperatorItem_image__hash" data-key="endministrator2" style="background-image:url(https://cdn.example/end2.png)"></div><span class="OperatorItem_nameText__hash">管理员</span><div class="OperatorItem_codename__hash">// Endministrator</div><div class="OperatorItem_index__hash">03 / 33</div></div>
              <div class="OperatorItem_operatorItem__hash"><div class="OperatorItem_image__hash" data-key="endministrator1" style="background-image:url(https://cdn.example/end1.png)"></div><span class="OperatorItem_nameText__hash">管理员</span><div class="OperatorItem_codename__hash">// Endministrator</div><div class="OperatorItem_index__hash">04 / 33</div></div>
            </section>
            """;

        IReadOnlyList<EndfieldOperatorCatalogEntry> entries = EndfieldOperatorCatalogParser.Parse(html);

        Assert.Equal(2, entries.Count);
        Assert.Equal(["endministrator2", "endministrator1"], entries.Select(entry => entry.Slug));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html><body>No operators</body></html>")]
    public void Parse_ReturnsEmptyForUnavailableCatalog(string? html)
    {
        Assert.Empty(EndfieldOperatorCatalogParser.Parse(html));
    }
}
