﻿namespace PataNext.Feature.RhythmEngineAudio.BGM
{
	public abstract class BFileDescription
	{
	}

	public class BFileSampleDescription : BFileDescription
	{
		public string SampleName;
	}

	public class BFileOnEnterFeverSoundDescription : BFileDescription
	{
	}

	public class BFileOnFeverLostSoundDescription : BFileDescription
	{
	}
}