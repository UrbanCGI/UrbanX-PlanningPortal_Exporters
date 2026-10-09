using System;
using System.Windows.Forms;

namespace BabylonJS_Installer
{
    static class Program
    {
        /// <summary>
        /// Point d'entrée principal de l'application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // UrbanCGI fork: the exporter's Update button starts this program with --update (see UpdateCommandLine).
            // Any other launch opens the main window exactly as before.
            if (Utilities.Updates.UpdateCommandLine.IsUpdateRequest(args))
            {
                // By now the exporter is closing 3ds Max, so a failure of the update window must still be logged and
                // 3ds Max opened again: errors come out of Application.Run instead of the standard error dialog.
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                try
                {
                    Application.Run(new UpdateForm(args));
                }
                catch (Exception ex)
                {
                    UpdateForm.RecoverAfterCrash(args, ex);
                }
                return;
            }

            Application.Run(new MainForm());
        }
    }
}
