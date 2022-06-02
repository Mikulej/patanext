using System.Runtime.InteropServices;
using System.Xml;
using Collections.Pooled;
using GodotCLR;
using PataNext.Export.Godot.Presentation;
using PataNext.Game.Client.Core.Inputs;
using Quadrum.Game.Modules.Simulation.Application;
using Quadrum.Game.Modules.Simulation.Players;
using Quadrum.Game.Modules.Simulation.RhythmEngine;
using Quadrum.Game.Modules.Simulation.RhythmEngine.Commands;
using Quadrum.Game.Modules.Simulation.RhythmEngine.Commands.Components;
using Quadrum.Game.Modules.Simulation.RhythmEngine.Components;
using Quadrum.Game.Modules.Simulation.RhythmEngine.Utility;
using revecs;
using revecs.Core;
using revecs.Extensions.Buffers;
using revghost;
using RustTest;

namespace PataNext.Export.Godot;

// For now it's used for getting inputs
public partial class RhythmEnginePresentation : PresentationGodotBaseSystem
{
    public RhythmEnginePresentation(Scope scope) : base(scope)
    {
    }

    private EngineQuery engineQuery;
    private PlayerQuery playerQuery;
    private TimeQuery timeQuery;

    protected override void GetMatchedComponents(PooledList<ComponentType> all, PooledList<ComponentType> or,
        PooledList<ComponentType> none)
    {
        engineQuery = new EngineQuery(GameWorld);
        playerQuery = new PlayerQuery(GameWorld);
        timeQuery = new TimeQuery(GameWorld);

        all.Add(RhythmEngineSettings.Type.GetOrCreate(GameWorld));
        all.Add(RhythmEngineState.Type.GetOrCreate(GameWorld));
    }

    protected override bool EntityMatch(in UEntityHandle entity)
    {
        return true;
    }

    protected override bool OnSetPresentation(in UEntitySafe entity, out NodeProxy node)
    {
        node = new NodeProxy("Rhythm Engine", "res://rhythm_engine.tscn");
        return true;
    }

    protected override bool OnRemovePresentation(in UEntitySafe entity, in NodeProxy node)
    {
        return true;
    }

    static string to_patapon_drum(int key)
    {
        return key switch
        {
            1 => "Pata",
            2 => "Pon",
            3 => "Don",
            4 => "Chaka",
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
        };
    }

    protected override void OnPresentationLoop()
    {
        base.OnPresentationLoop();

        var player = playerQuery.First();
        if (player.Handle.Equals(default))
            throw new InvalidOperationException("null player");

        var time = timeQuery.First().GameTime;
        
        foreach (var entity in QueryWithPresentation)
        {
            var node = GameWorld.GetComponentData(entity, GenericType);
            while (node.Call("has_input_left").AsBool())
            {
                var lastInput = (int) node.Call("get_last_input").AsInt();
                GodotCLR.Godot.Print($"Input: {(DefaultCommandKeys) lastInput}");
                
                // TODO: should be before the simulation update
                // HACK: (see todo) + 1 since the rhythm systems will receive the input on next frame
                player.Input.Actions[lastInput - 1].InterFrame.Pressed = time.Frame + 1;
            }

            var title = $"Rhythm Engine ({entity})";
            var currCommand = "Current:  ";
            var predicted = "Predicted:  \n";

            foreach (var engine in engineQuery)
            {
                title += "  Elapsed: " + (int) engine.state.Elapsed.TotalSeconds + "s";

                for (var i = 0; i < engine.progress.Count; i++)
                {
                    currCommand += to_patapon_drum(engine.progress[i].Value.KeyId);
                    if (i + 1 < engine.progress.Count)
                        currCommand += " ";
                }
                
                for (var i = 0; i < engine.predicted.Count; i++)
                {
                    var actionType = CommandActions.Type.GetOrCreate(GameWorld).UnsafeCast<RhythmCommandAction>();
                    var actions = GameWorld.ReadComponent(engine.predicted[i].Value.Handle, actionType);

                    for (var j = 0; j < actions.Length; j++)
                    {
                        predicted += to_patapon_drum(actions[j].Key);
                        if (j + 1 < actions.Length)
                            predicted += " ";
                    }

                    if (i + 1 < engine.predicted.Count)
                        predicted += "\n";
                }
            }

            Variant.New(title, out var titleVariant);
            Variant.New(currCommand, out var currCommandVariant);
            Variant.New(predicted, out var predictedVariant);
            node.SetProperty("title", ref titleVariant);
            node.SetProperty("curr_command", ref currCommandVariant);
            node.SetProperty("predicted", ref predictedVariant);
        }
    }

    private partial struct EngineQuery : IQuery<(
        Read<RhythmEngineSettings> settings,
        Write<RhythmEngineState> state,
        Write<RhythmEngineRecoveryState> recovery,
        Write<GameCommandState> executing,
        Write<RhythmEngineCommandProgress> progress,
        Read<RhythmEnginePredictedCommands> predicted
        )>
    {
    }

    private partial struct PlayerQuery : IQuery<(Write<GameRhythmInput> Input, All<PlayerDescription>)>
    {
    }
    
    private partial struct TimeQuery : IQuery<Read<GameTime>>
    {
    }
}