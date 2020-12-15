﻿using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities.Memory;
using Box2D.NetStandard.Collision.Shapes;
using GameHost.Core.Ecs;
using GameHost.Revolution.NetCode.Components;
using GameHost.Simulation.TabEcs;
using GameHost.Simulation.TabEcs.Interfaces;
using GameHost.Simulation.Utility.EntityQuery;
using GameHost.Worlds.Components;
using PataNext.Module.Simulation.Components;
using PataNext.Module.Simulation.Components.GameModes;
using PataNext.Module.Simulation.Components.GamePlay.RhythmEngine;
using PataNext.Module.Simulation.Components.GamePlay.Units;
using PataNext.Module.Simulation.Components.Roles;
using PataNext.Module.Simulation.Components.Units;
using PataNext.Module.Simulation.Game.Providers;
using PataNext.Simulation.Mixed.Components.GamePlay.RhythmEngine;
using StormiumTeam.GameBase.GamePlay;
using StormiumTeam.GameBase.Network;
using StormiumTeam.GameBase.Network.Authorities;
using StormiumTeam.GameBase.Physics.Components;
using StormiumTeam.GameBase.Physics.Systems;
using StormiumTeam.GameBase.Roles.Components;
using StormiumTeam.GameBase.Roles.Descriptions;
using StormiumTeam.GameBase.SystemBase;
using StormiumTeam.GameBase.Transform.Components;

namespace PataNext.Module.Simulation.GameModes.InBasement
{
	public class AtCityGameModeSystem : GameModeSystemBase<AtCityGameModeData>
	{
		public struct PlayerFreeRoamCharacter : IComponentData
		{
			public GameEntity Entity;
		}

		private FreeRoamUnitProvider unitProvider;
		private NetReportTimeSystem  reportTimeSystem;

		private IManagedWorldTime worldTime;
		
		public AtCityGameModeSystem(WorldCollection collection) : base(collection)
		{
			DependencyResolver.Add(() => ref unitProvider);
			DependencyResolver.Add(() => ref reportTimeSystem);
			DependencyResolver.Add(() => ref worldTime);
		}

		private EntityQuery playerQuery, playerWithoutInputQuery, playerWithInputQuery;

		protected override void OnDependenciesResolved(IEnumerable<object> dependencies)
		{
			base.OnDependenciesResolved(dependencies);

			playerQuery             = CreateEntityQuery(new[] {typeof(PlayerDescription)});
			playerWithInputQuery    = QueryWith(playerQuery, new[] {typeof(FreeRoamInputComponent)});
			playerWithoutInputQuery = QueryWithout(playerQuery, new[] {typeof(FreeRoamInputComponent)});
		}

		protected virtual void PlayLoop()
		{
			foreach (var player in playerQuery)
			{
				ref readonly var input = ref GetComponentData<FreeRoamInputComponent>(player);

				var character = GetComponentData<PlayerFreeRoamCharacter>(player).Entity;
				if (GetComponentData<Position>(character).Value.X <= -5 && !HasComponent<Relative<RhythmEngineDescription>>(player))
				{
					// Add RhythmEngine (fully simulated by client)
					var rhythmEngine = GameWorld.CreateEntity();
					GameWorld.AddComponent(rhythmEngine, new RhythmEngineDescription());
					GameWorld.AddComponent(rhythmEngine, new RhythmEngineController {State      = RhythmEngineState.Playing, StartTime = worldTime.Total.Add(TimeSpan.FromSeconds(1))});
					GameWorld.AddComponent(rhythmEngine, new RhythmEngineSettings {BeatInterval = TimeSpan.FromSeconds(0.5), MaxBeat   = 4});
					GameWorld.AddComponent(rhythmEngine, new RhythmEngineLocalState());
					GameWorld.AddComponent(rhythmEngine, new RhythmEngineExecutingCommand());
					GameWorld.AddComponent(rhythmEngine, new Relative<PlayerDescription>(Safe(player)));
					GameWorld.AddComponent(rhythmEngine, GameWorld.AsComponentType<RhythmEngineCommandProgressBuffer>());
					GameWorld.AddComponent(rhythmEngine, GameWorld.AsComponentType<RhythmEnginePredictedCommandBuffer>());
					GameWorld.AddComponent(rhythmEngine, new GameCommandState());
					GameWorld.AddComponent(rhythmEngine, new SetRemoteAuthority<SimulationAuthority>());
					AddComponent(rhythmEngine, new NetworkedEntity());
					AddComponent(rhythmEngine, new OwnedNetworkedEntity(Safe(player)));
					GameCombo.AddToEntity(GameWorld, rhythmEngine);
					RhythmSummonEnergy.AddToEntity(GameWorld, rhythmEngine);

					GameWorld.RemoveComponent(character.Handle, AsComponentType<UnitFreeRoamMovement>());
					GameWorld.AddComponent(player, AsComponentType<GameRhythmInputComponent>());
					GameWorld.AddComponent(player, new Relative<RhythmEngineDescription>(Safe(rhythmEngine)));
					GameWorld.AddComponent(rhythmEngine, new Relative<PlayerDescription>(Safe(player)));
				}
			}
		}

		private void CreateBox(Vector3 pos)
		{
			var ent = CreateEntity();
			AddComponent(ent, new EnvironmentCollider());
			AddComponent(ent, new Position(pos));
			AddComponent(ent, new Relative<TeamDescription>(environmentTeam));

			var physicsSystem  = World.GetOrCreate(wc => new Box2DPhysicsSystem(wc));
			var entitySettings = World.Mgr.CreateEntity();
			entitySettings.Set<Shape>(new PolygonShape(4, 2));

			physicsSystem.AssignCollider(ent, entitySettings);
		}

		private GameEntity unitTeam;
		private GameEntity environmentTeam;
		protected override async Task GetStateMachine(CancellationToken token)
		{
			environmentTeam = Safe(CreateEntity());
			AddComponent(environmentTeam, new TeamDescription());
			AddComponent(environmentTeam, new SimulationAuthority());
			GameWorld.AddBuffer<TeamEntityContainer>(environmentTeam.Handle);
			
			unitTeam = Safe(CreateEntity());
			AddComponent(unitTeam, new TeamDescription());
			AddComponent(unitTeam, new SimulationAuthority());
			GameWorld.AddBuffer<TeamEntityContainer>(unitTeam.Handle);
			
			GameWorld.AddBuffer<TeamEnemies>(unitTeam.Handle).Add(new TeamEnemies(environmentTeam));
			GameWorld.AddBuffer<TeamEnemies>(environmentTeam.Handle).Add(new TeamEnemies(unitTeam));
			
			CreateBox(new Vector3(-3, 0, 0));

			while (!token.IsCancellationRequested)
			{
				// Add missing input component to players
				foreach (var player in playerWithoutInputQuery.GetEnumerator())
				{
					AddComponent<FreeRoamInputComponent>(player);
					AddComponent<SetRemoteAuthority<InputAuthority>>(player);

					var character = Safe(unitProvider.SpawnEntityWithArguments(new PlayableUnitProvider.Create
					{
						Direction = UnitDirection.Right,
						Statistics = new UnitStatistics()
					}));

					AddComponent(player, new PlayerFreeRoamCharacter {Entity = character});
					
					AddComponent(character, new Owner(Safe(player)));
					AddComponent(character, new Relative<PlayerDescription>(Safe(player)));
					AddComponent(character, new Relative<TeamDescription>(unitTeam));
					//AddComponent(character, new SetRemoteAuthority<SimulationAuthority>());
					AddComponent(character, new SimulationAuthority());
					AddComponent(character, new NetworkedEntity());

					GameWorld.Link(character.Handle, player, true);
				}

				PlayLoop();
				await Task.Yield();
			}

			// Remove input component from players
			foreach (var entity in playerWithInputQuery.GetEnumerator())
				GameWorld.RemoveComponent(entity, AsComponentType<FreeRoamInputComponent>());
		}
	}
}