using System.Collections.Generic;
using System.Linq;
using Exuarch.Core;
using Exuarch.Web.Components;

namespace Exuarch.Web.Tests;

public class UiLevelTests
{
    [Theory]
    [InlineData("essentials", false)]
    [InlineData("everything", true)]
    [InlineData(null, true)]
    [InlineData("nonsense", true)]
    public void OnlyAStoredEssentialsHidesAnything(string stored, bool everything)
    {
        var level = new UiLevel();
        level.Load(stored);
        Assert.Equal(everything, level.ShowsEverything);
    }

    [Fact]
    public void ChangingTheLevelRaisesChangedOnce()
    {
        var level = new UiLevel();
        int changes = 0;
        level.Changed += () => changes++;
        level.Set(true);
        level.Set(false);
        level.Set(false);
        Assert.Equal(1, changes);
        Assert.Equal("essentials", level.Name);
    }

    [Fact]
    public void EssentialsListsTheTutorialDevicesAndWhatTheMachineUses()
    {
        var level = new UiLevel();
        level.Load("essentials");
        var none = new HashSet<string>();
        Assert.True(level.ShowsDeviceType("register", none));
        Assert.True(level.ShowsDeviceType("keypad", none));
        Assert.False(level.ShowsDeviceType("mmu", none));
        Assert.True(level.ShowsDeviceType("mmu", new HashSet<string> { "mmu" }));
        var registry = DeviceRegistry.CreateDefault();
        Assert.All(UiLevel.EssentialDeviceTypes, type => Assert.True(registry.IsRegistered(type), type));
        level.Set(true);
        Assert.All(registry.Types, type => Assert.True(level.ShowsDeviceType(type, none)));
    }
}
