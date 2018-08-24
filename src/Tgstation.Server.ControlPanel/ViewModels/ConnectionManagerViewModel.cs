﻿using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Client;
using Tgstation.Server.ControlPanel.Models;

namespace Tgstation.Server.ControlPanel.ViewModels
{
	public sealed class ConnectionManagerViewModel : ViewModelBase, ICommandReceiver<ConnectionManagerViewModel.ConnectionManagerCommand>, ITreeNode, IDisposable
	{
		public enum ConnectionManagerCommand
		{
			Connect,
			Delete,
			Close
		}


		const string LoadingGif = "resm:Tgstation.Server.ControlPanel.Assets.loading.gif";
		const string ErrorIcon = "resm:Tgstation.Server.ControlPanel.Assets.error.png";
		const string HttpPrefix = "http://";
		const string HttpsPrefix = "https://";

		public string Title => connection.Url.ToString();

		public string Icon => "resm:Tgstation.Server.ControlPanel.Assets.tgs.ico";

		public IReadOnlyList<ITreeNode> Children
		{
			get => children;
			set => this.RaiseAndSetIfChanged(ref children, value);
		}

		public int TimeoutSeconds
		{
			get => (int)Math.Ceiling(connection.Timeout.TotalSeconds);
			set
			{
				var newVal = TimeSpan.FromSeconds(value);
				if (newVal != connection.Timeout)
				{
					connection.Timeout = newVal;
					this.RaisePropertyChanged();
				}
			}
		}

		public bool Connected => serverClient != null && serverClient.Token.ExpiresAt < DateTimeOffset.Now;

		public bool Connecting { get; private set; }

		public bool InvalidCredentials { get; private set; }
		public bool AccountLocked { get; private set; }
		public bool ConnectionFailed { get; set; }
		public string ServerAddress {
			get => connection.Url.ToString();
			set
			{
				if (!value.StartsWith(usingHttp ? HttpPrefix : HttpsPrefix, StringComparison.OrdinalIgnoreCase))
					return;
				try
				{
					connection.Url = new Uri(value);
					Connect.Recheck();
				}
				catch (UriFormatException) { }
			}
		}

		public string ConnectionWord
		{
			get
			{
				if (Connected)
					return "Refresh";
				if (Connecting)
				{
					var baseString = "Connecting";
					for (var I = 0; I < (DateTimeOffset.Now - startedConnectingAt.Value).TotalSeconds; ++I)
						baseString += '.';
					return baseString;
				}
				return "Connect";
			}
		}

		public string DeleteWord => !confirmingDelete ? "Delete" : "Confirm?";

		public bool UsingHttp {
			get => usingHttp;
			set
			{
				if (usingHttp == value)
					return;
				usingHttp = value;

				try
				{
					connection.Url = new Uri(String.Concat(usingHttp ? HttpPrefix : HttpsPrefix, connection.Url.ToString().Remove(0, usingHttp ? HttpsPrefix.Length : HttpPrefix.Length)));
					this.RaisePropertyChanged(nameof(ServerAddress));
					Connect.Recheck();
				}
				catch (UriFormatException) { }
			}
		}

		public double TimeoutMs
		{
			get => connection.Timeout.TotalMilliseconds;
			set
			{
				if (value < 1)
					return;
				connection.Timeout = TimeSpan.FromMilliseconds(Math.Floor(value));
			}
		}

		public string Password
		{
			get => connection.Credentials.Password;
			set
			{
				connection.Credentials.Password = value;
				Connect.Recheck();
			}
		}

		public string Username
		{
			get => connection.Credentials.Username;
			set
			{
				connection.Credentials.Username = value;
				Connect.Recheck();
			}
		}

		public bool AllowSavingPassword
		{
			get => connection.Credentials.AllowSavingPassword;
			set => connection.Credentials.AllowSavingPassword = value;
		}

		public EnumCommand<ConnectionManagerCommand> Close { get; }
		public EnumCommand<ConnectionManagerCommand> Connect { get; }
		public EnumCommand<ConnectionManagerCommand> Delete { get; }

		readonly Connection connection;
		
		readonly IServerClientFactory serverClientFactory;
		readonly IRequestLogger requestLogger;

		readonly Action requestActivation;
		readonly Action<bool> closeOrDelete;

		IReadOnlyList<ITreeNode> children;

		IServerClient serverClient;

		DateTimeOffset? startedConnectingAt;

		bool usingHttp;
		bool confirmingDelete;

		public ConnectionManagerViewModel(IServerClientFactory serverClientFactory, IRequestLogger requestLogger, Connection connection, Action requestActivation, Action<bool> closeOrDelete)
		{
			this.serverClientFactory = serverClientFactory ?? throw new ArgumentNullException(nameof(serverClientFactory));
			this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
			this.requestLogger = requestLogger ?? throw new ArgumentNullException(nameof(requestLogger));
			this.closeOrDelete = closeOrDelete ?? throw new ArgumentNullException(nameof(closeOrDelete));
			this.requestActivation = requestActivation ?? throw new ArgumentNullException(nameof(requestActivation));

			if (connection.LastToken?.ExpiresAt.HasValue == true && connection.LastToken.ExpiresAt.Value < DateTimeOffset.Now)
			{
				serverClient = serverClientFactory.CreateServerClient(connection.Url, connection.LastToken, connection.Timeout);
				serverClient.AddRequestLogger(requestLogger);
				PostConnect();
			}

			Connect = new EnumCommand<ConnectionManagerCommand>(ConnectionManagerCommand.Connect, this);
			Close = new EnumCommand<ConnectionManagerCommand>(ConnectionManagerCommand.Close, this);
			Delete = new EnumCommand<ConnectionManagerCommand>(ConnectionManagerCommand.Delete, this);
		}

		public void Dispose()
		{
			Children = null;
			serverClient?.Dispose();
			serverClient = null;
		}

		void PostConnect()
		{
			var versionNode = new BasicNode
			{
				Title = "Version",
				Icon = LoadingGif
			};

			var apiVersionNode = new BasicNode
			{
				Title = "API Version",
				Icon = LoadingGif
			};

			async void GetServerVersion()
			{
				try
				{
					var serverInfo = await serverClient.Version(default).ConfigureAwait(false);
					versionNode.Title = String.Format(CultureInfo.InvariantCulture, "Version: {0}", serverInfo.Version);
					apiVersionNode.Title = String.Format(CultureInfo.InvariantCulture, "API Version: {0}", serverInfo.Version);
					apiVersionNode.Icon = null;
					versionNode.Icon = null;
				}
				catch
				{
					versionNode.Icon = ErrorIcon;
					apiVersionNode.Icon = ErrorIcon;
				}
				versionNode.RaisePropertyChanged(nameof(Icon));
				apiVersionNode.RaisePropertyChanged(nameof(Icon));
			}

			List<ITreeNode> childNodes = new List<ITreeNode>
			{
				versionNode,
				apiVersionNode
			};
			Children = childNodes;
			GetServerVersion();
		}

		async Task BeginConnect()
		{
			if (Connecting)
				throw new InvalidOperationException("Already connecting!");

			startedConnectingAt = DateTimeOffset.Now;
			Connecting = true;
			InvalidCredentials = false;
			AccountLocked = false;
			try
			{
				serverClient = await serverClientFactory.CreateServerClient(connection.Url, connection.Credentials.Username, connection.Credentials.Password, connection.Timeout).ConfigureAwait(false);
				serverClient.AddRequestLogger(requestLogger);
				PostConnect();
			}
			catch (UnauthorizedException)
			{
				InvalidCredentials = true;
			}
			catch (InsufficientPermissionsException)
			{
				AccountLocked = true;
			}
			catch (ClientException)
			{
				ConnectionFailed = true;
			}
			finally
			{
				Connecting = false;
			}
			connection.LastToken = serverClient.Token;
		}

		public async Task HandleDoubleClick(CancellationToken cancellationToken)
		{
			if (Connected || Connecting || !connection.Valid)
				requestActivation();
			else
				await BeginConnect().ConfigureAwait(false);
		}

		public bool CanRunCommand(ConnectionManagerCommand command)
		{
			switch (command)
			{
				case ConnectionManagerCommand.Delete:
				case ConnectionManagerCommand.Close:
					return true;
				case ConnectionManagerCommand.Connect:
					return connection.Valid;
				default:
					throw new ArgumentOutOfRangeException(nameof(command), command, "Invalid command!");
			}
		}

		public Task RunCommand(ConnectionManagerCommand command, CancellationToken cancellationToken)
		{
			switch (command)
			{
				case ConnectionManagerCommand.Delete:
					if (confirmingDelete)
						closeOrDelete(true);
					else
					{
						async void DeleteConfirmTimeout()
						{
							await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
							confirmingDelete = false;
							this.RaisePropertyChanged(nameof(DeleteWord));
						}
						confirmingDelete = true;
						this.RaisePropertyChanged(nameof(DeleteWord));
						DeleteConfirmTimeout();
					}
					break;
				case ConnectionManagerCommand.Close:
					closeOrDelete(false);
					break;
				case ConnectionManagerCommand.Connect:
				default:
					throw new ArgumentOutOfRangeException(nameof(command), command, "Invalid command!");
			}
			return Task.CompletedTask;
		}
	}
}
