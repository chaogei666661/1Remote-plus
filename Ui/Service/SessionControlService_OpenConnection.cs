using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using _1RM.Model.Protocol;
using _1RM.Model.Protocol.Base;
using _1RM.Model.ProtocolRunner;
using _1RM.Model.ProtocolRunner.Default;
using _1RM.Service.Locality;
using _1RM.Utils;
using _1RM.Utils.Tracing;
using _1RM.View;
using _1RM.View.Editor;
using _1RM.View.Host;
using _1RM.View.Utils;
using Shawn.Utils;
using Shawn.Utils.Wpf;
using Stylet;

namespace _1RM.Service
{
    public partial class SessionControlService
    {
        #region Open Via Different
        private static void ConnectRdpByMstsc(in RDP rdp)
        {
            // The .rdp holds the whole session configuration, so it goes into a directory of its own with an
            // unguessable name rather than under %TEMP% as "<server>_<port>_<hash>.rdp", and it is removed
            // when mstsc exits rather than only by a timer that a crash can outlive.
            var dir = SessionTempFile.CreateDirectory("rdp");
            var rdpFileName = $"{rdp.DisplayName}_{rdp.Port}_{MD5Helper.GetMd5Hash16BitString(rdp.UserName)}";
            var invalid = new string(Path.GetInvalidFileNameChars()) +
                          new string(Path.GetInvalidPathChars());
            rdpFileName = invalid.Aggregate(rdpFileName, (current, c) => current.Replace(c.ToString(), ""));
            var rdpFile = Path.Combine(dir, rdpFileName + ".rdp");
            var text = rdp.ToRdpConfig().ToString();

            // write a .rdp file for mstsc.exe
            if (RetryHelper.Try(() =>
                {
                    File.WriteAllText(rdpFile, text);
                }, actionOnError: exception => UnifyTracing.Error(exception)))
            {
                try
                {
                    string admin = rdp.IsAdministrativePurposes == true ? " /admin " : "";
                    var p = new Process
                    {
                        StartInfo =
                        {
                            FileName = "mstsc.exe",
                            Arguments = $"{admin} \"" + rdpFile + "\""
                        },
                    };
                    var protocol = rdp;
                    AddUnHostingWatch(p, protocol);
                    p.EnableRaisingEvents = true;
                    p.Start();
                    // mstsc reads the file at start-up, so the 30s backstop is what actually removes it in
                    // the common case; the Exited handler covers a session that ends before that.
                    SessionTempFile.DeleteWhenExited(p, dir);
                }
                catch (Exception e)
                {
                    SessionTempFile.TryDelete(dir);
                    UnifyTracing.Error(e);
                    MessageBoxHelper.ErrorAlert(e.Message + "\r\n while Run mstsc.exe");
                }
            }
            else
            {
                SessionTempFile.TryDelete(dir);
            }
        }

        private static void ConnectRemoteApp(in RdpApp remoteApp)
        {
            // see ConnectRdpByMstsc: one directory per invocation, not a name derived from the server
            var dir = SessionTempFile.CreateDirectory("remoteapp");
            var rdpFileName = $"{remoteApp.DisplayName}_{remoteApp.Port}_{remoteApp.UserName}";
            var invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            rdpFileName = invalid.Aggregate(rdpFileName, (current, c) => current.Replace(c.ToString(), ""));
            var rdpFile = Path.Combine(dir, rdpFileName + ".rdp");

            // write a .rdp file for mstsc.exet.Replace(c.ToString(), ""));
            var text = remoteApp.ToRdpConfig().ToString();
            // write a .rdp file for mstsc.exe
            if (RetryHelper.Try(() =>
            {
                File.WriteAllText(rdpFile, text);
            }, actionOnError: exception => UnifyTracing.Error(exception)))
            {
                var p = new Process
                {
                    StartInfo =
                    {
                        FileName = "cmd.exe",
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                var protocol = remoteApp;
                AddUnHostingWatch(p, protocol);
                p.Start();
                p.StandardInput.WriteLine($"mstsc \"" + rdpFile + "\"");
                p.StandardInput.WriteLine("exit");

                // The process here is the cmd.exe that launches mstsc and exits immediately, so its Exited
                // event would fire before mstsc has read the file. A short delay is the only signal
                // available; ten seconds is what it has always been.
                SessionTempFile.DeleteAfter(dir, 10);
            }
            else
            {
                SessionTempFile.TryDelete(dir);
            }
        }

        private void ConnectWithFullScreen(in ProtocolBase server, in Runner runner)
        {
            // fullscreen normally
            var host = runner.GetHost(server);
            if (host == null)
                return;

            lock (_dictLock)
            {
                Debug.Assert(!_connectionId2Hosts.ContainsKey(host.ConnectionId));
                _connectionId2Hosts.TryAdd(host.ConnectionId, host);
            }
            host.OnClosed += OnRequestCloseConnection;
            host.OnFullScreen2Window += this.MoveSessionToTabWindow;
            this.MoveSessionToFullScreen(host.ConnectionId);
            AuditSessionOpened(host.ConnectionId);
            host.Conn();
            SimpleLogHelper.Debug($@"Start Conn: {server.DisplayName}({server.GetHashCode()}) by host({host.GetHashCode()}) with full");
        }

        public string ConnectWithTab(in ProtocolBase protocolIn, in Runner runnerIn, string assignTabToken)
        {
            ProtocolBase protocol = protocolIn;
            Runner runner = runnerIn;
            if (protocol.AlwaysOpenInNewTabWindow == true && string.IsNullOrEmpty(assignTabToken))
            {
                assignTabToken = DateTime.Now.Ticks.ToString();
            }

            // Outside the lock on purpose: creating a window pumps the dispatcher.
            var tab = this.GetOrCreateTabWindow(assignTabToken);
            if (tab.IsClosing)
            {
                // Closes the attempt the audit log opened. Without this the entry stays in flight and the
                // attempt reads as neither succeeded nor failed.
                AuditConnectFailed(protocol, "TabWindowClosing");
                return "";
            }

            Execute.OnUIThreadSync(() =>
            {
                tab.Show();
                tab.ShowInTaskbar = true;

                // get display area size for host
                var host = runner.GetHost(protocol, tab);

                // Publishing the tab item and the host must be atomic against the cleanup pass, or it sees
                // a tab item whose host is not registered yet and retires the brand new window.
                lock (_dictLock)
                {
                    Debug.Assert(!_connectionId2Hosts.ContainsKey(host.ConnectionId));
                    host.OnClosed += OnRequestCloseConnection;
                    host.OnFullScreen2Window += this.MoveSessionToTabWindow;
                    tab.GetViewModel().AddItem(new TabItemViewModel(host, protocol.DisplayName));
                    _connectionId2Hosts.TryAdd(host.ConnectionId, host);
                }

                AuditSessionOpened(host.ConnectionId);
                host.Conn();
                tab.WindowState = tab.WindowState == WindowState.Minimized ? WindowState.Normal : tab.WindowState;
                tab.Activate();
            });
            return tab.Token;
        }
        #endregion

        private async Task<string> Connect(ProtocolBase protocol, string fromView, string assignTabToken = "", string assignRunnerName = "", string assignCredentialName = "")
        {

            #region prepare

            // connect count save to config
            _configurationService.Engagement.ConnectCount++;
            _configurationService.Save();


            // update the last conn time
            {
                var vmServer = _appData.GetItemById(protocol.DataSource?.DataSourceName ?? "", protocol.Id);
                vmServer?.ConnectTimeAddOrUpdate();
                if (IoC.Get<ConfigurationService>().General.ShowRecentlySessionInTray)
                    IoC.Get<TaskTrayService>().ReloadTaskTrayContextMenu();
            }

            // clone and decrypt!
            var protocolClone = protocol.Clone();
            protocolClone.DecryptToConnectLevel();
            protocolClone.GenerateSessionId();


            // apply alternate credential
            {
                if (protocolClone is ProtocolBaseWithAddressPort p)
                {
                    var c = await GetCredential(p, assignCredentialName);
                    if (c == null)
                    {
                        return "";
                    }

                    p.SetCredential(c, true);
                    p.DisplayName = c.Name;
                }
            }



            // check if it needs password
            if (protocolClone is ProtocolBaseWithAddressPortUserPwd { AskPasswordWhenConnect: true } pb)
            {
                bool flag = false;
                var pwdDlg = new PasswordPopupDialogViewModel(protocolClone is SSH or SFTP)
                {
                    Title = $"[{pb.ProtocolDisplayName}]({pb.DisplayName}) -> {pb.Address}:{pb.Port}",
                    UserName = pb.UserName
                };
                if (pb.UsePrivateKeyForConnect == true)
                {
                    pwdDlg.CanUsePrivateKeyForConnect = true;
                    pwdDlg.UsePrivateKeyForConnect = true;
                    pwdDlg.PrivateKey = pb.PrivateKey;
                }
                else
                {
                    pwdDlg.UsePrivateKeyForConnect = false;
                    pwdDlg.Password = pb.Password;
                }

                Execute.OnUIThreadSync(() =>
                {
                    MaskLayerController.ShowWindowWithMask(pwdDlg);
                });

                if (await pwdDlg.WaitDialogResult() == true)
                {
                    flag = true;
                    pb.UserName = pwdDlg.UserName;
                    if (pwdDlg.UsePrivateKeyForConnect)
                    {
                        pb.UsePrivateKeyForConnect = true;
                        pb.Password = "";
                        pb.PrivateKey = pwdDlg.PrivateKey;
                    }
                    else
                    {
                        pb.UsePrivateKeyForConnect = false;
                        pb.PrivateKey = "";
                        pb.Password = pwdDlg.Password;
                    }
                    pwdDlg.PrivateKey = "";
                    pwdDlg.Password = "";
                }
                else
                {
                    pwdDlg.Password = "";
                }


                if (flag == false)
                {
                    return "";
                }
            }

            #endregion


            // if is OnlyOneInstance server, and it is connected now, activate it and return.
            if (this.ActivateOrReConnIfServerSessionIsOpened(protocolClone))
                return "";

            // From here on the attempt is going to reach the network, and the address is still the real one:
            // ApplyTo below rewrites it to a loopback port when a proxy is in play. Everything after this
            // point either opens a session or reports why it did not.
            AuditConnectStarted(protocolClone);

            // run script before connected
            {
                int code = protocolClone.RunScriptBeforeConnect();
                if (0 != code)
                {
                    MessageBoxHelper.ErrorAlert($"Script ExitCode = {code}, connection abort!");
                    AuditConnectFailed(protocolClone, $"PreConnectScriptExitCode={code}");
                    return "";
                }
            }

            // Route through the selected proxy, if any. Deliberately after the instance check and the script
            // above, so both still see the real address, and before every protocol dispatch below, so all of
            // them connect to the loopback endpoint instead.
            if (IoC.Get<ProxyService>().ApplyTo(protocolClone) == EProxyApplyResult.Abort)
            {
                AuditConnectFailed(protocolClone, "ProxyUnavailable");
                return "";
            }

            // dispatch for specified protocol
            if (protocolClone is RdpApp rdpApp)
            {
                AuditSessionOpened(protocolClone.BuildConnectionId());
                ConnectRemoteApp(rdpApp);
                return "";
            }
            else if (protocolClone is RDP rdp)
            {
                if (rdp.IsNeedRunWithMstsc())
                {
                    AuditSessionOpened(protocolClone.BuildConnectionId());
                    ConnectRdpByMstsc(rdp);
                    return "";
                }
                // rdp full screen
                if (protocolClone.IsThisTimeConnWithFullScreen())
                {
                    this.ConnectWithFullScreen(protocolClone, new InternalDefaultRunner(RDP.ProtocolName));
                    return "";
                }
            }
            else if (protocolClone is SSH { OpenSftpOnConnected: true } ssh)
            {
                // open SFTP when SSH is connected.
                var tmpRunner = RunnerHelper.GetRunner(IoC.Get<ProtocolConfigurationService>(), protocolClone, SFTP.ProtocolName);
                // ProxyName is deliberately not copied: ssh has already been through ApplyTo above, so its
                // address is the loopback end of the tunnel. SFTP rides the same SSH port, so pointing it at
                // that same endpoint reuses the one tunnel; copying the name too would tunnel a tunnel.
                var sftp = new SFTP
                {
                    ColorHex = ssh.ColorHex,
                    IconBase64 = ssh.IconBase64,
                    DisplayName = ssh.DisplayName + " (SFTP)",
                    Address = ssh.Address,
                    Port = ssh.Port,
                    UserName = ssh.UserName,
                    Password = ssh.Password,
                    PrivateKey = ssh.PrivateKey,
                    TrustUnverifiedHost = ssh.TrustUnverifiedHost,
                };
                assignTabToken = await Connect(sftp, fromView, assignTabToken, tmpRunner.Name, assignCredentialName);
            }
            else if (protocolClone is LocalApp { RunWithHosting: false } localApp)
            {
                var tmp = WinCmdRunner.CheckFileExistsAndFullName(localApp.GetExePath());
                if (tmp.Item1)
                {
                    var process = Process.Start(tmp.Item2, localApp.GetArguments(false));
                    AddUnHostingWatch(process, localApp);
                    AuditSessionOpened(protocolClone.BuildConnectionId());
                }
                else
                {
                    AuditConnectFailed(protocolClone, "ExecutableNotFound");
                }
                return "";
            }


            string tabToken = "";
            var s = IoC.Get<ProtocolConfigurationService>();
            var runner = RunnerHelper.GetRunner(s, protocolClone, protocolClone.Protocol, assignRunnerName)!;
            if (runner.IsRunWithoutHosting())
            {
                runner.RunWithoutHosting(protocolClone);
                AuditSessionOpened(protocolClone.BuildConnectionId());
            }
            else
            {
                tabToken = ConnectWithTab(protocolClone, runner, assignTabToken);
            }
            return tabToken;
        }
    }
}