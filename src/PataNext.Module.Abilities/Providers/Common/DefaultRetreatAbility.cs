using PataNext.Game.Modules.Abilities.SystemBase;
using PataNext.Game.Modules.RhythmEngine.Commands;
using Quadrum.Game.Modules.Simulation.Abilities.Components.Aspects;
using revecs.Core;
using revecs.Extensions.Generator.Components;
using revghost;

namespace PataNext.Module.Abilities.Providers.Defaults;

public partial struct DefaultRetreatAbility : ISparseComponent
{
    public const float StopTime      = 1.5f;
	public const float MaxActiveTime = StopTime + 0.5f;
    public int LastActiveId;
    public float AccelerationFactor;
	public float StartPosition;
	public float BackVelocity;
    public bool  IsRetreating;
    public float ActiveTime;
    public class Provider : BaseRhythmAbilityProvider<DefaultRetreatAbility>
    {
       
        public Provider(Scope scope) : base(scope)
        {
        }

        protected override void GetComboCommands<TList>(TList componentTypes)
        {
            componentTypes.Add(RetreatCommand.ToComponentType(Simulation));
        }

        public override UEntityHandle SpawnEntity(CreateAbility data)
        {
            var ability = base.SpawnEntity(data);
            Simulation.AddMarchAbilityAspect(ability, new MarchAbilityAspect
            {
                AccelerationFactor = 1,
                Target = MarchAbilityAspect.ETarget.All
            });

            return ability;
        }
    }
}