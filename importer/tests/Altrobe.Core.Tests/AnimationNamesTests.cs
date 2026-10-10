using System.Text;
using Altrobe.Core.Convert;

namespace Altrobe.Core.Tests;

public class AnimationNamesTests
{
    [Fact]
    public void NamesAreIndexedByAnimationId()
    {
        Assert.Equal("Stand", AnimationNames.Name(0));
        Assert.Equal("Walk", AnimationNames.Name(4));
        Assert.Equal("Run", AnimationNames.Name(5));
        Assert.Equal("Attack2H", AnimationNames.Name(18));
        Assert.Equal("Animation 99999", AnimationNames.Name(99999));
    }

    [Fact]
    public void OnlyAnimationsFromClassicCount()
    {
        // wowdev.wiki M2/AnimationList: the Alpha and Vanilla sections are IDs 0-203; BC starts at 204.
        Assert.True(AnimationNames.IsClassic(0));
        Assert.True(AnimationNames.IsClassic(203)); // Cannibalize
        Assert.False(AnimationNames.IsClassic(204));
        Assert.False(AnimationNames.IsClassic(736)); // RisingSunKick, MoP
        Assert.False(AnimationNames.IsClassic(-1));
    }

    [Fact]
    public void TheSiteOffersOnlyClassicAnimations()
    {
        Assert.All(Altrobe.Core.Hosted.BundleBaker.Animations, id => Assert.True(AnimationNames.IsClassic(id), $"{AnimationNames.Name(id)} ({id})"));
    }

    [Fact]
    public void SequenceIdsComeFromTheConvertedModelsMetadata()
    {
        var meta = Encoding.UTF8.GetBytes("""{ "sequenceIds": [0, 4, 5, 69], "geosets": [] }""");
        Assert.Equal([0, 4, 5, 69], AssetConverter.SequenceIds(meta));
        Assert.Empty(AssetConverter.SequenceIds(Encoding.UTF8.GetBytes("""{ "geosets": [] }""")));
    }
}
