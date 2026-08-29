using System;
using System.Collections.Generic;
using System.Linq;

namespace _1RM.Utils.mRemoteNG
{
    /// <summary>
    /// One row of an mRemoteNG <c>confCons.csv</c> export. The field names match the CSV column titles
    /// (case-insensitively), which is how <see cref="MRemoteNgCsv"/> fills them by reflection.
    /// </summary>
    public class MRemoteNgItem
    {
        public string Name = "",
            Id = "",
            Parent = "",
            NodeType = "",
            Description = "",
            Icon = "",
            Panel = "",
            Username = "",
            Password = "",
            Domain = "",
            Hostname = "",
            VmId = "",
            Protocol = "",
            PuttySession = "",
            Port = "",
            ConnectToConsole = "",
            UseCredSsp = "",
            UseVmId = "",
            RenderingEngine = "",
            ICAEncryptionStrength = "",
            RDPAuthenticationLevel = "",
            LoadBalanceInfo = "",
            Colors = "",
            Resolution = "",
            AutomaticResize = "",
            DisplayWallpaper = "",
            DisplayThemes = "",
            EnableFontSmoothing = "",
            EnableDesktopComposition = "",
            CacheBitmaps = "",
            RedirectDiskDrives = "",
            RedirectPorts = "",
            RedirectPrinters = "",
            RedirectClipboard = "",
            RedirectSmartCards = "",
            RedirectSound = "",
            RedirectKeys = "",
            PreExtApp = "",
            PostExtApp = "",
            MacAddress = "",
            UserField = "",
            ExtApp = "",
            Favorite = "",
            VNCCompression = "",
            VNCEncoding = "",
            VNCAuthMode = "",
            VNCProxyType = "",
            VNCProxyIP = "",
            VNCProxyPort = "",
            VNCProxyUsername = "",
            VNCProxyPassword = "",
            VNCColors = "",
            VNCSmartSizeMode = "",
            VNCViewOnly = "",
            RDGatewayUsageMethod = "",
            RDGatewayHostname = "",
            RDGatewayUseConnectionCredentials = "",
            RDGatewayUsername = "",
            RDGatewayPassword = "",
            RDGatewayDomain = "",
            RedirectAudioCapture = "",
            RdpVersion = "";
    }

    /// <summary>
    /// The mRemoteNG CSV reading that does not need a window: turning the semicolon-separated export into
    /// <see cref="MRemoteNgItem"/> rows, disambiguating duplicate ids, prefixing a connection's name with
    /// its container path, and folding inherited values down from parents. <c>MRemoteNgImporter</c> is the
    /// thin wrapper that turns these rows into the app's protocol models.
    /// </summary>
    public static class MRemoteNgCsv
    {
        public const string NodeTypeConnection = "Connection";

        /// <summary>
        /// Reads the value under <paramref name="fieldName"/> for one row. Returns "" when the column is
        /// absent <em>or</em> when this row has fewer fields than the header — mRemoteNG drops trailing
        /// empty columns, so a short row is normal and must not throw and abort the whole import.
        /// </summary>
        public static string GetValue(List<string> keyList, string[] valueList, string fieldName)
        {
            var i = keyList.IndexOf(fieldName.ToLower());
            if (i >= 0 && i < valueList.Length)
                return valueList[i].Trim();
            return "";
        }

        public static List<string>? GetTitles(string firstLine)
        {
            if (string.IsNullOrWhiteSpace(firstLine))
                return null;
            var titles = firstLine.ToLower().Split(';').ToList();
            if (titles.Count == 0)
                return null;
            return titles;
        }

        /// <summary>
        /// Parses the export into rows keyed by id, then rewrites each connection's <c>Name</c> to include
        /// the names of its containers, joined by " - ".
        /// </summary>
        public static Dictionary<string, MRemoteNgItem>? ParseItems(string[] csvLines)
        {
            if (csvLines.Length == 0)
                return null;

            var titles = GetTitles(csvLines[0]);
            if (titles == null || titles.Count == 0)
                return null;

            var id2Item = new Dictionary<string, MRemoteNgItem>();
            var fields = typeof(MRemoteNgItem).GetFields();

            for (var i = 1; i < csvLines.Length; i++)
            {
                var arr = csvLines[i].Split(';');
                if (arr.Length < 7) continue;

                var item = new MRemoteNgItem();
                foreach (var field in fields)
                    field.SetValue(item, GetValue(titles, arr, field.Name));

                if (id2Item.ContainsKey(item.Id))
                {
                    var count = id2Item.Keys.Count(x => x == item.Id
                                                        || (x.StartsWith(item.Id + " (") && x.EndsWith(")")));
                    item.Id += $" ({count})";
                }
                id2Item.Add(item.Id, item);
            }

            foreach (var kv in id2Item.ToArray())
            {
                var item = kv.Value;
                if (item.NodeType != NodeTypeConnection) continue;
                var pid = item.Parent;
                while (id2Item.ContainsKey(pid))
                {
                    item.Name = $"{id2Item[pid].Name} - {item.Name}";
                    pid = id2Item[pid].Parent;
                }
            }

            return id2Item;
        }

        /// <summary>
        /// Fills each connection's empty fields from the nearest ancestor that has a value, which is how
        /// mRemoteNG's "Inherit" flags resolve at read time.
        /// </summary>
        public static void Inherit(Dictionary<string, MRemoteNgItem> items)
        {
            var fields = typeof(MRemoteNgItem).GetFields();

            foreach (var kv in items)
            {
                var item = kv.Value;
                if (item.NodeType != NodeTypeConnection)
                    continue;

                foreach (var field in fields)
                {
                    if (string.IsNullOrEmpty(field.GetValue(item)?.ToString()) == false) continue;

                    var pid = item.Parent;
                    while (items.ContainsKey(pid) && string.IsNullOrWhiteSpace(field.GetValue(items[pid])?.ToString()))
                        pid = items[pid].Parent;

                    if (items.ContainsKey(pid) && string.IsNullOrWhiteSpace(field.GetValue(items[pid])?.ToString()) == false)
                        field.SetValue(item, field.GetValue(items[pid]));
                }
            }
        }
    }
}
