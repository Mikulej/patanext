﻿using System;
using System.Collections.Generic;
using System.Text;
using DefaultEcs;
using GameHost.Core.Bindables;
using GameHost.Core.Ecs;
using GameHost.Core.IO;
using GameHost.Core.Modding;
using GameHost.Core.Threading;
using GameHost.Injection;
using GameHost.Injection.Dependency;
using GameHost.Input;
using GameHost.Input.Default;
using GameHost.IO;

namespace PataNext.Module.Presentation.Controls
{
	// todo: Temporary file, it should be replaced by a real abstract class that will be able to load other interfaces...
	public class XamlFileLoader : AppObject
	{
		private Action        fileDependencyResolver;
		private InputDatabase inputDatabase;
		private CModule       module;

		private Entity     reloadInputEntity;
		private IScheduler scheduler;

		public Bindable<string> Xaml;

		public XamlFileLoader(Context context) : base(context)
		{
			Xaml = new Bindable<string>();

			DependencyResolver.Add(() => ref scheduler);
			DependencyResolver.Add(() => ref module);
			DependencyResolver.Add(() => ref inputDatabase);

			DependencyResolver.OnComplete(OnDependenciesResolved);
		}

		private void OnDependenciesResolved(IEnumerable<object> dependencies)
		{
			var storage = new StorageCollection {module.Storage.Value, module.DllStorage};

			fileDependencyResolver = async () =>
			{
				if (targetFileName == null)
					throw new InvalidOperationException();

				IStorage child = storage;
				if (targetDirectory != null)
					child = await storage.GetOrCreateDirectoryAsync(targetDirectory);

				// Add it as a dep
				var resolver = new DependencyResolver(scheduler, Context, "LoadFileSystem.GetFile");
				resolver.AddDependency(new FileDependency($"{targetFileName}.xaml", child));
				resolver.OnComplete(async deps =>
				{
					foreach (var dep in deps)
					{
						if (!(dep is IFile file))
							continue;

						var result = Encoding.UTF8.GetString(await file.GetContentAsync());
						scheduler.Add(() => Xaml.Value = result);
					}
				});
			};

			reloadInputEntity = inputDatabase.RegisterSingle<PressAction>(new PressAction.Layout("kb and mouse",
				new CInput("keyboard/r")));
		}

		private string targetDirectory, targetFileName;

		public void SetTarget(string dir, string file)
		{
			targetDirectory = dir;
			targetFileName  = file;

			fileDependencyResolver();
		}

		public void Update()
		{
			if (reloadInputEntity.Get<PressAction>().HasBeenPressed)
				fileDependencyResolver();
		}
	}
}