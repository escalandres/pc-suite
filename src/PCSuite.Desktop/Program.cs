namespace PCSuite.Desktop;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    ///  Runs with normal user privileges; elevation is only requested
    ///  per-operation (Task 005), never for the whole process.
    /// </summary>
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        // 'Application' se califica porque el namespace PCSuite.Application
        // eclipsa a System.Windows.Forms.Application dentro de PCSuite.Desktop.
        System.Windows.Forms.Application.Run(new MainForm());
    }
}
