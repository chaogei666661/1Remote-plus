using System;
using System.Globalization;
using System.Text;

namespace _1RM.Utils.FileTransmit
{
    /// <summary>
    /// Tells the difference between what a remote file is called and what its name looks like.
    ///
    /// A file name is text, and text can be made to render as something other than itself. The oldest trick
    /// is U+202E RIGHT-TO-LEFT OVERRIDE: an entry literally named <c>invoice\u202Egnp.exe</c> is drawn by
    /// every conforming text stack — this file browser included — as <c>invoiceexe.png</c>. Double-clicking a
    /// file in that list downloads it and hands it to ShellExecute, which uses the real extension, so the
    /// user asks for a picture and starts a program. Zero-width and other invisible formatting characters do
    /// the quieter version of the same thing: two entries that are indistinguishable on screen.
    ///
    /// Nothing here mutates a name. The remote file keeps the name it has, and renaming still round-trips it
    /// exactly; this only decides how it is shown and whether opening it deserves a question first.
    /// </summary>
    public static class RemoteNameInspector
    {
        /// <summary>
        /// Whether the name contains a character that changes how the rest of it is drawn, or is not drawn at
        /// all. Non-breaking and ideographic spaces are deliberately not included: they are visible as a gap,
        /// which is what they claim to be.
        /// </summary>
        public static bool IsDeceptive(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            foreach (var c in name!)
                if (IsHidden(c))
                    return true;
            return false;
        }

        /// <summary>
        /// The name with every invisible character replaced by its code point, so that what is on screen is
        /// what is on the server. Ordinary names come back unchanged and unallocated.
        /// </summary>
        public static string ToDisplayText(string? name)
        {
            if (!IsDeceptive(name))
                return name ?? "";

            var builder = new StringBuilder(name!.Length + 8);
            foreach (var c in name)
            {
                if (IsHidden(c))
                    builder.Append("<U+").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture)).Append('>');
                else
                    builder.Append(c);
            }
            return builder.ToString();
        }

        /// <summary>
        /// The extension Windows will pick the associated program by: the one on the end of the name once the
        /// characters that only affect drawing are gone. Lower-cased and including the dot, or empty when the
        /// name has no extension.
        /// </summary>
        public static string EffectiveExtension(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return "";

            var stripped = StripHidden(name!);
            var dot = stripped.LastIndexOf('.');

            // A leading dot makes a hidden file, not an extension: ".bashrc" has none.
            if (dot <= 0 || dot == stripped.Length - 1)
                return "";

            return stripped.Substring(dot).ToLowerInvariant();
        }

        private static string StripHidden(string name)
        {
            var builder = new StringBuilder(name.Length);
            foreach (var c in name)
                if (!IsHidden(c))
                    builder.Append(c);
            return builder.ToString();
        }

        private static bool IsHidden(char c)
        {
            switch (CharUnicodeInfo.GetUnicodeCategory(c))
            {
                // Control covers the C0 and C1 ranges, Format the bidi overrides and isolates, the zero-width
                // joiners and the byte order mark.
                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                    return true;
                default:
                    return false;
            }
        }
    }
}
