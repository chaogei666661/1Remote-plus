using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;
using Shawn.Utils;

namespace _1RM.Utils
{
    /// <summary>
    /// Staging area for the files a session has to hand to an external program: the generated
    /// <c>.rdp</c> and, when a path contains non-ASCII characters, a copy of the private key.
    ///
    /// Both used to be written straight into <c>%TEMP%</c> under a name derived from the server, and removed
    /// by a sleeping task 10 or 30 seconds later. That name is predictable, the contents are the full session
    /// configuration and a private key, and anything that ends the process inside the sleep — a crash, a
    /// logout, the user quitting — leaves the file behind indefinitely.
    ///
    /// So: one directory per invocation with a random name, an explicit ACL where the platform allows one,
    /// and deletion driven by <see cref="Process.Exited"/> rather than by a clock. The delayed sweep stays as
    /// a backstop for the case where the file is still open when the process reports exit, but it is no
    /// longer the only thing that removes it.
    /// </summary>
    public static class SessionTempFile
    {
        /// <summary>
        /// Creates an empty directory under the user's temp folder that only this account can read.
        /// <paramref name="purpose"/> is only there to make the folder recognisable while it exists.
        /// </summary>
        public static string CreateDirectory(string purpose)
        {
            var path = Path.Combine(Path.GetTempPath(), $"{Assert.APP_NAME}_{purpose}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            RestrictToCurrentUser(path);
            return path;
        }

        /// <summary>
        /// Removes inheritance and grants the current account alone. The per-user temp directory is already
        /// restricted, so this is defence in depth rather than the only protection — and it is best effort:
        /// a redirected TEMP on a volume without ACLs must not stop a session from opening.
        /// </summary>
        private static void RestrictToCurrentUser(string path)
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                var owner = WindowsIdentity.GetCurrent().User;
                if (owner == null) return;

                var info = new DirectoryInfo(path);
                var security = info.GetAccessControl();
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                security.AddAccessRule(new FileSystemAccessRule(
                    owner,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                info.SetAccessControl(security);
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"SessionTempFile: cannot restrict {path}, {e.Message}");
            }
        }

        public static void TryDelete(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"SessionTempFile: cannot remove {directory}, {e.Message}");
            }
        }

        /// <summary>
        /// Deletes the directory when <paramref name="process"/> exits, and again a little later in case the
        /// program still had the file open at that moment — mstsc reads the .rdp at start-up, but a handle
        /// lingering for a moment after exit is not worth leaving the key on disk over.
        /// </summary>
        public static void DeleteWhenExited(Process process, string directory, int backstopSeconds = 30)
        {
            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) => TryDelete(directory);
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"SessionTempFile: cannot watch the process for {directory}, {e.Message}");
            }
            DeleteAfter(directory, backstopSeconds);
        }

        /// <summary>The backstop on its own, for a launch where no process object survives to be watched.</summary>
        public static void DeleteAfter(string directory, int seconds)
        {
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds));
                TryDelete(directory);
            });
        }
    }
}
