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
    internal static class AxMsRdpClient9NotSafeForScriptingExAdd
    {
        public static void SetExtendedProperty(this AxHost axHost, string propertyName, object value)
        {
            try
            {
                ((IMsRdpExtendedSettings)axHost.GetOcx()).set_Property(propertyName, ref value);
            }
            catch (Exception e)
            {
                SimpleLogHelper.Error(e);
            }
        }
    }

    internal class AxMsRdpClient9NotSafeForScriptingEx : AxMSTSCLib.AxMsRdpClient9NotSafeForScripting
    {
        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            // Falsifying the response to WM_GETOBJECT to resolve issue #1053 that the RDP client to crash when using the word capture feature
            if (m.Msg == Win32Api.WM_GETOBJECT)
            {
                m.Result = -1; // Setting it to IntPtr.Zero (or 0) did not resolve the issue.
                               // Setting it experimentally to 1 or -1 solved the problem, though I cannot explain why.
                return;
            }
            // Fix for the missing focus issue on the rdp client component
            if (m.Msg == Win32Api.WM_MOUSEACTIVATE)
            {
                if (!this.ContainsFocus)
                {
                    SimpleLogHelper.Debug("AxMsRdpClient9NotSafeForScriptingEx.WndProc: Focus");
                    this.Focus();
                }
            }
            base.WndProc(ref m);
        }
    }


    public sealed partial class AxMsRdpClient09Host : HostBase, IDisposable
    {
        private AxMsRdpClient9NotSafeForScriptingEx? _rdpClient = null;
        //private readonly DataSourceBase? _dataSource;
        private readonly RDP _rdpSettings;
        /// <summary>
        /// system scale factor, 100 = 100%, 200 = 200%
        /// </summary>
        private uint _primaryScaleFactor = 100;
        /// <summary>
        /// if has connected, then rdp can resize
        /// </summary>
        private bool _flagHasConnected = false;
        /// <summary>
        /// if has ever connected successfully, then enabled auto reconnect feature
        /// </summary>
        private bool _flagHasEverConnected = false;


        private readonly System.Timers.Timer _loginResizeTimer; // timer for login resize, to fix the issue that the rdp client size is not correct when login
        private DateTime _lastLoginTime = DateTime.MinValue;

        private readonly object _rdpClientDisposeLock = new object();
        /// <summary>Bumps on each Conn()/Dispose so a delayed Connect() from a previous wait cannot fire into a disposed or replaced ActiveX control.</summary>
        private int _connectEpoch;


        public static AxMsRdpClient09Host Create(RDP rdp, int width = 0, int height = 0)
        {
            AxMsRdpClient09Host? view = null;
            Execute.OnUIThreadSync(() =>
            {
                view = new AxMsRdpClient09Host(rdp, width, height);
            });
            return view!;
        }

        private AxMsRdpClient09Host(RDP rdp, int width = 0, int height = 0) : base(rdp, true)
        {
            InitializeComponent();


            MenuItems.Add(new System.Windows.Controls.Separator());
            MenuItems.Add(new System.Windows.Controls.MenuItem()
            {
                Header = "Ctrl + Alt + Del",
                Command = new RelayCommand((o) =>
                {
                    if (_rdpClient != null)
                    {
                        _rdpClient.Focus();
                        new MsRdpClientNonScriptableWrapper(_rdpClient.GetOcx()).SendKeys(
                            new int[] { 0x1d, 0x38, 0x53, 0x53, 0x38, 0x1d },
                            new bool[] { false, false, false, true, true, true, });
                    }
                }, o => HasConnected)
            });

            GridMessageBox.Visibility = Visibility.Collapsed;
            GridLoading.Visibility = Visibility.Visible;

            _rdpSettings = rdp;

            _loginResizeTimer = new Timer(300) { Enabled = false, AutoReset = false };
            _loginResizeTimer.Elapsed += (sender, args) =>
            {
                _loginResizeTimer.Stop();
                try
                {
                    var nw = (uint)(_rdpClient?.Width ?? 0);
                    var nh = (uint)(_rdpClient?.Height ?? 0);
                    // tip: the control default width is 288
                    if (_rdpClient?.DesktopWidth > nw
                        || _rdpClient?.DesktopHeight > nh)
                    {
                        SimpleLogHelper.DebugInfo($@"_loginResizeTimer start run... {_rdpClient?.DesktopWidth}, {nw}, {_rdpClient?.DesktopHeight}, {nh}");
                        ReSizeRdpToControlSize();
                    }
                    else
                    {
                        _lastLoginTime = DateTime.MinValue;
                    }
                }
                finally
                {
                    if (DateTime.Now < _lastLoginTime.AddMinutes(1))
                    {
                        _loginResizeTimer.Start();
                    }
                    else
                    {
                        SimpleLogHelper.DebugWarning($@"_loginResizeTimer stop");
                    }
                }
            };

            InitRdp(width, height);
            GlobalEventHelper.OnScreenResolutionChanged += OnScreenResolutionChanged;
        }

        ~AxMsRdpClient09Host()
        {
            SimpleLogHelper.Debug($"Release {this.GetType().Name}({this.GetHashCode()})");
            Dispose();
        }

        public void Dispose()
        {
            SimpleLogHelper.Debug($"Disposing {this.GetType().Name}({this.GetHashCode()})");
            _resizeEndTimer?.Dispose();
            _loginResizeTimer?.Dispose();
            System.Threading.Interlocked.Increment(ref _connectEpoch);
            RdpClientDispose();
            SimpleLogHelper.Debug($"Dispose done {this.GetType().Name}({this.GetHashCode()})");
        }

        private void OnScreenResolutionChanged()
        {
            lock (_rdpClientDisposeLock)
            {
                // 全屏模式下客户端机器发生了屏幕分辨率改变，则将RDP还原到窗口模式（仿照 MSTSC 的逻辑）
                if (_rdpClient?.FullScreen == true)
                {
                    Execute.OnUIThread(() =>
                    {
                        _rdpClient.FullScreen = false;
                    });
                }
            }
        }

        private void CreateRdpClient()
        {
            lock (_rdpClientDisposeLock)
            {
                _rdpClient = new AxMsRdpClient9NotSafeForScriptingEx();

                SimpleLogHelper.Debug("RDP Host: init new AxMsRdpClient9NotSafeForScriptingEx()");

                ((System.ComponentModel.ISupportInitialize)(_rdpClient)).BeginInit();
                _rdpClient.Dock = DockStyle.Fill;
                _rdpClient.Enabled = true;
                _rdpClient.BackColor = Color.Black;
                // set call back
                _rdpClient.OnRequestGoFullScreen += (sender, args) =>
                {
                    SimpleLogHelper.Debug("RDP Host:  OnRequestGoFullScreen");
                    OnGoToFullScreenRequested();
                };
                _rdpClient.OnRequestLeaveFullScreen += (sender, args) =>
                {
                    SimpleLogHelper.Debug("RDP Host:  OnRequestLeaveFullScreen");
                    OnConnectionBarRestoreWindowCall();
                };
                _rdpClient.OnRequestContainerMinimize += (sender, args) =>
                {
                    SimpleLogHelper.Debug("RDP Host:  OnRequestContainerMinimize");
                    if (ParentWindow is FullScreenWindowView)
                    {
                        ParentWindow.WindowState = WindowState.Minimized;
                    }
                };
                _rdpClient.OnDisconnected += OnRdpClientDisconnected;
                _rdpClient.OnConfirmClose += (sender, args) =>
                {
                    // invoke in the full screen mode.
                    SimpleLogHelper.Debug("RDP Host:  RdpOnConfirmClose");
                    base.OnClosed?.Invoke(base.ConnectionId);
                };
                _rdpClient.OnConnected += OnRdpClientConnected;
                _rdpClient.OnLoginComplete += OnRdpClientLoginComplete;
                ((System.ComponentModel.ISupportInitialize)(_rdpClient)).EndInit();
                RdpHost.Child = _rdpClient;

                SimpleLogHelper.Debug("RDP Host: init CreateControl();");
                _rdpClient.CreateControl();
            }
        }

        private void InitRdp(int width = 0, int height = 0, bool isReconnecting = false)
        {
            if (Status != ProtocolHostStatus.NotInit)
                return;
            try
            {
                Status = ProtocolHostStatus.Initializing;
                RdpClientDispose();
                CreateRdpClient();
                RdpInitServerInfo();
                RdpInitStatic();
                RdpInitConnBar();
                RdpInitRedirect();
                RdpInitDisplay(width, height, isReconnecting);
                RdpInitPerformance();
                RdpInitGateway();
                _rdpSettings.ApplyRdpControlAdditionalSettings(_rdpClient!);
                Status = ProtocolHostStatus.Initialized;
            }
            catch (Exception e)
            {
                GridMessageBox.Visibility = Visibility.Visible;
                TbMessageTitle.Visibility = Visibility.Collapsed;
                TbMessage.Text = e.Message;

                Status = ProtocolHostStatus.NotInit;
            }
        }

        #region Base Interface
        public override void Conn()
        {
            Debug.Assert(_rdpClient != null); if (_rdpClient == null) return;
            Dispatcher.Invoke(() =>
            {
                if (Status == ProtocolHostStatus.Connected || Status == ProtocolHostStatus.Connecting)
                {
                    return;
                }

                Status = ProtocolHostStatus.Connecting;
                GridLoading.Visibility = System.Windows.Visibility.Visible;
                RdpHost.Visibility = System.Windows.Visibility.Collapsed;
            });

            // Connect() is asynchronous: OnConnected is the only honest "Connected". Marking it here used
            // to make ReConn() think a still-handshaking session was already up. Wait for 3389 off the UI
            // thread first so a machine that just rebooted is given the same grace mstsc gives it.
            var epoch = System.Threading.Interlocked.Increment(ref _connectEpoch);
            _ = ConnectWhenEndpointReadyAsync(epoch);
        }

        private async Task ConnectWhenEndpointReadyAsync(int epoch)
        {
            try
            {
                await WaitForEndpointReadyAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                SimpleLogHelper.Debug($"RDP Host: wait-for-endpoint: {e.Message}");
            }

            await Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (epoch != _connectEpoch)
                        return;
                    if (_rdpClient == null)
                        return;
                    if (Status != ProtocolHostStatus.Connecting
                        && Status != ProtocolHostStatus.Initialized)
                        return;

                    Status = ProtocolHostStatus.Connecting;
                    GridLoading.Visibility = System.Windows.Visibility.Visible;
                    RdpHost.Visibility = System.Windows.Visibility.Collapsed;
                    _rdpClient.Connect();
                }
                catch (Exception e)
                {
                    GridMessageBox.Visibility = System.Windows.Visibility.Visible;
                    TbMessageTitle.Visibility = System.Windows.Visibility.Collapsed;
                    TbMessage.Text = e.Message;
                    Status = ProtocolHostStatus.Disconnected;
                }
            });
        }

        public override void Close()
        {
            this.Dispose();
            base.Close();
        }

        protected override void GoFullScreen()
        {
            if (_rdpSettings.RdpFullScreenFlag == ERdpFullScreenFlag.Disable
                || ParentWindow is not FullScreenWindowView
                || _rdpClient?.FullScreen == true)
            {
                return;
            }
            Debug.Assert(_rdpClient != null); if (_rdpClient == null) return;
            if (_rdpClient.FullScreen != true)
                _rdpClient.FullScreen = true; // this will invoke OnRequestGoFullScreen -> MakeNormal2FullScreen
        }

        public override ProtocolHostType GetProtocolHostType()
        {
            return ProtocolHostType.Native;
        }

        public override IntPtr GetHostHwnd()
        {
            return IntPtr.Zero;
        }

        public override bool CanResizeNow()
        {
            return Status == ProtocolHostStatus.Connected || Status == ProtocolHostStatus.Disconnected;
        }

        #endregion Base Interface


        #region WindowOnResizeEnd

        private readonly Timer _resizeEndTimer = new Timer(500) { Enabled = false, AutoReset = false };
        private readonly object _resizeEndLocker = new object();
        private bool _canAutoResizeByWindowSizeChanged = true;

        /// <summary>
        /// when tab window goes to min from max, base.SizeChanged invoke and size will get bigger, normal to min will not tiger this issue, don't know why.
        /// so stop resize when window status change to min until status restore.
        /// </summary>
        /// <param name="isEnable"></param>
        public override void ToggleAutoResize(bool isEnable)
        {
            lock (_resizeEndLocker)
            {
                _canAutoResizeByWindowSizeChanged = isEnable;
            }
        }

        private void ParentWindowResize_StartWatch()
        {
            lock (_resizeEndLocker)
            {
                _resizeEndTimer.Elapsed -= ResizeEndTimerOnElapsed;
                _resizeEndTimer.Elapsed += ResizeEndTimerOnElapsed;
                base.SizeChanged -= WindowSizeChanged;
                base.SizeChanged += WindowSizeChanged;
            }
        }

        private void ParentWindowResize_StopWatch()
        {
            lock (_resizeEndLocker)
            {
                _resizeEndTimer.Stop();
                _resizeEndTimer.Elapsed -= ResizeEndTimerOnElapsed;
                base.SizeChanged -= WindowSizeChanged;
            }
        }

        private uint _previousWidth = 0;
        private uint _previousHeight = 0;
        private void WindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ParentWindow?.WindowState != WindowState.Minimized
                && _canAutoResizeByWindowSizeChanged
                && this._rdpSettings.RdpWindowResizeMode == ERdpWindowResizeMode.AutoResize)
            {
                // start a timer to resize RDP after 500ms
                var nw = (uint)e.NewSize.Width;
                var nh = (uint)e.NewSize.Height;
                if (nw != _previousWidth || nh != _previousHeight)
                {
                    _previousWidth = (uint)e.NewSize.Width;
                    _previousHeight = (uint)e.NewSize.Height;
                    Execute.OnUIThreadSync(() =>
                    {
                        _loginResizeTimer.Stop();
                        _resizeEndTimer.Stop();
                        _resizeEndTimer.Start();
                    });
                }
            }
        }

        private void ResizeEndTimerOnElapsed(object? sender, ElapsedEventArgs e)
        {
            ReSizeRdpToControlSize();
        }

        #endregion WindowOnResizeEnd

        private void DisposeRdpClient()
        {
            lock (_rdpClientDisposeLock)
            {
                try
                {
                    if (_rdpClient is { IsDisposed: false })
                    {
                        _rdpClient.Dispose();
                    }
                    _rdpClient = null;
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Error($"Error disposing RDP client: {e}");
                }
            }
        }

        private void RdpClientDispose()
        {
            GlobalEventHelper.OnScreenResolutionChanged -= OnScreenResolutionChanged;
            try
            {
                // Use synchronous disposal to ensure the RDP client is fully disposed before continuing
                // This prevents race conditions where the client might be accessed or disposed multiple times
                Execute.OnUIThreadSync(DisposeRdpClient);
            }
            catch (Exception e)
            {
                SimpleLogHelper.Error($"Error scheduling RDP client disposal on UI thread: {e}");
                // 如果UI线程调度失败，直接处理
                DisposeRdpClient();
            }
            SimpleLogHelper.Debug("RDP Host: _rdpClient.Disposed.");
        }




        private const int MOUSE_RELEASE_WAIT_TIMEOUT_MS = 30 * 1000;
        private int _isReSizeRdpToControlSizeRunning = 0;
        /// <summary>
        /// set remote resolution to _rdpClient size if is AutoResize
        /// if focus == false, then set size only if new size != old size
        /// </summary>
        private void ReSizeRdpToControlSize()
        {
            if (!_flagHasConnected
                || _rdpClient?.FullScreen != false
                || _rdpSettings.RdpWindowResizeMode != ERdpWindowResizeMode.AutoResize) return;

            // This used to be a static field guarded by lock(this): the guard was per instance while the
            // flag was shared, so concurrent sessions clobbered each other, and any exception left it stuck
            // at true forever, silently killing auto-resize for every session in the process.
            if (Interlocked.CompareExchange(ref _isReSizeRdpToControlSizeRunning, 1, 0) != 0)
            {
                SimpleLogHelper.Debug($@"ReSizeRdpToControlSize return by isReSizeRdpToControlSizeRunning == true");
                return;
            }

            Task.Factory.StartNew(() =>
            {
                try
                {
                    // Window drag and drop resize only after mouse button release, 当拖动最大化的窗口时，需检测鼠标按键释放后再调整分辨率，详见：https://github.com/1Remote/1Remote/issues/553
                    // Control.MouseButtons reads the input state straight from Win32. The Mouse.LeftButton
                    // check it replaces needed a blocking hop to the UI thread on every iteration, and the
                    // loop had no upper bound.
                    var waitedMs = 0;
                    while ((System.Windows.Forms.Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left)
                    {
                        if (waitedMs >= MOUSE_RELEASE_WAIT_TIMEOUT_MS)
                        {
                            SimpleLogHelper.Warning(@"RDP ReSizeRdpToControlSize: gave up waiting for the mouse button to be released");
                            break;
                        }
                        Thread.Sleep(100);
                        waitedMs += 100;
                    }

                    var nw = (uint)(_rdpClient?.Width ?? 0);
                    var nh = (uint)(_rdpClient?.Height ?? 0);
                    // tip: the control default width is 288
                    if (_rdpClient?.DesktopWidth != nw
                        || _rdpClient?.DesktopHeight != nh)
                    {
                        SetRdpResolution(nw, nh, false);
                    }
                }
                catch (Exception e)
                {
                    SimpleLogHelper.Error(e);
                }
                finally
                {
                    Interlocked.Exchange(ref _isReSizeRdpToControlSizeRunning, 0);
                }
            });
        }


        private uint _lastScaleFactor = 0;
        /// <summary>
        /// if focus == false, then set size only if new size != old size
        /// </summary>
        private void SetRdpResolution(uint w, uint h, bool focus = false)
        {
            if (w <= 0 || h <= 0) return;

            lock (_resizeEndLocker)
            {
                if (_canAutoResizeByWindowSizeChanged == false) return;
            }

            _primaryScaleFactor = ScreenInfoEx.GetPrimaryScreenScaleFactor();
            var newScaleFactor = _primaryScaleFactor;
            if (this._rdpSettings is { IsScaleFactorFollowSystem: false, ScaleFactorCustomValue: { } })
                newScaleFactor = this._rdpSettings.ScaleFactorCustomValue ?? _primaryScaleFactor;
            bool needUpdate = focus
                         || _rdpClient?.DesktopWidth != w
                         || _rdpClient?.DesktopHeight != h
                         || newScaleFactor != _lastScaleFactor;
            if (newScaleFactor != 100)
            {
                // in this case we allow 1pix error
                needUpdate = focus
                        || Math.Abs((int)(_rdpClient?.DesktopWidth ?? 0) - (int)w) > 1
                        || Math.Abs((int)(_rdpClient?.DesktopHeight ?? 0) - (int)h) > 1
                        || newScaleFactor != _lastScaleFactor;
            }
            SimpleLogHelper.Debug($@"SetRdpResolution needUpdate = {needUpdate}, UpdateSessionDisplaySettings, by: W = {_rdpClient?.DesktopWidth} -> {w}, H = {_rdpClient?.DesktopHeight} -> {h}, ScaleFactor = {_lastScaleFactor} -> {newScaleFactor}, focus = {focus}");
            if (needUpdate)
                Execute.OnUIThreadSync(() =>
                {
                    try
                    {
                        _lastScaleFactor = newScaleFactor;
                        _rdpClient?.UpdateSessionDisplaySettings(w, h, w, h, 0, newScaleFactor, 100);
                    }
                    catch (COMException)
                    {
                        // ignore error code 0x8000FFFF
                    }
                    catch (Exception e)
                    {
                        SimpleLogHelper.Error(e);
                    }
                });
        }

        private System.Drawing.Rectangle GetScreenSizeIfRdpIsFullScreen()
        {
            if (_rdpSettings.RdpFullScreenFlag == ERdpFullScreenFlag.EnableFullAllScreens)
            {
                LocalityConnectRecorder.RdpCacheUpdate(_rdpSettings.Id, true, -1);
                return ScreenInfoEx.GetAllScreensSize();
            }

            int screenIndex = LocalityConnectRecorder.RdpCacheGet(_rdpSettings.Id)?.FullScreenLastSessionScreenIndex ?? -1;
            if (screenIndex < 0
                || screenIndex >= System.Windows.Forms.Screen.AllScreens.Length)
            {
                screenIndex = this.ParentWindow != null ? ScreenInfoEx.GetCurrentScreen(this.ParentWindow).Index : ScreenInfoEx.GetCurrentScreenBySystemPosition(ScreenInfoEx.GetMouseSystemPosition()).Index;
            }
            LocalityConnectRecorder.RdpCacheUpdate(_rdpSettings.Id, true, screenIndex);
            return System.Windows.Forms.Screen.AllScreens[screenIndex].Bounds;
        }

        /// <summary>
        /// set the parent window of rdp, if parent window is FullScreenWindowView and it's loaded, go full screen
        /// </summary>
        /// <param name="value"></param>
        public override void SetParentWindow(WindowBase? value)
        {
            base.SetParentWindow(value);
            if (value is FullScreenWindowView && value.IsLoaded && value.IsClosed == false)
            {
                this.GoFullScreen();
            }
        }

        public override void FocusOnMe()
        {
            Execute.OnUIThread(() =>
            {
                // Kill logical focus
                FocusManager.SetFocusedElement(FocusManager.GetFocusScope(RdpHost), null);
                Keyboard.ClearFocus();
                this.Focus();
                RdpHost.Focus();
                if (_rdpClient is { } rdp)
                {
                    // try to fix https://github.com/1Remote/1Remote/issues/530, but failed
                    rdp.Focus();
                    //rdp.Show();
                    //rdp.Update();
                    //rdp.Refresh();
                    //rdp.BringToFront();
                }
            });
        }
    }
}