using WinRedisClient.Core.Abstractions;
using WinRedisClient.Presentation.ViewModels;

namespace WinRedisClient.Core.Tests;

public class KeyTreeBuilderTests
{
    private static KeyItemViewModel Key(string name) => new(new KeyMetadata(
        name, "string", null, DateTimeOffset.UnixEpoch));

    [Fact]
    public void Build_PlacesFoldersBeforeLeafKeysAtEveryLevel()
    {
        var keys = new[]
        {
            Key("apple"),
            Key("beta:item"),
            Key("beta:nested:value"),
            Key("beta2"),
            Key("zeta:item"),
        };

        var roots = KeyTreeBuilder.Build(keys, ":");

        Assert.Equal(new[] { "beta", "zeta", "apple", "beta2" }, roots.Select(n => n.Name));
        Assert.True(roots[0].IsFolder);
        Assert.True(roots[1].IsFolder);
        Assert.False(roots[2].IsFolder);
        Assert.False(roots[3].IsFolder);

        var betaChildren = roots[0].Children;
        Assert.Equal(new[] { "nested", "item" }, betaChildren.Select(n => n.Name));
        Assert.True(betaChildren[0].IsFolder);
        Assert.False(betaChildren[1].IsFolder);
        Assert.All(roots, n => Assert.False(n.IsExpanded));
        Assert.All(betaChildren, n => Assert.False(n.IsExpanded));
    }

    [Fact]
    public void Build_RestoresExpandedFoldersFromSavedState()
    {
        var keys = new[] { Key("beta:item"), Key("zeta:item") };
        var expanded = new HashSet<string>(StringComparer.Ordinal) { "beta" };

        var roots = KeyTreeBuilder.Build(keys, ":", expanded);

        Assert.True(roots.Single(n => n.Name == "beta").IsExpanded);
        Assert.False(roots.Single(n => n.Name == "zeta").IsExpanded);
    }
}
