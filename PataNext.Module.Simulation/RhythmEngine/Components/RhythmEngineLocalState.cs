﻿using System;
using PataNext.Module.Simulation.RhythmEngine.Data;

namespace PataNext.Module.Simulation.RhythmEngine
{
	public interface IRhythmEngineState
	{
		public FlowPressure LastPressure           { get; set; }
		public int          RecoveryActivationBeat { get; set; }
		public TimeSpan     Elapsed                { get; set; }

		bool CanRunCommands                 => Elapsed > TimeSpan.Zero;
		bool IsRecovery(int activationBeat) => RecoveryActivationBeat > activationBeat;
	}

	public struct RhythmEngineLocalState : IRhythmEngineState
	{
		public FlowPressure LastPressure           { get; set; }
		public int          RecoveryActivationBeat { get; set; }
		public TimeSpan     Elapsed                { get; set; }

		public bool CanRunCommands => Elapsed > TimeSpan.Zero;

		public bool IsRecovery(int activationBeat)
		{
			return RecoveryActivationBeat > activationBeat;
		}
	}
}