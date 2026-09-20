using System;
using System.Collections.Generic;
using Server.Engines.MLQuests.Definitions;
using Server.Engines.MLQuests.Items;
using Server.Engines.MLQuests.Objectives;
using Server.Engines.MLQuests.Rewards;
using Server.Items;
using Server.Mobiles;
using Server.Tests;
using Xunit;

namespace Server.Engines.MLQuests;

[Collection("Sequential UOContent Tests")]
public class DreadhornQuestTests : IDisposable
{
    private readonly List<Mobile> _mobiles = [];
    private readonly List<Item> _items = [];
    private readonly DreadhornQuest _quest = Assert.IsType<DreadhornQuest>(
        MLQuestSystem.FindQuest(typeof(DreadhornQuest)));

    private static List<T> PortalsAt<T>(Point3D location) where T : Teleporter
    {
        var portals = new List<T>();
        foreach (var item in Map.Ilshenar.GetItemsAt(location))
        {
            if (item is T portal && item.Z == location.Z)
            {
                portals.Add(portal);
            }
        }

        return portals;
    }

    private PlayerMobile NewPlayer()
    {
        var player = new PlayerMobile { Player = true };
        _mobiles.Add(player);
        return player;
    }

    private BaseCreature NewQuester(bool sanctuary = false)
    {
        BaseCreature quester = sanctuary ? new LorekeeperOolua() : new LorekeeperCalendor();
        quester.AIObject?.AITimer?.Stop();
        _mobiles.Add(quester);
        return quester;
    }

    private MLQuestTeleporter NewEntrance()
    {
        var entrance = new MLQuestTeleporter(
            DreadhornQuest.WealdLanding, Map.Ilshenar, typeof(DreadhornQuest), 1074274);
        _items.Add(entrance);
        return entrance;
    }

    public void Dispose()
    {
        foreach (var mobile in _mobiles)
        {
            if (mobile is PlayerMobile player)
            {
                MLQuestSystem.GetContext(player)?.HandleDeletion();
                MLQuestSystem.Contexts.Remove(player);
            }

            mobile.Delete();
        }

        foreach (var item in _items)
        {
            item.Delete();
        }
    }

    [Fact]
    public void Definition_RegistersBothExistingQuestGivers_AndKeepsBossObjectivePending()
    {
        Assert.Contains(_quest, MLQuestSystem.QuestGivers[typeof(LorekeeperCalendor)]);
        Assert.Contains(_quest, MLQuestSystem.QuestGivers[typeof(LorekeeperOolua)]);
        Assert.True(_quest.Activated);
        Assert.True(_quest.OneTimeOnly);
        Assert.True(_quest.RecordCompletion);
        var objective = Assert.IsType<KillObjective>(Assert.Single(_quest.Objectives));
        Assert.Equal(1, objective.DesiredAmount);
        Assert.Empty(objective.AcceptedTypes);
        Assert.Same(ItemReward.Strongbox, Assert.Single(_quest.Rewards));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptingFromEitherQuester_GrantsAccess_CancelingRevokesIt(bool sanctuary)
    {
        var player = NewPlayer();
        var quester = NewQuester(sanctuary);
        var entrance = NewEntrance();

        Assert.False(entrance.CanTeleport(player));
        _quest.OnRefuse(quester, player);
        Assert.False(entrance.CanTeleport(player));
        _quest.OnAccept(quester, player);

        var instance = Assert.Single(MLQuestSystem.GetContext(player).QuestInstances);
        Assert.True(entrance.CanTeleport(player));
        Assert.False(instance.IsCompleted());
        Assert.NotSame(_quest, MLQuestSystem.RandomStarterQuest(quester, player, instance.PlayerContext));
        instance.Cancel();
        Assert.False(entrance.CanTeleport(player));
        Assert.True(_quest.CanOffer(quester, player, false));
    }

    [Fact]
    public void AcceptedQuest_DoesNotUnlockAnotherCharacter_OrCreditUnrelatedKills()
    {
        var player = NewPlayer();
        var quester = NewQuester();
        _quest.OnAccept(quester, player);
        var instance = MLQuestSystem.GetContext(player).FindInstance(_quest);
        var kill = Assert.IsType<KillObjectiveInstance>(Assert.Single(instance.Objectives));

        Assert.False(kill.AddKill(quester, quester.GetType()));
        Assert.False(kill.AddKill(player, typeof(PlayerMobile)));
        Assert.False(instance.IsCompleted());
        Assert.False(instance.ClaimReward);
        Assert.False(NewEntrance().CanTeleport(NewPlayer()));
        Assert.False(NewEntrance().CanTeleport(quester));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuestAccess_SurvivesContextSerialization(bool completed)
    {
        var player = NewPlayer();
        var quester = NewQuester();
        _quest.OnAccept(quester, player);
        var context = MLQuestSystem.GetContext(player);
        if (completed)
        {
            context.FindInstance(_quest).Remove();
            context.SetDoneQuest(_quest);
        }

        var writer = new BufferWriter(true);
        context.Serialize(writer);
        var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
        context.HandleDeletion();
        var reader = new BufferReader(bytes);
        var restored = new MLQuestContext(reader, 3);
        MLQuestSystem.Contexts[player] = restored;

        Assert.Equal(bytes.Length, reader.Position);
        Assert.Equal(completed, restored.HasDoneQuest(_quest));
        Assert.Equal(!completed, restored.IsDoingQuest(_quest));
        Assert.True(NewEntrance().CanTeleport(player));
        if (completed)
        {
            Assert.False(_quest.CanOffer(quester, player, false));
        }
        else
        {
            Assert.NotSame(_quest, MLQuestSystem.RandomStarterQuest(quester, player, restored));
        }
    }

    [Fact]
    public void Entrance_SurvivesItemSerialization()
    {
        var entrance = NewEntrance();
        var writer = new BufferWriter(true);
        entrance.Serialize(writer);
        var reader = new BufferReader(writer.Buffer.AsSpan(0, (int)writer.Position).ToArray());
        var restored = new MLQuestTeleporter(World.NewItem);
        _items.Add(restored);
        restored.Deserialize(reader);

        Assert.Equal(writer.Position, reader.Position);
        Assert.Equal(typeof(DreadhornQuest), restored.QuestType);
        Assert.Equal(Map.Ilshenar, restored.MapDest);
        Assert.Equal(DreadhornQuest.WealdLanding, restored.PointDest);
        Assert.True(restored.Active);
        var player = NewPlayer();
        Assert.False(restored.CanTeleport(player));
        _quest.OnAccept(NewQuester(), player);
        Assert.True(restored.CanTeleport(player));
    }

    [SkippableFact]
    public void GeneratedPortals_AreIdempotent_AndLeaveOtherItemsAlone()
    {
        TileDataRequirement.SkipIfMissing();
        var decoration = new Static(0x373A);
        _items.Add(decoration);
        decoration.MoveToWorld(DreadhornQuest.Entrance, Map.Ilshenar);
        var otherPortal = new Teleporter(new Point3D(1500, 1500, 0), Map.Ilshenar);
        _items.Add(otherPortal);
        otherPortal.MoveToWorld(DreadhornQuest.Entrance, Map.Ilshenar);

        _quest.Generate();
        var entrance = Assert.Single(PortalsAt<MLQuestTeleporter>(DreadhornQuest.Entrance), p => p.QuestType == typeof(DreadhornQuest));
        _items.Add(entrance);
        var exit = Assert.Single(PortalsAt<Teleporter>(DreadhornQuest.Exit), p => p.PointDest == DreadhornQuest.CaveLanding);
        _items.Add(exit);
        _quest.Generate();

        Assert.Single(PortalsAt<MLQuestTeleporter>(DreadhornQuest.Entrance), p => p.QuestType == typeof(DreadhornQuest));
        Assert.Single(PortalsAt<Teleporter>(DreadhornQuest.Exit), p => p.PointDest == DreadhornQuest.CaveLanding);
        Assert.False(decoration.Deleted);
        Assert.False(otherPortal.Deleted);
        Assert.True(entrance.Visible);
        Assert.False(entrance.Movable);
        Assert.True(exit.Visible);
        Assert.NotEqual(DreadhornQuest.Exit, entrance.PointDest);
        Assert.NotEqual(DreadhornQuest.Entrance, exit.PointDest);
    }

    [SkippableFact]
    public void EntryAndExit_MovePlayerAndFollowingPet_ExitWorksAfterCancellation()
    {
        TileDataRequirement.SkipIfMissing();
        var player = NewPlayer();
        var quester = NewQuester();
        var pet = new Horse();
        pet.AIObject?.AITimer?.Stop();
        _mobiles.Add(pet);
        pet.SetControlMaster(player);
        pet.ControlOrder = OrderType.Follow;
        player.MoveToWorld(DreadhornQuest.CaveLanding, Map.Ilshenar);
        pet.MoveToWorld(DreadhornQuest.CaveLanding, Map.Ilshenar);
        var entrance = NewEntrance();
        entrance.MoveToWorld(DreadhornQuest.Entrance, Map.Ilshenar);

        Assert.True(entrance.OnMoveOver(player));
        Assert.Equal(DreadhornQuest.CaveLanding, player.Location);
        _quest.OnAccept(quester, player);
        Assert.False(entrance.OnMoveOver(player));
        Assert.Equal(DreadhornQuest.WealdLanding, player.Location);
        Assert.Equal(player.Location, pet.Location);

        MLQuestSystem.GetContext(player).FindInstance(_quest).Cancel();
        var exit = new Teleporter(DreadhornQuest.CaveLanding, Map.Ilshenar);
        _items.Add(exit);
        exit.MoveToWorld(DreadhornQuest.Exit, Map.Ilshenar);
        Assert.False(exit.OnMoveOver(player));
        Assert.Equal(DreadhornQuest.CaveLanding, player.Location);
        Assert.Equal(player.Location, pet.Location);
        Assert.False(entrance.CanTeleport(player));
    }

    [SkippableFact]
    public void PortalLocations_HaveWalkableMapSurfaces()
    {
        TileDataRequirement.SkipIfMissing();
        foreach (var point in new[]
                 {
                     DreadhornQuest.Entrance, DreadhornQuest.CaveLanding,
                     DreadhornQuest.WealdLanding, DreadhornQuest.Exit
                 })
        {
            Assert.True(Map.Ilshenar.CanFit(point, 16, false, false, true),
                $"No walkable surface at {point}; ground Z is {Map.Ilshenar.GetAverageZ(point.X, point.Y)}.");
        }
    }
}
