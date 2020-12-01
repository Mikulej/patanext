﻿using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities.Memory;
using GameHost.Core.Ecs;
using GameHost.Simulation.TabEcs;
using GameHost.Worlds.Components;
using Microsoft.Extensions.Logging;
using StormiumTeam.GameBase.Physics.Components;
using StormiumTeam.GameBase.SystemBase;
using StormiumTeam.GameBase.Transform.Components;
using ZLogger;

namespace StormiumTeam.GameBase.Physics.Systems
{
	public class PhysicsSystem : GameAppSystem
	{
		private ILogger logger;

		private IManagedWorldTime worldTime;

		public Simulation Simulation { get; }

		public BufferPool BufferPool { get; }

		public const float MaximumDistance = 0f;

		public PhysicsSystem(WorldCollection collection) : base(collection)
		{
			BufferPool = new BufferPool();
			Simulation = Simulation.Create(BufferPool, new NarrowPhaseCallbacks(), new PoseIntegratorCallbacks(), new PositionFirstTimestepper());

			DependencyResolver.Add(() => ref worldTime);
			DependencyResolver.Add(() => ref logger);
		}

		private ComponentType disposeComponentType;

		protected override unsafe void OnDependenciesResolved(IEnumerable<object> dependencies)
		{
			base.OnDependenciesResolved(dependencies);

			if (sizeof(TypedIndex) != sizeof(PhysicsCollider))
				throw new Exception("size mismatch");

			disposeComponentType = GameWorld.RegisterComponent("DisposeShapeFromTypeIndex", new CallDisposeBoard(this, sizeof(TypedIndex), 0));
		}

		public TypedIndex SetColliderShape<TShape>(GameEntityHandle entity, TShape shape, bool disposeOnRemove = true)
			where TShape : unmanaged, IShape
		{
			if (TryGetComponentData(entity, out PhysicsCollider prev))
				Simulation.Shapes.RecursivelyRemoveAndDispose(prev.Shape, BufferPool);

			var index = Simulation.Shapes.Add(shape);
			AddComponent(entity, new PhysicsCollider {Shape = index});

			var disposeComponent = GameWorld.AddComponent(entity, disposeComponentType);
			GameWorld.GetComponentBoard<CallDisposeBoard>(disposeComponentType).SetValue(disposeComponent.Id, index);

			return index;
		}

		public unsafe bool Sweep(GameEntityHandle against, TypedIndex shape, RigidPose pose, BodyVelocity velocity,
		                         out HitResult    hit,
		                         float            maximumT = MaximumDistance)
		{
			if (!TryGetComponentData(against, out PhysicsCollider againstCollider))
			{
				hit = default;
				return false;
			}

			var againstPose = RigidPose.Identity;
			if (TryGetComponentData(against, out Position position))
				againstPose.Position = position.Value;

			Simulation.Shapes[againstCollider.Shape.Type].GetShapeData(againstCollider.Shape.Index, out var shapePointer, out _);
			Simulation.Shapes[shape.Type].GetShapeData(shape.Index, out var thisShapePtr, out _);

			var swee = Sweep(
				shapePointer, againstCollider.Shape.Type, againstPose, default,
				thisShapePtr, shape.Type, pose, velocity,
				out hit,
				maximumT
			);

			/*if (swee)
			{
				Simulation.Shapes.UpdateBounds(againstPose, ref againstCollider.Shape, out var b1);
				Simulation.Shapes.UpdateBounds(pose, ref shape, out var b2);

				Console.WriteLine($"A(min={b1.Min} max={b1.Max}) B(min={b2.Min} max={b2.Max}) && {b1.Contains(ref b2)} && {hit.time0} {hit.time1}");
			}*/

			return swee;
		}

		public unsafe bool Sweep<TShape>(GameEntityHandle against, TShape collider, RigidPose pose, BodyVelocity velocity,
		                                 out HitResult    hit,
		                                 float            maximumT = MaximumDistance)
			where TShape : IShape
		{
			if (!TryGetComponentData(against, out PhysicsCollider againstCollider))
			{
				hit = default;
				return false;
			}

			var againstPose = RigidPose.Identity;
			if (TryGetComponentData(against, out Position position))
				againstPose.Position = position.Value;

			Simulation.Shapes[againstCollider.Shape.Type].GetShapeData(againstCollider.Shape.Index, out var shapePointer, out _);
			return Sweep(
				shapePointer, againstCollider.Shape.Type, againstPose, default,
				Unsafe.AsPointer(ref collider), collider.TypeId, pose, velocity,
				out hit,
				maximumT
			);
		}

		public unsafe bool Sweep(PhysicsCollider colliderA, RigidPose poseA, BodyVelocity velocityA,
		                         PhysicsCollider colliderB, RigidPose poseB, BodyVelocity velocityB,
		                         out HitResult   hit,
		                         float           maximumT = MaximumDistance)
		{
			Simulation.Shapes[colliderA.Shape.Type].GetShapeData(colliderA.Shape.Index, out var shapePointerA, out _);
			Simulation.Shapes[colliderB.Shape.Type].GetShapeData(colliderB.Shape.Index, out var shapePointerB, out _);

			return Sweep(
				shapePointerA, colliderA.Shape.Type, poseA, velocityA,
				shapePointerB, colliderB.Shape.Type, poseB, velocityB,
				out hit,
				maximumT);
		}

		public unsafe bool Sweep(void*         a, int typeA, RigidPose poseA, BodyVelocity velocityA,
		                         void*         b, int typeB, RigidPose poseB, BodyVelocity velocityB,
		                         out HitResult hit,
		                         float         maximumT = MaximumDistance)
		{
			var task = Simulation.NarrowPhase.SweepTaskRegistry.GetTask(typeA, typeB);

			if (task == null)
			{
				logger.ZLogError("No Task Found for {0},{1}", typeA, typeB);
				hit = default;
				return false;
			}

			//Console.WriteLine($"{poseA.Position} {poseB.Position}");

			var filter = new AlwaysTrueSweepFilter();
			var intersect = task.Sweep(
				a, typeA, poseA.Orientation, velocityA,
				b, typeB, poseB.Position - poseA.Position, poseB.Orientation, velocityB,
				maximumT, 1e-2f, 1e-5f, 25, ref filter, Simulation.Shapes, Simulation.NarrowPhase.SweepTaskRegistry, BufferPool,
				out hit.time0, out hit.time1, out hit.position, out hit.normal
			);
			hit.position += poseA.Position;

			return intersect;
		}
	}
}