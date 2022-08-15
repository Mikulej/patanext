using PataNext.Module.Abilities.Providers.Defaults;
using Quadrum.Game.Modules.Simulation;
using Quadrum.Game.Modules.Simulation.Abilities.Components;
using Quadrum.Game.Modules.Simulation.Abilities.SystemBase;
using Quadrum.Game.Modules.Simulation.Common.Transform;
using Quadrum.Game.Modules.Simulation.Units;
using Quadrum.Game.Utilities;
using revecs.Core;
using revghost;

namespace PataNext.Module.Abilities.Scripts.Defaults;

public partial class DefaultRetreatScript : AbilityScript<DefaultRetreatAbility>
{
	private const float START_RETREAT_TIME = 0.2f;
    const float walkbackTime = 3.0f;
      
    public DefaultRetreatScript(Scope scope) : base(scope)
    {
    }
    private GameTimeQuery timeQuery;
    private SetupCommands setupCommands;
    private ExecuteCommands execCommands;
    protected override void OnInit()
    {
        base.OnInit();

        timeQuery = new GameTimeQuery(Simulation);
        setupCommands = new SetupCommands(Simulation);
		execCommands = new ExecuteCommands(Simulation);
    }

    private float dt;
    protected override void OnSetup(ReadOnlySpan<UEntityHandle> abilities)
    {
        dt = (float) timeQuery.First().GameTime.Delta.TotalSeconds;

    }
    protected override void OnExecute(UEntityHandle owner, UEntityHandle self)
    {
        //ref var ability = ref GetComponentData<DefaultRetreatAbility>(self);
        //ref var ability = ref setupCommands.UpdateDefaultRetreatAbility(entity);
		ref var ability = ref execCommands.UpdateDefaultRetreatAbility(self);
		ref readonly var state = ref execCommands.ReadAbilityState(self);

			if (state.ActivationVersion != ability.LastActiveId)
			{
				ability.IsRetreating = false;
				ability.ActiveTime   = 0;
				ability.LastActiveId = state.ActivationVersion;
			}
			//ref readonly var translation = ref execCommands.UpdateVelocityComponent(owner);
			ref readonly var translation = ref execCommands.ReadPositionComponent(owner);
			//ref var          velocity    = ref GetComponentData<Velocity>(owner).Value;
			ref var velocity = ref execCommands.UpdateVelocityComponent(owner);
			if (!HasActiveOrChainingState(self))
			{
				if (MathUtils.Distance(ability.StartPosition, translation.X) > 2.5f
				    && ability.ActiveTime > 0.1f)
				{
					velocity.X = (ability.StartPosition - translation.X) * 3;
				}

				ability.ActiveTime   = 0;
				ability.IsRetreating = false;
				return;
			}
			
			ref readonly var playState     = ref execCommands.ReadUnitPlayState(owner);
			var unitDirection = execCommands.ReadUnitDirection(owner).Value;

			var wasRetreating = ability.IsRetreating;
			var retreatSpeed  = playState.MovementAttackSpeed * 3f;

			ability.IsRetreating =  ability.ActiveTime <= walkbackTime;
			ability.ActiveTime   += dt;

			if (!wasRetreating && ability.IsRetreating)
			{
				ability.StartPosition = translation.X;
				velocity.X            = -unitDirection * retreatSpeed;
			}

			// there is a little stop when the character is stopping retreating
			if (ability.ActiveTime >= DefaultRetreatAbility.StopTime && ability.ActiveTime <= walkbackTime)
			{
				// if he weight more, he will stop faster
				velocity.X = MathUtils.LerpNormalized(velocity.X, 0, playState.Weight * 0.25f * dt);
			}

			if (!ability.IsRetreating && ability.ActiveTime > walkbackTime)
			{
				// we add '2.8f' to boost the speed when backing up, so the unit can't chain retreat to go further
				if (wasRetreating)
					ability.BackVelocity = Math.Abs(ability.StartPosition - translation.X) * 2.8f;

				var newPosX = MathUtils.MoveTowards(translation.X, ability.StartPosition, ability.BackVelocity * dt);
				velocity.X = (newPosX - translation.X) / dt;
			}

			//ref var unitController = ref GetComponentData<UnitControllerState>(owner);
			ref var unitController = ref execCommands.UpdateUnitControllerState(owner);
			unitController.ControlOverVelocityX = true;
    }
	private partial record struct SetupCommands :
        DefaultRetreatAbility.Cmd.IWrite,
        AbilityRhythmEngineSet.Cmd.IRead;

    private partial record struct ExecuteCommands :
        DefaultRetreatAbility.Cmd.IWrite,
        AbilityState.Cmd.IRead,
        PositionComponent.Cmd.IRead,
		UnitDirection.Cmd.IRead,
        UnitControllerState.Cmd.IWrite,
        UnitPlayState.Cmd.IRead,
        VelocityComponent.Cmd.IWrite;
}