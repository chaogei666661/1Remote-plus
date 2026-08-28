using System;
using System.Collections.Generic;
using _1RM.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Utils
{
    /// <summary>
    /// What happens to a password after "Copy password" is clicked.
    ///
    /// It used to be <c>Clipboard.SetDataObject(password)</c> and nothing else, so on any Windows 10 1809 or
    /// later desktop the password was retained in the Win+V history, uploaded to the cloud clipboard if
    /// sync was on, and left as the clipboard's contents until something replaced it. The three registered
    /// formats deal with the first two; the rules below are the third.
    ///
    /// The clipboard arrives as three delegates, so these run identically on Windows and on Linux — WPF's
    /// <c>Clipboard</c> needs an STA thread and a desktop, and neither is available to a test host.
    /// </summary>
    [TestClass]
    public class SecretClipboardTests
    {
        private string? _clipboard;
        private readonly List<string> _written = new List<string>();
        private int _clears;

        [TestInitialize]
        public void Setup()
        {
            TestInit.Init();
            _clipboard = null;
            _written.Clear();
            _clears = 0;
        }

        private SecretClipboard AClipboard()
        {
            return new SecretClipboard(
                write: text => { _written.Add(text); _clipboard = text; },
                read: () => _clipboard,
                clear: () => { ++_clears; _clipboard = null; });
        }

        [TestMethod]
        public void CopyingPutsTheSecretOnTheClipboard()
        {
            var sut = AClipboard();

            var token = sut.Copy("hunter2");

            Assert.AreNotEqual(0, token);
            Assert.AreEqual("hunter2", _clipboard);
        }

        /// <summary>
        /// The whole point of the change: the password does not stay there. Before this, the next paste into
        /// a chat window or a ticket was whatever the user had forgotten was still on the clipboard.
        /// </summary>
        [TestMethod]
        public void WhenItsTimeIsUpTheSecretIsTakenBackOff()
        {
            var sut = AClipboard();
            var token = sut.Copy("hunter2");

            Assert.IsTrue(sut.Expire(token));
            Assert.IsNull(_clipboard);
            Assert.AreEqual(1, _clears);
        }

        /// <summary>
        /// A timer the user never saw must not delete something they copied deliberately. This is the case
        /// that makes an unconditional <c>Clipboard.Clear()</c> the wrong implementation.
        /// </summary>
        [TestMethod]
        public void SomethingTheUserCopiedSinceIsLeftAlone()
        {
            var sut = AClipboard();
            var token = sut.Copy("hunter2");

            _clipboard = "a paragraph the user is in the middle of moving";

            Assert.IsFalse(sut.Expire(token));
            Assert.AreEqual("a paragraph the user is in the middle of moving", _clipboard);
            Assert.AreEqual(0, _clears);
        }

        /// <summary>
        /// Copy twice and only the second timer may fire. The first one arriving late would cut the second
        /// password's life to whatever was left of the first's.
        /// </summary>
        [TestMethod]
        public void AnEarlierCopysTimerDoesNotClearALaterOne()
        {
            var sut = AClipboard();
            var first = sut.Copy("first-password");
            var second = sut.Copy("second-password");

            Assert.IsFalse(sut.Expire(first));
            Assert.AreEqual("second-password", _clipboard);

            Assert.IsTrue(sut.Expire(second));
            Assert.IsNull(_clipboard);
        }

        /// <summary>
        /// The expiry runs from a delay, and the app can be closed and the action repeated in between. A
        /// second call with a token that has already fired must not clear whatever is there by then.
        /// </summary>
        [TestMethod]
        public void ExpiringTwiceOnlyClearsOnce()
        {
            var sut = AClipboard();
            var token = sut.Copy("hunter2");

            Assert.IsTrue(sut.Expire(token));
            _clipboard = "something else entirely";

            Assert.IsFalse(sut.Expire(token));
            Assert.AreEqual("something else entirely", _clipboard);
            Assert.AreEqual(1, _clears);
        }

        /// <summary>
        /// A server with no password stored. There is nothing to copy and therefore nothing to schedule; a
        /// token would make the caller start a timer that could only clear someone else's clipboard.
        /// </summary>
        [TestMethod]
        public void AnEmptySecretIsNotCopiedAndNotScheduled()
        {
            var sut = AClipboard();

            Assert.AreEqual(0, sut.Copy(null));
            Assert.AreEqual(0, sut.Copy(""));
            Assert.AreEqual(0, _written.Count);
            Assert.IsFalse(sut.IsHoldingSecret);
        }

        /// <summary>
        /// Token 0 is what <see cref="SecretClipboard.Copy"/> returns when nothing happened. Treating it as
        /// a live copy would let a no-op clear the clipboard.
        /// </summary>
        [TestMethod]
        public void TheNothingHappenedTokenClearsNothing()
        {
            var sut = AClipboard();
            sut.Copy("hunter2");

            Assert.IsFalse(sut.Expire(0));
            Assert.AreEqual("hunter2", _clipboard);
        }

        /// <summary>
        /// Another process can hold the clipboard open, and WPF turns that into an exception. It must not
        /// reach the action menu, and it must not leave a timer pointing at a copy that never happened.
        /// </summary>
        [TestMethod]
        public void AClipboardThatRefusesTheWriteIsNotACrashAndNotATimer()
        {
            var sut = new SecretClipboard(
                write: _ => throw new InvalidOperationException("CLIPBRD_E_CANT_OPEN"),
                read: () => _clipboard,
                clear: () => ++_clears);

            Assert.AreEqual(0, sut.Copy("hunter2"));
            Assert.IsFalse(sut.IsHoldingSecret);
        }

        /// <summary>
        /// If the clipboard cannot be read when the timer fires we clear anyway. The alternative leaves a
        /// password sitting there forever because one read lost a race with another process, and a
        /// clipboard entry the user has to copy again is much the cheaper failure.
        /// </summary>
        [TestMethod]
        public void AnUnreadableClipboardIsClearedRatherThanLeftHoldingAPassword()
        {
            var sut = new SecretClipboard(
                write: text => _clipboard = text,
                read: () => throw new InvalidOperationException("CLIPBRD_E_CANT_OPEN"),
                clear: () => { ++_clears; _clipboard = null; });

            var token = sut.Copy("hunter2");

            Assert.IsTrue(sut.Expire(token));
            Assert.AreEqual(1, _clears);
        }

        /// <summary>
        /// A clear that fails is reported as "did not clear" and leaves the copy tracked, so it is not
        /// mistaken for a clipboard that no longer holds a secret.
        /// </summary>
        [TestMethod]
        public void AClearThatFailsIsNotReportedAsSuccess()
        {
            var sut = new SecretClipboard(
                write: text => _clipboard = text,
                read: () => _clipboard,
                clear: () => throw new InvalidOperationException("CLIPBRD_E_CANT_OPEN"));

            var token = sut.Copy("hunter2");

            Assert.IsFalse(sut.Expire(token));
            Assert.IsTrue(sut.IsHoldingSecret);
        }

        /// <summary>
        /// The three format names are not ours to choose — Windows matches them by string, and a typo would
        /// leave the password in the history while the code looked as though it had excluded it.
        /// </summary>
        [TestMethod]
        public void TheClipboardExclusionFormatsAreSpeltTheWayWindowsExpects()
        {
            Assert.AreEqual("ExcludeClipboardContentFromMonitorProcessing", SecretClipboard.FORMAT_EXCLUDE_FROM_MONITORS);
            Assert.AreEqual("CanIncludeInClipboardHistory", SecretClipboard.FORMAT_CAN_INCLUDE_IN_HISTORY);
            Assert.AreEqual("CanUploadToCloudClipboard", SecretClipboard.FORMAT_CAN_UPLOAD_TO_CLOUD);
        }

        /// <summary>
        /// 0 has to keep meaning "leave it there": it is the behaviour every existing installation has, and
        /// somebody's workflow depends on a clipboard manager picking the value up later.
        /// </summary>
        [TestMethod]
        public void ZeroSecondsMeansTheClipboardIsLeftAlone()
        {
            Assert.AreEqual(0, SecretClipboard.NormaliseLifetimeSeconds(0));
            Assert.AreEqual(0, SecretClipboard.NormaliseLifetimeSeconds(-1));
            Assert.AreEqual(0, SecretClipboard.NormaliseLifetimeSeconds(int.MinValue));
        }

        /// <summary>
        /// A one-second lifetime is not a stricter setting, it is a broken one: the password is gone before
        /// the user has reached the other window, and they would conclude the copy did not work.
        /// </summary>
        [TestMethod]
        public void AnUnusablySmallLifetimeIsRaisedToTheFloorRatherThanHonoured()
        {
            Assert.AreEqual(SecretClipboard.MIN_LIFETIME_SECONDS, SecretClipboard.NormaliseLifetimeSeconds(1));
            Assert.AreEqual(SecretClipboard.MIN_LIFETIME_SECONDS, SecretClipboard.NormaliseLifetimeSeconds(SecretClipboard.MIN_LIFETIME_SECONDS - 1));
        }

        [TestMethod]
        public void AnOverlongLifetimeIsCappedAndTheUsualValuesAreKept()
        {
            Assert.AreEqual(SecretClipboard.MAX_LIFETIME_SECONDS, SecretClipboard.NormaliseLifetimeSeconds(SecretClipboard.MAX_LIFETIME_SECONDS + 1));
            Assert.AreEqual(SecretClipboard.MAX_LIFETIME_SECONDS, SecretClipboard.NormaliseLifetimeSeconds(int.MaxValue));
            Assert.AreEqual(30, SecretClipboard.NormaliseLifetimeSeconds(30));
            Assert.AreEqual(SecretClipboard.DEFAULT_LIFETIME_SECONDS, SecretClipboard.NormaliseLifetimeSeconds(SecretClipboard.DEFAULT_LIFETIME_SECONDS));
        }

        /// <summary>
        /// Copying the same password twice in a row is an ordinary thing to do, and the second copy has to
        /// get its own full lifetime rather than inheriting what was left of the first.
        /// </summary>
        [TestMethod]
        public void CopyingTheSameSecretAgainStartsItsLifetimeOver()
        {
            var sut = AClipboard();
            var first = sut.Copy("hunter2");
            var second = sut.Copy("hunter2");

            Assert.AreNotEqual(first, second);
            Assert.IsFalse(sut.Expire(first));
            Assert.AreEqual("hunter2", _clipboard);
            Assert.IsTrue(sut.Expire(second));
        }
    }
}
