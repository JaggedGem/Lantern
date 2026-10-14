using System.Diagnostics;

namespace Lantern;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        ApplicationConfiguration.Initialize();
        using var window = new MainWindow();
        System.Windows.Forms.Application.ThreadException += async (_, args) => await window.HandleUnexpectedErrorAsync(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                var directory = Path.Combine(Application.LanternApplication.DefaultDataDirectory, "logs");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log"), args.ExceptionObject.ToString());
            }
            catch (Exception loggingError) { Trace.TraceError(loggingError.ToString()); }
        };
        System.Windows.Forms.Application.Run(window);
    }
}
