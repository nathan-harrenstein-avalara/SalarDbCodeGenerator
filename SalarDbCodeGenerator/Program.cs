using SalarDbCodeGenerator.DbProject;
using System;
using System.Windows.Forms;

namespace SalarDbCodeGenerator
{
    internal static class Program
    {
        public static string[] ProgramArgs = null;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ProgramArgs = args;
#if DEBUG
            Application.ThreadException += Application_ThreadException_Debug;
#else
			Application.ThreadException += Application_ThreadException_Release;
#endif
            PleaseWait.ShowPleaseWait("Initializing Systems", true, false);
            Application.Run(new frmCodeGen());
        }

        private static void Application_ThreadException_Debug(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            PleaseWait.Abort();
            MessageBox.Show("Error, something went wrong!\n" + e.Exception.ToString(), "Unhandled error", MessageBoxButtons.OK, MessageBoxIcon.Stop);
        }

        private static void Application_ThreadException_Release(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            PleaseWait.Abort();
            MessageBox.Show("Error, something went wrong!\n" + Common.GetExceptionTechMessage(e.Exception), "Unhandled error", MessageBoxButtons.OK, MessageBoxIcon.Stop);
        }
    }
}