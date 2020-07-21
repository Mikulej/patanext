﻿using System;
using ENet;
using GameHost.Applications;
using GameHost.Core.Ecs;
using GameHost.Core.IO;
using GameHost.Inputs.Features;
using GameHost.Simulation.Application;
using GameHost.Transports;
using RevolutionSnapshot.Core.Buffers;

namespace PataNext.Export.Desktop
{
	[RestrictToApplication(typeof(SimulationApplication))]
	public class AddInputSimulationFeature : AppSystem
	{
		private ENetTransportDriver enetDriver;
		private HeaderTransportDriver driver;

		private DataBufferWriter header;
		
		public AddInputSimulationFeature(WorldCollection collection) : base(collection)
		{
			header = new DataBufferWriter(0);
			
			AddDisposable(enetDriver = new ENetTransportDriver(1));
			AddDisposable(driver = new HeaderTransportDriver(enetDriver)
			{
				Header = header
			});
		}

		protected override void OnInit()
		{
			base.OnInit();

			header.WriteInt((int) MessageType.InputData);

			var addr = new Address();
			addr.Port = 5960;
			enetDriver.Connect(addr);

			World.Mgr.CreateEntity()
			     .Set<IFeature>(new ClientInputFeature(driver, default));
		}

		protected override void OnUpdate()
		{
			base.OnUpdate();
			
			enetDriver.Update();
			
			while (enetDriver.Accept().IsCreated)
			{}

			TransportEvent ev;
			while ((ev = enetDriver.PopEvent()).Type != TransportEvent.EType.None)
			{
				switch (ev.Type)
				{
					case TransportEvent.EType.None:
						break;
					case TransportEvent.EType.RequestConnection:
						break;
					case TransportEvent.EType.Connect:
						break;
					case TransportEvent.EType.Disconnect:
						break;
					case TransportEvent.EType.Data:
						var reader = new DataBufferReader(ev.Data);
						var type = (MessageType) reader.ReadValue<int>();
						switch (type)
						{
							case MessageType.Unknown:
								break;
							case MessageType.Rpc:
								break;
							case MessageType.InputData:
							{
								var subType = (EMessageInputType) reader.ReadValue<int>();
								switch (subType)
								{
									case EMessageInputType.None:
										break;
									case EMessageInputType.Register:
										throw new NotImplementedException($"GameHost shouldn't receive {nameof(EMessageInputType.Register)} event");
									case EMessageInputType.ReceiveRegister:
									{
										break;
									}
									case EMessageInputType.ReceiveInputs:
									{
										break;
									}
									default:
										throw new ArgumentOutOfRangeException();
								}

								break;
							}
							case MessageType.SimulationData:
								break;
							default:
								throw new ArgumentOutOfRangeException();
						}

						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
			}
		}

		public override void Dispose()
		{
			base.Dispose();
			header.Dispose();
		}
	}
}