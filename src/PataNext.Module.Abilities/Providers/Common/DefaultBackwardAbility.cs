using PataNext.Game.Modules.Abilities.SystemBase;
using PataNext.Game.Modules.RhythmEngine.Commands;
using Quadrum.Game.Modules.Simulation.Abilities.Components.Aspects;
using revecs.Core;
using revecs.Extensions.Generator.Components;
using revghost;

namespace PataNext.Module.Abilities.Providers.Defaults;

public partial struct DefaultBackwardAbility : ISparseComponent
{
    public class Provider : BaseRhythmAbilityProvider<DefaultBackwardAbility>
    {
        public Provider(Scope scope) : base(scope)
        {
        }

        protected override void GetComboCommands<TList>(TList componentTypes)
        {
            componentTypes.Add(BackwardCommand.ToComponentType(Simulation));
        }

        public override UEntityHandle SpawnEntity(CreateAbility data)
        {
            var ability = base.SpawnEntity(data);
            Simulation.AddMarchAbilityAspect(ability, new MarchAbilityAspect
            {
                AccelerationFactor = -0.5f,
                Target = MarchAbilityAspect.ETarget.All
            });

            return ability;
        }
    }
}