﻿
using System;
using System.Collections.Generic;
using GameHost.Core.Ecs;
using GameHost.Native;
using GameHost.Native.Fixed;
using GameHost.Simulation.TabEcs;
using GameHost.Simulation.TabEcs.HLAPI;
using GameHost.Simulation.Utility.EntityQuery;
using GameHost.Simulation.Utility.Resource;
using PataNext.Module.Simulation.Components;
using PataNext.Module.Simulation.Components.GamePlay.Abilities;
using PataNext.Module.Simulation.Components.GamePlay.RhythmEngine;
using PataNext.Module.Simulation.Components.Roles;
using PataNext.Module.Simulation.Game.RhythmEngine;
using PataNext.Module.Simulation.Passes;
using PataNext.Module.Simulation.Resources;
using PataNext.Simulation.Mixed.Components.GamePlay.RhythmEngine;
using StormiumTeam.GameBase.Network.Authorities;
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
			abilityQuery = CreateEntityQuery(new[]
			{
				GameWorld.AsComponentType<Relative<RhythmEngineDescription>>(),
				GameWorld.AsComponentType<OwnerActiveAbility>(),
				GameWorld.AsComponentType<OwnedRelative<AbilityDescription>>(),
			});
			validAbilityMask = CreateEntityQuery(new[]
			{
				GameWorld.AsComponentType<AbilityActivation>(),
				GameWorld.AsComponentType<AbilityState>(),
				GameWorld.AsComponentType<AbilityEngineSet>(),
				GameWorld.AsComponentType<AbilityCommands>(),
			});
		}

		private List<GameEntityHandle> abilityToUpdateCooldown = new();

		// TODO: The foreach loop should do parallel work.
		public void OnAbilityPreSimulationPass()
		{
			validAbilityMask.CheckForNewArchetypes();

			var relativeEngineAccessor    = new ComponentDataAccessor<Relative<RhythmEngineDescription>>(GameWorld);
			var engineStateAccessor       = new ComponentDataAccessor<RhythmEngineLocalState>(GameWorld);
			var engineSettingsAccessor    = new ComponentDataAccessor<RhythmEngineSettings>(GameWorld);
			var executingCommandAccessor  = new ComponentDataAccessor<RhythmEngineExecutingCommand>(GameWorld);
			var gameComboStateAccessor    = new ComponentDataAccessor<GameCombo.State>(GameWorld);
			var gameComboSettingsAccessor = new ComponentDataAccessor<GameCombo.Settings>(GameWorld);
			var gameCommandStateAccessor  = new ComponentDataAccessor<GameCommandState>(GameWorld);

			var abilityStateComponent      = AsComponentType<AbilityState>();
			var ownerActiveAbilityAccessor = new ComponentDataAccessor<OwnerActiveAbility>(GameWorld);
			var abilityStateAccessor       = new ComponentDataAccessor<AbilityState>(GameWorld);
			var abilityActivationAccessor  = new ComponentDataAccessor<AbilityActivation>(GameWorld);
			var abilityCommands            = new ComponentDataAccessor<AbilityCommands>(GameWorld);
			var engineSetAccessor          = new ComponentDataAccessor<AbilityEngineSet>(GameWorld);
			foreach (var entity in abilityQuery.GetEnumerator())
			{
				// Indicate whether or not we own the entity simulation.
				// If we don't, we should only fill displaying state, and not modifying external and internal state.
				var isSimulationOwned = GameWorld.HasComponent<SimulationAuthority>(entity);
				var abilityBuffer     = GameWorld.GetBuffer<OwnedRelative<AbilityDescription>>(entity);

				ref var activeSelf = ref ownerActiveAbilityAccessor[entity];

				var engineEntity = relativeEngineAccessor[entity].Handle;

				ref readonly var engineState       = ref engineStateAccessor[engineEntity];
				ref readonly var engineSettings    = ref engineSettingsAccessor[engineEntity];
				ref readonly var executingCommand  = ref executingCommandAccessor[engineEntity];
				ref readonly var gameComboState    = ref gameComboStateAccessor[engineEntity];
				ref readonly var gameComboSettings = ref gameComboSettingsAccessor[engineEntity];
				ref readonly var gameCommandState  = ref gameCommandStateAccessor[engineEntity];

				// Indicate whether or not we own the engine simulation
				var isRhythmEngineOwned  = GameWorld.HasComponent<SimulationAuthority>(engineEntity);
				var isNewIncomingCommand = updateAndCheckNewIncomingCommand(gameComboState, gameCommandState, executingCommand, ref activeSelf);
				var isActivation = activeSelf.LastActivationTime == -1
				                   && gameComboState.Count > 0
				                   && gameCommandState.StartTimeSpan <= engineState.Elapsed;

				var isOnMount = false;

				#region Check For Active Hero Mode

				var isHeroModeActive = false;
				if (TryGetComponentData(activeSelf.Active.Handle, out AbilityActivation activeAbilityActivation)
				    && TryGetComponentData(activeSelf.Active.Handle, out AbilityCommands activeAbilityCommands)
				    && activeAbilityActivation.Type.HasFlag(EAbilityActivationType.HeroMode))
				{
					isHeroModeActive = true;

					ref var abilityState = ref abilityStateAccessor[activeSelf.Active.Handle];
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

				var priorityType = (int) (isHeroModeActive ? EAbilityActivationType.HeroMode : EAbilityActivationType.NoConstraints);
				var priority     = -1;
				{
					var previousCommand = default(GameResource<RhythmCommandResource>);
					var offset          = 1;
					if (executingCommand.ActivationBeatStart > RhythmEngineUtility.GetActivationBeat(engineState, engineSettings))
						offset++;

					var cmdIdx = activeSelf.CurrentCombo.GetLength() - 1 - offset;
					if (cmdIdx > 0 && activeSelf.CurrentCombo.GetLength() >= cmdIdx + 1)
						previousCommand = activeSelf.CurrentCombo.Span[cmdIdx];

					abilityToUpdateCooldown.Clear();
					foreach (var ownedAbility in abilityBuffer)
					{
						var abilityEntity = ownedAbility.Target.Handle;
						if (!validAbilityMask.MatchAgainst(abilityEntity))
							continue;

						ref var abilityState = ref abilityStateAccessor[abilityEntity];
						abilityState.Phase = EAbilityPhase.None;

						engineSetAccessor[abilityEntity] = new AbilityEngineSet
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

						ref readonly var activation = ref abilityActivationAccessor[abilityEntity];
						ref readonly var commands   = ref abilityCommands[abilityEntity];

						// The conditions must indicate a flag, and then a negative statement (if we are flag A, check why we can't activate it)
						var canActivate = !(
							activation.Type.HasFlag(EAbilityActivationType.HeroMode) && !(gameComboSettings.CanEnterFever(gameComboState) && executingCommand.IsPerfect)
							|| activation.Type.HasFlag(EAbilityActivationType.Mount) && !isOnMount
							|| activation.Type.HasFlag(EAbilityActivationType.Unmounted) && isOnMount
						);

						if (abilityState.CommandCooldown > 0 && (activeSelf.Active != Safe(abilityEntity) || abilityState.Combo > 0))
						{
							canActivate = false;
						}

						if (!canActivate)
						{
							abilityToUpdateCooldown.Add(abilityEntity);
							continue;
						}

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
							activeSelf.Incoming = Safe(abilityEntity);
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
					if (HasComponent(activeSelf.Active.Handle, abilityStateComponent))
					{
						ref var state = ref abilityStateAccessor[activeSelf.Active.Handle];
						state.Phase = EAbilityPhase.None;
						state.Combo = 0;
					}

					activeSelf.PreviousActive = activeSelf.Active;
					activeSelf.Active         = default;
				}

				// Set next active ability, and reset imperfect data if active.
				if (activeSelf.Active != activeSelf.Incoming)
				{
					if (isActivation)
					{
						if (HasComponent(activeSelf.Active.Handle, abilityStateComponent))
						{
							ref var state = ref abilityStateAccessor[activeSelf.Active.Handle];
							state.Combo = 0;
						}

						activeSelf.PreviousActive = activeSelf.Active;
						activeSelf.Active         = activeSelf.Incoming;
						if (HasComponent(activeSelf.Active.Handle, abilityStateComponent))
						{
							ref var state = ref abilityStateAccessor[activeSelf.Active.Handle];
							state.HeroModeImperfectCountWhileActive = 0;
						}
					}
				}

				// Decrease cooldowns of abilities that have one when a command has been triggered.
				var engineElapsedMs = (int) (engineState.Elapsed.Ticks / TimeSpan.TicksPerMillisecond);
				if (isActivation)
				{
					activeSelf.LastActivationTime = engineElapsedMs;

					foreach (var abilityEntity in abilityToUpdateCooldown)
					{
						ref var cooldown = ref abilityStateAccessor[abilityEntity].CommandCooldown;
						cooldown = Math.Max(0, cooldown - 1);
					}
				}

				// We update incoming state before active state (in case if it's the same ability...)
				if (activeSelf.Incoming != default)
				{
					ref var incomingState = ref abilityStateAccessor[activeSelf.Incoming.Handle];
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
					ref var activeController = ref abilityStateAccessor[activeSelf.Active.Handle];
					activeAbilityActivation = abilityActivationAccessor[activeSelf.Active.Handle];

					if (gameCommandState.StartTimeSpan <= engineState.Elapsed)
					{
						activeController.Phase = EAbilityPhase.None;
						if (isActivation)
						{
							if (activeController.ActivationVersion == 0)
								activeController.ActivationVersion++;

							activeController.ActivationVersion++;

							if (activeAbilityActivation.DefaultCooldownOnActivation > 0)
								GetComponentData<AbilityState>(activeSelf.Active.Handle).CommandCooldown += activeAbilityActivation.DefaultCooldownOnActivation;

							if (activeAbilityActivation.Type.HasFlag(EAbilityActivationType.HeroMode) && isRhythmEngineOwned
							                                                                          && HasComponent<RhythmSummonEnergy>(engineEntity))
							{
								GetComponentData<RhythmSummonEnergy>(engineEntity).Value += 15;
							}
						}
					}

					if (activeAbilityActivation.Type.HasFlag(EAbilityActivationType.HeroMode)
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