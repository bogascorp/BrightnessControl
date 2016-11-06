using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace BrightnessControl
{
    internal enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_INVALID_STATE = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    internal enum WindowCompositionAttribute
    {
        // ...
        WCA_ACCENT_POLICY = 19
        // ...
    }

    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
        const int ERROR_GEN_FAILURE = 0x1F;
        static TimeSpan HIDING_DELAY = TimeSpan.FromSeconds(5);
        static TimeSpan CHANGE_DELAY = TimeSpan.FromMilliseconds(250);

        DispatcherTimeout brightnessTimeout, hideTimeout;
        DisplayConfiguration.PHYSICAL_MONITOR[] physicalMonitors;
        HotkeyManager hotkeys;
        System.Windows.Forms.NotifyIcon notifyIcon;
        int[] brightnessVariations = new int[5] { 0, 25, 50, 75, 100 };
        int variationPos = 4;

        public MainWindow()
        {
            InitializeComponent();
            physicalMonitors = DisplayConfiguration.GetPhysicalMonitors(DisplayConfiguration.GetCurrentMonitor());
            notifyIcon = new System.Windows.Forms.NotifyIcon();
            notifyIcon.Text = "Brightness";
            using (Stream stream = Application.GetResourceStream(new Uri("brightness.ico", UriKind.Relative)).Stream)
            {
                notifyIcon.Icon = new System.Drawing.Icon(stream);
            }
            notifyIcon.Visible = true;
            notifyIcon.Click += NotifyIcon_Click;
            notifyIcon.ContextMenu = new System.Windows.Forms.ContextMenu();
            notifyIcon.ContextMenu.MenuItems.Add(new System.Windows.Forms.MenuItem("Settings", (sender, e) => SettingsWindow.ShowInstance()));
            notifyIcon.ContextMenu.MenuItems.Add(new System.Windows.Forms.MenuItem("Quit", (sender, e) => Close()));
            brightnessSlider.Value = DisplayConfiguration.GetMonitorBrightness(physicalMonitors[0]) * 100;
            brightnessToggler.Click += BrightnessToggler_Click;
            LostFocus += MainWindow_LostFocus;
        }

        private void MainWindow_LostFocus(object sender, RoutedEventArgs e)
        {
            Visibility = Visibility.Hidden;
        }

        private void BrightnessToggler_Click(object sender, RoutedEventArgs e)
        {
            variationPos++;
            if (variationPos > 4)
                variationPos = 0;
                    brightnessSlider.Value = brightnessVariations[variationPos];

        }

        private void NotifyIcon_Click(object sender, EventArgs e)
        {
            if (brightnessTimeout != null)
            {
                brightnessTimeout.Cancel();
            }
            grid_toggler.Visibility = Visibility.Visible;
            grid_slider.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Visible;
            Left = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea.Width - 264 - 64;
            Top = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea.Height - 148;
        }

        internal void EnableBlur()
        {
            var windowHelper = new WindowInteropHelper(this);

            var accent = new AccentPolicy();
            accent.AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND;

            var accentStructSize = Marshal.SizeOf(accent);

            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData();
            data.Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY;
            data.SizeOfData = accentStructSize;
            data.Data = accentPtr;

            SetWindowCompositionAttribute(windowHelper.Handle, ref data);

            Marshal.FreeHGlobal(accentPtr);
        }

        public void RegisterHotkeys()
        {
            hotkeys = new HotkeyManager(this);
            try
            {
                hotkeys.Register(0, (ModifierKeys)Properties.Settings.Default.VolumeDownModifiers, (Key)Properties.Settings.Default.VolumeDownKey);
                hotkeys.Register(1, (ModifierKeys)Properties.Settings.Default.VolumeUpModifiers, (Key)Properties.Settings.Default.VolumeUpKey);
                hotkeys.Pressed += hotkeys_Pressed;
            }
            catch (HotkeyAlreadyRegisteredException e)
            {
                MessageBox.Show(string.Format("Failed to register {0} + {1} as a Hotkey.\nPlease try selecting something different in the settings.", e.Modifiers, e.Key), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void UnregisterHotkeys()
        {
            hotkeys.Dispose();
        }

        private void ScheduleHiding()
        {
            if (hideTimeout != null)
            {
                hideTimeout.Cancel();
            }
            hideTimeout = new DispatcherTimeout(() => Visibility = Visibility.Hidden , HIDING_DELAY);
        }

        private void RefreshBrightness()
        {
            foreach (DisplayConfiguration.PHYSICAL_MONITOR physicalMonitor in physicalMonitors)
            {
                try
                {
                    double newBrightness = brightnessSlider.Value;
                    if (newBrightness >= 0 && newBrightness < 25)
                        variationPos = 0;
                    else
                        if (newBrightness >= 25 && newBrightness < 50)
                        variationPos = 1;
                    else
                        if (newBrightness >= 50 && newBrightness < 75)
                        variationPos = 2;
                    else
                        if (newBrightness >= 75 && newBrightness < 100)
                            variationPos = 3;
                    else
                        variationPos = 4;
                    brightnessToggler.Content = newBrightness + "%";

                    DisplayConfiguration.SetMonitorBrightness(physicalMonitor, newBrightness / brightnessSlider.Maximum);
                }
                catch (Win32Exception e_)
                {
                    // LG Flatron W2443T sometimes causes ERROR_GEN_FAILURE when rapidly changing brightness or contrast
                    if (e_.NativeErrorCode == ERROR_GEN_FAILURE)
                    {
                        Debug.WriteLine("ERROR_GEN_FAILURE while setting brightness, rescheduling");
                        brightnessTimeout = new DispatcherTimeout(RefreshBrightness, CHANGE_DELAY);
                        break;
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }


        private void brightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (brightnessTimeout != null)
            {
                brightnessTimeout.Cancel();
            }
            brightnessTimeout = new DispatcherTimeout(RefreshBrightness, CHANGE_DELAY);
            ScheduleHiding();
        }


        private void hotkeys_Pressed(object sender, PressedEventArgs e)
        {
            Left = 64;
            Top = 64;
            grid_toggler.Visibility = Visibility.Collapsed;
            grid_slider.Visibility = Visibility.Visible;
            Visibility = Visibility.Visible;
            switch (e.Id)
            {
                case 0:
                    brightnessSlider.Value -= 5;
                    break;
                case 1:
                    brightnessSlider.Value += 5;
                    break;
            }
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            grid_toggler.Visibility = Visibility.Collapsed;
            grid_slider.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Hidden;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Visibility = Visibility.Hidden;
            RegisterHotkeys();
            EnableBlur();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            UnregisterHotkeys();

            DisplayConfiguration.DestroyPhysicalMonitors(physicalMonitors);
        }
    }
}
