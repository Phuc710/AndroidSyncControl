using System;
using System.IO;

namespace AndroidSyncControl.Updater
{
    internal class Program
    {
        static int Main(string[] args)
        {
            var options = new UpdateOptions();

            // Default log location in %LOCALAPPDATA%\AndroidSyncControl\logs\updater.log
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string defaultLog = Path.Combine(localAppData, "AndroidSyncControl", "logs", "updater.log");
            UpdateEngine.SetLogFile(defaultLog);

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.Equals("--wait-pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    if (int.TryParse(args[++i], out int pid)) options.WaitPid = pid;
                }
                else if (arg.Equals("--target-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    options.TargetDir = args[++i];
                }
                else if (arg.Equals("--package-path", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    options.PackagePath = args[++i];
                }
                else if (arg.Equals("--expected-sha256", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    options.ExpectedSha256 = args[++i];
                }
                else if (arg.Equals("--restart-exe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    options.RestartExe = args[++i];
                }
                else if (arg.Equals("--current-version", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    options.CurrentVersion = args[++i];
                }
                else if (arg.Equals("--new-version", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    options.NewVersion = args[++i];
                }
                else if (arg.Equals("--silent", StringComparison.OrdinalIgnoreCase))
                {
                    options.Silent = true;
                }
            }

            if (string.IsNullOrWhiteSpace(options.TargetDir))
            {
                // Fallback to parent directory of updater exe if not passed
                options.TargetDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            }

            if (string.IsNullOrWhiteSpace(options.PackagePath))
            {
                UpdateEngine.Log("[ERROR] --package-path is required.");
                return 1;
            }

            bool success = UpdateEngine.Run(options);
            return success ? 0 : 2;
        }
    }
}
