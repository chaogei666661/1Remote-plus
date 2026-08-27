using System;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json;
using _1RM.Model.Protocol.Base;
using _1RM.Service;
using _1RM.Utils.RdpFile;
using Shawn.Utils;
using System.Collections.Generic;
using _1RM.Service.Locality;
using _1RM.Utils;
using Shawn.Utils.Wpf;
using MSTSCLib;
using System.Reflection;
using AxMSTSCLib;
using _1RM.View.Host.ProtocolHosts;
using System.Windows.Forms;

namespace _1RM.Model.Protocol
{
    public enum ERdpWindowResizeMode
    {
        AutoResize = 0,
        Stretch = 1,
        Fixed = 2,
        StretchFullScreen = 3,
        FixedFullScreen = 4,
    }

    public enum ERdpFullScreenFlag
    {
        Disable = 0,
        EnableFullScreen = 1,
        EnableFullAllScreens = 2,
    }

    public enum EDisplayPerformance
    {
        /// <summary>
        /// Auto judge(by connection speed)
        /// </summary>
        Auto = 0,

        /// <summary>
        /// Low(8bit color with no feature support)
        /// </summary>
        Low = 1,

        /// <summary>
        /// Middle(16bit color with only font smoothing and desktop composition)
        /// </summary>
        Middle = 2,

        /// <summary>
        /// High(32bit color with full features support)
        /// </summary>
        High = 3,
    }

    public enum EGatewayMode
    {
        AutomaticallyDetectGatewayServerSettings = 0,
        UseTheseGatewayServerSettings = 1,
        DoNotUseGateway = 2,
    }

    public enum EGatewayLogonMethod
    {
        Password = 0,
        SmartCard = 1,
    }


    public enum EAudioRedirectionMode
    {
        RedirectToLocal = 0,
        LeaveOnRemote = 1,
        Disabled = 2,
    }

    public enum EAudioQualityMode
    {
        Dynamic = 0,
        Medium = 1,
        High = 2,
    }

    public class RdpLocalSetting
    {
        public DateTime LastUpdateTime { get; set; } = DateTime.MinValue;
        public bool FullScreenLastSessionIsFullScreen { get; set; } = false;
        public int FullScreenLastSessionScreenIndex { get; set; } = -1;
    }

    public class RdpControlAdditionalSetting
    {
        public string Name { get; set; } = "";
        public string? Value { get; set; } = "";
        public string ValueType { get; set; } = nameof(Int32);
        public string Description { get; set; } = "";
        public string HelpHrl { get; set; } = "";

        public T? GetValue<T>()
        {
            if (string.IsNullOrEmpty(Value))
                return default;
            return (T)Convert.ChangeType(Value, typeof(T));
        }
    }

    // ReSharper disable once InconsistentNaming
    // Partial, in the same way SessionControlService is: RDP.AdditionalSettings.cs holds the free-text
    // settings box for the ActiveX control, RDP.RdpFile.cs the .rdp import and export.
    public sealed partial class RDP : ProtocolBaseWithAddressPortUserPwd
    {
        public static string ProtocolName = "RDP";
        public RDP() : base(ProtocolName, "RDP.V1", "RDP")
        {
            base.Port = "3389";
            base.UserName = "Administrator";
        }

        private bool? _isAdministrativePurposes = false;
        public bool? IsAdministrativePurposes
        {
            get => _isAdministrativePurposes;
            set => SetAndNotifyIfChanged(ref _isAdministrativePurposes, value);
        }

        private string _domain = "";
        public string Domain
        {
            get => _domain;
            set => SetAndNotifyIfChanged(ref _domain, value);
        }

        private string _loadBalanceInfo = "";
        public string LoadBalanceInfo
        {
            get => _loadBalanceInfo;
            set => SetAndNotifyIfChanged(ref _loadBalanceInfo, value);
        }

        #region Display

        private ERdpFullScreenFlag? _rdpFullScreenFlag = ERdpFullScreenFlag.EnableFullScreen;
        public ERdpFullScreenFlag? RdpFullScreenFlag
        {
            get => _rdpFullScreenFlag;
            set
            {
                SetAndNotifyIfChanged(ref _rdpFullScreenFlag, value);
                switch (value)
                {
                    case ERdpFullScreenFlag.EnableFullAllScreens:
                        IsConnWithFullScreen = true;
                        if (RdpWindowResizeMode == ERdpWindowResizeMode.Fixed)
                            RdpWindowResizeMode = ERdpWindowResizeMode.FixedFullScreen;
                        if (RdpWindowResizeMode == ERdpWindowResizeMode.Stretch)
                            RdpWindowResizeMode = ERdpWindowResizeMode.StretchFullScreen;
                        break;

                    case ERdpFullScreenFlag.Disable:
                        IsConnWithFullScreen = false;
                        if (RdpWindowResizeMode == ERdpWindowResizeMode.FixedFullScreen)
                            RdpWindowResizeMode = ERdpWindowResizeMode.Fixed;
                        if (RdpWindowResizeMode == ERdpWindowResizeMode.StretchFullScreen)
                            RdpWindowResizeMode = ERdpWindowResizeMode.Stretch;
                        break;

                    case ERdpFullScreenFlag.EnableFullScreen:
                    default:
                        break;
                }
            }
        }

        private bool? _isConnWithFullScreen = false;
        public bool? IsConnWithFullScreen
        {
            get => _isConnWithFullScreen;
            set => SetAndNotifyIfChanged(ref _isConnWithFullScreen, value);
        }

        private bool? _isFullScreenWithConnectionBar = true;
        public bool? IsFullScreenWithConnectionBar
        {
            get => _isFullScreenWithConnectionBar;
            set
            {
                SetAndNotifyIfChanged(ref _isFullScreenWithConnectionBar, value);
                if (value == false)
                {
                    IsPinTheConnectionBarByDefault = false;
                }
            }
        }


        private bool? _isPinTheConnectionBarByDefault = false;
        public bool? IsPinTheConnectionBarByDefault
        {
            get => _isPinTheConnectionBarByDefault;
            set => SetAndNotifyIfChanged(ref _isPinTheConnectionBarByDefault, value);
        }

        private ERdpWindowResizeMode? _rdpWindowResizeMode = ERdpWindowResizeMode.AutoResize;
        public ERdpWindowResizeMode? RdpWindowResizeMode
        {
            get => _rdpWindowResizeMode;
            set
            {
                var tmp = value;
                if (RdpFullScreenFlag == ERdpFullScreenFlag.Disable)
                {
                    if (tmp == ERdpWindowResizeMode.FixedFullScreen)
                        tmp = ERdpWindowResizeMode.Fixed;
                    if (tmp == ERdpWindowResizeMode.StretchFullScreen)
                        tmp = ERdpWindowResizeMode.Stretch;
                }
                _rdpWindowResizeMode = tmp;
                RaisePropertyChanged(nameof(RdpWindowResizeMode));
            }
        }

        private int? _rdpWidth = 800;
        public int? RdpWidth
        {
            get => _rdpWidth;
            set => SetAndNotifyIfChanged(ref _rdpWidth, value);
        }

        private int? _rdpHeight = 600;
        public int? RdpHeight
        {
            get => _rdpHeight;
            set => SetAndNotifyIfChanged(ref _rdpHeight, value);
        }


        private bool? _isScaleFactorFollowSystem = true;
        public bool? IsScaleFactorFollowSystem
        {
            get => _isScaleFactorFollowSystem;
            set => SetAndNotifyIfChanged(ref _isScaleFactorFollowSystem, value);
        }

        private uint? _scaleFactorCustomValue = 100;
        public uint? ScaleFactorCustomValue
        {
            get => _scaleFactorCustomValue;
            set
            {
                uint? @new = value;
                if (value != null)
                {
                    @new = (uint)value;
                    if (@new > 300)
                        @new = 300;
                    if (@new < 100)
                        @new = 100;
                }
                SetAndNotifyIfChanged(ref _scaleFactorCustomValue, @new);
            }
        }


        private EDisplayPerformance? _displayPerformance = EDisplayPerformance.Auto;
        public EDisplayPerformance? DisplayPerformance
        {
            get => _displayPerformance;
            set => SetAndNotifyIfChanged(ref _displayPerformance, value);
        }

        #endregion Display

        #region resource switch

        private bool? _enableClipboard = true;
        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool? EnableClipboard
        {
            get => _enableClipboard;
            set => SetAndNotifyIfChanged(ref _enableClipboard, value);
        }

        private bool? _enableDiskDrives = false;
        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool? EnableDiskDrives
        {
            get => _enableDiskDrives;
            set => SetAndNotifyIfChanged(ref _enableDiskDrives, value);
        }

        private bool? _enableRedirectDrivesPlugIn = false;
        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool? EnableRedirectDrivesPlugIn
        {
            get => _enableRedirectDrivesPlugIn;
            set => SetAndNotifyIfChanged(ref _enableRedirectDrivesPlugIn, value);
        }



        private bool? _enableRedirectCameras = false;
        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool? EnableRedirectCameras
        {
            get => _enableRedirectCameras;
            set => SetAndNotifyIfChanged(ref _enableRedirectCameras, value);
        }



        private bool? _enableKeyCombinations = true;
        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool? EnableKeyCombinations
        {
            get => _enableKeyCombinations;
            set => SetAndNotifyIfChanged(ref _enableKeyCombinations, value);
        }


        private EAudioRedirectionMode? _audioRedirectionMode = EAudioRedirectionMode.RedirectToLocal;
        [DefaultValue(EAudioRedirectionMode.RedirectToLocal)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public EAudioRedirectionMode? AudioRedirectionMode
        {
            get => _audioRedirectionMode;
            set => SetAndNotifyIfChanged(ref _audioRedirectionMode, value);
        }

        private EAudioQualityMode? _audioQualityMode = EAudioQualityMode.Dynamic;
        [DefaultValue(EAudioQualityMode.Dynamic)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public EAudioQualityMode? AudioQualityMode
        {
            get => _audioQualityMode;
            set => SetAndNotifyIfChanged(ref _audioQualityMode, value);
        }


        private bool? _enableAudioCapture = false;
        [DefaultValue(false)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool? EnableAudioCapture
        {
            get => _enableAudioCapture;
            set => SetAndNotifyIfChanged(ref _enableAudioCapture, value);
        }


        private bool? _enablePorts = false;
        [DefaultValue(false)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool? EnablePorts
        {
            get => _enablePorts;
            set => SetAndNotifyIfChanged(ref _enablePorts, value);
        }


        private bool? _enablePrinters = false;
        [DefaultValue(false)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool? EnablePrinters
        {
            get => _enablePrinters;
            set => SetAndNotifyIfChanged(ref _enablePrinters, value);
        }


        private bool? _enableSmartCardsAndWinHello = false;
        [DefaultValue(false)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool? EnableSmartCardsAndWinHello
        {
            get => _enableSmartCardsAndWinHello;
            set => SetAndNotifyIfChanged(ref _enableSmartCardsAndWinHello, value);
        }

        #endregion resource switch

        #region MSTSC model

        private bool _mstscModeEnabled = false;
        public bool MstscModeEnabled
        {
            get => _mstscModeEnabled;
            set => SetAndNotifyIfChanged(ref _mstscModeEnabled, value);
        }


        private string _rdpFileAdditionalSettings = "";
        public string RdpFileAdditionalSettings
        {
            get => _rdpFileAdditionalSettings;
            set => SetAndNotifyIfChanged(ref _rdpFileAdditionalSettings, value);
        }



        #endregion

        #region Gateway

        private EGatewayMode? _gatewayMode = EGatewayMode.DoNotUseGateway;
        public EGatewayMode? GatewayMode
        {
            get => _gatewayMode;
            set => SetAndNotifyIfChanged(ref _gatewayMode, value);
        }


        private bool? _gatewayBypassForLocalAddress = true;
        public bool? GatewayBypassForLocalAddress
        {
            get => _gatewayBypassForLocalAddress;
            set => SetAndNotifyIfChanged(ref _gatewayBypassForLocalAddress, value);
        }


        private string _gatewayHostName = "";
        public string GatewayHostName
        {
            get => _gatewayHostName;
            set => SetAndNotifyIfChanged(ref _gatewayHostName, value);
        }


        private EGatewayLogonMethod? _gatewayLogonMethod = EGatewayLogonMethod.Password;
        public EGatewayLogonMethod? GatewayLogonMethod
        {
            get => _gatewayLogonMethod;
            set => SetAndNotifyIfChanged(ref _gatewayLogonMethod, value);
        }


        private string _gatewayUserName = "";
        public string GatewayUserName
        {
            get => _gatewayUserName;
            set => SetAndNotifyIfChanged(ref _gatewayUserName, value);
        }


        private string _gatewayPassword = "";
        public string GatewayPassword
        {
            get => _gatewayPassword;
            set => SetAndNotifyIfChanged(ref _gatewayPassword, value);
        }

        #endregion Gateway

        //private RdpLocalSetting _autoSetting = new RdpLocalSetting();
        //public RdpLocalSetting AutoSetting
        //{
        //    get => _autoSetting;
        //    private set => SetAndNotifyIfChanged(ref _autoSetting, value);
        //}

        public override bool IsOnlyOneInstance()
        {
            return true;
        }

        public override ProtocolBase? CreateFromJsonString(string jsonString)
        {
            try
            {
                return JsonConvert.DeserializeObject<RDP>(jsonString);
            }
            catch (Exception e)
            {
                SimpleLogHelper.Debug(e);
                return null;
            }
        }

        public override double GetListOrder()
        {
            return 0;
        }

        public override bool IsThisTimeConnWithFullScreen()
        {
            if (this.RdpFullScreenFlag == ERdpFullScreenFlag.EnableFullAllScreens
                || this.RdpFullScreenFlag != ERdpFullScreenFlag.Disable && this.IsConnWithFullScreen == true
                || this.RdpFullScreenFlag != ERdpFullScreenFlag.Disable && LocalityConnectRecorder.RdpCacheGet(this.Id)?.FullScreenLastSessionIsFullScreen == true)
                return true;
            return false;
        }













        public bool IsNeedRunWithMstsc()
        {
            if (MstscModeEnabled == true)
            {
                return true;
            }

            // for those people using 2+ monitors in different scale factors, we will try "mstsc.exe" instead of internal runner.
            // check if screens are in different scale factors
            int factor = (int)(new ScreenInfoEx(System.Windows.Forms.Screen.PrimaryScreen).ScaleFactor * 100);
            if (IsThisTimeConnWithFullScreen()
                && System.Windows.Forms.Screen.AllScreens.Length > 1
                && RdpFullScreenFlag == ERdpFullScreenFlag.EnableFullAllScreens
                && System.Windows.Forms.Screen.AllScreens.Select(screen => (int)(new ScreenInfoEx(screen).ScaleFactor * 100)).Any(factor2 => factor != factor2)
                )
            {
                return true;
            }


            return false;
        }


        #region IDataErrorInfo

        [JsonIgnore]
        public override string this[string columnName]
        {
            get
            {
                switch (columnName)
                {
                    case nameof(RdpControlAdditionalSettings):
                        {
                            var sss = SplitAdditionalSettings(RdpControlAdditionalSettings);
                            string message = "";
                            foreach (var tuple in sss)
                            {
                                if (!string.IsNullOrWhiteSpace(tuple.Item3))
                                {
                                    message += tuple.Item3 + "\n";
                                }
                                //else if (GetRdpControlAdditionalSettingKeys().Any(x=>x.StartsWith(tuple.Item1+":")) == false)
                                //{
                                //    message += $"{tuple.Item1}: key is not supported\n";
                                //}
                            }
                            return message;
                        }
                    default:
                        {
                            return base[columnName];
                        }
                }
            }
        }
        #endregion
    }
}