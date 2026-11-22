using System;
using System.Windows.Forms;
using TerrariaTas.Studio.Connection;
using TerrariaTas.Studio.Hub;

namespace TerrariaTas.Studio;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var settings = StudioSettings.Load();
        using var connection = new GameConnection();
        Application.Run(new HubForm(new StudioState(connection), settings));
        settings.Save();
    }
}
