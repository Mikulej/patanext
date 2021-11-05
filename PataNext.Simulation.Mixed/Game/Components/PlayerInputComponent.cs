﻿using System;
using System.Runtime.CompilerServices;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.Features.ShareWorldState.BaseSystems;
using GameHost.Simulation.TabEcs.Interfaces;
using GameHost.Simulation.Utility.InterTick;

namespace PataNext.Module.Simulation.Components
{
	public enum AbilitySelection
	{
		Horizontal = 0,
		Top        = 1,
		Bottom     = 2
	}

	public unsafe struct GameRhythmInputComponent : IComponentData
	{
		public struct RhythmAction
		{
			public InterFramePressAction InterFrame;
			public bool                  IsActive;
			public bool                  IsSliding;
		}

		private RhythmAction action0;
		private RhythmAction action1;
		private RhythmAction action2;
		private RhythmAction action3;

		public Span<RhythmAction> Actions
		{
			get
			{
				fixed (RhythmAction* fixedPtr = &action0)
				{
					return new Span<RhythmAction>(fixedPtr, 4);
				}
			}
		}

		public InterFramePressAction AbilityInterFrame;
		public AbilitySelection      Ability;
		public float                 Panning;

		public class Register : RegisterGameHostComponentData<GameRhythmInputComponent>
		{
		}
	}

	public partial struct FreeRoamInputComponent : IComponentData
	{
		public float                 HorizontalMovement;
		public InterFramePressAction Up;
		public InterFramePressAction Down;

		// Should they be known to the host?
		/*public InterFramePressAction Confirm;
		public InterFramePressAction Cancel;*/

		public class Register : RegisterGameHostComponentData<FreeRoamInputComponent>
		{
		}
	}
}