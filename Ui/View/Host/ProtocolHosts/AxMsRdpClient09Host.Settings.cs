using _1RM.Model;
using _1RM.Model.Protocol;
using _1RM.Service.Locality;
using _1RM.Utils;
using _1RM.Utils.RdpFile;
using _1RM.Utils.WindowsApi;
using MSTSCLib;
using Shawn.Utils;
using Shawn.Utils.Wpf;
using Shawn.Utils.WpfResources.Theme.Styles;
using Stylet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using Color = System.Drawing.Color;
using Timer = System.Timers.Timer;

namespace _1RM.View.Host.ProtocolHosts
{
    /// <summary>
    /// Everything that copies a server's settings onto the ActiveX RDP control: credentials, the connection
    /// bar, device redirection, display, performance and the gateway.
    ///
    /// Split out of the code-behind - the same way SessionControlService is split - because it is one long
    /// mapping from the RDP model onto the control's property bag, and it buried the part of the host that
    /// actually does something: creating the control, connecting, resizing and tearing down.
    /// </summary>
    public sealed partial class AxMsRdpClient09Host
    {
        /// <summary>
        /// init server connection info: user name\ psw \ port \ LoadBalanceInfo...
        /// </summary>
        private void RdpInitServerInfo()
        {
            #region server info
            Debug.Assert(_rdpClient != null); if (_rdpClient == null) return;
            // server connection info: user name\ psw \ port ...
            _rdpClient.Server = _rdpSettings.Address;
            _rdpClient.Domain = _rdpSettings.Domain;
            _rdpClient.UserName = _rdpSettings.UserName;
            _rdpClient.AdvancedSettings2.RDPPort = _rdpSettings.GetPort();


            if (string.IsNullOrWhiteSpace(_rdpSettings.LoadBalanceInfo) == false)
            {
                var loadBalanceInfo = _rdpSettings.LoadBalanceInfo;
                if (loadBalanceInfo.Length % 2 == 1)
                    loadBalanceInfo += " ";
                loadBalanceInfo += "\r\n";
                var bytes = Encoding.UTF8.GetBytes(loadBalanceInfo);
                _rdpClient.AdvancedSettings2.LoadBalanceInfo = Encoding.Unicode.GetString(bytes);
            }



            var secured = (MSTSCLib.IMsTscNonScriptable)_rdpClient.GetOcx();
            secured.ClearTextPassword = UnSafeStringEncipher.DecryptOrReturnOriginalString(_rdpSettings.Password);
            _rdpClient.FullScreenTitle = _rdpSettings.DisplayName + " - " + _rdpSettings.SubTitle;

            #endregion server info
        }

        private void RdpInitStatic()
        {
            Debug.Assert(_rdpClient != null); if (_rdpClient == null) return;
            SimpleLogHelper.Debug("RDP Host: init Static");
            _rdpClient.AdvancedSettings2.EncryptionEnabled = 1;
            _rdpClient.AdvancedSettings5.EnableAutoReconnect = true;
            // setting PublicMode to false allows the saving of credentials, which prevents
            _rdpClient.AdvancedSettings6.PublicMode = false;
            _rdpClient.AdvancedSettings5.EnableWindowsKey = 1;
            _rdpClient.AdvancedSettings5.GrabFocusOnConnect = true;
            _rdpClient.AdvancedSettings2.keepAliveInterval = 1000 * 60 * 1; // 1000 = 1000 ms
            _rdpClient.AdvancedSettings2.overallConnectionTimeout = 600; // The new time, in seconds. The maximum value is 600, which represents 10 minutes.

            // enable CredSSP, will use CredSsp if the client supports.
            _rdpClient.AdvancedSettings9.EnableCredSspSupport = true;

            //- 0: If server authentication fails, connect to the computer without warning (Connect and don't warn me)
            //- 1: If server authentication fails, don't establish a connection (Don't connect)
            //- 2: If server authentication fails, show a warning and allow me to connect or refuse the connection (Warn me)
            //- 3: No authentication requirement specified.
            // This was hardcoded to 0, which silently accepted any server identity — the exact warning mstsc
            // shows for an untrusted certificate was suppressed for every connection. Self-signed
            // certificates are normal on internal networks, so the escape hatch is per server rather than
            // global.
            // uint literals: the property is uint, and a conditional over plain 0/2 has the natural type int,
            // which has no implicit conversion to uint
            _rdpClient.AdvancedSettings9.AuthenticationLevel = _rdpSettings.TrustUnverifiedHost ? 0u : 2u;

            // ref: https://docs.microsoft.com/en-us/windows/win32/termserv/imsrdpclientadvancedsettings6-connecttoadministerserver
            _rdpClient.AdvancedSettings7.ConnectToAdministerServer = _rdpSettings.IsAdministrativePurposes == true;
        }

        private void RdpInitConnBar()
        {
            Debug.Assert(_rdpClient != null); if (_rdpClient == null) return;
            SimpleLogHelper.Debug("RDP Host: init conn bar");
            _rdpClient.AdvancedSettings6.DisplayConnectionBar = _rdpSettings.IsFullScreenWithConnectionBar == true;
            if (_rdpClient.AdvancedSettings6.DisplayConnectionBar)
            {
                _rdpClient.AdvancedSettings6.ConnectionBarShowPinButton = true;
                _rdpClient.AdvancedSettings6.PinConnectionBar = _rdpSettings.IsPinTheConnectionBarByDefault == true;
                _rdpClient.AdvancedSettings6.ConnectionBarShowMinimizeButton = true;
                _rdpClient.AdvancedSettings6.ConnectionBarShowRestoreButton = true;
            }
            _rdpClient.AdvancedSettings6.BitmapVirtualCache32BppSize = 48;
        }

        public void NotifyRedirectDeviceChange(int msg, IntPtr wParam, IntPtr lParam)
        {
            const int WM_DEVICECHANGE = 0x0219;

            /* from https://docs.microsoft.com/en-us/windows/win32/termserv/imsrdpclientnonscriptable-notifyredirectdevicechange
             *      https://learn.microsoft.com/en-us/windows/win32/devio/wm-devicechange
             * wParam case when msg == WM_DEVICECHANGE:
             * DBT_DEVNODES_CHANGED     0x0007      A device has been added to or removed from the system. param = 0
             * DBT_DEVICEARRIVAL        0x8000      A device or piece of media has been inserted and is now available. param = A pointer to a structure identifying the device inserted. 
             */
            SimpleLogHelper.Debug($"RDP: NotifyRedirectDeviceChange Receive(0x{msg:X}, 0x{wParam:X}, 0x{lParam:X})");
            if (msg == WM_DEVICECHANGE
                && _rdpClient != null
                && ((IMsRdpClientNonScriptable3)_rdpClient.GetOcx()).RedirectDynamicDevices)
            {
                new MsRdpClientNonScriptableWrapper(_rdpClient.GetOcx()).NotifyRedirectDeviceChange(wParam, lParam);
            }
        }

        private void RdpInitRedirect()
        {
            Debug.Assert(_rdpClient != null); if (_rdpClient == null) return;
            SimpleLogHelper.Debug("RDP Host: init Redirect");


            #region Redirect

            // purpose is not clear
            ((IMsRdpClientNonScriptable3)_rdpClient.GetOcx()).RedirectDynamicDrives = true; // Specifies or retrieves whether dynamically attached Plug and Play (PnP) drives that are enumerated while in a session are available for redirection. https://docs.microsoft.com/en-us/windows/win32/termserv/imsrdpclientnonscriptable3-redirectdynamicdrives

            if (_rdpSettings.EnableDiskDrives == true || _rdpSettings.EnableRedirectDrivesPlugIn == true)
            {
                _rdpClient.AdvancedSettings9.RedirectDrives = true;
                // you must redirect disk drive if you want to redirect usb disk
                if (_rdpSettings.EnableRedirectDrivesPlugIn == true)
                {
                    ((IMsRdpClientNonScriptable3)_rdpClient.GetOcx()).RedirectDynamicDevices = true; // Specifies whether dynamically attached PnP devices that are enumerated while in a session are available for redirection. https://docs.microsoft.com/en-us/windows/win32/termserv/imsrdpclientnonscriptable3-redirectdynamicdevices
                    RedirectDevice();
                }
            }

            // disable local disk
            if (_rdpSettings.EnableDiskDrives == false)
            {
                var ocx = (MSTSCLib.IMsRdpClientNonScriptable7)_rdpClient.GetOcx();
                ocx.DriveCollection.RescanDrives(false);
                for (int i = 0; i < ocx.DriveCollection.DriveCount; i++)
                {
                    ocx.DriveCollection.DriveByIndex[(uint)i].RedirectionState = false;
                }
            }


            _rdpClient.AdvancedSettings9.RedirectClipboard = _rdpSettings.EnableClipboard == true;
            _rdpClient.AdvancedSettings9.RedirectPrinters = _rdpSettings.EnablePrinters == true;
            _rdpClient.AdvancedSettings9.RedirectPOSDevices = _rdpSettings.EnablePorts == true;
            _rdpClient.AdvancedSettings9.RedirectSmartCards = _rdpSettings.EnableSmartCardsAndWinHello == true;


            if (_rdpSettings.EnableKeyCombinations == true)
            {
                // - 0 Apply key combinations only locally at the client computer.
                // - 1 Apply key combinations at the remote server.
                // - 2 Apply key combinations to the remote server only when the client is running in full-screen mode. This is the default value.
                _rdpClient.SecuredSettings3.KeyboardHookMode = 1;
            }
            else
            {
                _rdpClient.SecuredSettings3.KeyboardHookMode = 0;
            }

            if (_rdpSettings.AudioRedirectionMode == EAudioRedirectionMode.RedirectToLocal)
            {
                // - 0 (Audio redirection is enabled and the option for redirection is "Bring to this computer". This is the default mode.)
                // - 1 (Audio redirection is enabled and the option is "Leave at remote computer". The "Leave at remote computer" option is supported only when connecting remotely to a host computer that is running Windows Vista. If the connection is to a host computer that is running Windows Server 2008, the option "Leave at remote computer" is changed to "Do not play".)
                // - 2 (Audio redirection is enabled and the mode is "Do not play".)
                _rdpClient.SecuredSettings3.AudioRedirectionMode = 0;

                // Only set AudioQuality Moode when AudioRedirectionMode == RedirectToLocal
                if (_rdpSettings.AudioQualityMode == EAudioQualityMode.Dynamic)
                {
                    // - 0 Dynamic audio quality. This is the default audio quality setting. The server dynamically adjusts audio output quality in response to network conditions and the client and server capabilities.
                    // - 1 Medium audio quality. The server uses a fixed but compressed format for audio output.
                    // - 2 High audio quality. The server provides audio output in uncompressed PCM format with lower processing overhead for latency.
                    _rdpClient.AdvancedSettings8.AudioQualityMode = 0;
                }
                else if (_rdpSettings.AudioQualityMode == EAudioQualityMode.Medium)
                {
                    // - 1 Medium audio quality. The server uses a fixed but compressed format for audio output.
                    _rdpClient.AdvancedSettings8.AudioQualityMode = 1;
                }
                else if (_rdpSettings.AudioQualityMode == EAudioQualityMode.High)
                {
                    // - 2 High audio quality. The server provides audio output in uncompressed PCM format with lower processing overhead for latency.
                    _rdpClient.AdvancedSettings8.AudioQualityMode = 2;
                }

            }
            else if (_rdpSettings.AudioRedirectionMode == EAudioRedirectionMode.LeaveOnRemote)
            {
                // - 1 (Audio redirection is enabled and the option is "Leave at remote computer". The "Leave at remote computer" option is supported only when connecting remotely to a host computer that is running Windows Vista. If the connection is to a host computer that is running Windows Server 2008, the option "Leave at remote computer" is changed to "Do not play".)
                _rdpClient.SecuredSettings3.AudioRedirectionMode = 1;
            }
            else if (_rdpSettings.AudioRedirectionMode == EAudioRedirectionMode.Disabled)
            {
                // - 2 Disable sound redirection; do not play sounds at the server.
                _rdpClient.SecuredSettings3.AudioRedirectionMode = 2;
            }

            if (_rdpSettings.EnableAudioCapture == true)
            {
                // indicates whether the default audio input device is redirected from the client to the remote session
                _rdpClient.AdvancedSettings8.AudioCaptureRedirectionMode = true;
            }
            else
            {
                _rdpClient.AdvancedSettings8.AudioCaptureRedirectionMode = false;
            }
            #endregion Redirect
        }


        public void RedirectDevice()
        {
            var ocx = _rdpClient?.GetOcx() as MSTSCLib.IMsRdpClientNonScriptable7;
            if (ocx == null)
                return;

            // Collect FriendlyNames of cameras redirected via the RDPECCAM channel so that
            // the same physical device is not also claimed by the USB DeviceCollection channel,
            // which would cause a server-side double-redirect conflict. ref: https://github.com/1Remote/1Remote/issues/1071
            var cameraFriendlyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // redirect camera
            {
                ocx.CameraRedirConfigCollection.RedirectByDefault = false;
                if (_rdpSettings.EnableRedirectCameras == true)
                {
                    ocx.CameraRedirConfigCollection.Rescan(); // enumerates connected camera devices
                    for (int i = 0; i < ocx.CameraRedirConfigCollection.Count; i++)
                    {
                        var camera = ocx.CameraRedirConfigCollection.ByIndex[(uint) i];
                        camera.Redirected = true;
                        cameraFriendlyNames.Add(ocx.CameraRedirConfigCollection.ByIndex[(uint)i].FriendlyName ?? "");
                        SimpleLogHelper.Debug($"Redirect camera: {camera.FriendlyName}");
                    }
                }
            }
            // redirect device
            {
                ocx.DeviceCollection.RescanDevices(false);
                for (uint i = 0; i < ocx.DeviceCollection.DeviceCount; i++)
                {
                    var d = ocx.DeviceCollection.DeviceByIndex[i];
                    if (!string.IsNullOrEmpty(d.FriendlyName) && cameraFriendlyNames.Contains(d.FriendlyName))
                    {
                        // Skip cameras already handled by `redirect camera` to avoid double-redirect conflict
                        SimpleLogHelper.Debug($"Redirect device: skip {d.FriendlyName}({d.DeviceDescription}) for being already redirected as camera");
                        continue;
                    }

                    SimpleLogHelper.Debug($"Redirect device: {d.FriendlyName}({d.DeviceDescription})");
                    d.RedirectionState = true;
                }
            }
        }


        private void RdpInitDisplay(int width = 0, int height = 0, bool isReconnecting = false)
        {
            Debug.Assert(_rdpClient != null); if (_rdpClient == null) return;
            #region Display

            _primaryScaleFactor = ScreenInfoEx.GetPrimaryScreenScaleFactor();
            SimpleLogHelper.Debug($"RDP Host: init Display with ScaleFactor = {_primaryScaleFactor}, W = {width}, H = {height}, isReconnecting = {isReconnecting}");

            if (this._rdpSettings.IsScaleFactorFollowSystem == false && this._rdpSettings.ScaleFactorCustomValue != null)
            {
                _rdpClient.SetExtendedProperty("DesktopScaleFactor", this._rdpSettings.ScaleFactorCustomValue ?? _primaryScaleFactor);
            }
            else
            {
                _rdpClient.SetExtendedProperty("DesktopScaleFactor", _primaryScaleFactor);
            }
            _rdpClient.SetExtendedProperty("DeviceScaleFactor", (uint)100);
            if (_rdpSettings.RdpWindowResizeMode == ERdpWindowResizeMode.Stretch || _rdpSettings.RdpWindowResizeMode == ERdpWindowResizeMode.StretchFullScreen)
                _rdpClient.AdvancedSettings2.SmartSizing = true;
            // to enhance user experience, i let the form handled full screen
            _rdpClient.AdvancedSettings6.ContainerHandledFullScreen = 1;

            // pre-set the rdp width & height
            switch (_rdpSettings.RdpWindowResizeMode)
            {
                case ERdpWindowResizeMode.Stretch:
                case ERdpWindowResizeMode.Fixed:
                    _rdpClient.DesktopWidth = (int)(_rdpSettings.RdpWidth ?? 800);
                    _rdpClient.DesktopHeight = (int)(_rdpSettings.RdpHeight ?? 600);
                    break;
                case ERdpWindowResizeMode.FixedFullScreen:
                case ERdpWindowResizeMode.StretchFullScreen:
                    {
                        var size = GetScreenSizeIfRdpIsFullScreen();
                        _rdpClient.DesktopWidth = size.Width;
                        _rdpClient.DesktopHeight = size.Height;
                        break;
                    }
                case ERdpWindowResizeMode.AutoResize:
                case null:
                default:
                    {
                        // default case, set rdp size to tab window size.
                        if (width < 100)
                            width = 800;
                        if (height < 100)
                            height = 600;


                        //if (isReconnecting == true)
                        //{
                        //    // if isReconnecting == true, then width is DesktopWidth, ScaleFactor should be 100
                        //    _rdpClient.DesktopWidth = (int)(width);
                        //    _rdpClient.DesktopHeight = (int)(height);
                        //}
                        //else
                        {
                            // if isReconnecting == false, then width is Tab width, true width = Tab width * ScaleFactor
                            if (_rdpSettings.IsThisTimeConnWithFullScreen())
                            {
                                var size = GetScreenSizeIfRdpIsFullScreen();
                                _rdpClient.DesktopWidth = size.Width;
                                _rdpClient.DesktopHeight = size.Height;
                                SimpleLogHelper.DebugInfo($"RDP Host: init Display set FullScreen DesktopWidth = {_rdpClient.DesktopWidth},  DesktopHeight = {_rdpClient.DesktopHeight}");
                            }
                            else
                            {
                                _rdpClient.DesktopWidth = (int)(width * (_primaryScaleFactor / 100.0));
                                _rdpClient.DesktopHeight = (int)(height * (_primaryScaleFactor / 100.0));
                                SimpleLogHelper.DebugInfo(@$"RDP Host: init Display set DesktopWidth = {width} * {(_primaryScaleFactor / 100.0):F3} = {_rdpClient.DesktopWidth},  DesktopHeight = {height} * {(_primaryScaleFactor / 100.0):F3} = {_rdpClient.DesktopHeight},     RdpControl.Width = {_rdpClient.Width}, RdpControl.Height = {_rdpClient.Height}");
                                if (_primaryScaleFactor > 100)
                                {
                                    // size compensation since https://github.com/1Remote/1Remote/issues/537
                                    int c = (_primaryScaleFactor % 100) switch
                                    {
                                        50 => 1,
                                        75 => 2,
                                        _ => 0
                                    };
                                    if (ColorAndBrushHelper.ColorIsTransparent(_rdpSettings.ColorHex) != true)
                                    {
                                        c *= 2;
                                    }
                                    if (c < _rdpClient.DesktopWidth && c < _rdpClient.DesktopHeight)
                                    {
                                        //_rdpClient.DesktopWidth -= c;
                                        _rdpClient.DesktopHeight -= c;
                                    }
                                    SimpleLogHelper.DebugInfo($"RDP Host: init Display set DesktopWidth = {_rdpClient.DesktopWidth},  DesktopHeight = {_rdpClient.DesktopHeight}");
                                }
                            }
                        }

                        break;
                    }
            }



            switch (_rdpSettings.RdpFullScreenFlag)
            {
                case ERdpFullScreenFlag.Disable:
                    base.CanFullScreen = false;
                    break;

                case ERdpFullScreenFlag.EnableFullAllScreens:
                    base.CanFullScreen = true;
                    ((IMsRdpClientNonScriptable5)_rdpClient.GetOcx()).UseMultimon = true;
                    break;
                case ERdpFullScreenFlag.EnableFullScreen:
                default:
                    base.CanFullScreen = true;
                    break;
            }

            #endregion Display

            // 2022.07.23 try to fix the rdp error code 4360, ref: https://forum.asg-rd.com/showthread.php?tid=11016&page=2
            _rdpClient.AdvancedSettings8.BitmapPersistence = 0;
            _rdpClient.AdvancedSettings8.CachePersistenceActive = 0;

            SimpleLogHelper.Debug($"RDP Host: Display init end: RDP.DesktopWidth = {_rdpClient.DesktopWidth}, RDP.DesktopHeight = {_rdpClient.DesktopHeight},");
        }

        private void RdpInitPerformance()
        {
            Debug.Assert(_rdpClient != null); if (_rdpClient == null) return;
            SimpleLogHelper.Debug("RDP Host: init Performance");

            #region Performance

            // if win11 disable BandwidthDetection, make a workaround for #437 to hide info button after OS Win11 22H2 to avoid app crash when click the info button on Win11
            // detail: https://github.com/1Remote/1Remote/issues/437
            // 20250126: removed due to https://github.com/1Remote/1Remote/issues/559 is fixed
            //try
            //{
            //    if (_1RM.Utils.WindowsApi.WindowsVersionHelper.IsWindows1122H2OrHigher()) // Win11 22H2
            //    {
            //        _rdpClient.AdvancedSettings9.BandwidthDetection = false;
            //    }
            //}
            //catch (Exception)
            //{
            //    // ignored
            //}

            // ref: https://docs.microsoft.com/en-us/windows/win32/termserv/imsrdpclientadvancedsettings-performanceflags
            int nDisplayPerformanceFlag = 0;
            if (_rdpSettings.DisplayPerformance != EDisplayPerformance.Auto)
            {
                // ref: https://docs.microsoft.com/en-us/windows/win32/termserv/imsrdpclientadvancedsettings7-networkconnectiontype
                // CONNECTION_TYPE_MODEM (1 (0x1)) Modem (56 Kbps)
                // CONNECTION_TYPE_BROADBAND_LOW (2 (0x2)) Low-speed broadband (256 Kbps to 2 Mbps) CONNECTION_TYPE_SATELLITE (3 (0x3)) Satellite (2 Mbps to 16 Mbps, with high latency)
                // CONNECTION_TYPE_BROADBAND_HIGH (4 (0x4)) High-speed broadband (2 Mbps to 10 Mbps) CONNECTION_TYPE_WAN (5 (0x5)) Wide area network (WAN) (10 Mbps or higher, with high latency)
                // CONNECTION_TYPE_LAN (6 (0x6)) Local area network (LAN) (10 Mbps or higher)
                //
                // This used to be hardcoded to 1 for every quality level, so picking "High" still told the
                // server the link was a 56K modem and it compressed and throttled accordingly.
                _rdpClient.AdvancedSettings8.NetworkConnectionType = _rdpSettings.DisplayPerformance switch
                {
                    EDisplayPerformance.Low => 1,     // MODEM
                    EDisplayPerformance.Middle => 4,  // BROADBAND_HIGH
                    EDisplayPerformance.High => 6,    // LAN
                    _ => 6,
                };
                switch (_rdpSettings.DisplayPerformance)
                {
                    case EDisplayPerformance.Auto:
                        break;

                    case EDisplayPerformance.Low:
                        // 8,16,24,32
                        _rdpClient.ColorDepth = 8;
                        nDisplayPerformanceFlag += 0x00000001;//TS_PERF_DISABLE_WALLPAPER;      Wallpaper on the desktop is not displayed.
                        nDisplayPerformanceFlag += 0x00000002;//TS_PERF_DISABLE_FULLWINDOWDRAG; Full-window drag is disabled; only the window outline is displayed when the window is moved.
                        nDisplayPerformanceFlag += 0x00000004;//TS_PERF_DISABLE_MENUANIMATIONS; Menu animations are disabled.
                        nDisplayPerformanceFlag += 0x00000008;//TS_PERF_DISABLE_THEMING ;       Themes are disabled.
                        nDisplayPerformanceFlag += 0x00000020;//TS_PERF_DISABLE_CURSOR_SHADOW;  No shadow is displayed for the cursor.
                        nDisplayPerformanceFlag += 0x00000040;//TS_PERF_DISABLE_CURSORSETTINGS; Cursor blinking is disabled.
                        break;

                    case EDisplayPerformance.Middle:
                        _rdpClient.ColorDepth = 16;
                        nDisplayPerformanceFlag += 0x00000001;//TS_PERF_DISABLE_WALLPAPER;      Wallpaper on the desktop is not displayed.
                        nDisplayPerformanceFlag += 0x00000002;//TS_PERF_DISABLE_FULLWINDOWDRAG; Full-window drag is disabled; only the window outline is displayed when the window is moved.
                        nDisplayPerformanceFlag += 0x00000004;//TS_PERF_DISABLE_MENUANIMATIONS; Menu animations are disabled.
                        nDisplayPerformanceFlag += 0x00000008;//TS_PERF_DISABLE_THEMING ;       Themes are disabled.
                        nDisplayPerformanceFlag += 0x00000020;//TS_PERF_DISABLE_CURSOR_SHADOW;  No shadow is displayed for the cursor.
                        nDisplayPerformanceFlag += 0x00000040;//TS_PERF_DISABLE_CURSORSETTINGS; Cursor blinking is disabled.
                        nDisplayPerformanceFlag += 0x00000080;//TS_PERF_ENABLE_FONT_SMOOTHING;        Enable font smoothing.
                        nDisplayPerformanceFlag += 0x00000100;//TS_PERF_ENABLE_DESKTOP_COMPOSITION ;  Enable desktop composition.

                        break;

                    case EDisplayPerformance.High:
                        _rdpClient.ColorDepth = 32;
                        nDisplayPerformanceFlag += 0x00000080;//TS_PERF_ENABLE_FONT_SMOOTHING;        Enable font smoothing.
                        nDisplayPerformanceFlag += 0x00000100;//TS_PERF_ENABLE_DESKTOP_COMPOSITION ;  Enable desktop composition.
                        break;
                }
            }
            SimpleLogHelper.Debug("RdpInit: DisplayPerformance = " + _rdpSettings.DisplayPerformance + ", flag = " + Convert.ToString(nDisplayPerformanceFlag, 2));
            _rdpClient.AdvancedSettings9.PerformanceFlags = nDisplayPerformanceFlag;

            #endregion Performance
        }

        private void RdpInitGateway()
        {
            Debug.Assert(_rdpClient != null); if (_rdpClient == null) return;
            SimpleLogHelper.Debug("RDP Host: init Gateway");

            #region Gateway

            // Specifies whether Remote Desktop Gateway (RD Gateway) is supported.
            if (_rdpClient.TransportSettings.GatewayIsSupported != 0
                && _rdpSettings.GatewayMode != EGatewayMode.DoNotUseGateway)
            {
                // https://docs.microsoft.com/en-us/windows/win32/termserv/imsrdpclienttransportsettings-gatewayprofileusagemethod
                _rdpClient.TransportSettings2.GatewayProfileUsageMethod = 1; // Use explicit settings, as specified by the user.

                // ref: https://docs.microsoft.com/en-us/windows/win32/termserv/imsrdpclienttransportsettings-gatewayusagemethod
                _rdpClient.TransportSettings.GatewayUsageMethod = _rdpSettings.GatewayMode switch
                {
                    EGatewayMode.UseTheseGatewayServerSettings =>
                    1 // 1 : Always use an RD Gateway server. In the RDC client UI, the Bypass RD Gateway server for local addresses check box is cleared.
                    ,
                    EGatewayMode.AutomaticallyDetectGatewayServerSettings =>
                    2 // 2 : Use an RD Gateway server if a direct connection cannot be made to the RD Session Host server. In the RDC client UI, the Bypass RD Gateway server for local addresses check box is selected.
                    ,
                    _ => throw new ArgumentOutOfRangeException()
                };

                _rdpClient.TransportSettings2.GatewayHostname = _rdpSettings.GatewayHostName;
                //_rdpClient.TransportSettings2.GatewayDomain = "XXXXX";

                // ref: https://docs.microsoft.com/en-us/windows/win32/termserv/imsrdpclienttransportsettings-gatewaycredssource
                // TSC_PROXY_CREDS_MODE_USERPASS (0): Use a password (NTLM) as the authentication method for RD Gateway.
                // TSC_PROXY_CREDS_MODE_SMARTCARD (1): Use a smart card as the authentication method for RD Gateway.
                // TSC_PROXY_CREDS_MODE_ANY (4): Use any authentication method for RD Gateway.
                switch (_rdpSettings.GatewayLogonMethod)
                {
                    case EGatewayLogonMethod.SmartCard:
                        _rdpClient.TransportSettings.GatewayCredsSource = 1; // TSC_PROXY_CREDS_MODE_SMARTCARD
                        break;

                    case EGatewayLogonMethod.Password:
                        _rdpClient.TransportSettings.GatewayCredsSource = 0; // TSC_PROXY_CREDS_MODE_USERPASS
                        _rdpClient.TransportSettings2.GatewayUsername = _rdpSettings.GatewayUserName;
                        _rdpClient.TransportSettings2.GatewayPassword = _rdpSettings.GatewayPassword;
                        break;

                    default:
                        _rdpClient.TransportSettings.GatewayCredsSource = 4; // TSC_PROXY_CREDS_MODE_ANY
                        break;
                }

                _rdpClient.TransportSettings2.GatewayCredSharing = 0;
            }

            #endregion Gateway
        }
    }
}
