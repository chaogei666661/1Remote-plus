using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _1RM.Model.Protocol;
using _1RM.Model.Protocol.Base;
using _1RM.Model.ProtocolRunner.Default;
using _1RM.Service;
using _1RM.Utils;
using _1RM.Utils.PuTTY;
using _1RM.View.Host;
using _1RM.View.Host.ProtocolHosts;
using Shawn.Utils;
using Shawn.Utils.Wpf;

namespace _1RM.Model.ProtocolRunner
{
    public static class RunnerHelper
    {
        /// <summary>
        /// get a selected runner, or default runner.
        /// </summary>
        public static Runner GetRunner(ProtocolConfigurationService protocolConfigurationService, ProtocolBase server, string protocolName, string? assignRunnerName = null)
        {
            if (protocolConfigurationService.ProtocolConfigs.TryGetValue(protocolName, out var p) == false)
            {
                //SimpleLogHelper.Debug($"we can not customize runner for protocol: {protocolName}");
                return new InternalDefaultRunner(protocolName);
            }

            if (p.Runners.Count == 0)
            {
                //SimpleLogHelper.Debug($"we don't have any runner for protocol: {protocolName}");
                return new InternalDefaultRunner(protocolName);
            }

            var r = p.Runners.FirstOrDefault(x => x.Name == assignRunnerName);
            r ??= p.Runners.FirstOrDefault(x => x.Name == server.SelectedRunnerName);
            r ??= p.Runners.FirstOrDefault(x => x.Name == p.SelectedRunnerName);
            r ??= p.Runners.FirstOrDefault();
            return r ?? new InternalDefaultRunner(protocolName);
        }

        public static bool IsRunWithoutHosting(this Runner runner)
        {
            return runner is ExternalRunner { RunWithHosting: false };
        }

        public static void RunWithoutHosting(this Runner runner, ProtocolBase protocol)
        {
            if (runner is not ExternalRunner er) return;
            var (isOk, exePath, exeArguments, environmentVariables, keyDir) = er.GetStartInfo(protocol);
            if (!isOk) return;

            var startInfo = new ProcessStartInfo();
            if (environmentVariables?.Count > 0)
                foreach (var kv in environmentVariables)
                {
                    if (startInfo.EnvironmentVariables.ContainsKey(kv.Key) == false)
                        startInfo.EnvironmentVariables.Add(kv.Key, kv.Value);
                    startInfo.EnvironmentVariables[kv.Key] = kv.Value;
                }

            startInfo.UseShellExecute = false;
            startInfo.FileName = exePath;
            startInfo.Arguments = exeArguments;
            var process = new Process() { StartInfo = startInfo };
            SessionControlService.AddUnHostingWatch(process, protocol);
            process.EnableRaisingEvents = true;
            process.Start();
            if (keyDir.Length > 0)
                SessionTempFile.DeleteWhenExited(process, keyDir);
        }


        /// <summary>
        /// return (noError?, exePath, exeArguments, environmentVariables, privateKeyTempDir)
        ///
        /// privateKeyTempDir is "" unless a copy of the private key had to be staged; when it is not, the
        /// caller owns that directory and has to delete it once the program it launched has finished with it.
        /// </summary>
        private static Tuple<bool, string, string, Dictionary<string, string>, string> GetStartInfo(this Runner runner, ProtocolBase protocol)
        {
            string exePath = "";
            string exeArguments = "";
            string keyDir = "";
            var environmentVariables = new Dictionary<string, string>();
            if (runner is ExternalRunner er)
            {
                exePath = er.ExePath;
                // prepare args
                exeArguments = er.Arguments;
                if (runner is ExternalRunnerForSSH runnerForSsh)
                {
                    switch (protocol)
                    {
                        case SSH ssh when string.IsNullOrEmpty(ssh.PrivateKey) == false:
                        case SFTP sftp when string.IsNullOrEmpty(sftp.PrivateKey) == false:
                            var pw = protocol as ProtocolBaseWithAddressPortUserPwd;
                            // if private key is not all ascii, copy it to temp file
                            if (pw?.IsPrivateKeyAllAscii() == false && File.Exists(pw.PrivateKey))
                            {
                                // A copy of a private key under its own name in %TEMP% is both guessable and
                                // only removed by a sleeping task, so it goes into a directory of its own
                                // that the caller deletes as soon as the program using it is gone.
                                keyDir = SessionTempFile.CreateDirectory("key");
                                var pk = Path.Combine(keyDir, new FileInfo(pw.PrivateKey).Name);
                                File.Copy(pw.PrivateKey, pk, true);
                                pw.PrivateKey = pk;
                            }
                            exeArguments = runnerForSsh.ArgumentsForPrivateKey;
                            break;
                    }
                }

                // make environment variables
                foreach (var kv in er.EnvironmentVariables)
                {
                    environmentVariables.Add(kv.Key, OtherNameAttributeExtensions.Replace(protocol, kv.Value.Replace("%SSH_PRIVATE_KEY_PATH%", "%1RM_PRIVATE_KEY_PATH%")));
                }


                // Percent-encoding, some password may contain special characters, SFTP\XFTP need to encode them.
                // see: https://github.com/1Remote/1Remote/issues/673
                // ref: https://winscp.net/eng/docs/session_url#special
                er.ApplySpecialCharacters(protocol);
            }
            else if (runner is InternalExeRunner ir)
            {
                exePath = ir.GetExeInstallPath(); // TODO: WHAT IF A CUSTOM EXE PATH?
                var check = WinCmdRunner.CheckFileExistsAndFullName(exePath);
                if (check.Item1 == false)
                {
                    ir.Install();
                }
                exeArguments = ir.GetExeArguments(protocol);
            }
            else
            {
                SimpleLogHelper.Error($"GetStartInfo: Runner '{runner.Name}' is not supported!");
                if (keyDir.Length > 0) SessionTempFile.TryDelete(keyDir);
                return new Tuple<bool, string, string, Dictionary<string, string>, string>(false, "", "",
                    new Dictionary<string, string>(), "");
            }

            // check exe path exists
            var tmp = WinCmdRunner.CheckFileExistsAndFullName(exePath);
            if (tmp.Item1 == false)
            {
                MessageBoxHelper.ErrorAlert($"Exe file '{exePath}' of runner '{runner.Name}' does not existed!");
                if (keyDir.Length > 0) SessionTempFile.TryDelete(keyDir);
                return new Tuple<bool, string, string, Dictionary<string, string>, string>(false, "", "",
                    new Dictionary<string, string>(), "");
            }
            exePath = tmp.Item2;
            exeArguments = OtherNameAttributeExtensions.Replace(protocol, exeArguments);
            return new Tuple<bool, string, string, Dictionary<string, string>, string>(true, exePath, exeArguments, environmentVariables, keyDir);
        }

        public static HostBase GetHost(this Runner runner, ProtocolBase protocol, TabWindowView? tab = null)
        {
            Debug.Assert(runner.IsRunWithoutHosting() == false);

            if (runner is ExternalRunner er)
            {
                // custom runner
                var (isOk, exePath, exeArguments, environmentVariables, keyDir) = er.GetStartInfo(protocol);
                if (isOk)
                {
                    var integrateHost = IntegrateHost.Create(protocol, runner, exePath, exeArguments, environmentVariables);
                    // IntegrateHost owns the process it starts and does not hand it back, so a copied key is
                    // swept on the backstop timer here. The program has read it long before that.
                    if (keyDir.Length > 0) SessionTempFile.DeleteAfter(keyDir, 30);
                    return integrateHost;
                }
            }
            if (runner is InternalExeRunner ir)
            {
                // default runner
                var (isOk, exePath, exeArguments, environmentVariables, keyDir) = ir.GetStartInfo(protocol);
                if (isOk)
                {
                    var integrateHost = IntegrateHost.Create(protocol, runner, exePath, exeArguments, environmentVariables);
                    if (keyDir.Length > 0) SessionTempFile.DeleteAfter(keyDir, 30);
                    return integrateHost;
                }
            }

            // build-in runner
            switch (protocol)
            {
                case RDP rdp:
                    {
                        var size = tab?.GetTabContentSize(ColorAndBrushHelper.ColorIsTransparent(protocol.ColorHex) == true);
                        return AxMsRdpClient09Host.Create(rdp, (int)(size?.Width ?? 0), (int)(size?.Height ?? 0));
                    }
                case VNC vnc:
                    {
                        return VncHost.Create(vnc);
                    }
                case SFTP sftp:
                    {
                        return FileTransmitHost.Create(sftp);
                    }
                case FTP ftp:
                    {
                        return FileTransmitHost.Create(ftp);
                    }
                case LocalApp app:
                    {
                        return IntegrateHost.Create(app, runner, app.GetExePath(), app.GetArguments(false));
                    }
                default:
                    break;
            }
            SimpleLogHelper.Fatal($"Host of {protocol.GetType()} is not implemented, or the runner ${runner.Name} is not supported");
            throw new NotImplementedException($"Host of {protocol.GetType()} is not implemented, or the ${runner.Name} is not supported");
        }
    }
}