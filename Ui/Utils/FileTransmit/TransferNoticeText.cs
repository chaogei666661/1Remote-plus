using System;
using System.Collections.Generic;

namespace _1RM.Utils.FileTransmit
{
    /// <summary>
    /// Builds the "and these are the ones it left out" half of a transfer notice.
    ///
    /// The transfer pane's status line is a single 30-pixel-high <c>TextBlock</c>, so whatever is put in it
    /// past the first line is not read by anybody. The link warning was built with
    /// <c>string.Join(", ", …)</c> over the whole list, and the list is not small in the cases that matter:
    /// an ordinary Windows profile carries a dozen compatibility junctions (<c>Application Data</c>,
    /// <c>My Documents</c>, <c>Start Menu</c> and the rest), and uploading a drive root can leave hundreds
    /// of folders unread. That produced a string tens of kilobytes long of which one line was visible, and
    /// the visible line was the part the user could already guess.
    ///
    /// So a notice names a few and counts the rest. The count is the part that tells the user whether
    /// something went wrong, and it is what a long list buried.
    /// </summary>
    public static class TransferNoticeText
    {
        /// <summary>How many paths a notice spells out before it starts counting.</summary>
        public const int DefaultLimit = 3;

        /// <summary>
        /// How long one path may be before its head is cut off. The tail is kept: the folder that was
        /// skipped is at the end, and the part of the path the user chose is at the start.
        /// </summary>
        private const int MaxEntryLength = 64;

        /// <summary>
        /// <paramref name="paths"/> as one line: up to <paramref name="limit"/> of them, comma separated,
        /// followed by whatever <paramref name="describeOmitted"/> makes of the number left over.
        /// </summary>
        /// <param name="paths">The paths a scan or a transfer left out. Blank entries are ignored.</param>
        /// <param name="limit">How many to spell out. Anything below one is read as one.</param>
        /// <param name="describeOmitted">
        /// Given the number not spelled out, the localised text that says so. The caller owns this because
        /// this class is deliberately free of the app's translation service. Null falls back to a bare
        /// <c>(+n)</c>, which is not a sentence but is at least a number.
        /// </param>
        /// <returns>One line, or an empty string when there is nothing to say.</returns>
        public static string Summarise(IEnumerable<string>? paths, int limit = DefaultLimit, Func<int, string>? describeOmitted = null)
        {
            if (paths == null)
                return "";
            if (limit < 1)
                limit = 1;

            var shown = new List<string>(limit);
            var omitted = 0;
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                if (shown.Count < limit)
                    shown.Add(Shorten(path!.Trim()));
                else
                    ++omitted;
            }

            if (shown.Count == 0)
                return "";

            var joined = string.Join(", ", shown);
            if (omitted == 0)
                return joined;

            var tail = describeOmitted == null ? $"(+{omitted})" : describeOmitted(omitted);
            return string.IsNullOrWhiteSpace(tail) ? joined : joined + " " + tail;
        }

        /// <summary>
        /// How many of <paramref name="paths"/> are worth naming. The message templates lead with this
        /// number, and it has to agree with what <see cref="Summarise"/> actually put on the line, so both
        /// count the same way.
        /// </summary>
        public static int Count(IEnumerable<string>? paths)
        {
            if (paths == null)
                return 0;
            var count = 0;
            foreach (var path in paths)
                if (!string.IsNullOrWhiteSpace(path))
                    ++count;
            return count;
        }

        private static string Shorten(string path)
        {
            if (path.Length <= MaxEntryLength)
                return path;
            return "..." + path.Substring(path.Length - (MaxEntryLength - 3));
        }
    }
}
