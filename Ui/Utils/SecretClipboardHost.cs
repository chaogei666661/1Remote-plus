using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using _1RM.Service;
using Shawn.Utils;
using Stylet;

namespace _1RM.Utils
{
    /// <summary>
    /// The one <see cref="SecretClipboard"/> the app uses, wired to WPF's clipboard and to a timer.
    ///
    /// Separate from <see cref="SecretClipboard"/> because everything in here needs an STA thread and a
    /// desktop: <c>Clipboard</c> throws off the UI thread, and the expiry has to come back onto it.
    /// </summary>
    public static class SecretClipboardHost
    {
        private static readonly SecretClipboard Clipboard = new SecretClipboard(Write, Read, Clear);

        /// <summary>
        /// Copies a password, then schedules it to be taken off the clipboard again. Safe to call from the
        /// UI thread only, which is where every "copy password" action already runs.
        /// </summary>
        public static void Copy(string? secret)
        {
            var token = Clipboard.Copy(secret);
            if (token == 0) return;

            var seconds = SecretClipboard.NormaliseLifetimeSeconds(
                IoC.TryGet<ConfigurationService>()?.General.SecretClipboardSeconds ?? SecretClipboard.DEFAULT_LIFETIME_SECONDS);
            if (seconds <= 0) return;

            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds));
                // Back onto the UI thread: the clipboard is apartment-bound, and this is a thread-pool
                // thread with nothing above it to catch what that would throw.
                Execute.OnUIThread(() =>
                {
                    try
                    {
                        Clipboard.Expire(token);
                    }
                    catch (Exception e)
                    {
                        SimpleLogHelper.Warning($"SecretClipboardHost: expiry failed, {e.Message}");
                    }
                });
            });
        }

        /// <summary>
        /// A fresh four-byte zero per format. The two DWORD formats are read as a value, not as a flag, and
        /// a shared stream would already be at its end by the second one.
        /// </summary>
        private static MemoryStream Dword0() => new MemoryStream(new byte[] { 0, 0, 0, 0 });

        private static void Write(string secret)
        {
            var data = new DataObject();
            data.SetText(secret);
            data.SetData(SecretClipboard.FORMAT_EXCLUDE_FROM_MONITORS, Dword0());
            data.SetData(SecretClipboard.FORMAT_CAN_INCLUDE_IN_HISTORY, Dword0());
            data.SetData(SecretClipboard.FORMAT_CAN_UPLOAD_TO_CLOUD, Dword0());
            // copy: true so the value survives this process exiting, which is what every other copy in the
            // app already does and what a user pasting after closing the window expects.
            System.Windows.Clipboard.SetDataObject(data, copy: true);
        }

        private static string? Read()
        {
            return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null;
        }

        private static void Clear()
        {
            System.Windows.Clipboard.Clear();
        }
    }
}
