using System;
using System.Diagnostics;
using System.IO;
using Shawn.Utils;

namespace _1RM.Utils.RdpFile
{
    /// <summary>
    /// The <b>Preview .rdp</b> button of the RDP and RemoteApp editor forms. Both forms had their own copy
    /// of this, and both copies had the same two problems.
    ///
    /// The file was written straight into <c>%TEMP%</c> under a name derived from the server and never
    /// removed. It is the same file mstsc is given, so it carries the session's password as a DPAPI blob:
    /// the connect path stopped leaving that lying around in a round that gave it a per-session directory
    /// with a restricted ACL, and the preview button was missed.
    ///
    /// Then it was opened by writing <c>"notepad " + path</c> into the standard input of a <c>cmd.exe</c>.
    /// The path holds the display name, and cmd reads an unquoted <c>&amp;</c> in it as a command separator
    /// - so a server called <c>x&amp;calc&amp;y</c> ran <c>calc</c>, with the user's account, when its
    /// editor page was previewed. A display name is an ordinary column of the server list, which a shared
    /// MySQL or PostgreSQL source, a SQLite file on a share, a synced profile folder or a restored
    /// <c>.1rbak</c> all let somebody else write. A name with a plain space in it did not work at all,
    /// for the same missing quoting.
    /// </summary>
    public static class RdpFilePreview
    {
        /// <summary>
        /// How long the directory survives. notepad reads the whole file at start-up and does not keep it
        /// open, so this only has to cover the launch. It is not tied to <see cref="Process.Exited"/>
        /// because <c>notepad.exe</c> may hand the path to an already-running editor and return at once,
        /// which would delete the file before it was read.
        /// </summary>
        private const int DELETE_AFTER_SECONDS = 60;

        /// <summary>
        /// Writes <paramref name="content"/> to a session temp directory as <paramref name="fileName"/> and
        /// opens it in Notepad. <paramref name="fileName"/> is expected to have come from
        /// <see cref="RdpFileName"/>.
        /// </summary>
        public static void Show(string content, string fileName)
        {
            var dir = SessionTempFile.CreateDirectory("rdp-preview");
            try
            {
                var path = Path.Combine(dir, fileName);
                File.WriteAllText(path, content);

                var p = new Process
                {
                    StartInfo =
                    {
                        FileName = "notepad.exe",
                        UseShellExecute = false,
                    }
                };
                // ArgumentList, not a concatenated command line: the argument is quoted for us and never
                // reaches a shell, so neither a space nor a cmd metacharacter in it means anything.
                p.StartInfo.ArgumentList.Add(path);
                p.Start();

                SessionTempFile.DeleteAfter(dir, DELETE_AFTER_SECONDS);
            }
            catch (Exception e)
            {
                SessionTempFile.TryDelete(dir);
                SimpleLogHelper.Error(e);
                MessageBoxHelper.ErrorAlert(e.Message);
            }
        }
    }
}
