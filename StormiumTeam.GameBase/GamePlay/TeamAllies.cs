using System.Diagnostics.CodeAnalysis;
using GameHost.Injection;
using GameHost.Revolution.NetCode;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Systems;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.TabEcs;
using GameHost.Simulation.TabEcs.Interfaces;

namespace StormiumTeam.GameBase.GamePlay
{
    /// <summary>
    /// Get team allies of this team
    /// </summary>
    public struct TeamAllies : IComponentBuffer
    {
        public GameEntity Team;

        public TeamAllies(GameEntity team) => Team = team;

        public struct Snapshot : IReadWriteSnapshotData<Snapshot, GhostSetup>, ISnapshotSyncWithComponent<TeamAllies, GhostSetup>
        {
            public uint Tick { get; set; }

            public Ghost Ghost;

            public void Serialize(in BitBuffer buffer, in Snapshot baseline, in GhostSetup setup)
            {
                buffer.AddGhostDelta(Ghost, baseline.Ghost);
            }

            public void Deserialize(in BitBuffer buffer, in Snapshot baseline, in GhostSetup setup)
            {
                Ghost = buffer.ReadGhostDelta(baseline.Ghost);
            }

            public void FromComponent(in TeamAllies component, in GhostSetup setup)
            {
                Ghost = setup.ToGhost(component.Team);
            }

            public void ToComponent(ref TeamAllies component, in GhostSetup setup)
            {
                component.Team = setup.FromGhost(Ghost);
            }
        }

        public class Serializer : DeltaBufferSerializerBase<Snapshot, TeamAllies, GhostSetup>
        {
            public Serializer([NotNull] ISnapshotInstigator instigator, [NotNull] Context ctx) : base(instigator, ctx)
            {
            }
        }
    }
}