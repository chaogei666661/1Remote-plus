using _1RM;
using _1RM.Service;
using _1RM.Utils;
using _1RM.Utils.ExternalSecret;
using _1RM.Utils.SessionScript;
using Shawn.Utils.Interface;

namespace Tests
{
    public static class TestInit
    {
        /// <summary>
        /// The least the app's static helpers need before anything under test touches them: a salt for the
        /// string cipher, and a language service that echoes keys back so asserting on translated text does
        /// not depend on which language file happens to be loaded.
        ///
        /// It also opts out of the <c>cmd://</c> and session-script approval gates. Those gates exist to put
        /// a dialog in front of a command nobody approved, and a test run has nobody to answer it; the tests
        /// that cover a gate itself turn its opt-out back off for their own duration.
        /// </summary>
        public static void Init()
        {
            UnSafeStringEncipher.Init("tests-only-salt");
            ExternalSecretTrustStore.AutoApproveForTests = true;
            SessionScriptTrustStore.AutoApproveForTests = true;

            IoC.GetByType = (type, key) =>
            {
                if (type == typeof(ILanguageService) || type == typeof(LanguageService) || type == typeof(MockLanguageService))
                    return new MockLanguageService();
                return null;
            };
        }
    }
}
