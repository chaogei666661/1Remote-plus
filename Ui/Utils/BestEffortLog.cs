using System;

namespace _1RM.Utils
{
    /// <summary>
    /// Runs one log call and swallows whatever it throws, so that an operation which has already done its
    /// work is never reported as failed because it could not write a line about itself.
    ///
    /// It exists because <c>SimpleLogHelper</c> can throw on a perfectly ordinary message. Every log call
    /// walks the stack for the caller's source file and cuts the directory off the front of it with
    /// <c>fileName.Substring(fileName.LastIndexOf("\\") + 1)</c> — the culture-sensitive overload, with no
    /// <see cref="StringComparison"/>. ICU's Thai collation treats a backslash as ignorable, so under
    /// <c>th-TH</c> that search answers the length of the string rather than the position of the last
    /// separator, and the slice throws <see cref="ArgumentOutOfRangeException"/>. Windows source paths are
    /// full of backslashes, so on a Thai desktop the throw is not an edge case: it is every log call in the
    /// app. Linux paths have none, which is why it had never been seen.
    ///
    /// <c>SimpleLogHelper</c> lives in the <c>VShawn/Shawn.Utils</c> submodule and cannot be corrected from
    /// this repository. The consequence can be: losing a log line is a nuisance, losing a finished backup
    /// because of one is not.
    ///
    /// Take the log call as a delegate rather than as a message on purpose. A lambda is compiled into the
    /// type that wrote it, so the frame the logger walks back to is still the call site's own file and
    /// line; a helper that took a string would put <em>this</em> file's name on every warning in the app.
    /// </summary>
    public static class BestEffortLog
    {
        public static void Write(Action log)
        {
            try
            {
                log();
            }
            catch (Exception)
            {
                // Nothing to do and nowhere to say it: the thing that failed is the thing that reports
                // failures. The caller's own work is unaffected, which is the whole point of being here.
            }
        }
    }
}
