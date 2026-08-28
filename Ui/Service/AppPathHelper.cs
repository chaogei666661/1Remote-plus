using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using _1RM.Utils;
using _1RM.View.Guidance;
using Shawn.Utils;
using Shawn.Utils.Wpf.FileSystem;

namespace _1RM.Service
{
    public class AppPathHelper
    {
        public static AppPathHelper Instance { get; set; } = new AppPathHelper(Environment.CurrentDirectory, Environment.CurrentDirectory);

        public readonly string BaseDirPath;
        public readonly string BaseDirPathForLocality;

        public static void CreateDirIfNotExist(string path, bool isFile)
        {
            DirectoryInfo? di = null;
            if (isFile)
            {
                var fi = new FileInfo(path);
                if (fi.Directory?.Exists == false)
                {
                    di = fi.Directory;
                }
            }
            else
            {
                di = new DirectoryInfo(path);
            }
            if (di?.Exists == false)
            {
                try
                {
                    di.Create();
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Error(e);
                }
            }
        }

        public AppPathHelper(string baseDirPath, string baseDirPathForLocality)
        {
            BaseDirPath = baseDirPath;
            BaseDirPathForLocality = baseDirPathForLocality;
        }

        public const string FORCE_INTO_PORTABLE_MODE = "FORCE_INTO_PORTABLE_MODE";
        public const string FORCE_INTO_APPDATA_MODE = "FORCE_INTO_APPDATA_MODE";

        #region Remoting
        public string ProfileJsonPath => Path.Combine(BaseDirPath, Assert.APP_NAME + ".json");
        public string ProfileAdditionalDataSourceJsonPath => Path.Combine(BaseDirPath, Assert.APP_NAME + ".dataSources.json");
        public string SqliteDbDefaultPath => Path.Combine(BaseDirPath, $"{Assert.APP_NAME}.db");
        public string ProtocolRunnerDirPath => Path.Combine(BaseDirPath, "Protocols");
        #endregion


        #region Locality
        public string LogFilePath => Path.Combine(BaseDirPathForLocality, ".logs", $"{Assert.APP_NAME}.log.md");
        public string LocalityDirPath => Path.Combine(BaseDirPathForLocality, ".locality");
        /// <summary>Fingerprints of hosts the user has explicitly accepted. See <see cref="HostTrustService"/>.</summary>
        public string HostTrustJsonPath => Path.Combine(BaseDirPathForLocality, ".locality", "known_hosts.json");
        /// <summary>
        /// The <c>cmd://</c> command lines the user has agreed to run. Locality, not profile: an approval to
        /// execute something is about this machine and must not travel with a synced or shared database.
        /// See <see cref="Utils.ExternalSecret.ExternalSecretTrustStore"/>.
        /// </summary>
        public string ExternalSecretTrustJsonPath => Path.Combine(BaseDirPathForLocality, ".locality", "known_commands.json");
        /// <summary>
        /// The before-connect / after-disconnect scripts the user has agreed to run. Locality for the same
        /// reason as the one above: the server list those commands live in may be shared, the approval to
        /// execute them is about this machine alone.
        /// See <see cref="Utils.SessionScript.SessionScriptTrustStore"/>.
        /// </summary>
        public string SessionScriptTrustJsonPath => Path.Combine(BaseDirPathForLocality, ".locality", "known_session_scripts.json");
        public string LocalityIconDirPath => Path.Combine(BaseDirPathForLocality, ".icons");
        /// <summary>Where recorded terminal output goes when no folder was chosen in the settings.</summary>
        public string SessionLogDirPath => Path.Combine(BaseDirPathForLocality, ".sessionlogs");
        public string KittyDirPath => Path.Combine(BaseDirPathForLocality, "KiTTY");
        public string PuttyDirPath => Path.Combine(BaseDirPathForLocality, "PuTTY");
        #endregion
    }
}
