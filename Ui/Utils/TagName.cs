using System;
using System.Text;

namespace _1RM.Utils
{
    /// <summary>
    /// One answer to "are these two tags the same tag", on every desktop.
    ///
    /// Tags are stored, keyed and matched in lower case, and every place that did the lowering used
    /// <c>string.ToLower()</c> - which lowercases by <see cref="System.Globalization.CultureInfo.CurrentCulture"/>,
    /// the Windows user locale. Under <c>tr-TR</c> and <c>az-Latn-AZ</c> that maps <c>I</c> to the dotless
    /// <c>ı</c>, so a tag typed as <c>LINUX</c> is stored as <c>lınux</c> there and as <c>linux</c>
    /// everywhere else. Two consequences, and the first one travels:
    ///
    /// <list type="bullet">
    /// <item>Merely opening a server rewrites its tag list through the fold
    /// (<c>ProtocolBaseViewModel.Server</c>), so a shared MySQL, PostgreSQL or network-share data source
    /// ends up holding both spellings. The tag bar shows two tags that are indistinguishable on screen,
    /// each with some of the servers, and neither locale's filter finds the other's.</item>
    /// <item><c>.locality/*.json</c> keys the pinned state and the header-bar order on the same fold, so
    /// changing the Windows display language loses both on the same machine.</item>
    /// </list>
    ///
    /// The comparisons had the mirror of it: <c>StringComparison.CurrentCultureIgnoreCase</c> answers
    /// <c>false</c> for <c>windows</c> against <c>WINDOWS</c> under <c>tr-TR</c>, so typing an upper-case
    /// tag into the filter bar matched nothing at all there.
    ///
    /// Normalising to form C is part of the same job rather than an extra: a linguistic comparison used to
    /// treat a precomposed <c>café</c> and a decomposed one as the same tag - macOS writes the decomposed
    /// form, and it arrives here through an import or a paste - and an ordinal comparison would not. What
    /// an ordinal comparison also stops doing is treating <c>prod</c> and <c>pro&#x200B;d</c> as one tag,
    /// which is the part that was never wanted.
    /// </summary>
    public static class TagName
    {
        /// <summary>
        /// The stored form of a tag name: trimmed, composed, lower-cased without asking the locale.
        /// </summary>
        public static string Fold(string? name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return Compose(name!.Trim()).ToLowerInvariant();
        }

        /// <summary>
        /// <see cref="Fold"/> plus the two edits a tag typed into the tag editor gets: the <c>#</c> that
        /// marks it in the filter bar is not part of the name, and a space would split it into two words
        /// there.
        /// </summary>
        public static string Rectify(string? name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return Fold(name!.Replace("#", "").Replace(" ", "-"));
        }

        /// <summary>
        /// Whether two names denote the same tag. Both sides are folded first, so this is the same question
        /// the dictionary keys ask and it does not depend on either side already being stored form.
        /// </summary>
        public static bool AreSame(string? a, string? b)
        {
            return string.Equals(Fold(a), Fold(b), StringComparison.Ordinal);
        }

        /// <summary>
        /// Form C, or the string unchanged. <see cref="string.Normalize(NormalizationForm)"/> throws on an
        /// unpaired surrogate, and a tag that cannot be composed is still a tag the user typed - losing it
        /// to an exception on the server list's load path would be a far worse trade than comparing it
        /// as it stands.
        /// </summary>
        private static string Compose(string value)
        {
            try
            {
                return value.IsNormalized(NormalizationForm.FormC) ? value : value.Normalize(NormalizationForm.FormC);
            }
            catch (ArgumentException)
            {
                return value;
            }
        }
    }
}
