using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media.Imaging;
using _1RM.Model;
using _1RM.Model.Protocol;
using _1RM.Model.Protocol.Base;
using Shawn.Utils.Wpf.Image;

namespace _1RM.Utils.mRemoteNG
{
    public static class MRemoteNgImporter
    {
        private const string NodeTypeConnection = "Connection";

        public static List<ProtocolBase>? FromCsv(string csvPath, List<string> icons)
        {
            if (!File.Exists(csvPath))
                return null;

            var csvLines = File.ReadAllLines(csvPath, Encoding.UTF8);
            if (csvLines.Length == 0)
                return null;

            var id2MRemoteNgItem = MRemoteNgCsv.ParseItems(csvLines);
            if (id2MRemoteNgItem == null || id2MRemoteNgItem.Count == 0)
                return null;

            MRemoteNgCsv.Inherit(id2MRemoteNgItem);

            var list = new List<ProtocolBase>();
            var r = new Random();
            foreach (var kv in id2MRemoteNgItem)
            {
                var item = kv.Value;
                if (item.NodeType != NodeTypeConnection)
                    continue;

                ProtocolBase? server = null;
                List<string> tags = new List<string>();
                //if (id2MRemoteNgItem.ContainsKey(item.Parent))
                //{
                //    string tag = id2MRemoteNgItem[item.Parent].Name;
                //    var pid = id2MRemoteNgItem[item.Parent].Parent;
                //    while (id2MRemoteNgItem.ContainsKey(pid))
                //    {
                //        tag = $"{id2MRemoteNgItem[pid].Name} - {tag}";
                //        pid = id2MRemoteNgItem[pid].Parent;
                //    }
                //}
                if (id2MRemoteNgItem.ContainsKey(item.Parent))
                    tags = new List<string>() { id2MRemoteNgItem[item.Parent].Name };

                switch (item.Protocol.ToLower())
                {
                    case "rdp":
                        server = new RDP()
                        {
                            DisplayName = item.Name,
                            Tags = tags,
                            Address = item.Hostname,
                            UserName = item.Username,
                            Password = item.Password,
                            Port = item.Port,
                            Domain = item.Domain,
                            LoadBalanceInfo = item.LoadBalanceInfo,
                            RdpWindowResizeMode = ERdpWindowResizeMode.AutoResize, // string.Equals( getValue(title, arr, "AutomaticResize"), "TRUE", StringComparison.CurrentCultureIgnoreCase) ? ERdpWindowResizeMode.AutoResize : ERdpWindowResizeMode.Fixed,
                            IsConnWithFullScreen = string.Equals(item.Resolution, "Fullscreen", StringComparison.CurrentCultureIgnoreCase),
                            RdpFullScreenFlag = ERdpFullScreenFlag.EnableFullScreen,
                            DisplayPerformance = item.Colors.IndexOf("32", StringComparison.Ordinal) >= 0 ? EDisplayPerformance.High : EDisplayPerformance.Auto,
                            IsAdministrativePurposes = string.Equals(item.ConnectToConsole, "TRUE", StringComparison.CurrentCultureIgnoreCase),
                            EnableClipboard = string.Equals(item.RedirectClipboard, "TRUE", StringComparison.CurrentCultureIgnoreCase),
                            EnableDiskDrives = string.Equals(item.RedirectDiskDrives, "TRUE", StringComparison.CurrentCultureIgnoreCase),
                            EnableKeyCombinations = string.Equals(item.RedirectKeys, "TRUE", StringComparison.CurrentCultureIgnoreCase),
                            AudioRedirectionMode = string.Equals(item.RedirectSound, "BringToThisComputer", StringComparison.CurrentCultureIgnoreCase) ? EAudioRedirectionMode.RedirectToLocal : (string.Equals(item.RedirectSound, "LeaveAtRemoteComputer", StringComparison.CurrentCultureIgnoreCase) ? EAudioRedirectionMode.LeaveOnRemote : EAudioRedirectionMode.Disabled),
                            EnableAudioCapture = string.Equals(item.RedirectAudioCapture, "TRUE", StringComparison.CurrentCultureIgnoreCase),
                            EnablePorts = string.Equals(item.RedirectPorts, "TRUE", StringComparison.CurrentCultureIgnoreCase),
                            EnablePrinters = string.Equals(item.RedirectPrinters, "TRUE", StringComparison.CurrentCultureIgnoreCase),
                            EnableSmartCardsAndWinHello = string.Equals(item.RedirectSmartCards, "TRUE", StringComparison.CurrentCultureIgnoreCase),
                            GatewayMode = string.Equals(item.RDGatewayUsageMethod, "Never", StringComparison.CurrentCultureIgnoreCase) ? EGatewayMode.DoNotUseGateway :
                                (string.Equals(item.RDGatewayUsageMethod, "Detect", StringComparison.CurrentCultureIgnoreCase) ? EGatewayMode.AutomaticallyDetectGatewayServerSettings : EGatewayMode.UseTheseGatewayServerSettings),
                            GatewayHostName = item.RDGatewayHostname,
                            GatewayPassword = item.RDGatewayPassword,
                        };

                        break;

                    case "ssh1":
                        server = new SSH()
                        {
                            DisplayName = item.Name,
                            Tags = tags,
                            Address = item.Hostname,
                            UserName = item.Username,
                            Password = item.Password,
                            Port = item.Port,
                            SshVersion = 1
                        };
                        break;

                    case "ssh2":
                        server = new SSH()
                        {
                            DisplayName = item.Name,
                            Tags = tags,
                            Address = item.Hostname,
                            UserName = item.Username,
                            Password = item.Password,
                            Port = item.Port,
                            SshVersion = 2
                        };
                        break;

                    case "vnc":
                        server = new VNC()
                        {
                            DisplayName = item.Name,
                            Tags = tags,
                            Address = item.Hostname,
                            Password = item.Password,
                            Port = item.Port,
                        };
                        break;

                    case "telnet":
                        server = new Telnet()
                        {
                            DisplayName = item.Name,
                            Tags = tags,
                            Address = item.Hostname,
                            Port = item.Port,
                        };
                        break;
                }

                if (server != null)
                {
                    if (icons.Count > 0)
                    {
                        server.IconBase64 = icons[r.Next(0, icons.Count)];
                    }
                    list.Add(server);
                }
            }

            return list;
        }
    }
}
