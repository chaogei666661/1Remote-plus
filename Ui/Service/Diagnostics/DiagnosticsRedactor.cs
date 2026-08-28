using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace _1RM.Service.Diagnostics
{
    /// <summary>
    /// Strips the things a diagnostics bundle must not carry.
    ///
    /// The bundle exists to be attached to a bug report, which means it is written specifically in order to
    /// be sent to a stranger. That makes redaction the feature, not a nicety: a runner definition holds the
    /// command line the app runs, which regularly contains <c>-pw %1RM_PASSWORD%</c>, and a profile holds
    /// the WebDAV credentials.
    ///
    /// Replace rather than delete, so the reader can see that a value was there and how long it was.
    /// </summary>
    public static class DiagnosticsRedactor
    {
        public const string PLACEHOLDER = "[redacted]";

        /// <summary>
        /// Key names whose value is removed wherever one appears as <c>key=value</c>, <c>key: value</c> or
        /// <c>"key": "value"</c>. Matched case-insensitively and as a substring, so PrivateKeyPassphrase is
        /// caught by "passphrase".
        /// </summary>
        private static readonly string[] SecretKeys =
        {
            "password", "passwd", "pwd", "passphrase", "secret", "token", "apikey", "api_key",
            "privatekey", "private_key", "credential", "authorization", "cookie", "sessionkey",
        };

        private static readonly Regex JsonPair = new Regex(
            "\"(?<key>[A-Za-z0-9_\\-]*)\"\\s*:\\s*\"(?<value>(\\\\.|[^\"\\\\])*)\"",
            RegexOptions.Compiled);

        private static readonly Regex KeyValuePair = new Regex(
            "(?<key>[A-Za-z0-9_\\-\\.]+)\\s*[=:]\\s*(?<value>\"[^\"]*\"|'[^']*'|[^\\s,;&]+)",
            RegexOptions.Compiled);

        /// <summary>Command-line switches that carry a secret as the next token, or glued to it.</summary>
        private static readonly Regex PasswordSwitch = new Regex(
            "(?<switch>(?<![A-Za-z0-9])(-pw|--password|/pass))(?<sep>[ =:]?)(?<value>\"[^\"]*\"|'[^']*'|[^\\s]+)",
            RegexOptions.Compiled);

        /// <summary>The body of a PEM block, which is a private key however it got into a log.</summary>
        private static readonly Regex PemBlock = new Regex(
            "-----BEGIN[^-]*PRIVATE KEY-----[\\s\\S]*?-----END[^-]*PRIVATE KEY-----",
            RegexOptions.Compiled);

        /// <summary>A cmd:// external secret: the command itself is what fetches the password.</summary>
        private static readonly Regex ExternalSecretUri = new Regex(
            "cmd://\\S+", RegexOptions.Compiled);

        public static string Redact(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";

            var result = PemBlock.Replace(text!, "-----BEGIN PRIVATE KEY----- " + PLACEHOLDER + " -----END PRIVATE KEY-----");
            result = ExternalSecretUri.Replace(result, "cmd://" + PLACEHOLDER);
            result = JsonPair.Replace(result, m => IsSecretKey(m.Groups["key"].Value)
                ? $"\"{m.Groups["key"].Value}\": \"{Mask(m.Groups["value"].Value)}\""
                : m.Value);
            result = PasswordSwitch.Replace(result, m =>
                m.Groups["switch"].Value + (m.Groups["sep"].Value.Length > 0 ? m.Groups["sep"].Value : " ") + PLACEHOLDER);
            result = KeyValuePair.Replace(result, m => IsSecretKey(m.Groups["key"].Value)
                ? m.Groups["key"].Value + "=" + Mask(m.Groups["value"].Value)
                : m.Value);

            return result;
        }

        /// <summary>
        /// A host name or an IP is not a secret by itself, but it names a customer's estate, so where the
        /// bundle has to mention one it keeps only its shape: enough to tell two hosts apart.
        /// </summary>
        public static string RedactHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host)) return "";
            var h = host!.Trim();
            if (h.Length <= 2) return new string('*', h.Length);
            return h.Substring(0, 1) + new string('*', Math.Min(h.Length - 2, 8)) + h.Substring(h.Length - 1);
        }

        /// <summary>The value is gone, but its length is not: "was it empty?" is a real support question.</summary>
        private static string Mask(string value)
        {
            var unquoted = Unquote(value);
            // A value that has already been through here keeps the length it reported the first time.
            // The bundle reads files that can already contain a placeholder - a log line that recorded a
            // redacted value, or a re-export - and re-masking would replace the real length with the
            // length of the word "[redacted]:7".
            if (unquoted.StartsWith(PLACEHOLDER, StringComparison.Ordinal)) return unquoted;
            return unquoted.Length == 0 ? "" : $"{PLACEHOLDER}:{unquoted.Length}";
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[value.Length - 1] == '"') ||
                 (value[0] == '\'' && value[value.Length - 1] == '\'')))
                return value.Substring(1, value.Length - 2);
            return value;
        }

        public static bool IsSecretKey(string? key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            var k = key!.ToLowerInvariant();
            foreach (var secret in SecretKeys)
                if (k.Contains(secret))
                    return true;
            return false;
        }

        /// <summary>The key names treated as secret, for the manifest that documents what was stripped.</summary>
        public static IReadOnlyList<string> SecretKeyNames => SecretKeys;
    }
}
