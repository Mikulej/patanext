﻿using System;
using System.Collections.Generic;
using System.Numerics;
using GameHost.Simulation.TabEcs;
using PataNext.Game.Abilities;
using PataNext.Module.Simulation.Components.GamePlay.Abilities;
using PataNext.Module.Simulation.Components.GamePlay.Units;
using PataNext.Module.Simulation.Game.GamePlay.Abilities;
using StormiumTeam.GameBase;

namespace PataNext.Module.Simulation.Game.GamePlay
{
	public static class AbilityUtility
	{
		public static int CompileStat(AbilityEngineSet engineSet, int original, double defaultMultiplier, double feverMultiplier, double perfectMultiplier)
		{
			var originalF = original * defaultMultiplier;
			if (engineSet.ComboSettings.CanEnterFever(engineSet.ComboState))
			{
				originalF *= feverMultiplier;
				if (engineSet.CurrentCommand.IsPerfect)
					originalF *= perfectMultiplier;
			}

			return original + ((int) Math.Round(originalF) - original);
		}

		public static float CompileStat(AbilityEngineSet engineSet, float original, double defaultMultiplier, double feverMultiplier, double perfectMultiplier)
		{
			var originalF = original * defaultMultiplier;
			if (engineSet.ComboSettings.CanEnterFever(engineSet.ComboState))
			{
				originalF *= feverMultiplier;
				if (engineSet.CurrentCommand.IsPerfect)
					originalF *= perfectMultiplier;
			}

			return (float) (original + (originalF - original));
		}

		public static UnitPlayState CompileStat<TList>(AbilityEngineSet engineSet, UnitPlayState playState,
		                                        in StatisticModifier defaultModifier, 
		                                        in StatisticModifier feverModifier, 
		                                        in StatisticModifier perfectModifier,
		                                        TList list = default,
		                                        GameWorld gameWorld = null)
			where TList : IList<ComponentReference>
		{
			defaultModifier.Multiply(ref playState, list, gameWorld);
			if (engineSet.ComboSettings.CanEnterFever(engineSet.ComboState))
			{
				feverModifier.Multiply(ref playState, list, gameWorld);
				if (engineSet.CurrentCommand.IsPerfect)
					perfectModifier.Multiply(ref playState, list, gameWorld);
			}

			return playState;
		}
		
		public struct GetTargetVelocityParameters
		{
			public Vector3       TargetPosition;
			public Vector3       PreviousPosition;
			public Vector3       PreviousVelocity;
			public UnitPlayState PlayState;
			public float         Acceleration;
			public float         Delta;
		}

		public static float GetTargetVelocityX(GetTargetVelocityParameters param, float deaccel_distance = -1, float deaccel_distance_max = -1)
		{
			var speed = MathUtils.LerpNormalized(Math.Abs(param.PreviousVelocity.X),
				param.PlayState.MovementAttackSpeed,
				param.PlayState.GetAcceleration() * param.Acceleration * param.Delta);

			if (deaccel_distance >= 0)
			{
				var dist = MathUtils.Distance(param.TargetPosition.X, param.PreviousPosition.X);
				if (dist > deaccel_distance && dist < deaccel_distance_max)
				{
					speed *= MathUtils.UnlerpNormalized(deaccel_distance, deaccel_distance_max, dist);
					speed =  Math.Max(speed, param.Delta);
				}
			}

			var newPosX = MathUtils.MoveTowards(param.PreviousPosition.X, param.TargetPosition.X, speed * param.Delta);

			return (newPosX - param.PreviousPosition.X) / param.Delta;
		}
	}
}