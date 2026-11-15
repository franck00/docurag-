using DocuRag.Core.Cosmos;

namespace DocuRag.Core.Tests;

public class SemanticCacheKeyTests
{
    [Fact]
    public void Same_question_with_different_casing_and_spacing_shares_the_exact_key() =>
        Assert.Equal(
            SemanticCache.BuildId("Quel est le délai de recours ?", null),
            SemanticCache.BuildId("  quel est   le DÉLAI de recours?", " "));

    [Fact]
    public void Doc_type_is_part_of_the_key() =>
        Assert.NotEqual(
            SemanticCache.BuildId("Quel est le délai ?", "ohada"),
            SemanticCache.BuildId("Quel est le délai ?", "douane"));
}
