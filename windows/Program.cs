using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace BrickMyBoard
{
    static class Program
    {
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
        [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int handle);
        const int ATTACH_PARENT_PROCESS = -1, STD_OUTPUT_HANDLE = -11;

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                // Diagnostics print to the terminal that launched us, unless output is already redirected to a file or pipe.
                var stdout = GetStdHandle(STD_OUTPUT_HANDLE);
                if (stdout == IntPtr.Zero || stdout == new IntPtr(-1)) AttachConsole(ATTACH_PARENT_PROCESS);
                try { Cli.Run(args); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
            }
            bool created;
            using (var single = new Mutex(true, "Local\\BrickMyBoard.Running", out created))
            {
                if (!created) { AppModel.SignalExisting(); return 0; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => Crash(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(e.ExceptionObject as Exception);
                using (var model = new AppModel()) Application.Run(model.Context);
            }
            return 0;
        }

        /// <summary>Writes the failure to %LOCALAPPDATA%\BrickMyBoard\crash.log and tells the user. Ending the process releases every lock.</summary>
        static void Crash(Exception error)
        {
            var text = DateTime.Now.ToString("s") + " " + (error == null ? "Unknown error" : error.ToString()) + "\r\n";
            var path = "";
            try
            {
                var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BrickMyBoard");
                System.IO.Directory.CreateDirectory(folder);
                path = System.IO.Path.Combine(folder, "crash.log");
                System.IO.File.AppendAllText(path, text);
            }
            catch (Exception) { }
            MessageBox.Show("BrickMyBoard hit a problem and will close. Every keyboard has been released.\n\n" + (error == null ? "" : error.Message) + (path.Length > 0 ? "\n\nDetails: " + path : ""), "BrickMyBoard", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.Exit(2);
        }
    }
}
