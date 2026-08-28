using System;
using System.Threading;
using Shawn.Utils;

namespace _1RM.Utils
{
    /// <summary>
    /// A password put on the clipboard the way a credential manager has to put one there: kept out of
    /// Windows' clipboard history and cloud clipboard, and taken back off again shortly afterwards.
    ///
    /// "Copy password" used to be a bare <c>Clipboard.SetDataObject(password)</c>, which has two problems on
    /// any Windows 10 1809 or later desktop:
    ///
    /// <list type="number">
    /// <item>Clipboard history (Win+V) keeps the last 25 entries, and with "Sync across your devices" on,
    /// the cloud clipboard uploads them to the user's Microsoft account and pushes them to their other
    /// machines. A password copied to paste into one session was therefore retained, in cleartext, in a
    /// place that survives the app closing and that anyone who walks up to an unlocked desktop can open —
    /// and, on a managed fleet, in a place the operator did not choose.</item>
    /// <item>Nothing ever took it off. The password stayed the clipboard's contents until something else
    /// replaced it, so the next paste into a chat window, a ticket or a terminal was whatever the user had
    /// forgotten was still there.</item>
    /// </list>
    ///
    /// Windows publishes three registered clipboard formats for the first problem, and every password
    /// manager uses them. The second is this class: the copy is remembered, and when its time is up the
    /// clipboard is only cleared if it still holds what was put there — a user who has copied something
    /// else since must not lose it to a timer they never saw.
    ///
    /// The clipboard itself arrives as three delegates. WPF's <c>Clipboard</c> needs an STA thread and a
    /// desktop, and the rules above are worth checking without one.
    /// </summary>
    public sealed class SecretClipboard
    {
        /// <summary>
        /// Presence alone asks clipboard monitors, the history and the cloud to leave the item alone.
        /// </summary>
        public const string FORMAT_EXCLUDE_FROM_MONITORS = "ExcludeClipboardContentFromMonitorProcessing";

        /// <summary>A serialised DWORD of zero keeps the item out of the Win+V history.</summary>
        public const string FORMAT_CAN_INCLUDE_IN_HISTORY = "CanIncludeInClipboardHistory";

        /// <summary>A serialised DWORD of zero stops the item being synced to the user's other devices.</summary>
        public const string FORMAT_CAN_UPLOAD_TO_CLOUD = "CanUploadToCloudClipboard";

        public const int DEFAULT_LIFETIME_SECONDS = 30;

        /// <summary>
        /// Below this the feature stops being usable — a password has to survive the walk to the other
        /// window — so a smaller number is read as the floor rather than as "almost never".
        /// </summary>
        public const int MIN_LIFETIME_SECONDS = 5;

        public const int MAX_LIFETIME_SECONDS = 3600;

        /// <summary>
        /// Zero and below mean "leave it there", which is what the app did before and what someone whose
        /// workflow depends on a clipboard manager will want. Anything else is pulled into range.
        /// </summary>
        public static int NormaliseLifetimeSeconds(int seconds)
        {
            if (seconds <= 0) return 0;
            if (seconds < MIN_LIFETIME_SECONDS) return MIN_LIFETIME_SECONDS;
            if (seconds > MAX_LIFETIME_SECONDS) return MAX_LIFETIME_SECONDS;
            return seconds;
        }

        private readonly Action<string> _write;
        private readonly Func<string?> _read;
        private readonly Action _clear;

        private readonly object _lock = new object();
        private string? _held;
        private long _generation;

        /// <param name="write">Puts the secret on the clipboard, excluded from history and the cloud.</param>
        /// <param name="read">The clipboard's current text, or null when it holds something else.</param>
        /// <param name="clear">Empties the clipboard.</param>
        public SecretClipboard(Action<string> write, Func<string?> read, Action clear)
        {
            _write = write ?? throw new ArgumentNullException(nameof(write));
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _clear = clear ?? throw new ArgumentNullException(nameof(clear));
        }

        /// <summary>
        /// Copies <paramref name="secret"/>. Returns the token to hand to <see cref="Expire"/> when the
        /// lifetime is up, or 0 when there was nothing to copy or the copy failed — in which case there is
        /// nothing to schedule.
        /// </summary>
        public long Copy(string? secret)
        {
            if (string.IsNullOrEmpty(secret)) return 0;

            lock (_lock)
            {
                try
                {
                    _write(secret!);
                }
                catch (Exception e)
                {
                    // Another process can hold the clipboard open. Nothing was copied, so nothing is owed
                    // an expiry, and the previous copy — if there was one — is still the one being tracked.
                    SimpleLogHelper.Warning($"SecretClipboard: could not copy, {e.Message}");
                    return 0;
                }

                _held = secret;
                return Interlocked.Increment(ref _generation);
            }
        }

        /// <summary>
        /// Takes the secret back off the clipboard if it is still there. Returns whether it cleared.
        /// </summary>
        public bool Expire(long token)
        {
            lock (_lock)
            {
                // A later copy has superseded this one and brought its own timer. Clearing here would cut
                // the newer secret's life short.
                if (token == 0 || token != Interlocked.Read(ref _generation)) return false;
                if (_held == null) return false;

                string? current;
                try
                {
                    current = _read();
                }
                catch (Exception e)
                {
                    // Unreadable is treated as still ours and cleared. The alternative leaves a password on
                    // the clipboard indefinitely because one read happened to lose a race, and a clipboard
                    // entry the user has to copy again is the cheaper of the two failures.
                    SimpleLogHelper.Warning($"SecretClipboard: cannot read the clipboard, clearing anyway. {e.Message}");
                    current = _held;
                }

                if (!string.Equals(current, _held, StringComparison.Ordinal))
                {
                    // The user has copied something else since. It is theirs, not ours to delete.
                    _held = null;
                    return false;
                }

                try
                {
                    _clear();
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Warning($"SecretClipboard: could not clear, {e.Message}");
                    return false;
                }

                _held = null;
                return true;
            }
        }

        /// <summary>Whether a copied secret is still being tracked. For the tests.</summary>
        public bool IsHoldingSecret
        {
            get { lock (_lock) { return _held != null; } }
        }
    }
}
