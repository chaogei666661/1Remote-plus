using System;
using System.Collections.Generic;
using _1RM.Utils;
using _1RM.Utils.RdpFile;

namespace _1RM.Model.Protocol
{
    /// <summary>
    /// Translation between a server as this app stores it and a .rdp file as mstsc understands it. Both
    /// directions live here: <see cref="ToRdpConfig"/> for launching mstsc or exporting, and
    /// <see cref="FromRdpConfig"/> for importing a .rdp someone was sent.
    ///
    /// Kept apart from RDP.cs because it is a mapping table rather than behaviour - long, mechanical, and
    /// the two halves have to be read against each other whenever a setting is added.
    /// </summary>
    public sealed partial class RDP
    {
        /// <summary>
        /// To rdp file object
        /// </summary>
        /// <returns></returns>
        public RdpConfig ToRdpConfig()
        {
            var rdpConfig = new RdpConfig(DisplayName, $"{this.Address}:{this.GetPort()}",
                this.UserName, UnSafeStringEncipher.DecryptOrReturnOriginalString(Password),
                RdpFileAdditionalSettings)
            {
                Domain = this.Domain,
                LoadBalanceInfo = this.LoadBalanceInfo,
                // 2 = warn me, matching the ActiveX host. See TrustUnverifiedHost.
                AuthenticationLevel = this.TrustUnverifiedHost ? 0 : 2,
                DisplayConnectionBar = this.IsFullScreenWithConnectionBar == true ? 1 : 0
            };

            switch (this.RdpFullScreenFlag)
            {
                case ERdpFullScreenFlag.Disable:
                    rdpConfig.ScreenModeId = 1;
                    rdpConfig.DesktopWidth = this.RdpWidth > 0 ? this.RdpWidth ?? 800 : 800;
                    rdpConfig.DesktopHeight = this.RdpHeight > 0 ? this.RdpHeight ?? 600 : 600;
                    break;

                case ERdpFullScreenFlag.EnableFullAllScreens:
                    rdpConfig.ScreenModeId = 2;
                    rdpConfig.UseMultimon = 1;
                    break;

                case ERdpFullScreenFlag.EnableFullScreen:
                    rdpConfig.ScreenModeId = 2;
                    break;

                default:
                    break;
            }

            switch (this.RdpWindowResizeMode)
            {
                case ERdpWindowResizeMode.Stretch:
                    rdpConfig.SmartSizing = 1;
                    rdpConfig.DynamicResolution = 0;
                    break;

                case ERdpWindowResizeMode.Fixed:
                    rdpConfig.SmartSizing = 0;
                    rdpConfig.DynamicResolution = 0;
                    rdpConfig.DesktopWidth = this.RdpWidth > 0 ? this.RdpWidth ?? 800 : 800;
                    rdpConfig.DesktopHeight = this.RdpHeight > 0 ? this.RdpHeight ?? 600 : 600;
                    break;

                case ERdpWindowResizeMode.AutoResize:
                default:
                    rdpConfig.SmartSizing = 0;
                    rdpConfig.DynamicResolution = 1;
                    break;
            }

            rdpConfig.NetworkAutodetect = 0;
            switch (this.DisplayPerformance)
            {
                case EDisplayPerformance.Low:
                    rdpConfig.ConnectionType = 1;
                    rdpConfig.SessionBpp = 8;
                    rdpConfig.AllowDesktopComposition = 0;
                    rdpConfig.AllowFontSmoothing = 0;
                    rdpConfig.DisableFullWindowDrag = 1;
                    rdpConfig.DisableThemes = 1;
                    rdpConfig.DisableWallpaper = 1;
                    rdpConfig.DisableMenuAnims = 1;
                    rdpConfig.DisableCursorSetting = 1;
                    break;

                case EDisplayPerformance.Middle:
                    rdpConfig.SessionBpp = 16;
                    rdpConfig.ConnectionType = 3;
                    rdpConfig.AllowDesktopComposition = 1;
                    rdpConfig.AllowFontSmoothing = 1;
                    rdpConfig.DisableFullWindowDrag = 1;
                    rdpConfig.DisableThemes = 1;
                    rdpConfig.DisableWallpaper = 1;
                    rdpConfig.DisableMenuAnims = 1;
                    rdpConfig.DisableCursorSetting = 1;
                    break;

                case EDisplayPerformance.High:
                    rdpConfig.SessionBpp = 32;
                    rdpConfig.ConnectionType = 7;
                    rdpConfig.AllowDesktopComposition = 1;
                    rdpConfig.AllowFontSmoothing = 1;
                    rdpConfig.DisableFullWindowDrag = 0;
                    rdpConfig.DisableThemes = 0;
                    rdpConfig.DisableWallpaper = 0;
                    rdpConfig.DisableMenuAnims = 0;
                    rdpConfig.DisableCursorSetting = 0;
                    break;

                case EDisplayPerformance.Auto:
                default:
                    rdpConfig.NetworkAutodetect = 1;
                    break;
            }


            if (this.EnableDiskDrives == true)
            {
                rdpConfig.DriveStoreDirect = "*";
                rdpConfig.RedirectDrives = 1;
            }
            else
            {
                rdpConfig.DriveStoreDirect = "";
                rdpConfig.RedirectDrives = 0;
            }

            if (this.EnableRedirectDrivesPlugIn == true)
            {
                rdpConfig.RedirectDrives = 1;
                rdpConfig.DriveStoreDirect += ";DynamicDrives";
                rdpConfig.DriveStoreDirect = rdpConfig.DriveStoreDirect.Trim(';');
            }

            if (this.EnableClipboard == true)
                rdpConfig.RedirectClipboard = 1;
            if (this.EnablePrinters == true)
                rdpConfig.RedirectPrinters = 1;
            if (this.EnablePorts == true)
                rdpConfig.RedirectComPorts = 1;
            else
                rdpConfig.RedirectComPorts = 0;

            if (this.EnableSmartCardsAndWinHello == true)
                rdpConfig.RedirectSmartCards = 1;
            if (this.EnableKeyCombinations == true)
                rdpConfig.KeyboardHook = 2;
            else
                rdpConfig.KeyboardHook = 0;

            if (this.AudioRedirectionMode == EAudioRedirectionMode.RedirectToLocal)
                rdpConfig.AudioMode = 0;
            else if (this.AudioRedirectionMode == EAudioRedirectionMode.LeaveOnRemote)
                rdpConfig.AudioMode = 1;
            else if (this.AudioRedirectionMode == EAudioRedirectionMode.Disabled)
                rdpConfig.AudioMode = 2;

            if (this.AudioQualityMode == EAudioQualityMode.Dynamic)
                rdpConfig.AudioQualityMode = 0;
            else if (this.AudioQualityMode == EAudioQualityMode.Medium)
                rdpConfig.AudioQualityMode = 1;
            else if (this.AudioQualityMode == EAudioQualityMode.High)
                rdpConfig.AudioQualityMode = 2;

            if (this.EnableAudioCapture == true)
                rdpConfig.AudioCaptureMode = 1;

            rdpConfig.AutoReconnectionEnabled = 1;

            switch (GatewayMode)
            {
                case EGatewayMode.AutomaticallyDetectGatewayServerSettings:
                    rdpConfig.GatewayUsageMethod = 2;
                    break;

                case EGatewayMode.UseTheseGatewayServerSettings:
                    rdpConfig.GatewayUsageMethod = 1;
                    break;

                case EGatewayMode.DoNotUseGateway:
                default:
                    rdpConfig.GatewayUsageMethod = 0;
                    break;
            }
            rdpConfig.GatewayHostname = this.GatewayHostName;
            rdpConfig.GatewayCredentialsSource = 4;
            return rdpConfig;
        }

        public static RDP FromRdpConfig(RdpConfig rdpConfig, List<string> iconsBase64)
        {
            var r = new Random();
            var rdp = new RDP()
            {
                DisplayName = rdpConfig.Name,
                IconBase64 = iconsBase64[r.Next(0, iconsBase64.Count)],
            };

            {
                var i = rdpConfig.FullAddress.LastIndexOf(":", StringComparison.Ordinal);
                if (i > 0
                    && int.TryParse(rdpConfig.FullAddress.Substring(i + 1), out var port))
                {
                    rdp.Address = rdpConfig.FullAddress.Substring(0, i);
                    rdp.Port = port.ToString();
                }
                else
                {
                    rdp.Address = rdpConfig.FullAddress;
                }
            }

            rdp.UserName = rdpConfig.Username;

            rdp.Domain = rdpConfig.Domain;
            rdp.LoadBalanceInfo = rdpConfig.LoadBalanceInfo;
            rdp.IsFullScreenWithConnectionBar = rdpConfig.DisplayConnectionBar == 1;

            rdp.RdpFullScreenFlag = ERdpFullScreenFlag.EnableFullScreen;
            switch (rdpConfig.ScreenModeId)
            {
                case 1:
                    rdp.IsConnWithFullScreen = false;
                    break;
                case 2:
                    rdp.IsConnWithFullScreen = true;
                    rdp.RdpFullScreenFlag = rdpConfig.UseMultimon > 0 ? ERdpFullScreenFlag.EnableFullAllScreens : ERdpFullScreenFlag.EnableFullScreen;
                    break;

            }
            rdp.RdpWidth = rdpConfig.DesktopWidth > 0 ? rdpConfig.DesktopWidth : 800;
            rdp.RdpHeight = rdpConfig.DesktopHeight > 0 ? rdpConfig.DesktopHeight : 600;

            if (rdpConfig.SmartSizing > 0)
            {
                rdp.RdpWindowResizeMode = ERdpWindowResizeMode.Stretch;
            }
            else if (rdpConfig.DynamicResolution > 0)
            {
                rdp.RdpWindowResizeMode = ERdpWindowResizeMode.AutoResize;
            }
            else
            {
                rdp.RdpWindowResizeMode = ERdpWindowResizeMode.Fixed;
            }


            rdp.DisplayPerformance = EDisplayPerformance.Auto;
            rdp.EnableDiskDrives = rdpConfig.RedirectDrives > 0 || false == string.IsNullOrEmpty(rdpConfig.DriveStoreDirect.Replace("DynamicDrives", "").Trim());
            rdp.EnableRedirectDrivesPlugIn = rdpConfig.DriveStoreDirect.IndexOf("DynamicDrives", StringComparison.OrdinalIgnoreCase) >= 0;
            rdp.EnableClipboard = rdpConfig.RedirectClipboard > 0;
            rdp.EnablePrinters = rdpConfig.RedirectPrinters > 0;
            rdp.EnablePorts = rdpConfig.RedirectComPorts > 0;
            rdp.EnableSmartCardsAndWinHello = rdpConfig.RedirectSmartCards > 0;
            rdp.EnableKeyCombinations = rdpConfig.KeyboardHook > 0;
            switch (rdpConfig.AudioMode)
            {
                case 0: rdp.AudioRedirectionMode = EAudioRedirectionMode.RedirectToLocal; break;
                case 1: rdp.AudioRedirectionMode = EAudioRedirectionMode.LeaveOnRemote; break;
                case 2: rdp.AudioRedirectionMode = EAudioRedirectionMode.Disabled; break;
            }
            switch (rdpConfig.AudioQualityMode)
            {
                case 0: rdp.AudioQualityMode = EAudioQualityMode.Dynamic; break;
                case 1: rdp.AudioQualityMode = EAudioQualityMode.Medium; break;
                case 2: rdp.AudioQualityMode = EAudioQualityMode.High; break;
            }
            rdp.EnableAudioCapture = rdpConfig.AudioCaptureMode > 0;


            switch (rdpConfig.GatewayUsageMethod)
            {
                case 0: rdp.GatewayMode = EGatewayMode.DoNotUseGateway; break;
                case 1: rdp.GatewayMode = EGatewayMode.UseTheseGatewayServerSettings; break;
                case 2: rdp.GatewayMode = EGatewayMode.AutomaticallyDetectGatewayServerSettings; break;
            }
            rdp.GatewayHostName = rdpConfig.GatewayHostname;
            return rdp;
        }
    }
}
