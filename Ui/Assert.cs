using System;

namespace _1RM
{
    internal static class Assert
    {
        // APP_NAME is an identity, not a label: it names the AppData folder, the database, the profile,
        // the log, the startup task, the Credential Manager entries and the PuTTY session prefix. Renaming
        // it would orphan every one of those for existing users, so the product rename lives entirely in
        // APP_DISPLAY_NAME, which is the only one humans ever read.
        private const string APP_NAME_RAW = "1Remote";
        private const string APP_DISPLAY_NAME_RAW = "1Remote Plus";
#if DEBUG
        public const string APP_NAME = $"{APP_NAME_RAW}_Debug";
#if FOR_MICROSOFT_STORE_ONLY
        public const string APP_DISPLAY_NAME = $"{APP_DISPLAY_NAME_RAW}(Store)_Debug";
#else
        public const string APP_DISPLAY_NAME = $"{APP_DISPLAY_NAME_RAW}_Debug";
#endif
#else
        public const string APP_NAME = $"{APP_NAME_RAW}";
#if FOR_MICROSOFT_STORE_ONLY
        public const string APP_DISPLAY_NAME = $"{APP_DISPLAY_NAME_RAW}(Store)";
#else
        public const string APP_DISPLAY_NAME = $"{APP_DISPLAY_NAME_RAW}";
#endif
#endif


        public const string SENTRY_IO_DEN = "===REPLACE_ME_WITH_SENTRY_IO_DEN===";
        public const string STRING_SALT = "===REPLACE_ME_WITH_SALT===";

        /// <summary>
        /// True when the build was produced without the encryption salt secret, so <see cref="STRING_SALT"/>
        /// is still the placeholder that ships in source — a value anybody can read here. Every stored
        /// password in a database written by such a build is recoverable by anyone with the repository, and
        /// pointing it at a store created by an official release means feeding a known key to real secrets.
        ///
        /// The check deliberately does not compare against a second copy of the placeholder literal:
        /// scripts/Set-Secret.ps1 rewrites every occurrence of that literal in this file, so the copy would
        /// be substituted along with the constant and the comparison would always succeed.
        /// </summary>
        public static bool IsUsingPlaceholderSalt =>
            STRING_SALT.StartsWith("===REPLACE_ME", StringComparison.Ordinal)
            && STRING_SALT.EndsWith("===", StringComparison.Ordinal);
    }
}
