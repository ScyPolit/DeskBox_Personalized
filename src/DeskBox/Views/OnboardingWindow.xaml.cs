using System.Numerics;
using DeskBox.Helpers;
using DeskBox.Models;
using DeskBox.Platform;
using DeskBox.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using WinRT.Interop;

namespace DeskBox.Views;

public sealed partial class OnboardingWindow : Window
{
    internal const int CurrentOnboardingVersion = 3;
    private const int DesiredWindowWidth = 1040;
    private const int DesiredWindowHeight = 680;
    private const int MinWindowWidth = 700;
    private const int MinWindowHeight = 560;
    private const int WindowWorkAreaMargin = 80;
    private const int CompactLayoutThreshold = 860;
    private const int StepCount = 5;
    private static readonly UIntPtr OnboardingWindowSubclassId = new(0xD05C0B01);

    private readonly SettingsService _settingsService;
    private readonly LocalizationService _localizationService;
    private readonly AppWindow _appWindow;
    private readonly IntPtr _hWnd;

    private int _stepIndex;
    private bool _hasLoaded;
    private bool _isClosed;
    private bool _isAnimating;
    private bool _hasInitializedFeatureToggles;
    private bool _isSynchronizingFeatureToggles;
    private bool _hasToggledDuringSummonStep;
    private bool _hasCompletedSummonPractice;
    private bool _isCompactLayout;
    private Task _featureWidgetSelectionUpdateTask = Task.CompletedTask;
    private readonly Win32Helper.SubclassProc _windowSubclassProc;

    public OnboardingWindow(
        SettingsService settingsService,
        LocalizationService localizationService,
        int initialStep = 0)
    {
        _settingsService = settingsService;
        _localizationService = localizationService;
        _stepIndex = Math.Clamp(initialStep, 0, StepCount - 1);
        _windowSubclassProc = WindowSubclassProc;
        InitializeComponent();
        _localizationService.LanguageChanged += OnLanguageChanged;
        _settingsService.SettingsChanged += OnFeatureWidgetSettingsChanged;
        App.Current.OnboardingWidgetsVisibilityChanged += OnOnboardingWidgetsVisibilityChanged;

        WindowsCompatibilityService.ApplySafeBackdrop(this);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarHost);

        Title = localizationService.T("Onboarding.WindowTitle");
        _hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        AppBranding.ApplyWindowIcon(_appWindow);
        ResizeAndCenterForDisplay(windowId);
        InstallMinimumSizeHook();

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = false;
        }

        SizeChanged += (_, _) => ApplyResponsiveLayout();
        RootGrid.Loaded += (_, _) =>
        {
            _hasLoaded = true;
            ApplyResponsiveLayout();
            StepPips.SelectedPageIndex = _stepIndex;
            SetupStep(animate: false);
            ApplySceneState(_stepIndex, animate: false);
            StartSceneLoops(_stepIndex);
            PlaySceneEntrance();
        };

        RootGrid.ActualThemeChanged += (_, _) =>
        {
            SetupStep(animate: false);
            ApplySceneState(_stepIndex, animate: false);
        };

        Closed += (_, _) =>
        {
            _isClosed = true;
            WindowsCompatibilityService.ReleaseTitleBarExtensions(this, _appWindow);
            StopSceneLoops();
            RemoveMinimumSizeHook();
            _localizationService.LanguageChanged -= OnLanguageChanged;
            _settingsService.SettingsChanged -= OnFeatureWidgetSettingsChanged;
            App.Current.OnboardingWidgetsVisibilityChanged -= OnOnboardingWidgetsVisibilityChanged;
        };
    }

    public void RestartIntro(int stepIndex = 0)
    {
        if (!_hasLoaded)
        {
            return;
        }

        _stepIndex = Math.Clamp(stepIndex, 0, StepCount - 1);
        SetupStep(animate: false);
        ApplySceneState(_stepIndex, animate: false);
        StartSceneLoops(_stepIndex);
        PlaySceneEntrance();
    }

    // ════════════════════════════════════════════════════════════
    //  Window setup
    // ════════════════════════════════════════════════════════════

    private void ResizeAndCenterForDisplay(Microsoft.UI.WindowId windowId)
    {
        var workArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary).WorkArea;
        double scale = GetCurrentDpiScale();
        int desiredWidth = ToPhysicalPixels(DesiredWindowWidth, scale);
        int desiredHeight = ToPhysicalPixels(DesiredWindowHeight, scale);
        int minWidth = ToPhysicalPixels(MinWindowWidth, scale);
        int minHeight = ToPhysicalPixels(MinWindowHeight, scale);
        int workAreaMargin = ToPhysicalPixels(WindowWorkAreaMargin, scale);
        int width = Math.Clamp(desiredWidth, minWidth, Math.Max(minWidth, workArea.Width - workAreaMargin));
        int height = Math.Clamp(desiredHeight, minHeight, Math.Max(minHeight, workArea.Height - workAreaMargin));

        _appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        _appWindow.Move(new Windows.Graphics.PointInt32(
            workArea.X + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - height) / 2)));
    }

    private void ApplyResponsiveLayout()
    {
        double width = RootGrid.ActualWidth;
        double height = RootGrid.ActualHeight;
        if (width <= 0)
        {
            return;
        }

        bool compact = width < CompactLayoutThreshold || height < 560;
        _isCompactLayout = compact;

        // Narrower scene canvas column + tighter copy margins on small windows.
        RootGrid.ColumnDefinitions[0].Width = new GridLength(compact ? 0.36 : 0.42, GridUnitType.Star);
        var sideMargin = compact ? 26 : 44;
        TextScrollViewer.Margin = new Thickness(sideMargin, 0, sideMargin, 0);
        FooterNav.Margin = new Thickness(compact ? 26 : 44, 0, compact ? 26 : 44, compact ? 14 : 24);
    }

    // ════════════════════════════════════════════════════════════
    //  Step navigation
    // ════════════════════════════════════════════════════════════

    private FrameworkElement GetStepPanel(int index) => index switch
    {
        0 => Step1Panel,
        1 => Step2Panel,
        2 => Step3Panel,
        3 => Step4Panel,
        4 => Step5Panel,
        _ => Step1Panel
    };

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_stepIndex <= 0 || _isAnimating)
        {
            return;
        }

        NavigateToStep(_stepIndex - 1);
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isAnimating)
        {
            return;
        }

        if (_stepIndex < StepCount - 1)
        {
            NavigateToStep(_stepIndex + 1);
            return;
        }

        await CompleteOnboardingAsync();
    }

    private async Task CompleteOnboardingAsync()
    {
        NextButton.IsEnabled = false;
        BackButton.IsEnabled = false;
        SetFeatureTogglesEnabled(false);
        await _featureWidgetSelectionUpdateTask;
        _settingsService.Settings.HasCompletedOnboarding = true;
        _settingsService.Settings.CompletedOnboardingVersion = CurrentOnboardingVersion;
        _settingsService.Settings.OnboardingStepIndex = 0;
        await _settingsService.SaveAsync();
        Close();
    }

    private void NavigateToStep(int newStep)
    {
        if (newStep < 0 || newStep >= StepCount || newStep == _stepIndex || _isAnimating)
        {
            return;
        }

        _isAnimating = true;
        var currentPanel = GetStepPanel(_stepIndex);
        var newPanel = GetStepPanel(newStep);

        newPanel.Visibility = Visibility.Visible;

        // The scene stage stays mounted and morphs; only the text column swaps.
        ApplySceneState(newStep, animate: true);
        StartSceneLoops(newStep);
        PlayTextColumnSwap(currentPanel, newPanel);

        _stepIndex = newStep;
        _settingsService.Settings.OnboardingStepIndex = newStep;
        _settingsService.SaveDebounced(notifySubscribers: false);

        UpdateFooterState();
        SetupStep(animate: true);
    }

    /// <summary>
    /// Crossfades the copy column with element-property storyboards (Opacity +
    /// RenderTransform.Y): the outgoing panel fades while the incoming one
    /// fades in with a soft 6px rise — a real cross-dissolve with no blank
    /// gap. Storyboards are used instead of raw visual animations because the
    /// incoming panel was just uncollapsed and its visual can still be
    /// rebuilt under StartAnimation, silently dropping the animation and
    /// leaving the panel blank until the completion timeout snaps it on.
    /// </summary>
    private void PlayTextColumnSwap(
        FrameworkElement outgoing,
        FrameworkElement incoming)
    {
        if (!WindowsCompatibilityService.AreAnimationsEnabled)
        {
            outgoing.Visibility = Visibility.Collapsed;
            incoming.Opacity = 1;
            incoming.RenderTransform = null;
            _isAnimating = false;
            return;
        }

        outgoing.IsHitTestVisible = false;
        incoming.IsHitTestVisible = true;
        incoming.Opacity = 0;
        incoming.RenderTransform = new TranslateTransform { Y = 6 };

        var storyboard = new Storyboard();

        var fadeOut = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(fadeOut, outgoing);
        Storyboard.SetTargetProperty(fadeOut, nameof(UIElement.Opacity));

        var fadeIn = new DoubleAnimation
        {
            To = 1,
            BeginTime = TimeSpan.FromMilliseconds(60),
            Duration = new Duration(TimeSpan.FromMilliseconds(260)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fadeIn, incoming);
        Storyboard.SetTargetProperty(fadeIn, nameof(UIElement.Opacity));

        var riseIn = new DoubleAnimation
        {
            To = 0,
            BeginTime = TimeSpan.FromMilliseconds(60),
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(riseIn, incoming);
        Storyboard.SetTargetProperty(
            riseIn,
            "(UIElement.RenderTransform).(TranslateTransform.Y)");

        storyboard.Children.Add(fadeOut);
        storyboard.Children.Add(fadeIn);
        storyboard.Children.Add(riseIn);
        storyboard.Begin();

        int generation = ++_transitionGeneration;
        _ = CompleteSwapAsync(storyboard, outgoing, incoming, generation);
    }

    private int _transitionGeneration;

    private async Task CompleteSwapAsync(
        Storyboard storyboard,
        FrameworkElement outgoing,
        FrameworkElement incoming,
        int generation)
    {
        await Task.Delay(460);
        if (_isClosed || generation != _transitionGeneration)
        {
            return;
        }

        // Land base values first, then release the storyboard's hold so the
        // panels settle without a post-animation snap.
        outgoing.Visibility = Visibility.Collapsed;
        outgoing.Opacity = 1;
        outgoing.IsHitTestVisible = true;
        incoming.Opacity = 1;
        incoming.RenderTransform = null;
        storyboard.Stop();
        _isAnimating = false;
    }

    // ════════════════════════════════════════════════════════════
    //  Step setup dispatch
    // ════════════════════════════════════════════════════════════

    private void SetupStep(bool animate)
    {
        if (!animate)
        {
            for (int index = 0; index < StepCount; index++)
            {
                var panel = GetStepPanel(index);
                panel.Visibility = index == _stepIndex
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                panel.Opacity = 1;
                panel.RenderTransform = null;
                panel.IsHitTestVisible = true;
            }
        }

        switch (_stepIndex)
        {
            case 2:
                SetupSummonStep();
                break;
            case 4:
                SetupFeatureStep();
                break;
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Footer
    // ════════════════════════════════════════════════════════════

    private void UpdateFooterState()
    {
        bool canGoBack = _stepIndex > 0;
        BackButton.Visibility = canGoBack ? Visibility.Visible : Visibility.Collapsed;
        BackButton.IsEnabled = canGoBack;
        ToolTipService.SetToolTip(BackButton, _localizationService.T("Onboarding.Back"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            BackButton, _localizationService.T("Onboarding.Back"));
        NextButton.Content = _stepIndex == StepCount - 1
            ? _localizationService.T("Onboarding.Start")
            : _localizationService.T("Onboarding.Next");
        NextButton.IsEnabled = true;
        UpdateProgressDots();
    }

    private void UpdateProgressDots()
    {
        if (StepPips.SelectedPageIndex != _stepIndex)
        {
            StepPips.SelectedPageIndex = _stepIndex;
        }
    }

    private void StepPips_SelectionChanged(
        PipsPager sender,
        PipsPagerSelectedIndexChangedEventArgs args)
    {
        int target = sender.SelectedPageIndex;
        if (target == _stepIndex || _isAnimating)
        {
            return;
        }

        NavigateToStep(target);
    }

    // ════════════════════════════════════════════════════════════
    //  Localization
    // ════════════════════════════════════════════════════════════

    private void OnLanguageChanged()
    {
        Title = _localizationService.T("Onboarding.WindowTitle");
        Localized.RefreshAll(_localizationService);
        SetupStep(animate: false);
        UpdateFooterState();
        RefreshSummonStatus();
    }

    // ════════════════════════════════════════════════════════════
    //  Visual helpers
    // ════════════════════════════════════════════════════════════

    internal static Microsoft.UI.Composition.Visual GetVisual(UIElement element)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        // Scale/Rotation animations pivot around CenterPoint, whose default
        // (0,0) makes press/pop keyframes drift toward the top-left — anchor
        // every scene element at its own center.
        var size = element.ActualSize;
        visual.CenterPoint = new Vector3(size.X / 2f, size.Y / 2f, 0f);
        return visual;
    }

    // ════════════════════════════════════════════════════════════
    //  Minimum-size window subclass
    // ════════════════════════════════════════════════════════════

    private void InstallMinimumSizeHook()
    {
        Win32Helper.SetWindowSubclass(_hWnd, _windowSubclassProc, OnboardingWindowSubclassId, UIntPtr.Zero);
    }

    private void RemoveMinimumSizeHook()
    {
        Win32Helper.RemoveWindowSubclass(_hWnd, _windowSubclassProc, OnboardingWindowSubclassId);
    }

    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    private IntPtr WindowSubclassProc(
        IntPtr hWnd,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr refData)
    {
        const uint WmGetMinMaxInfo = 0x0024;
        const uint WmNcDestroy = 0x0082;

        if (message == WmGetMinMaxInfo)
        {
            var minMaxInfo = System.Runtime.InteropServices.Marshal.PtrToStructure<MinMaxInfo>(lParam);
            double scale = GetCurrentDpiScale();
            minMaxInfo.MinTrackSize.X = Math.Max(minMaxInfo.MinTrackSize.X, ToPhysicalPixels(MinWindowWidth, scale));
            minMaxInfo.MinTrackSize.Y = Math.Max(minMaxInfo.MinTrackSize.Y, ToPhysicalPixels(MinWindowHeight, scale));
            System.Runtime.InteropServices.Marshal.StructureToPtr(minMaxInfo, lParam, false);
            return IntPtr.Zero;
        }

        if (message == WmNcDestroy)
        {
            RemoveMinimumSizeHook();
        }

        return Win32Helper.DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private double GetCurrentDpiScale()
    {
        return Win32Helper.GetDpiScaleForWindow(_hWnd, RootGrid.XamlRoot);
    }

    private static int ToPhysicalPixels(int logicalPixels, double scale)
    {
        return (int)Math.Round(logicalPixels * scale);
    }
}
