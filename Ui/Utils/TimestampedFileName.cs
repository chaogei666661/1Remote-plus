using System;
using System.Globalization;

namespace _1RM.Utils
{
    /// <summary>
    /// The name this app suggests for a file it is about to write for the user — an export, a report, a
    /// diagnostics bundle.
    ///
    /// It exists because the two oldest such names were built with <c>yyyyMMddhhmmss</c>. Lower-case
    /// <c>hh</c> is the twelve-hour clock, and there was no <c>tt</c> to say which half of the day it was,
    /// so a JSON export taken at 09:30 and one taken at 21:30 were both offered as <c>20260828093000</c> —
    /// the second silently overwriting the first if the user accepted the suggestion twice in a day, and
    /// neither sorting into the right order in a folder listing.
    ///
    /// The culture is pinned for a second reason. <c>DateTime.ToString</c> formats the year in the current
    /// culture's calendar, so the same moment is 2026 on most desktops, 2569 under a Thai locale and 1448
    /// under a Hijri one. A file name is an identifier, not something to be read in the user's calendar.
    /// </summary>
    public static class TimestampedFileName
    {
        /// <summary>Sorts chronologically as text, which is the only thing a folder listing can do.</summary>
        public const string TIMESTAMP_FORMAT = "yyyyMMdd-HHmmss";

        public static string Stamp(DateTime when) => when.ToString(TIMESTAMP_FORMAT, CultureInfo.InvariantCulture);

        public static string Stamp() => Stamp(DateTime.Now);

        /// <param name="prefix">Says what the file is; goes in front of the stamp.</param>
        /// <param name="extension">With or without the leading dot.</param>
        public static string For(string prefix, string extension, DateTime when)
        {
            var name = string.IsNullOrWhiteSpace(prefix) ? Stamp(when) : $"{prefix.Trim()}-{Stamp(when)}";
            if (string.IsNullOrWhiteSpace(extension)) return name;
            return extension.StartsWith(".", StringComparison.Ordinal) ? name + extension : name + "." + extension;
        }

        public static string For(string prefix, string extension) => For(prefix, extension, DateTime.Now);
    }
}
