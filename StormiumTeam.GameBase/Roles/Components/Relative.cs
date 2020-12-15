﻿using System.Diagnostics.CodeAnalysis;
using GameHost.Injection;
using GameHost.Revolution.NetCode;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Systems;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.Features.ShareWorldState.BaseSystems;
using GameHost.Simulation.TabEcs;
using GameHost.Simulation.TabEcs.Interfaces;
using StormiumTeam.GameBase.Roles.Interfaces;

namespace StormiumTeam.GameBase.Roles.Components
{
	/// <summary>
	/// A relative path to a <see cref="GameEntity"/> with <see cref="Interfaces.IEntityDescription"/>
	/// </summary>
	/// <typeparam name="TDescription"></typeparam>
	public readonly struct Relative<TDescription> : IComponentData
		where TDescription : Interfaces.IEntityDescription
	{
		/// <summary>
		/// Path to the entity
		/// </summary>
		public readonly GameEntity Target;

		public GameEntityHandle Handle => Target.Handle;

		public Relative(GameEntity target)
		{
			Target = target;
		}

		public abstract class Register : RegisterGameHostComponentData<Relative<TDescription>>
		{

		}

		public class Serializer : DeltaComponentSerializerBase<RelativeSnapshot<TDescription>, Relative<TDescription>, GhostSetup>
		{
			public Serializer([NotNull] ISnapshotInstigator instigator, [NotNull] Context ctx) : base(instigator, ctx)
			{
				AddToBufferSettings = false;
			}
		}
	}

	public struct RelativeSnapshot<TDescription> : IReadWriteSnapshotData<RelativeSnapshot<TDescription>, GhostSetup>, ISnapshotSyncWithComponent<Relative<TDescription>, GhostSetup>
		where TDescription : IEntityDescription
	{
		public uint Tick { get; set; }

		public GameEntity Entity;

		public void Serialize(in BitBuffer buffer, in RelativeSnapshot<TDescription> baseline, in GhostSetup setup)
		{
			buffer.AddUIntD4Delta(Entity.Id, baseline.Entity.Id)
			      .AddUIntD4Delta(Entity.Version, baseline.Entity.Version);
		}

		public void Deserialize(in BitBuffer buffer, in RelativeSnapshot<TDescription> baseline, in GhostSetup setup)
		{
			Entity = new GameEntity(buffer.ReadUIntD4Delta(baseline.Entity.Id), buffer.ReadUIntD4Delta(baseline.Entity.Version));
		}

		public void FromComponent(in Relative<TDescription> component, in GhostSetup setup)
		{
			Entity = setup[component.Target];
		}

		public void ToComponent(ref Relative<TDescription> component, in GhostSetup setup)
		{
			component = new Relative<TDescription>(setup[Entity]);
		}
	}
}