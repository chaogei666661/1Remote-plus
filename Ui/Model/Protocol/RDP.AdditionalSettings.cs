using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MSTSCLib;

namespace _1RM.Model.Protocol
{
    /// <summary>
    /// The free-text "additional settings" box for the in-app RDP control: the list of properties it will
    /// accept, the parser for what the user typed, and the reflection that pushes the result onto the
    /// ActiveX control. Split out of RDP.cs because none of it is part of describing a server - it is a
    /// small parser that happens to live on the model, and it made the protocol itself hard to read.
    /// </summary>
    public sealed partial class RDP
    {
        private string _rdpControlAdditionalSettings = "";
        public string RdpControlAdditionalSettings
        {
            get => _rdpControlAdditionalSettings;
            set => SetAndNotifyIfChanged(ref _rdpControlAdditionalSettings, value);
        }


        private static List<string>? _rdpControlAdditionalSettingKeys = null;
        public static List<string> GetRdpControlAdditionalSettingKeys()
        {
            if (_rdpControlAdditionalSettingKeys != null)
            {
                return _rdpControlAdditionalSettingKeys;
            }

            var excludeKeys = new HashSet<string>()
            {
                "Name", "Parent",
                nameof(AxMSTSCLib.AxMsRdpClient10.Server),
                nameof(AxMSTSCLib.AxMsRdpClient10.Domain),
                nameof(AxMSTSCLib.AxMsRdpClient10.UserName),
                nameof(IMsRdpClientAdvancedSettings8.RDPPort),
                nameof(AxMSTSCLib.AxMsRdpClient10.FullScreenTitle),

                nameof(AxMSTSCLib.AxMsRdpClient10.Width),
                nameof(AxMSTSCLib.AxMsRdpClient10.Height),
                nameof(AxMSTSCLib.AxMsRdpClient10.Handle),
                nameof(AxMSTSCLib.AxMsRdpClient10.FullScreen),
                nameof(AxMSTSCLib.AxMsRdpClient10.Enabled),
                nameof(AxMSTSCLib.AxMsRdpClient10.AutoSize),
                nameof(AxMSTSCLib.AxMsRdpClient10.DesktopHeight),
                nameof(AxMSTSCLib.AxMsRdpClient10.DesktopWidth),
                nameof(AxMSTSCLib.AxMsRdpClient10.Disposing),
                nameof(AxMSTSCLib.AxMsRdpClient10.DeviceDpi),
                nameof(AxMSTSCLib.AxMsRdpClient10.Left),
                nameof(AxMSTSCLib.AxMsRdpClient10.Right),
                nameof(AxMSTSCLib.AxMsRdpClient10.Top),
                nameof(AxMSTSCLib.AxMsRdpClient10.Bottom),
                nameof(AxMSTSCLib.AxMsRdpClient10.Visible),

                nameof(IMsRdpClientAdvancedSettings8.ConnectToAdministerServer),
                //nameof(IMsRdpClientAdvancedSettings8.DisplayConnectionBar),
                //nameof(IMsRdpClientAdvancedSettings8.PinConnectionBar),

                nameof(IMsRdpClientAdvancedSettings8.EnableMouse),
                nameof(IMsRdpClientAdvancedSettings8.LoadBalanceInfo),

                //nameof(IMsRdpClientAdvancedSettings8.RedirectDrives),
                //nameof(IMsRdpClientAdvancedSettings8.RedirectClipboard),
                //nameof(IMsRdpClientAdvancedSettings8.RedirectPrinters),
                //nameof(IMsRdpClientAdvancedSettings8.RedirectPOSDevices),
                //nameof(IMsRdpClientAdvancedSettings8.RedirectSmartCards),
            };


            // get all writable properties of AxMSTSCLib.AxMsRdpClient10/IMsRdpClientAdvancedSettings8 by reflection, which type is int or bool or string
            var keys = new List<string>();
            {
                {
                    var type = typeof(IMsRdpClientAdvancedSettings8);
                    var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.CanWrite && (p.PropertyType == typeof(int) || p.PropertyType == typeof(bool) || p.PropertyType == typeof(string)));
                    foreach (var propertyInfo in properties)
                    {
                        if (excludeKeys.Contains(propertyInfo.Name)) continue;
                        string typeStr = ":s:";
                        if (propertyInfo.PropertyType == typeof(int))
                        {
                            typeStr = ":i:";
                        }
                        else if (propertyInfo.PropertyType == typeof(bool))
                        {
                            typeStr = ":i:";
                        }

                        keys.Add($"{propertyInfo.Name}{typeStr}");
                    }
                }
                {
                    var type = typeof(AxMSTSCLib.AxMsRdpClient10);
                    var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.CanWrite && (p.PropertyType == typeof(int) || p.PropertyType == typeof(bool) || p.PropertyType == typeof(string)));
                    foreach (var propertyInfo in properties)
                    {
                        if (excludeKeys.Contains(propertyInfo.Name)) continue;
                        string typeStr = ":s:";
                        if (propertyInfo.PropertyType == typeof(int))
                        {
                            typeStr = ":i:";
                        }
                        else if (propertyInfo.PropertyType == typeof(bool))
                        {
                            typeStr = ":i:";
                        }

                        keys.Add($"{propertyInfo.Name}{typeStr}");
                    }
                }
            }
            _rdpControlAdditionalSettingKeys = keys.Distinct().OrderBy(x => x.ToLower()[0]).ToList();
            return _rdpControlAdditionalSettingKeys;
        }

        /// <summary>
        /// separate the rdpControlAdditionalSettings into `key`,`value`,`error message`, and `original string` tuples
        /// </summary>
        private static List<Tuple<string, string, string>> SplitAdditionalSettings(string rdpControlAdditionalSettings)
        {
            var results = new List<Tuple<string, string, string>>(); // return key, value, error message
            if (string.IsNullOrWhiteSpace(rdpControlAdditionalSettings) != false) return results;
            var separators = new[] { ":s:", ":i:", ":b:" };
            foreach (var s in rdpControlAdditionalSettings.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int count = separators.Count(separator => s.IndexOf(separator, StringComparison.OrdinalIgnoreCase) >= 0);
                if (count != 1)
                {
                    results.Add(new Tuple<string, string, string>(s, "", $"{s}: format error"));
                }
                else
                {
                    foreach (var separator in separators)
                    {
                        if (s.IndexOf(separator, StringComparison.OrdinalIgnoreCase) <= 0) continue;
                        var ss = s.Split(new[]{ separator }, StringSplitOptions.RemoveEmptyEntries);
                        if (ss.Length != 2)
                        {
                            results.Add(new Tuple<string, string, string>(ss[0].Trim(), "", $"{s}: format error"));
                        }
                        else
                        {
                            var key = ss[0].Trim();
                            if (results.Any(x => x.Item1 == key))
                            {
                                results.Add(new Tuple<string, string, string>(key, "", $"{key}: duplicate key"));
                                break;
                            }
                            var value = ss[1].Trim();
                            switch (separator)
                            {
                                case ":i:":
                                    results.Add(new Tuple<string, string, string>(key, value, int.TryParse(value, out var i) ? "" : $"{key}: value is not int"));
                                    break;
                                case ":s:":
                                    results.Add(new Tuple<string, string, string>(key, value, ""));
                                    break;
                                case ":b:":
                                    results.Add(new Tuple<string, string, string>(key, value, ""));
                                    break;
                                default:
                                    results.Add(new Tuple<string, string, string>(key, value, $"{key}: `{separator}` is not supported"));
                                    break;
                            }
                        }
                        break;
                    }
                }
            }
            return results;
        }

        public void ApplyRdpControlAdditionalSettings(AxMSTSCLib.AxMsRdpClient9NotSafeForScripting _rdpClient)
        {
            var sss = SplitAdditionalSettings(_rdpControlAdditionalSettings);
            var propertiesAxMsRdpClient10 = typeof(AxMSTSCLib.AxMsRdpClient10).GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.CanWrite && (p.PropertyType == typeof(int) || p.PropertyType == typeof(bool) || p.PropertyType == typeof(string))).ToArray();
            var propertiesIMsRdpClientAdvancedSettings8 = typeof(IMsRdpClientAdvancedSettings8).GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.CanWrite && (p.PropertyType == typeof(int) || p.PropertyType == typeof(bool) || p.PropertyType == typeof(string))).ToArray();
            foreach (var tuple in sss)
            {
                if(tuple.Item3 != "") continue;
                if(GetRdpControlAdditionalSettingKeys().Any(x => x.StartsWith(tuple.Item1 + ":")) == false) continue;
                var key = tuple.Item1;
                var value = tuple.Item2;

                // AxMsRdpClient10
                {
                    var pp = propertiesAxMsRdpClient10.FirstOrDefault(x => x.Name == key);
                    if (pp != null && (pp.CanWrite || pp.SetMethod != null))
                    {
                        if (pp.PropertyType == typeof(int))
                        {
                            if (int.TryParse(value, out var i))
                            {
                                pp.SetValue(_rdpClient, i);
                            }
                        }
                        else if (pp.PropertyType == typeof(bool))
                        {
                            if (int.TryParse(value, out var i))
                            {
                                pp.SetValue(_rdpClient, i > 0);
                            }
                        }
                        else if (pp.PropertyType == typeof(string))
                        {
                            pp.SetValue(_rdpClient, value);
                        }
                    }
                }
                // IMsRdpClientAdvancedSettings8
                {
                    var pp = propertiesIMsRdpClientAdvancedSettings8.FirstOrDefault(x => x.Name == key);
                    if (pp != null && (pp.CanWrite || pp.SetMethod != null))
                    {
                        if (pp.PropertyType == typeof(int))
                        {
                            if (int.TryParse(value, out var i))
                            {
                                pp.SetValue(_rdpClient.AdvancedSettings, i);
                            }
                        }
                        else if (pp.PropertyType == typeof(bool))
                        {
                            if (int.TryParse(value, out var i))
                            {
                                pp.SetValue(_rdpClient.AdvancedSettings, i > 0);
                            }
                        }
                        else if (pp.PropertyType == typeof(string))
                        {
                            pp.SetValue(_rdpClient.AdvancedSettings, value);
                        }
                    }
                }
            }
        }
    }
}
