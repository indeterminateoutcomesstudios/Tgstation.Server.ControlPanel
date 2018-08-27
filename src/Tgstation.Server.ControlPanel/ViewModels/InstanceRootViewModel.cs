﻿using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Api.Rights;
using Tgstation.Server.Client;

namespace Tgstation.Server.ControlPanel.ViewModels
{
	sealed class InstanceRootViewModel : ViewModelBase, ITreeNode
	{
		public string Title => "Instances";

		public string Icon
		{
			get => icon;
			private set => this.RaiseAndSetIfChanged(ref icon, value);
		}

		public IReadOnlyList<ITreeNode> Children
		{
			get => children;
			set => this.RaiseAndSetIfChanged(ref children, value);
		}

		readonly PageContextViewModel pageContext;
		readonly IInstanceManagerClient instanceManagerClient;
		readonly IUserRightsProvider userRightsProvider;

		IReadOnlyList<ITreeNode> children;
		string icon;
		bool loading;

		public InstanceRootViewModel(PageContextViewModel pageContext, IInstanceManagerClient instanceManagerClient, IUserRightsProvider userRightsProvider)
		{
			this.pageContext = pageContext ?? throw new ArgumentNullException(nameof(pageContext));
			this.instanceManagerClient = instanceManagerClient ?? throw new ArgumentNullException(nameof(instanceManagerClient));
			this.userRightsProvider = userRightsProvider ?? throw new ArgumentNullException(nameof(userRightsProvider));

			async void InitalLoad() => await Refresh(default).ConfigureAwait(false);
			InitalLoad();
		}

		async Task Refresh(CancellationToken cancellationToken)
		{
			lock (this)
			{
				if (loading)
					return;
				loading = true;
			}

			try
			{
				var hasReadRight = userRightsProvider.InstanceManagerRights.HasFlag(InstanceManagerRights.List) || userRightsProvider.InstanceManagerRights.HasFlag(InstanceManagerRights.Read);
				var hasCreateRight = userRightsProvider.InstanceManagerRights.HasFlag(InstanceManagerRights.Create);

				if (!hasReadRight && !hasCreateRight)
				{
					Icon = "resm:Tgstation.Server.ControlPanel.Assets.denied.jpg";
					Children = null;
					return;
				}

				AddInstanceViewModel auvm = null;
				var newChildren = new List<ITreeNode>();
				if (hasCreateRight)
				{
					auvm = new AddInstanceViewModel(pageContext, instanceManagerClient, this);
					newChildren.Add(auvm);
				}

				BasicNode basic = null;
				if (hasReadRight)
				{
					basic = new BasicNode()
					{
						Title = "Loading...",
						Icon = "resm:Tgstation.Server.ControlPanel.Assets.hourglass.png"
					};
					newChildren.Add(basic);
				}

				Children = newChildren;

				if (hasReadRight)
					try
					{
						var instances = await instanceManagerClient.List(cancellationToken).ConfigureAwait(false);

						newChildren = new List<ITreeNode>();
						if (hasCreateRight)
							newChildren.Add(auvm);
						newChildren.AddRange(instances.Select(x => new InstanceViewModel(instanceManagerClient, pageContext, x)));
						Children = newChildren;
						Icon = "resm:Tgstation.Server.ControlPanel.Assets.folder.png";
					}
					catch
					{
						basic.Title = "Error!";
						basic.Icon = "resm:Tgstation.Server.ControlPanel.Assets.error.png";
					}
			}
			finally
			{
				loading = false;
			}
		}

		public Task HandleDoubleClick(CancellationToken cancellationToken) => Refresh(cancellationToken);
	}
}