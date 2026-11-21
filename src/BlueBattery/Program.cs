namespace BlueBattery;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Nur eine Instanz im Tray zulassen.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\BlueBattery.SingleInstance", out bool isFirst);
        if (!isFirst)
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
