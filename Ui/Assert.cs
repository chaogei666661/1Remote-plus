using System;

namespace _1RM
{
    internal static class Assert
    {
        private const string APP_NAME_RAW = "1Remote";
#if DEBUG
        public const string APP_NAME = $"{APP_NAME_RAW}_Debug";
#if FOR_MICROSOFT_STORE_ONLY
        public const string APP_DISPLAY_NAME = $"{APP_NAME}(Store)_Debug";
#else
        public const string APP_DISPLAY_NAME = APP_NAME;
#endif
#else
        public const string APP_NAME = $"{APP_NAME_RAW}";
#if FOR_MICROSOFT_STORE_ONLY
        public const string APP_DISPLAY_NAME = $"{APP_NAME}(Store)";
#else
        public const string APP_DISPLAY_NAME = APP_NAME;
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
