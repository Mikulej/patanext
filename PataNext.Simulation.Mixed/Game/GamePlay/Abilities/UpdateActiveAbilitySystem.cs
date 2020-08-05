﻿using System;
using System.Collections.Generic;
using GameHost.Core.Ecs;
using GameHost.Native;
using GameHost.Native.Fixed;
using GameHost.Simulation.TabEcs;
using GameHost.Simulation.Utility.EntityQuery;
using GameHost.Simulation.Utility.Resource;
using PataNext.Module.Simulation.Components;
using PataNext.Module.Simulation.Components.GamePlay.Abilities;
using PataNext.Module.Simulation.Components.GamePlay.RhythmEngine;
using PataNext.Module.Simulation.Components.Roles;
using PataNext.Module.Simulation.Game.RhythmEngine;
using PataNext.Module.Simulation.Passes;
using PataNext.Module.Simulation.Resources;
using StormiumTeam.GameBase.Roles.Components;
using StormiumTeam.GameBase.SystemBase;
using Array = NetFabric.Hyperlinq.Array;

namespace PataNext.Module.Simulation.Game.GamePlay.Abilities
{
	public class UpdateActiveAbilitySystem : GameAppSystem, IAbilityPreSimulationPass
	{
		public UpdateActiveAbilitySystem(WorldCollection collection) : base(collection)
		{
		}

		private EntityQuery abilityQuery;
		private EntityQuery validRhythmEngineMask;
		private EntityQuery validAbilityMask;

		protected override void OnDependenciesResolved(IEnumerable<object> dependencies)
		{
			base.OnDependenciesResolved(dependencies);
			abilityQuery = CreateEntityQuery(stackalloc[]
			{
				GameWorld.AsComponentType<Relative<RhythmEngineDescription>>(),
				GameWorld.AsComponentType<OwnerActiveAbility>(),
				GameWorld.AsComponentType<OwnedRelative<AbilityDescription>>(),
			});
			validAbilityMask = CreateEntityQuery(stackalloc[]
			{
				GameWorld.AsComponentType<AbilityActivation>(),
				GameWorld.AsComponentType<AbilityState>(),
				GameWorld.AsComponentType<AbilityEngineSet>(),
				GameWorld.AsComponentType<AbilityCommands>(),
			});
		}

		public void OnAbilityPreSimulationPass()
		{
			validAbilityMask.CheckForNewArchetypes();
			foreach (var entity in abilityQuery.GetEntities())
			{
				// Indicate whether or not we own the entity simulation.
				// If we don't, we should only fill displaying state, and not modifying external and internal state.
				var isSimulationOwned = GameWorld.HasComponent<IsSimulationOwned>(entity);
				var abilityBuffer     = GameWorld.GetBuffer<OwnedRelative<AbilityDescription>>(entity);

				ref var activeSelf = ref GetComponentData<OwnerActiveAbility>(entity);

				ref readonly var engineEntity      = ref GetComponentData<Relative<RhythmEngineDescription>>(entity).Target;
				ref readonly var engineState       = ref GetComponentData<RhythmEngineLocalState>(engineEntity);
				ref readonly var engineSettings    = ref GetComponentData<RhythmEngineSettings>(engineEntity);
				ref readonly var executingCommand  = ref GetComponentData<RhythmEngineExecutingCommand>(engineEntity);
				ref readonly var gameComboState    = ref GetComponentData<GameCombo.State>(engineEntity);
				ref readonly var gameComboSettings = ref GetComponentData<GameCombo.Settings>(engineEntity);
				ref readonly var gameCommandState  = ref GetComponentData<GameCommandState>(engineEntity);

				var isNewIncomingCommand = updateAndCheckNewIncomingCommand(gameComboState, gameCommandState, executingCommand, ref activeSelf);

				#region Check For Active Hero Mode

				var isHeroModeActive = false;
				if (TryGetComponentData(activeSelf.Active, out AbilityActivation activeAbilityActivation)
				    && TryGetComponentData(activeSelf.Active, out AbilityCommands activeAbilityCommands)
				    && activeAbilityActivation.Type == EAbilityActivationType.HeroMode)
				{
					isHeroModeActive = true;

					ref var abilityState = ref GetComponentData<AbilityState>(activeSelf.Active);
					// Set as inactive if we can't chain it with the next command.
					if (executingCommand.CommandTarget != activeAbilityCommands.Chaining)
					{
						if (!Array.Contains(activeAbilityCommands.HeroModeAllowedCommands.Span, executingCommand.CommandTarget))
							isHeroModeActive = false;
						else if (isNewIncomingCommand)
							abilityState.HeroModeImperfectCountWhileActive++;
					}
					// Set as inactive if we had too much imperfect combo.
					else if (!executingCommand.IsPerfect && activeAbilityActivation.HeroModeImperfectLimitBeforeDeactivation > 0
					                                     && isNewIncomingCommand)
					{
						abilityState.HeroModeImperfectCountWhileActive++;
					}
					else if (!gameComboSettings.CanEnterFever(gameComboState))
						isHeroModeActive = false;

					if (abilityState.HeroModeImperfectCountWhileActive >= activeAbilityActivation.HeroModeImperfectLimitBeforeDeactivation)
						isHeroModeActive = false;
				}

				#endregion

				// If we have the Hero Mode active, this mean we already know it will be the next incoming command
				activeSelf.Incoming = isHeroModeActive ? activeSelf.Active : default;

				#region Select Correct abilities, and update state of owned ones

				var priorityType = (int) (isHeroModeActive ? EAbilityActivationType.HeroMode : EAbilityActivationType.Normal);
				var priority     = -1;
				{
					var previousCommand = default(GameResource<RhythmCommandResource>);
					var offset          = 1;
					if (executingCommand.ActivationBeatStart > RhythmEngineUtility.GetActivationBeat(engineState, engineSettings))
						offset++;

					var cmdIdx = activeSelf.CurrentCombo.GetLength() - 1 - offset;
					if (cmdIdx > 0 && activeSelf.CurrentCombo.GetLength() >= cmdIdx + 1)
						previousCommand = activeSelf.CurrentCombo.Span[cmdIdx];

					foreach (var ownedAbility in abilityBuffer)
					{
						var abilityEntity = ownedAbility.Target;
						if (!validAbilityMask.MatchAgainst(abilityEntity))
							continue;

						ref var abilityState = ref GetComponentData<AbilityState>(abilityEntity);
						abilityState.Phase = EAbilityPhase.None;

						GetComponentData<AbilityEngineSet>(abilityEntity) = new AbilityEngineSet
						{
							Engine          = engineEntity,
							Process         = engineState,
							Settings        = engineSettings,
							CurrentCommand  = executingCommand,
							ComboState      = gameComboState,
							ComboSettings   = gameComboSettings,
							CommandState    = gameCommandState,
							Command         = executingCommand.CommandTarget,
							PreviousCommand = previousCommand,
						};

						ref readonly var activation = ref GetComponentData<AbilityActivation>(abilityEntity);
						ref readonly var commands   = ref GetComponentData<AbilityCommands>(abilityEntity);
						if (activation.Type == EAbilityActivationType.HeroMode && !(gameComboSettings.CanEnterFever(gameComboState) && executingCommand.IsPerfect))
							continue;

						var commandPriority     = commands.Combos.GetLength();
						var commandPriorityType = (int) activation.Type;

						if (commands.Chaining == executingCommand.CommandTarget
						    && isComboIdentical(commands.Combos, activeSelf.CurrentCombo)
						    && (activeSelf.Incoming == default || activeSelf.Incoming != default && ((priority < commandPriority && priorityType == commandPriorityType) || priorityType < commandPriorityType))
						    && (activation.Selection == gameCommandState.Selection
						        || activeSelf.Incoming == default && activation.Selection == AbilitySelection.Horizontal))
						{
							if (activation.Selection == gameCommandState.Selection)
							{
								priority = commandPriority;
							}

							priorityType        = (int) activation.Type;
							activeSelf.Incoming = abilityEntity;
						}
					}
				}

				#endregion

				// If we are not chaining anymore or if the chain is finished, terminate our current command.
				if ((!gameCommandState.HasActivity(engineState, engineSettings) && gameCommandState.ChainEndTimeMs < engineState.Elapsed.TotalMilliseconds
				     || gameComboState.Count <= 0)
				    && activeSelf.Active != default)
				{
					// It might be possible that the entity got deleted... so we need to check if it got the component
					if (HasComponent<AbilityState>(activeSelf.Active))
					{
						ref var state = ref GetComponentData<AbilityState>(activeSelf.Active);
						state.Phase = EAbilityPhase.None;
						state.Combo = 0;
					}

					activeSelf.Active = default;
				}

				// Set next active ability, and reset imperfect data if active.
				if (activeSelf.Active != activeSelf.Incoming)
				{
					if (executingCommand.ActivationBeatStart <= RhythmEngineUtility.GetActivationBeat(engineState, engineSettings))
					{
						if (HasComponent<AbilityState>(activeSelf.Active))
						{
							ref var state = ref GetComponentData<AbilityState>(activeSelf.Active);
							state.Combo = 0;
						}

						activeSelf.Active = activeSelf.Incoming;
						if (HasComponent<AbilityState>(activeSelf.Active))
						{
							ref var state = ref GetComponentData<AbilityState>(activeSelf.Active);
							state.HeroModeImperfectCountWhileActive = 0;
						}
					}
				}

				// We update incoming state before active state (in case if it's the same ability...)
				if (activeSelf.Incoming != default)
				{
					ref var incomingState = ref GetComponentData<AbilityState>(activeSelf.Incoming);
					incomingState.Phase |= EAbilityPhase.WillBeActive;
					if (isNewIncomingCommand)
					{
						incomingState.UpdateVersion++;
						incomingState.Combo++;
						if (!isHeroModeActive)
							incomingState.HeroModeImperfectCountWhileActive = 0;
					}
				}

				// Update data in the active ability
				if (activeSelf.Active != default)
				{
					ref var activeController = ref GetComponentData<AbilityState>(activeSelf.Active);
					activeAbilityActivation = GetComponentData<AbilityActivation>(activeSelf.Active);

					var engineElapsedMs = (int) (engineState.Elapsed.Ticks / TimeSpan.TicksPerMillisecond);
					if (gameCommandState.StartTimeSpan <= engineState.Elapsed)
					{
						activeController.Phase = EAbilityPhase.None;
						if (activeSelf.LastActivationTime == -1)
						{
							activeSelf.LastActivationTime = engineElapsedMs;
							if (activeController.ActivationVersion == 0)
								activeController.ActivationVersion++;

							activeController.ActivationVersion++;

							if (activeAbilityActivation.Type == EAbilityActivationType.HeroMode && isSimulationOwned)
							{
								// TODO: Add JinnEnergy
								/*gameCombo.JinnEnergy                   += 15;
								gameComboStateFromEntity[engineEntity] =  gameCombo;*/
							}
						}
					}

					if (activeAbilityActivation.Type == EAbilityActivationType.HeroMode
					    && activeController.Combo <= 1              // only do it if it's the first combo...
					    && activeSelf.Active == activeSelf.Incoming // only if the next command is the same as the current one...
					    && gameCommandState.StartTimeSpan + engineSettings.BeatInterval > engineState.Elapsed)
					{
						// delay the command for the first frame
						activeController.Phase |= EAbilityPhase.HeroActivation;
					}

					if ((activeController.Phase & EAbilityPhase.HeroActivation) == 0)
						activeController.Phase |= gameCommandState.IsGamePlayActive(engineElapsedMs) ? EAbilityPhase.Active : EAbilityPhase.Chaining;
				}
			}
		}

		private static bool updateAndCheckNewIncomingCommand(in  GameCombo.State    gameCombo, in GameCommandState commandState, in RhythmEngineExecutingCommand executingCommand,
		                                                     ref OwnerActiveAbility activeSelf)
		{
			if (gameCombo.Count <= 0)
			{
				activeSelf.LastActivationTime = -1;
				activeSelf.CurrentCombo.Clear();
				return false;
			}

			if (activeSelf.LastCommandActiveTime == commandState.StartTimeMs
			    || executingCommand.CommandTarget == default)
				return false;

			activeSelf.LastCommandActiveTime = commandState.StartTimeMs;
			activeSelf.LastActivationTime    = -1;
			activeSelf.AddCombo(executingCommand.CommandTarget);
			return true;

		}

		private static bool isComboIdentical(FixedBuffer32<GameResource<RhythmCommandResource>> abilityCombo, FixedBuffer32<GameResource<RhythmCommandResource>> unitCombo)
		{
			var start = unitCombo.GetLength() - 1 - abilityCombo.GetLength();
			var end   = unitCombo.GetLength() - 1;

			if ((end - start) < abilityCombo.GetLength() || start < 0)
				return false;

			for (var i = start; i != end; i++)
			{
				if (abilityCombo.Span[i - start] != unitCombo.Span[i])
					return false;
			}

			return true;
		}
	}
}