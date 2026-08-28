using System;
using System.Linq;
using System.Text;

namespace _1RM.Utils.RdpFile
{
    /// <summary>
    /// The name of a <c>.rdp</c> file this app writes, derived from the server the user named.
    ///
    /// Four places used to build one: the mstsc connect path, the RemoteApp connect path, and the preview
    /// button of each of the two RDP editor forms. Three of them stripped
    /// <see cref="System.IO.Path.GetInvalidFileNameChars"/> out of the stem and the export action stripped
    /// nothing at all, so a server called <c>web01 / dmz</c> handed the save dialog a path with a directory
    /// separator in it.
    ///
    /// Stripping the illegal characters is not the whole job either. A stem that is left empty produces a
    /// file called <c>.rdp</c>; a stem that happens to be <c>CON</c> or <c>LPT1</c> names a DOS device
    /// rather than a file, so the write goes to the console instead of to disk; a stem ending in a space or
    /// a dot is silently renamed by Win32; and a long display name plus a temp directory can pass MAX_PATH,
    /// which turns a connect into a <c>PathTooLongException</c> nobody sees.
    ///
    /// The illegal set is written out here rather than read from <c>Path.GetInvalidFileNameChars()</c>: on
    /// Linux that call answers with <c>/</c> and NUL alone, so a test running anywhere but Windows would
    /// pass without checking anything. The set below is Windows', on every platform.
    /// </summary>
    public static class RdpFileName
    {
        public const string EXTENSION = ".rdp";

        /// <summary>Used when nothing usable is left of the stem, so the file always has a name.</summary>
        public const string FALLBACK_STEM = "rdp";

        /// <summary>
        /// How much of the stem survives. A temp directory path plus <c>.rdp</c> leaves room for far more
        /// than this, and a name longer than it is not telling the user anything by its tail.
        /// </summary>
        public const int MAX_STEM_LENGTH = 64;

        private const string ILLEGAL = "<>:\"/\\|?*";

        /// <summary>Names Win32 still resolves to a device, with or without an extension.</summary>
        private static readonly string[] ReservedStems =
        {
            "CON", "PRN", "AUX", "NUL", "CLOCK$",
            "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        /// <summary>
        /// The stem the connect and preview paths use: the display name, the port and something that
        /// distinguishes two entries for the same host. Sanitised, so the caller does not have to be.
        /// </summary>
        public static string ForSession(string? displayName, string? port, string? discriminator)
        {
            return Make($"{displayName}_{port}_{discriminator}");
        }

        /// <summary>
        /// A whole file name, extension included, that Win32 can hold and a save dialog can be given.
        /// </summary>
        public static string Make(string? proposedStem)
        {
            return Sanitise(proposedStem) + EXTENSION;
        }

        /// <summary>The stem on its own, without <see cref="EXTENSION"/>.</summary>
        public static string Sanitise(string? proposedStem)
        {
            var stem = new StringBuilder();
            foreach (var c in proposedStem ?? "")
            {
                // Control characters are illegal in a Win32 name too, and a newline in a stem would also
                // break every log line and dialog that ever repeats the path back.
                if (ILLEGAL.IndexOf(c) >= 0 || char.IsControl(c)) continue;
                stem.Append(c);
            }

            var result = stem.ToString();
            if (result.Length > MAX_STEM_LENGTH)
                result = result.Substring(0, MAX_STEM_LENGTH);

            // Win32 drops these when it stores the name, so a file asked for as "srv. " arrives as "srv"
            // and the path the caller kept no longer refers to it.
            result = result.TrimEnd(' ', '.').TrimStart();

            if (result.Length == 0)
                return FALLBACK_STEM;

            if (ReservedStems.Any(x => string.Equals(x, result, StringComparison.OrdinalIgnoreCase)))
                return "_" + result;

            return result;
        }
    }
}
