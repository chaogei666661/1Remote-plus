using System;
using System.Text;

namespace _1RM.Utils.Diagnostics
{
    /// <summary>
    /// Renders a <see cref="ConnectionFailure"/> as the block of text a session panel shows.
    ///
    /// Separate from the classifier so the classification stays free of the UI and the resource dictionary,
    /// and so a test can assert on the category without asserting on prose.
    /// </summary>
    public static class ConnectionFailureMessage
    {
        /// <summary>
        /// Title line, actionable hint, and the raw message underneath. The raw message is always kept: the
        /// hint is a guess made from a category, and hiding what the server actually said would make a
        /// misclassification impossible to see through.
        /// </summary>
        /// <param name="translate">Resource lookup. Injected so this is testable without the app's IoC.</param>
        public static string Build(ConnectionFailure failure, string endpoint, Func<string, string> translate)
        {
            if (failure == null) throw new ArgumentNullException(nameof(failure));
            if (translate == null) throw new ArgumentNullException(nameof(translate));

            var sb = new StringBuilder();

            var headline = translate(failure.HintKey);
            sb.Append(headline);

            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                sb.AppendLine();
                sb.AppendLine();
                sb.Append(translate("conn_fail_endpoint")).Append(' ').Append(endpoint);
            }

            // Only when it adds something. A raw message the hint already paraphrases is noise, and an empty
            // one — VncSharpCore's connection-lost event has none — would leave a dangling label.
            if (!string.IsNullOrWhiteSpace(failure.RawMessage)
                && !string.Equals(failure.RawMessage.Trim(), headline.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine();
                sb.AppendLine();
                sb.Append(translate("conn_fail_details")).Append(' ').Append(failure.RawMessage.Trim());
            }

            return sb.ToString();
        }
    }
}
