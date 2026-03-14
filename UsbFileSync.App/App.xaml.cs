using UsbFileSync.App.Services;

namespace UsbFileSync.App;

public partial class App : System.Windows.Application
{
	protected override async void OnStartup(System.Windows.StartupEventArgs e)
	{
		if (SyncWorkerHost.TryGetPipeName(e.Args, out var pipeName))
		{
			ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
			base.OnStartup(e);

			var exitCode = await SyncWorkerHost.RunAsync(pipeName).ConfigureAwait(true);
			Shutdown(exitCode);
			return;
		}

		base.OnStartup(e);

		MainWindow = new MainWindow();
		MainWindow.Show();
	}
}
