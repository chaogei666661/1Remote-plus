using System.Globalization;
using _1RM.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils
{
    /// <summary>
    /// A tag is only useful if the same word is the same tag on every machine that opens the list. It was
    /// not: every fold in the app went through <c>string.ToLower()</c>, which asks the Windows user locale,
    /// and every comparison went through <c>StringComparison.CurrentCultureIgnoreCase</c>, which asks the
    /// same. Turkish and Azerbaijani map <c>I</c> to the dotless <c>ı</c>, so those two calls disagree with
    /// the rest of the world about <c>LINUX</c>.
    ///
    /// The cases below set the culture explicitly rather than trusting the box they run on, so they say the
    /// same thing on a developer's machine, on CI and here.
    /// </summary>
    [TestClass]
    public class TagNameTests
    {
        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
        }

        private static void UnderCulture(string culture, System.Action body)
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                body();
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        /// <summary>
        /// The one that travels. <c>ProtocolBaseViewModel</c> folds a server's tags every time it is shown
        /// and writes them back, so on a shared data source a Turkish desktop used to turn everyone's
        /// <c>linux</c> tag into a second, differently spelled tag that nobody else's filter matches.
        /// </summary>
        [TestMethod]
        public void ATagIsSpelledTheSameWayOnATurkishDesktopAsOnAnyOther()
        {
            UnderCulture("en-US", () => Assert.AreEqual("linux", TagName.Fold("LINUX")));
            UnderCulture("tr-TR", () => Assert.AreEqual("linux", TagName.Fold("LINUX")));
            UnderCulture("az-Latn-AZ", () => Assert.AreEqual("linux", TagName.Fold("LINUX")));
            UnderCulture("th-TH", () => Assert.AreEqual("linux", TagName.Fold("LINUX")));

            UnderCulture("tr-TR", () => Assert.AreEqual("windows", TagName.Fold("Windows")));
            UnderCulture("tr-TR", () => Assert.AreEqual("prod-db-i", TagName.Fold("PROD-DB-I")));
        }

        /// <summary>
        /// Typing an upper-case tag into the filter bar found nothing on a Turkish desktop, because the
        /// comparison was linguistic: <c>string.Equals("windows", "WINDOWS", CurrentCultureIgnoreCase)</c>
        /// is <c>false</c> there.
        /// </summary>
        [TestMethod]
        public void AnUpperCaseFilterStillFindsItsTagOnATurkishDesktop()
        {
            UnderCulture("tr-TR", () =>
            {
                Assert.IsTrue(TagName.AreSame("windows", "WINDOWS"));
                Assert.IsTrue(TagName.AreSame("linux", "LINUX"));
                Assert.IsTrue(TagName.AreSame("LINUX", "linux"));
            });
        }

        [TestMethod]
        public void TwoDifferentTagsStayDifferent()
        {
            Assert.IsFalse(TagName.AreSame("prod", "staging"));
            Assert.IsFalse(TagName.AreSame("prod", ""));
            Assert.IsFalse(TagName.AreSame("prod", null));
        }

        /// <summary>
        /// A zero-width space is invisible, so <c>prod</c> and <c>pro&#x200B;d</c> read as the same tag on
        /// screen and are not. The old linguistic comparison called them equal, which hid one of them; the
        /// fold is ordinal, so they are now two tags, both visible in the tag bar.
        /// </summary>
        [TestMethod]
        public void AnInvisibleCharacterMakesADifferentTagRatherThanASilentlyMergedOne()
        {
            Assert.IsFalse(TagName.AreSame("prod", "pro\u200Bd"));
            Assert.IsFalse(TagName.AreSame("file", "\uFB01le"));
        }

        /// <summary>
        /// The other half of moving off a linguistic comparison. macOS stores a decomposed <c>é</c>, and a
        /// tag pasted or imported from there used to match its precomposed twin only because the comparison
        /// was linguistic. Composing in the fold keeps that working without keeping the locale.
        /// </summary>
        [TestMethod]
        public void APrecomposedAccentAndADecomposedOneAreStillTheSameTag()
        {
            Assert.AreEqual(TagName.Fold("caf\u00e9"), TagName.Fold("cafe\u0301"));
            Assert.IsTrue(TagName.AreSame("Caf\u00c9", "cafe\u0301"));
        }

        [TestMethod]
        public void FoldTrimsAndSurvivesNothing()
        {
            Assert.AreEqual("prod", TagName.Fold("  PROD  "));
            Assert.AreEqual("", TagName.Fold(null));
            Assert.AreEqual("", TagName.Fold(""));
            Assert.AreEqual("", TagName.Fold("   "));
        }

        /// <summary>
        /// <c>Normalize</c> throws on an unpaired surrogate. A tag the user typed is not worth an exception
        /// on the path that loads the server list, so it is compared as it stands.
        /// </summary>
        [TestMethod]
        public void ATagThatCannotBeComposedIsKeptRatherThanThrown()
        {
            var lonely = "tag\uD800";

            Assert.AreEqual("tag\uD800", TagName.Fold(lonely));
            Assert.IsTrue(TagName.AreSame(lonely, "TAG\uD800"));
        }

        [TestMethod]
        public void RectifyStripsTheHashAndTheSpacesTheFilterBarWouldSplitOn()
        {
            Assert.AreEqual("web-servers", TagName.Rectify("#Web Servers"));
            Assert.AreEqual("", TagName.Rectify("#"));

            // Surrounding spaces become dashes before anything trims, which is what the old expression did
            // too. Asserted so that a later tidy-up of it is a deliberate change and not a surprise.
            Assert.AreEqual("--web-servers--", TagName.Rectify("  Web Servers  "));
            Assert.AreEqual("", TagName.Rectify(null));

            UnderCulture("tr-TR", () => Assert.AreEqual("windows-hosts", TagName.Rectify("#Windows Hosts")));
        }
    }
}
