using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Shawn.Utils;

namespace _1RM.Service.Diagnostics
{
    /// <summary>
    /// One file to attach to a bug report: the log, the version, what the machine is, and the protocol
    /// runner definitions — with every secret taken out first.
    ///
    /// Before this, answering "what does your setup look like?" meant asking the user to find
    /// <c>.logs\1Remote.log.md</c>, describe their runner settings from memory, and remember their build
    /// number. In practice that produces a report with the log missing and the one setting that mattered
    /// unmentioned. The reason it was never just "attach the folder" is that the folder holds the credential
    /// database and runner command lines with passwords in them, so the packing has to be selective and the
    /// text has to be scrubbed.
    /// </summary>
    public static class DiagnosticsBundle
    {
        public const string MANIFEST_ENTRY = "README.txt";

        public static string SuggestedFileName() =>
            $"{Assert.APP_NAME}-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip";

        /// <summary>
        /// Writes the bundle. Returns how many entries it holds, the manifest included.
        /// </summary>
        public static int Create(string zipPath)
        {
            var directory = Path.GetDirectoryName(zipPath);
            if (!string.IsNullOrEmpty(directory))
                AppPathHelper.CreateDirIfNotExist(directory!, false);
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            var count = 0;
            using (var file = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                WriteText(archive, MANIFEST_ENTRY, BuildManifest());
                ++count;

                WriteText(archive, "environment.txt", BuildEnvironmentReport());
                ++count;

                if (TryReadRedacted(AppPathHelper.Instance.LogFilePath, out var log))
                {
                    WriteText(archive, "app.log.md", log);
                    ++count;
                }

                if (TryReadRedacted(AppPathHelper.Instance.ProfileJsonPath, out var profile))
                {
                    WriteText(archive, "profile.redacted.json", profile);
                    ++count;
                }

                count += AddRunnerDefinitions(archive);
            }

            SimpleLogHelper.Info($"DiagnosticsBundle: wrote {count} entries to {zipPath}");
            return count;
        }

        /// <summary>
        /// The protocol runner definitions. These are the single most useful thing in a report about a
        /// session that will not open, and the single most likely place for a password to be sitting in
        /// plain sight, because the argument template is free text the user wrote.
        /// </summary>
        private static int AddRunnerDefinitions(ZipArchive archive)
        {
            var count = 0;
            var dir = AppPathHelper.Instance.ProtocolRunnerDirPath;
            if (!Directory.Exists(dir)) return 0;

            foreach (var path in Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
            {
                if (!TryReadRedacted(path, out var text)) continue;
                WriteText(archive, "protocols/" + Path.GetFileName(path), text);
                ++count;
            }
            return count;
        }

        private static bool TryReadRedacted(string path, out string text)
        {
            text = "";
            try
            {
                if (!File.Exists(path)) return false;
                // Share-allowing: the log is open in this very process.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                text = DiagnosticsRedactor.Redact(reader.ReadToEnd());
                return true;
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"DiagnosticsBundle: cannot read {path}, {e.Message}");
                return false;
            }
        }

        private static void WriteText(ZipArchive archive, string entryName, string content)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(content);
        }

        /// <summary>
        /// Says what is in the bundle and what was taken out, so the person attaching it can decide whether
        /// to send it without having to read every line first.
        /// </summary>
        public static string BuildManifest()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Assert.APP_NAME} diagnostics bundle");
            sb.AppendLine($"created: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
            sb.AppendLine();
            sb.AppendLine("Contents");
            sb.AppendLine("  README.txt              this file");
            sb.AppendLine("  environment.txt         version, OS, runtime and locale");
            sb.AppendLine("  app.log.md              the application log");
            sb.AppendLine("  profile.redacted.json   application settings");
            sb.AppendLine("  protocols/*.json        protocol runner definitions");
            sb.AppendLine();
            sb.AppendLine("Not included");
            sb.AppendLine("  the server database, the credential vault, host trust and command approvals,");
            sb.AppendLine("  session recordings, and the connection audit log.");
            sb.AppendLine();
            sb.AppendLine("Redaction");
            sb.AppendLine("  Every included text file was scrubbed before it was written: the value of any");
            sb.AppendLine("  field whose name contains one of");
            sb.AppendLine("    " + string.Join(", ", DiagnosticsRedactor.SecretKeyNames));
            sb.AppendLine($"  is replaced with {DiagnosticsRedactor.PLACEHOLDER}:<length>, as are PEM private key blocks,");
            sb.AppendLine("  cmd:// external secret commands, and -pw / --password command-line arguments.");
            sb.AppendLine();
            sb.AppendLine("  Read it before sending it. Redaction is a filter over free text, not a proof:");
            sb.AppendLine("  a password typed into a field that is not named like one will still be in here.");
            return sb.ToString();
        }

        public static string BuildEnvironmentReport()
        {
            var sb = new StringBuilder();
            void Line(string key, string value) => sb.AppendLine($"{key,-24}{value}");

            Line("app.version", AppVersion.Version.ToString());
            Line("app.placeholder_salt", Assert.IsUsingPlaceholderSalt ? "yes (unofficial build)" : "no");
            Line("os.version", SafeRead(() => Environment.OSVersion.ToString()));
            Line("os.64bit", SafeRead(() => Environment.Is64BitOperatingSystem.ToString()));
            Line("process.64bit", SafeRead(() => Environment.Is64BitProcess.ToString()));
            Line("runtime", SafeRead(() => Environment.Version.ToString()));
            Line("processors", SafeRead(() => Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture)));
            Line("culture", SafeRead(() => CultureInfo.CurrentCulture.Name));
            Line("ui.culture", SafeRead(() => CultureInfo.CurrentUICulture.Name));
            // Deliberately not the user name or the machine name: the bundle is meant to be forwarded.
            return sb.ToString();
        }

        private static string SafeRead(Func<string> read)
        {
            try
            {
                return read() ?? "";
            }
            catch (Exception e)
            {
                return "unavailable: " + e.GetType().Name;
            }
        }
    }
}
