using Server.Engines.MLQuests.Items;
using Server.Engines.MLQuests.Objectives;
using Server.Engines.MLQuests.Rewards;
using Server.Items;

namespace Server.Engines.MLQuests.Definitions;

public class DreadhornQuest : MLQuest
{
    public static readonly Point3D Entrance = new(1450, 1471, -22);
    public static readonly Point3D WealdLanding = new(2189, 1253, 0);
    public static readonly Point3D Exit = new(2189, 1251, 1);
    public static readonly Point3D CaveLanding = new(1450, 1473, -23);

    public DreadhornQuest()
    {
        Activated = true;
        OneTimeOnly = true;
        Title = 1074645; // Dreadhorn
        Description = 1074646;
        RefusalMessage = 1074647;
        InProgressMessage = 1074648;
        CompletionMessage = 1074649;

        // TODO: Bind the kill objective to DreadHorn when the boss is implemented.
        Objectives.Add(new KillObjective(1, [], "dread horn"));
        Rewards.Add(ItemReward.Strongbox);
    }

    public override void Generate()
    {
        base.Generate();

        EnsureTeleporter(Entrance, WealdLanding, true);
        EnsureTeleporter(Exit, CaveLanding, false);
    }

    private static void EnsureTeleporter(Point3D source, Point3D destination, bool requiresQuest)
    {
        foreach (var item in Map.Ilshenar.GetItemsAt(source))
        {
            if (item.Z == source.Z && item is Teleporter teleporter &&
                teleporter.PointDest == destination && teleporter.MapDest == Map.Ilshenar &&
                (requiresQuest
                    ? teleporter is MLQuestTeleporter { QuestType: not null } questTeleporter &&
                      questTeleporter.QuestType == typeof(DreadhornQuest)
                    : teleporter.GetType() == typeof(Teleporter)))
            {
                return;
            }
        }

        Teleporter portal = requiresQuest
            ? new MLQuestTeleporter(destination, Map.Ilshenar, typeof(DreadhornQuest), 1074274)
            : new Teleporter(destination, Map.Ilshenar);
        portal.ItemID = 0x373A;
        portal.Visible = true;
        portal.MoveToWorld(source, Map.Ilshenar);
    }
}
