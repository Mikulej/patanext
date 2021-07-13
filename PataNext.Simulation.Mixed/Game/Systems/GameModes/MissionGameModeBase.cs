﻿using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameHost.Core.Ecs;
using GameHost.Simulation.TabEcs.Interfaces;
using GameHost.Simulation.Utility.EntityQuery;
using StormiumTeam.GameBase.SystemBase;

namespace PataNext.Module.Simulation.GameModes
{
	public struct GameModeRequestLoadMap : IComponentData
	{
	}
	
	public struct GameModeRequestEndRound : IComponentData
	{
	}

	public class MissionGameModeBase<TGameMode> : GameModeSystemBase<TGameMode>
		where TGameMode : struct, IComponentData
	{
		public MissionGameModeBase(WorldCollection collection) : base(collection)
		{
		}

		protected EntityQuery EndRoundQuery { get; set; }

		protected override void OnDependenciesResolved(IEnumerable<object> dependencies)
		{
			base.OnDependenciesResolved(dependencies);

			EndRoundQuery = CreateEntityQuery(new[] {typeof(GameModeRequestEndRound)});
		}

		protected override async Task GetStateMachine(CancellationToken token)
		{
			await GameModeInitialisation();
			//await GameModeLoadMap(token);

			while (!token.IsCancellationRequested)
			{
				await GameModeStartRound();

				EndRoundQuery.RemoveAllEntities();

				while (!token.IsCancellationRequested
				       && !EndRoundQuery.Any())
				{
					await GameModePlayLoop();
					await Task.Yield();
				}

				await GameModeEndRound();
				await Task.Yield();
			}

			await GameModeCleanUp();
		}

		protected virtual async Task GameModeLoadMap(CancellationToken token)
		{
			token.ThrowIfCancellationRequested();
			
			var request = GameWorld.CreateEntity();
			GameWorld.AddComponent(request, default(GameModeRequestLoadMap));

			while (!token.IsCancellationRequested && HasComponent<GameModeRequestLoadMap>(request))
				await Task.Yield();

			await Task.Yield();
		}

		public void RequestEndRound()
		{
			GameWorld.AddComponent(GameWorld.CreateEntity(), default(GameModeRequestEndRound));
		}

		protected virtual Task GameModeInitialisation() => Task.CompletedTask;
		protected virtual Task GameModeStartRound()     => Task.CompletedTask;

		protected virtual Task GameModePlayLoop()  => Task.CompletedTask;
		protected virtual Task GameModeEndRound()  => Task.CompletedTask;
		protected virtual Task GameModeCleanUp() => Task.CompletedTask;
	}
}