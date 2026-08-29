using Microsoft.VisualStudio.TestTools.UnitTesting;
using _1RM.Utils;

namespace Tests.Utils
{
    /// <summary>
    /// This is the guard that stops a value out of a shared database from becoming extra command-line
    /// switches (a username of <c>root -proxycmd calc.exe</c> being the motivating example). It had no
    /// tests, so these pin the exact CommandLineToArgvW escaping the code produces before anyone refactors
    /// it. The expected strings are built from named pieces because a hand-typed literal full of backslashes
    /// and quotes is its own source of bugs.
    /// </summary>
    [TestClass]
    public class ProcessArgumentEscaperTests
    {
        private const string Q = "\"";
        private const string BS = "\\";

        [TestMethod]
        public void EmptyAndNullBecomeAQuotedEmptyString()
        {
            Assert.AreEqual(Q + Q, ProcessArgumentEscaper.Escape(""));
            Assert.AreEqual(Q + Q, ProcessArgumentEscaper.Escape(null));
        }

        [TestMethod]
        public void APlainValueIsLeftUntouched()
        {
            Assert.AreEqual("hello", ProcessArgumentEscaper.Escape("hello"));
            Assert.AreEqual("C:" + BS + "tools" + BS + "putty.exe",
                ProcessArgumentEscaper.Escape("C:" + BS + "tools" + BS + "putty.exe"));
        }

        [TestMethod]
        public void WhitespaceForcesQuoting()
        {
            Assert.AreEqual(Q + "hello world" + Q, ProcessArgumentEscaper.Escape("hello world"));
            Assert.AreEqual(Q + "a\tb" + Q, ProcessArgumentEscaper.Escape("a\tb"));
        }

        [TestMethod]
        public void AnEmbeddedQuoteIsBackslashEscaped()
        {
            // say "hi"  ->  "say \"hi\""
            var input = "say " + Q + "hi" + Q;
            var expected = Q + "say " + BS + Q + "hi" + BS + Q + Q;
            Assert.AreEqual(expected, ProcessArgumentEscaper.Escape(input));
        }

        [TestMethod]
        public void TrailingBackslashesBeforeTheClosingQuoteAreDoubled()
        {
            // "a b\"  ->  "a b\\"  (the run must be doubled so the closing quote is not escaped away)
            var input = "a b" + BS;
            var expected = Q + "a b" + BS + BS + Q;
            Assert.AreEqual(expected, ProcessArgumentEscaper.Escape(input));
        }

        [TestMethod]
        public void BackslashesRightBeforeAQuoteAreDoubledThenTheQuoteIsEscaped()
        {
            // a\"b  ->  "a\\\"b"
            var input = "a" + BS + Q + "b";
            var expected = Q + "a" + BS + BS + BS + Q + "b" + Q;
            Assert.AreEqual(expected, ProcessArgumentEscaper.Escape(input));
        }

        [TestMethod]
        public void AnInjectionAttemptStaysASingleArgument()
        {
            var input = "root -proxycmd calc.exe";
            var expected = Q + "root -proxycmd calc.exe" + Q;
            Assert.AreEqual(expected, ProcessArgumentEscaper.Escape(input));
        }
    }
}
