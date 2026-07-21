using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Button = Avalonia.Controls.Button;
using Brushes = Avalonia.Media.Brushes;
using CheckBox = Avalonia.Controls.CheckBox;
using Color = Avalonia.Media.Color;
using ComboBox = Avalonia.Controls.ComboBox;
using Control = Avalonia.Controls.Control;
using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using NumericUpDown = Avalonia.Controls.NumericUpDown;
using Orientation = Avalonia.Layout.Orientation;
using Point = Avalonia.Point;
using ProgressBar = Avalonia.Controls.ProgressBar;
using TextBox = Avalonia.Controls.TextBox;
using ToolTip = Avalonia.Controls.ToolTip;
using VerticalAlignment = Avalonia.Layout.VerticalAlignment;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using AvaloniaImage = Avalonia.Controls.Image;

namespace SmartShellCleanerAvalonia;

public sealed class MainWindow : Window
{
    private sealed class ModalWindow : Window
    {
        public ModalWindow()
        {
            KeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                Close();
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public IntPtr Handle;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);

    private const uint FlashTray = 2;
    private const uint FlashUntilForeground = 12;
    private const string CleaningAuditEditorPhone = "79185625343";
    private const string SonyaOperatorPhone = "79518418035";
    private const string ArtemOperatorPhone = "79518369646";
    private const string KirillOperatorPhone = "79951227218";
    private static readonly HashSet<string> CleaningAuditAccessPhones = new(StringComparer.Ordinal)
    {
        "79185625343",
        "79518369646"
    };

    private static readonly SolidColorBrush PrimaryAccent = new(Color.Parse(UiLabAppearance.EffectiveAccent));
    private static readonly SolidColorBrush BrightAccent = new(Color.Parse(UiLabAppearance.EffectiveBrightAccent));
    private static readonly SolidColorBrush DeepAccent = new(Color.Parse(UiLabAppearance.EffectiveDeepAccent));
    private static readonly SolidColorBrush SoftAccent = new(Color.Parse(UiLabAppearance.EffectiveSoftAccent));
    private static readonly SolidColorBrush AccentBorder = new(Color.Parse(UiLabAppearance.EffectiveAccentBorder));
    private static readonly Dictionary<string, SolidColorBrush> ThemeBrushes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<(string Key, SolidColorBrush Brush)> ThemePanelBrushes = new();
    private static readonly Dictionary<string, AvaloniaBitmap?> ThemeBackgroundArtworks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<AvaloniaBitmap?> DragonArtwork = new(() => LoadDragonArtwork("dragon-winged-cc0.png"));
    private static readonly Lazy<AvaloniaBitmap?> DragonHeadArtwork = new(() => LoadDragonArtwork("dragon-head-cc0.png"));
    private static readonly Lazy<AvaloniaBitmap?> DragonFlamesArtwork = new(() => LoadDragonArtwork("dragon-flames-cc0.png"));
    private static readonly Lazy<AvaloniaBitmap?> DragonSceneArtwork = new(() => LoadDragonArtwork("rodion-dragon-night-art.png"));

    private sealed record BookingDraft(
        string ClientQuery,
        string? ClientUuid,
        DateTime StartLocal,
        decimal DurationHours,
        string Comment,
        List<int> HostIds);

    private sealed record LogCategoryFilter(string Title, string[] Types)
    {
        public override string ToString() => Title;
    }

    private sealed class CleaningAuditEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Operator { get; set; } = "";
        public string HostName { get; set; } = "";
        public DateTime ClientLeftAtUtc { get; set; }
        public DateTime? CleanedAtUtc { get; set; }
        public DateTime? LastReminderAtUtc { get; set; }
        public string SuspiciousReason { get; set; } = "";
    }

    private sealed class PendingCleaningAudit
    {
        public string HostName { get; set; } = "";
        public string Operator { get; set; } = "";
        public DateTime ClientLeftAtUtc { get; set; }
        public DateTime? LastReminderAtUtc { get; set; }
    }

    private sealed class CleaningAuditDocument
    {
        public List<CleaningAuditEntry> Entries { get; set; } = new();
        public List<PendingCleaningAudit> Pending { get; set; } = new();
    }

    private sealed class ShopTabState
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "Счёт 1";
        public bool AutoTitle { get; set; }
        public DateTime CreatedAtLocal { get; set; } = DateTime.Now;
        public string ClientUuid { get; set; } = "";
        public string ClientDisplay { get; set; } = "";
        public string PaymentMethod { get; set; } = "";
        public List<CartItem> Items { get; set; } = new();
    }

    private readonly SmartShellService service = new();
    private readonly AppConfig config;
    private readonly Dictionary<string, Point> layout = BuildLayout();
    private readonly Dictionary<string, PcRecord> records = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> navButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Canvas mapCanvas = new()
    {
        Width = 960,
        Height = 700,
        Background = UiLabAppearance.HasBackdrop ? Brushes.Transparent : Brush("#080d12")
    };
    private AvaloniaImage? themeBackdropImage;
    private TransitioningContentControl contentHost = CreateContentHost();
    private Border sidePanel = new();
    private ColumnDefinition? inspectorColumn;
    private double currentInspectorWidth = 320;
    private StackPanel toastHost = new();
    private readonly System.Windows.Forms.NotifyIcon windowsNotifier = new();
    private readonly List<PsTimerSession> psTimers = new();
    private readonly List<ShopItem> allShopItems = new();
    private readonly List<ClientInfo> clientResults = new();
    private readonly List<CartItem> cartItems = new();
    private readonly List<ShopTabState> shopTabs = new();
    private readonly Dictionary<int, BookingInfo> upcomingBookingsByHost = new();
    private readonly Dictionary<string, TextBlock> hostHoverRemainingBlocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBlock> hostBottomBlocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBlock> computerRemainingBlocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> selectedHostKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Border mapSelectionRectangle = new()
    {
        Background = Brush("#332f8cff"),
        BorderBrush = Brush("#2f8cff"),
        BorderThickness = new Thickness(1),
        IsHitTestVisible = false,
        IsVisible = false
    };
    private readonly Dictionary<string, HashSet<int>> cleaningReminderMinutes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CleaningAuditEntry> cleaningAuditEntries = new();
    private readonly Dictionary<string, PendingCleaningAudit> pendingCleaningAudits = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> shopModeButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> shopSortButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> paymentButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> debtPaymentButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AdminCallInfo> adminCalls = new();
    private readonly HashSet<int> knownAdminCallHostIds = new();
    private readonly string authBrowserProfileDir = Path.Combine(Path.GetTempPath(), "SmartShellCleanerBrowser-" + Guid.NewGuid().ToString("N"));
    private readonly DispatcherTimer psUiTimer = new();
    private readonly CancellationTokenSource browserBridgeCts = new();
    private TextBlock title = new();
    private TextBlock source = new();
    private TextBlock summary = new();
    private TextBlock selectedTitle = new();
    private TextBlock selectedBody = new();
    private readonly DispatcherTimer timer = new();
    private ComboBox? psDeviceBox;
    private TextBox? psMinutesBox;
    private TextBox? psClientBox;
    private StackPanel? psTimersList;
    private ScrollViewer? mapScrollView;
    private StackPanel? shopCatalogList;
    private StackPanel? clientResultsList;
    private StackPanel? cartItemsList;
    private StackPanel? shopTabsHost;
    private TextBox? shopSearchBox;
    private TextBox? clientSearchBox;
    private TextBox? addPcBox;
    private TextBox? depositAmountBox;
    private TextBox? bonusAmountBox;
    private TextBox? bonusCommentBox;
    private CheckBox? depositAlreadyCreditedBox;
    private TextBox? cashPartBox;
    private TextBox? cardPartBox;
    private TextBox? cashReceivedBox;
    private TextBlock? selectedClientText;
    private TextBlock? storeStatusText;
    private TextBlock? cartTotalText;
    private TextBlock? cashChangeText;
    private StackPanel? cashChangePanel;
    private Button? checkoutButton;
    private Button? cleanedButton;
    private StackPanel? singleSelectionActions;
    private StackPanel? bulkSelectionActions;
    private StackPanel? computerGroupsHost;
    private StackPanel? dashboardBody;
    private Border? computerBulkBar;
    private TextBlock? computerBulkText;
    private Button? computerTariffButton;
    private Button? computerPowerButton;
    private Button? headerCallsButton;
    private StackPanel? headerCallsRows;
    private Popup? headerCallsPopup;
    private Point? mapSelectionStart;
    private bool mapSelectionDragging;
    private bool mapSelectionAdditive;
    private bool psTimerAttentionPending;
    private ClientInfo? selectedClient;
    private ApiAccessInfo? apiAccessInfo;
    private PcRecord? selected;
    private string screen = "map";
    private string shopMode = "GOOD";
    private string shopSortColumn = "title";
    private bool shopSortDescending;
    private string paymentMethod = "";
    private string debtPaymentMethod = "CASH";
    private string computerSortColumn = "name";
    private bool computerSortDescending;
    private bool hostsLoadedOnce;
    private bool refreshInProgress;
    private bool creditDepositInProgress;
    private bool shopLoading;
    private bool dashboardLoading;
    private bool splitPaymentSyncing;
    private bool shopTabSyncing;
    private bool adminCallsLoadedOnce;
    private string appliedOperatorPaletteIdentity = "";
    private string activeShopTabId = "";
    private DateTime upcomingBookingsRefreshAfterUtc = DateTime.MinValue;
    private DateTime dashboardRefreshAfterUtc = DateTime.MinValue;
    private Popup? activeHostHoverPopup;
    private bool activeHostHoverPointerInside;
    private TcpListener? browserBridgeListener;
    private bool allowApplicationExit;
    private bool bridgeTokenApplyInProgress;
    private bool trayHintShown;

    public MainWindow()
    {
        service.DeleteConfig();
        config = service.LoadConfig();
        Title = "SmartShell Club Panel - Avalonia";
        Width = 1360;
        Height = 820;
        MinWidth = 1120;
        MinHeight = 680;
        SystemDecorations = SystemDecorations.None;
        Background = Brush("#080d12");
        LoadCleaningAuditState();
        LoadShopTabs();
        ConfigureWindowsNotifications();
        StartBrowserBridge();
        Content = BuildRoot();
        ConfigureMapSelection();
        Closing += (_, e) =>
        {
            if (allowApplicationExit || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown) return;
            e.Cancel = true;
            HideToTray();
        };

        foreach (var record in SmartShellService.BuildFallbackHosts())
            records[record.Name] = record;

        SwitchScreen("map");
        RefreshMap();
        UpdateSelectedPanel();
        UpdateSummary("ожидание первого обновления");

        timer.Tick += async (_, _) => await RefreshAsync();

        psUiTimer.Interval = TimeSpan.FromSeconds(1);
        psUiTimer.Tick += (_, _) =>
        {
            UpdatePsTimers();
            UpdateHostCountdownCards();
            UpdateSelectedElapsedPanel();
        };
        psUiTimer.Start();

        Dispatcher.UIThread.Post(async () =>
        {
            timer.Interval = TimeSpan.FromSeconds(Math.Max(5, config.PollSeconds));
            if (!string.IsNullOrWhiteSpace(config.AccessToken))
            {
                await RefreshCurrentOperatorAccessAsync();
                await RefreshAsync();
                timer.Start();
            }
            else
            {
                source.Text = "ожидаю SmartShell Bridge...";
                summary.Text = "SmartShell не подключён";
            }
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        timer.Stop();
        psUiTimer.Stop();
        browserBridgeCts.Cancel();
        browserBridgeListener?.Stop();
        SaveActiveShopTab();
        SaveCleaningAuditState();
        windowsNotifier.Dispose();
        service.DeleteConfig();
        TryDeleteAuthBrowserProfile();
        base.OnClosed(e);
    }

    private Grid BuildRoot()
    {
        var theme = UiLabAppearance.CurrentKey;
        var topNavigation = IsTopNavigationShell();
        var sidebarWidth = theme switch
        {
            "violet" => 84,
            "material" => 96,
            "suki" => 252,
            _ => 216
        };
        var inspectorWidth = theme switch
        {
            "violet" => 304,
            "suki" => 340,
            "material" => 336,
            _ => 320
        };
        var headerHeight = theme switch
        {
            "suki" => 82,
            "material" => 64,
            "violet" => 76,
            _ => 70
        };
        var root = new Grid
        {
            ColumnDefinitions = topNavigation
                ? new ColumnDefinitions($"*,{inspectorWidth}")
                : new ColumnDefinitions($"{sidebarWidth},*,{inspectorWidth}"),
            RowDefinitions = topNavigation
                ? new RowDefinitions($"{headerHeight},58,*")
                : new RowDefinitions($"{headerHeight},*"),
            Background = Brush("#080d12")
        };
        currentInspectorWidth = inspectorWidth;
        inspectorColumn = root.ColumnDefinitions[^1];

        if (UiLabAppearance.HasBackdrop)
        {
            var backdrop = BuildThemeBackdrop();
            Grid.SetRowSpan(backdrop, topNavigation ? 3 : 2);
            Grid.SetColumnSpan(backdrop, topNavigation ? 2 : 3);
            root.Children.Add(backdrop);
        }

        var sidebar = BuildSidebar();
        if (topNavigation)
        {
            Grid.SetRow(sidebar, 1);
            Grid.SetColumnSpan(sidebar, 2);
        }
        else
        {
            Grid.SetRowSpan(sidebar, 2);
        }
        root.Children.Add(sidebar);

        var header = BuildHeader();
        Grid.SetColumn(header, topNavigation ? 0 : 1);
        Grid.SetColumnSpan(header, 2);
        root.Children.Add(header);

        contentHost.Margin = ContentMargin(false);
        Grid.SetRow(contentHost, topNavigation ? 2 : 1);
        Grid.SetColumn(contentHost, topNavigation ? 0 : 1);
        root.Children.Add(contentHost);

        sidePanel.Margin = theme switch
        {
            "suki" => new Thickness(12, 20, 12, 20),
            "material" => new Thickness(12, 20, 16, 20),
            _ => new Thickness(16, 28, 0, 24)
        };
        sidePanel.Padding = new Thickness(theme == "material" ? 20 : 18);
        sidePanel.Background = PanelBrush("#121b25", 146);
        sidePanel.BorderBrush = Brush("#263244");
        sidePanel.BorderThickness = new Thickness(1);
        sidePanel.CornerRadius = new CornerRadius(theme switch { "suki" => 8, "material" => 6, "violet" => 4, _ => 10 });
        sidePanel.Child = BuildSideContent();
        Grid.SetRow(sidePanel, topNavigation ? 2 : 1);
        Grid.SetColumn(sidePanel, topNavigation ? 1 : 2);
        root.Children.Add(sidePanel);

        toastHost.HorizontalAlignment = HorizontalAlignment.Right;
        toastHost.VerticalAlignment = VerticalAlignment.Bottom;
        toastHost.Margin = new Thickness(0, 0, 22, 22);
        toastHost.Spacing = 10;
        Grid.SetRowSpan(toastHost, topNavigation ? 3 : 2);
        Grid.SetColumnSpan(toastHost, topNavigation ? 2 : 3);
        root.Children.Add(toastHost);

        return root;
    }

    private static bool IsTopNavigationShell() => UiLabAppearance.CurrentKey is "semi" or "oled";

    private static bool IsRailShell() => UiLabAppearance.CurrentKey is "violet" or "material";

    private static Thickness ContentMargin(bool fullWidth)
    {
        return UiLabAppearance.CurrentKey switch
        {
            "suki" => new Thickness(24, 20, fullWidth ? 24 : 16, 24),
            "material" => new Thickness(24, 20, fullWidth ? 24 : 16, 24),
            "violet" => new Thickness(22, 20, fullWidth ? 22 : 14, 22),
            "semi" => new Thickness(24, 20, fullWidth ? 24 : 16, 24),
            "oled" => new Thickness(20, 18, fullWidth ? 20 : 12, 20),
            _ => fullWidth ? new Thickness(28, 24, 28, 24) : new Thickness(28, 24, 24, 24)
        };
    }

    private static IPageTransition CreatePageTransition()
    {
        return UiLabAppearance.CurrentKey switch
        {
            "semi" => new PageSlide(TimeSpan.FromMilliseconds(180), PageSlide.SlideAxis.Vertical),
            "suki" => new PageSlide(TimeSpan.FromMilliseconds(240), PageSlide.SlideAxis.Horizontal),
            "material" => new PageSlide(TimeSpan.FromMilliseconds(220), PageSlide.SlideAxis.Horizontal),
            "oled" => new CrossFade(TimeSpan.FromMilliseconds(110)),
            "violet" => new CrossFade(TimeSpan.FromMilliseconds(160)),
            _ => new CrossFade(TimeSpan.FromMilliseconds(190))
        };
    }

    private static TransitioningContentControl CreateContentHost() => new() { PageTransition = CreatePageTransition() };

    private Border BuildSidebar()
    {
        if (IsTopNavigationShell())
            return BuildTopNavigation();
        if (IsRailShell())
            return BuildNavigationRail();

        var suki = UiLabAppearance.CurrentKey == "suki";
        var border = new Border
        {
            Background = Brush("#111820"),
            BorderBrush = Brush("#26313d"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Margin = new Thickness(0),
            CornerRadius = suki ? new CornerRadius(0, 8, 8, 0) : new CornerRadius(0)
        };

        var dock = new DockPanel();
        border.Child = dock;

        var top = new StackPanel { Margin = new Thickness(10, 20, 8, 0), Spacing = 6 };
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);

        top.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 0, 0, 26),
            Children =
            {
                new Border
                {
                    Width = 32,
                    Height = 32,
                    CornerRadius = new CornerRadius(8),
                    Background = Brush("#102a4d"),
                    Child = new TextBlock
                    {
                        Text = "S",
                        Foreground = Brush("#2f8cff"),
                        FontWeight = FontWeight.Bold,
                        FontSize = 18,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                },
                new TextBlock
                {
                    Text = "SMART\nSHELL",
                    Foreground = Brush("#f5f7ff"),
                    FontSize = 14,
                    FontWeight = FontWeight.SemiBold,
                    LineHeight = 14,
                    Margin = new Thickness(10, 1, 0, 0)
                }
            }
        });

        AddMainNavigation(top);
        top.Children.Add(new Separator { Margin = new Thickness(0, 10), Background = Brush("#2b3442") });
        top.Children.Add(NavButton("gamepad", "Таймеры PS", "ps"));
        top.Children.Add(new Separator { Margin = new Thickness(0, 10), Background = Brush("#2b3442") });
        top.Children.Add(SideButton("↻", "Обновить", async () => await RefreshAsync()));
        top.Children.Add(SideButton("↗", "Открыть SmartShell", OpenSmartShellMap));
        top.Children.Add(SideButton("⚙", "SmartShell API", () => _ = ShowApiSettingsDialogAsync(false)));

        var bottom = new StackPanel { Margin = new Thickness(10, 0, 8, 18), VerticalAlignment = VerticalAlignment.Bottom, Spacing = 8 };
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(bottom);
        addPcBox = new TextBox
        {
            Watermark = "Добавить ПК",
            Height = 36,
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550")
        };
        bottom.Children.Add(addPcBox);
        bottom.Children.Add(SideButton("⌫", "Очистить уборку", ClearCleaningHistory));
        bottom.Children.Add(SideButton("+", "Добавить ПК", AddPcFromSidebar));

        return border;
    }

    private Border BuildTopNavigation()
    {
        var border = new Border
        {
            Background = Brush("#111820"),
            BorderBrush = Brush("#26313d"),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(18, 6, 12, 6)
        };
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        AddMainNavigation(nav);
        nav.Children.Add(NavButton("gamepad", "Таймеры PS", "ps"));
        grid.Children.Add(new ScrollViewer
        {
            Content = nav,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        });

        addPcBox = new TextBox
        {
            Width = 76,
            Height = 42,
            Watermark = "ПК",
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550")
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                addPcBox,
                NavigationActionButton("add", "Добавить ПК", AddPcFromSidebar),
                NavigationActionButton("clear", "Очистить уборку", ClearCleaningHistory),
                NavigationActionButton("refresh", "Обновить", async () => await RefreshAsync()),
                NavigationActionButton("external", "Открыть SmartShell", OpenSmartShellMap),
                NavigationActionButton("settings", "SmartShell API", () => _ = ShowApiSettingsDialogAsync(false))
            }
        };
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        border.Child = grid;
        return border;
    }

    private Border BuildNavigationRail()
    {
        var material = UiLabAppearance.CurrentKey == "material";
        var border = new Border
        {
            Background = Brush("#111820"),
            BorderBrush = Brush("#26313d"),
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        var dock = new DockPanel();
        border.Child = dock;

        var brandMark = AppMark(material ? 32 : 36);
        brandMark.Margin = new Thickness(0);
        brandMark.HorizontalAlignment = HorizontalAlignment.Center;
        var brand = new Border
        {
            Height = material ? 64 : 72,
            Child = brandMark,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        DockPanel.SetDock(brand, Dock.Top);
        dock.Children.Add(brand);

        var bottom = new StackPanel
        {
            Width = 68,
            Margin = new Thickness(0, 8, 0, 16),
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(bottom);
        addPcBox = new TextBox
        {
            Watermark = "ПК",
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550")
        };
        bottom.Children.Add(addPcBox);
        bottom.Children.Add(NavigationActionButton("refresh", "Обновить", async () => await RefreshAsync()));
        bottom.Children.Add(NavigationActionButton("external", "Открыть SmartShell", OpenSmartShellMap));
        bottom.Children.Add(NavigationActionButton("settings", "SmartShell API", () => _ = ShowApiSettingsDialogAsync(false)));
        bottom.Children.Add(NavigationActionButton("clear", "Очистить уборку", ClearCleaningHistory));
        bottom.Children.Add(NavigationActionButton("add", "Добавить ПК", AddPcFromSidebar));

        var nav = new StackPanel
        {
            Width = 68,
            Margin = new Thickness(0, 4),
            Spacing = material ? 4 : 6,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        AddMainNavigation(nav);
        nav.Children.Add(NavButton("gamepad", "PS", "ps"));
        dock.Children.Add(new ScrollViewer
        {
            Content = nav,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        return border;
    }

    private void AddMainNavigation(StackPanel panel)
    {
        panel.Children.Add(NavButton("dashboard", "Дашборд", "dashboard"));
        panel.Children.Add(NavButton("computer", "Компьютеры", "details"));
        panel.Children.Add(NavButton("map", "Карта клуба", "map"));
        panel.Children.Add(NavButton("calendar", "Бронирование", "bookings"));
        panel.Children.Add(NavButton("goods", "Магазин", "shop"));
        panel.Children.Add(NavButton("payments", "Платежи", "payments"));
        panel.Children.Add(NavButton("clients", "Клиенты", "clients"));
        panel.Children.Add(NavButton("logs", "Логи", "logs"));
        panel.Children.Add(NavButton("audit", "Журнал уборки", "cleaning-log"));
    }

    private Button NavigationActionButton(string icon, string tooltip, Action action)
    {
        var rail = IsRailShell();
        var button = new Button
        {
            Width = IsTopNavigationShell() || rail ? 42 : double.NaN,
            Height = IsTopNavigationShell() ? 42 : 38,
            Padding = new Thickness(9),
            HorizontalAlignment = rail ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = Brush("#182231"),
            BorderBrush = Brush("#2b3550"),
            Content = DashboardIcon(icon, "#cbd3df", 18)
        };
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => action();
        return button;
    }

    private Grid BuildHeader()
    {
        var theme = UiLabAppearance.CurrentKey;
        var compactRail = IsRailShell();
        var header = new Grid
        {
            Background = theme == "suki" ? Brush("#111820") : Brush("#080d12"),
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto")
        };
        header.PointerPressed += (_, e) =>
        {
            if (e.Source is Button || (e.Source as Control)?.FindAncestorOfType<Button>() != null)
                return;
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title.Foreground = Brush("#f5f7ff");
        title.FontSize = theme switch { "suki" => 23, "material" => 20, "violet" => 19, _ => 21 };
        title.FontWeight = theme == "material" ? FontWeight.Medium : FontWeight.SemiBold;
        source.Foreground = Brush("#8f9aad");
        source.FontSize = 12;
        source.Margin = new Thickness(0, 6, 0, 0);
        titleStack.Children.Add(title);
        titleStack.Children.Add(source);

        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(theme == "material" ? 22 : 28, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = compactRail ? 0 : 10
        };
        if (!compactRail && !IsTopNavigationShell())
            brand.Children.Add(AppMark(34));
        brand.Children.Add(titleStack);
        header.Children.Add(brand);

        summary.Foreground = BrightAccent;
        summary.FontSize = 13;
        summary.FontWeight = FontWeight.SemiBold;
        summary.VerticalAlignment = VerticalAlignment.Center;
        summary.Margin = new Thickness(0, 0, 8, 0);
        summary.MaxWidth = 250;
        summary.TextTrimming = TextTrimming.CharacterEllipsis;

        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 12, 0),
            Children =
            {
                summary,
                BuildHeaderFastAccess(),
                BuildHeaderClientSearch(),
                BuildHeaderCalls(),
                BuildHeaderOperatorMenu()
            }
        };
        Grid.SetColumn(tools, 1);
        header.Children.Add(tools);

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Stretch,
            Children =
            {
                WindowButton("—", () => WindowState = WindowState.Minimized),
                WindowButton("□", ToggleWindowState),
                WindowButton("×", HideToTray, true)
            }
        };
        Grid.SetColumn(controls, 2);
        header.Children.Add(controls);

        return header;
    }

    private Control BuildHeaderFastAccess()
    {
        var button = HeaderToolButton("+", "Быстрые операции", 40);
        var rows = new StackPanel { Spacing = 2, Margin = new Thickness(6) };
        var popup = HeaderPopup(button, 246, rows);

        Button Action(string icon, string text, Func<Task> action)
        {
            var item = new Button
            {
                Height = 40,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Foreground = Brush("#f5f7ff"),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 0),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children =
                    {
                        DashboardIcon(icon, "#2f8cff", 17),
                        new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }
                    }
                }
            };
            item.Click += async (_, _) =>
            {
                popup.IsOpen = false;
                try { await action(); }
                catch (Exception ex) { ShowToast(text, ex.Message); }
            };
            return item;
        }

        rows.Children.Add(Action("wallet", "Пополнить депозит", OpenQuickDepositAsync));
        rows.Children.Add(Action("calendar", "Забронировать", OpenQuickBookingAsync));
        rows.Children.Add(Action("cash", "Приходный ордер", () => ShowCashOrderDialogAsync("PKO")));
        rows.Children.Add(Action("cash", "Расходный ордер", () => ShowCashOrderDialogAsync("RKO")));
        rows.Children.Add(Action("goods", "Внесение на склад", ShowWarehouseDialogAsync));
        rows.Children.Add(Action("clients", "Создать клиента", async () => { await OpenSmartShellClientRegistrationAsync(); }));

        button.Click += (_, _) => popup.IsOpen = !popup.IsOpen;
        return new Grid { Children = { button, popup } };
    }

    private Control BuildHeaderClientSearch()
    {
        const double searchWidth = 245;
        var search = new TextBox
        {
            Width = searchWidth,
            Height = 38,
            Watermark = "Найти клиента",
            Background = Brush("#101820"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#344154"),
            Padding = new Thickness(38, 0, 10, 0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var searchIcon = DashboardIcon("search", "#c9d3e6", 17);
        searchIcon.Margin = new Thickness(12, 0, 0, 0);
        searchIcon.HorizontalAlignment = HorizontalAlignment.Left;
        searchIcon.IsHitTestVisible = false;
        var rows = new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var resultsScroll = new ScrollViewer
        {
            Content = rows,
            MaxHeight = 260,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var popup = HeaderPopup(search, searchWidth, resultsScroll);
        popup.Placement = PlacementMode.BottomEdgeAlignedLeft;
        var version = 0;

        async Task RenderAsync(string query, int currentVersion)
        {
            await Task.Delay(280);
            if (currentVersion != version) return;
            rows.Children.Clear();
            if (query.Length < 2)
            {
                popup.IsOpen = false;
                return;
            }

            try
            {
                var clients = await service.SearchClientsAsync(config, query, CancellationToken.None);
                if (currentVersion != version) return;
                if (clients.Count == 0)
                {
                    rows.Children.Add(HeaderPopupMessage("Клиенты не найдены"));
                }
                else
                {
                    foreach (var client in clients)
                    {
                        var item = new Button
                        {
                            MinHeight = 48,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            Background = Brushes.Transparent,
                            BorderBrush = Brushes.Transparent,
                            HorizontalContentAlignment = HorizontalAlignment.Stretch,
                            Padding = new Thickness(10, 6),
                            Content = new Grid
                            {
                                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                                Children =
                                {
                                    new StackPanel
                                    {
                                        Spacing = 2,
                                        Children =
                                        {
                                            new TextBlock { Text = string.IsNullOrWhiteSpace(client.Nickname) ? client.Phone : client.Nickname, Foreground = Brush("#f5f7ff"), FontWeight = FontWeight.SemiBold },
                                            new TextBlock { Text = client.Phone, Foreground = Brush("#8f9aad"), FontSize = 12 }
                                        }
                                    },
                                    DashboardIcon("clients", "#2f8cff", 17)
                                }
                            }
                        };
                        Grid.SetColumn(((Grid)item.Content).Children[1], 1);
                        item.Click += async (_, _) =>
                        {
                            popup.IsOpen = false;
                            search.Text = "";
                            var details = await service.LoadClientDetailsAsync(config, client.Uuid, CancellationToken.None) ?? client;
                            await ShowClientCardDialogAsync(details, this);
                        };
                        rows.Children.Add(item);
                    }
                }
                popup.IsOpen = true;
            }
            catch (Exception ex)
            {
                rows.Children.Add(HeaderPopupMessage("Ошибка поиска: " + ex.Message));
                popup.IsOpen = true;
            }
        }

        search.TextChanged += (_, _) =>
        {
            var query = (search.Text ?? "").Trim();
            _ = RenderAsync(query, ++version);
        };
        search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                popup.IsOpen = false;
                search.Text = "";
            }
        };

        return new Grid { Children = { search, searchIcon, popup } };
    }

    private Control BuildHeaderCalls()
    {
        headerCallsButton = HeaderToolButton("Вызовы", "Вызовы клиентов", 122, "event");
        headerCallsRows = new StackPanel { Spacing = 4, Margin = new Thickness(6) };
        headerCallsPopup = HeaderPopup(headerCallsButton, 330, headerCallsRows);
        headerCallsButton.Click += async (_, _) =>
        {
            await RefreshAdminCallsAsync(false);
            RenderAdminCallsPopup();
            headerCallsPopup.IsOpen = !headerCallsPopup.IsOpen;
        };
        UpdateAdminCallsButton();
        return new Grid { Children = { headerCallsButton, headerCallsPopup } };
    }

    private Control BuildHeaderOperatorMenu()
    {
        var button = HeaderToolButton("", "Профиль оператора", 40, "user");
        var name = new TextBlock { Text = "Оператор", Foreground = Brush("#f5f7ff"), FontWeight = FontWeight.SemiBold };
        var phone = new TextBlock { Foreground = Brush("#8f9aad"), FontSize = 12 };
        var role = new TextBlock { Foreground = Brush("#17c964"), FontSize = 12 };
        var themeOptions = new StackPanel { Spacing = 4, IsVisible = false };

        Control ThemeLabel(UiPresetOption preset, bool compact)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
            grid.Children.Add(new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.Parse(preset.Accent)),
                VerticalAlignment = VerticalAlignment.Center
            });
            var text = new StackPanel
            {
                Spacing = compact ? 0 : 2,
                Children =
                {
                    new TextBlock { Text = preset.Title, Foreground = Brush("#f5f7ff"), FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = preset.Library, Foreground = Brush("#8f9aad"), FontSize = 11, IsVisible = !compact }
                }
            };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var marker = new TextBlock
            {
                Text = compact ? "⌄" : (preset.Key == UiLabAppearance.CurrentKey ? "✓" : ""),
                Foreground = preset.Key == UiLabAppearance.CurrentKey ? BrightAccent : Brush("#8f9aad"),
                FontSize = compact ? 17 : 14,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(marker, 2);
            grid.Children.Add(marker);
            return grid;
        }

        var themeButton = new Button
        {
            Height = 42,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 0),
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550"),
            Content = ThemeLabel(UiLabAppearance.Current, true)
        };
        var changeInfo = new TextBlock
        {
            Text = UiStyleDescription(UiLabAppearance.CurrentKey),
            Foreground = Brush("#a9b2c5"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 270
        };
        var paletteButton = new Button
        {
            Height = 38,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 0),
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550"),
            Content = IconLabel("◉", UiLabAppearance.CustomPalette == null ? "Настроить палитру" : "Изменить свою палитру")
        };

        var themeItems = new List<(UiPresetOption Preset, Button Button)>();
        foreach (var preset in UiLabAppearance.Options)
        {
            var item = new Button
            {
                Height = 48,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12, 0),
                Background = preset.Key == UiLabAppearance.CurrentKey ? SoftAccent : Brushes.Transparent,
                BorderBrush = preset.Key == UiLabAppearance.CurrentKey ? AccentBorder : Brushes.Transparent,
                Content = ThemeLabel(preset, false)
            };
            item.Click += (_, _) =>
            {
                if (preset.Key == UiLabAppearance.CurrentKey)
                {
                    themeOptions.IsVisible = false;
                    return;
                }

                ApplyUiPreset(preset);
            };
            themeItems.Add((preset, item));
            themeOptions.Children.Add(item);
        }

        themeButton.Click += (_, _) => themeOptions.IsVisible = !themeOptions.IsVisible;
        var body = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(14),
            Children =
            {
                name,
                phone,
                role,
                new Border { Height = 1, Background = Brush("#334052"), Margin = new Thickness(0, 6) },
                new TextBlock { Text = "Тема оформления", Foreground = Brush("#8f9aad"), FontSize = 12 },
                themeButton,
                themeOptions,
                changeInfo,
                paletteButton,
                new TextBlock
                {
                    Text = "Полная тема применится автоматически",
                    Foreground = Brush("#778399"),
                    FontSize = 11
                }
            }
        };
        var popup = HeaderPopup(button, 310, body);
        paletteButton.Click += async (_, _) =>
        {
            popup.IsOpen = false;
            await ShowPaletteEditorAsync();
        };
        button.Click += async (_, _) =>
        {
            try
            {
                apiAccessInfo ??= await service.LoadApiAccessInfoAsync(config, CancellationToken.None);
                UpdateCleaningAuditAccess();
                name.Text = apiAccessInfo.DisplayName;
                phone.Text = apiAccessInfo.Phone;
                role.Text = apiAccessInfo.Roles.Any(x => x.Equals("active_operator", StringComparison.OrdinalIgnoreCase))
                    ? "Вы на смене"
                    : apiAccessInfo.RoleText;
            }
            catch (Exception ex)
            {
                role.Text = "Не удалось загрузить профиль: " + ex.Message;
            }
            popup.IsOpen = !popup.IsOpen;
        };
        return new Grid { Children = { button, popup } };
    }

    private Button HeaderToolButton(string text, string tooltip, double width, string? icon = null)
    {
        Control content = icon == null
            ? new TextBlock { Text = text, FontSize = 19, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }
            : new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = string.IsNullOrWhiteSpace(text) ? 0 : 7,
                Children =
                {
                    DashboardIcon(icon, "#2f8cff", 17),
                    new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, IsVisible = !string.IsNullOrWhiteSpace(text) }
                }
            };
        var button = new Button
        {
            Width = width,
            Height = 38,
            Padding = new Thickness(0),
            Background = Brush("#101c2b"),
            BorderBrush = Brush("#213653"),
            Foreground = Brush("#2f8cff"),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = content
        };
        ToolTip.SetTip(button, tooltip);
        return button;
    }

    private Popup HeaderPopup(Control target, double width, Control child)
    {
        return new Popup
        {
            PlacementTarget = target,
            Placement = PlacementMode.BottomEdgeAlignedRight,
            VerticalOffset = 7,
            IsLightDismissEnabled = true,
            Topmost = true,
            Child = new Border
            {
                Width = width,
                MaxHeight = 520,
                Background = Brush("#151d28"),
                BorderBrush = Brush("#344154"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 24, OffsetY = 10, Color = Color.Parse("#99000000") }),
                Child = child
            }
        };
    }

    private TextBlock HeaderPopupMessage(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = Brush("#8f9aad"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12)
        };
    }

    private async Task OpenQuickDepositAsync()
    {
        var client = await ShowClientPickerAsync("Пополнение депозита");
        if (client != null)
            await ShowDepositTopUpDialogAsync(client, this);
    }

    private async Task OpenQuickBookingAsync()
    {
        await OpenBookingDialogAsync(new PcRecord { Name = "Бронирование" }, Array.Empty<int>());
    }

    private async Task<ClientInfo?> ShowClientPickerAsync(string dialogTitle)
    {
        var dialog = new ModalWindow
        {
            Title = dialogTitle,
            Width = 520,
            Height = 580,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#0d1217"),
            SystemDecorations = SystemDecorations.None
        };
        var search = new TextBox
        {
            Height = 44,
            Watermark = "Логин, телефон или последние 4 цифры",
            Background = Brush("#151d28"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2f8cff"),
            Padding = new Thickness(12, 0)
        };
        var rows = new StackPanel { Spacing = 4 };
        var version = 0;

        async Task SearchAsync(string query, int currentVersion)
        {
            await Task.Delay(250);
            if (version != currentVersion) return;
            rows.Children.Clear();
            if (query.Length < 2)
            {
                rows.Children.Add(HeaderPopupMessage("Начните вводить логин или телефон клиента."));
                return;
            }

            try
            {
                var clients = (await service.SearchClientsAsync(config, query, CancellationToken.None)).Take(20).ToList();
                if (version != currentVersion) return;
                if (clients.Count == 0)
                {
                    rows.Children.Add(HeaderPopupMessage("Клиенты не найдены."));
                    return;
                }

                foreach (var client in clients)
                {
                    var item = new Button
                    {
                        MinHeight = 54,
                        Background = Brush("#151d28"),
                        BorderBrush = Brush("#253143"),
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        Padding = new Thickness(14, 8),
                        Content = new StackPanel
                        {
                            Spacing = 3,
                            Children =
                            {
                                new TextBlock { Text = string.IsNullOrWhiteSpace(client.Nickname) ? client.Phone : client.Nickname, Foreground = Brush("#f5f7ff"), FontWeight = FontWeight.SemiBold },
                                new TextBlock { Text = client.Phone, Foreground = Brush("#8f9aad"), FontSize = 12 }
                            }
                        }
                    };
                    item.Click += (_, _) => dialog.Close(client);
                    rows.Children.Add(item);
                }
            }
            catch (Exception ex)
            {
                rows.Children.Add(HeaderPopupMessage("Ошибка поиска: " + ex.Message));
            }
        }

        search.TextChanged += (_, _) =>
        {
            var query = (search.Text ?? "").Trim();
            _ = SearchAsync(query, ++version);
        };

        var close = HeaderToolButton("×", "Закрыть", 36);
        close.Click += (_, _) => dialog.Close(null);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock
        {
            Text = dialogTitle,
            Foreground = Brush("#f5f7ff"),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        var list = new ScrollViewer
        {
            Content = rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var body = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 14,
            Children = { header, search, list }
        };
        Grid.SetRow(search, 1);
        Grid.SetRow(list, 2);
        dialog.Content = new Border
        {
            Background = Brush("#0d1217"),
            BorderBrush = Brush("#344154"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24),
            Child = body
        };
        rows.Children.Add(HeaderPopupMessage("Начните вводить логин или телефон клиента."));
        return await dialog.ShowDialog<ClientInfo?>(this);
    }

    private async Task ShowCashOrderDialogAsync(string type)
    {
        var income = type.Equals("PKO", StringComparison.OrdinalIgnoreCase);
        var dialog = new ModalWindow
        {
            Title = income ? "Приходный ордер" : "Расходный ордер",
            Width = 440,
            Height = 430,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#0d1217"),
            SystemDecorations = SystemDecorations.None
        };
        apiAccessInfo ??= await service.LoadApiAccessInfoAsync(config, CancellationToken.None);
        var amount = SaleInput("Сумма");
        amount.Height = 46;
        var comment = new TextBox
        {
            Height = 96,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Watermark = "Комментарий",
            Background = Brush("#151d28"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#344154"),
            Padding = new Thickness(12)
        };
        var status = new TextBlock { Foreground = Brush("#ffb020"), TextWrapping = TextWrapping.Wrap };
        var submit = SalePrimaryButton(income ? "Внести" : "Списать");
        submit.HorizontalAlignment = HorizontalAlignment.Stretch;
        submit.Click += async (_, _) =>
        {
            if (!TryParseMoney(amount.Text, out var sum) || sum <= 0)
            {
                status.Text = "Введите сумму больше нуля.";
                return;
            }

            submit.IsEnabled = false;
            try
            {
                await service.CreateCashOrderAsync(config, income ? "PKO" : "RKO", sum, comment.Text ?? "", CancellationToken.None);
                dialog.Close();
                ShowToast(income ? "Приходный ордер" : "Расходный ордер", "Операция на " + MoneyText(sum) + " создана.");
                dashboardRefreshAfterUtc = DateTime.MinValue;
                if (screen == "dashboard") await RefreshDashboardAsync();
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
                submit.IsEnabled = true;
            }
        };
        var close = HeaderToolButton("×", "Закрыть", 36);
        close.Click += (_, _) => dialog.Close();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                DashboardIcon("cash", income ? "#17c964" : "#ff4d4f", 20),
                new TextBlock { Text = dialog.Title, Foreground = Brush("#f5f7ff"), FontSize = 18, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }
            }
        });
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        dialog.Content = new Border
        {
            Background = Brush("#0d1217"),
            BorderBrush = Brush("#344154"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    header,
                    new TextBlock { Text = apiAccessInfo.DisplayName, Foreground = Brush("#8f9aad") },
                    amount,
                    comment,
                    status,
                    submit
                }
            }
        };
        await dialog.ShowDialog(this);
    }

    private async Task ShowWarehouseDialogAsync()
    {
        var goods = (await service.LoadShopItemsAsync(config, CancellationToken.None))
            .Where(x => x.Type.Equals("GOOD", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Title)
            .ToList();
        var dialog = new ModalWindow
        {
            Title = "Внесение на склад",
            Width = 900,
            Height = 650,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#0d1217"),
            SystemDecorations = SystemDecorations.None
        };
        var search = SaleInput("Поиск товара");
        var catalog = new StackPanel { Spacing = 3 };
        var selectedRows = new StackPanel { Spacing = 6 };
        var quantities = new Dictionary<int, string>();
        var comment = new TextBox
        {
            Height = 70,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Watermark = "Общий комментарий",
            Background = Brush("#151d28"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#344154")
        };
        var status = new TextBlock { Foreground = Brush("#ffb020"), TextWrapping = TextWrapping.Wrap };

        void RenderSelected()
        {
            selectedRows.Children.Clear();
            foreach (var pair in quantities.ToList())
            {
                var item = goods.First(x => x.EntityId == pair.Key);
                var remove = HeaderToolButton("×", "Убрать товар", 34);
                remove.Click += (_, _) =>
                {
                    quantities.Remove(item.EntityId);
                    RenderSelected();
                };
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80,Auto"), ColumnSpacing = 8 };
                grid.Children.Add(new TextBlock { Text = item.Title, Foreground = Brush("#f5f7ff"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                var quantityInput = new TextBox
                {
                    Text = pair.Value,
                    Height = 34,
                    Background = Brush("#101820"),
                    Foreground = Brush("#f5f7ff"),
                    BorderBrush = Brush("#2f8cff"),
                    HorizontalContentAlignment = HorizontalAlignment.Center
                };
                quantityInput.TextChanged += (_, _) => quantities[item.EntityId] = quantityInput.Text ?? "";
                Grid.SetColumn(quantityInput, 1);
                grid.Children.Add(quantityInput);
                Grid.SetColumn(remove, 2);
                grid.Children.Add(remove);
                selectedRows.Children.Add(new Border
                {
                    Background = Brush("#151d28"),
                    BorderBrush = Brush("#253143"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6),
                    Child = grid
                });
            }
            if (quantities.Count == 0)
                selectedRows.Children.Add(HeaderPopupMessage("Выберите товары слева."));
        }

        void RenderCatalog()
        {
            catalog.Children.Clear();
            var query = (search.Text ?? "").Trim();
            foreach (var item in goods.Where(x => string.IsNullOrWhiteSpace(query) || x.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(100))
            {
                var row = new Button
                {
                    Height = 42,
                    Background = quantities.ContainsKey(item.EntityId) ? Brush("#16243a") : Brushes.Transparent,
                    BorderBrush = Brushes.Transparent,
                    Foreground = Brush("#f5f7ff"),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(10, 0),
                    Content = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                        Children =
                        {
                            new TextBlock { Text = item.Title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center },
                            new TextBlock { Text = item.StockText, Foreground = Brush("#8f9aad"), VerticalAlignment = VerticalAlignment.Center }
                        }
                    }
                };
                Grid.SetColumn(((Grid)row.Content).Children[1], 1);
                row.Click += (_, _) =>
                {
                    if (!quantities.ContainsKey(item.EntityId))
                        quantities[item.EntityId] = "1";
                    RenderCatalog();
                    RenderSelected();
                };
                catalog.Children.Add(row);
            }
        }

        var submit = SalePrimaryButton("Внести на склад");
        submit.HorizontalAlignment = HorizontalAlignment.Stretch;
        submit.Click += async (_, _) =>
        {
            var items = new List<Dictionary<string, object?>>();
            foreach (var pair in quantities)
            {
                if (!int.TryParse(pair.Value, out var quantity) || quantity <= 0)
                {
                    status.Text = "Количество каждого товара должно быть целым числом больше нуля.";
                    return;
                }
                items.Add(new Dictionary<string, object?> { ["id"] = pair.Key, ["quantity"] = quantity });
            }
            if (items.Count == 0)
            {
                status.Text = "Выберите хотя бы один товар.";
                return;
            }

            submit.IsEnabled = false;
            try
            {
                await service.AddGoodsToWarehouseAsync(config, items, comment.Text ?? "", CancellationToken.None);
                dialog.Close();
                ShowToast("Склад", "Остатки товаров обновлены.");
                allShopItems.Clear();
                await EnsureShopItemsLoadedAsync();
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
                submit.IsEnabled = true;
            }
        };
        var close = HeaderToolButton("×", "Закрыть", 36);
        close.Click += (_, _) => dialog.Close();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock { Text = "Внесение на склад", Foreground = Brush("#f5f7ff"), FontSize = 18, FontWeight = FontWeight.SemiBold });
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        var left = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 10,
            Children =
            {
                search,
                new ScrollViewer { Content = catalog, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            }
        };
        Grid.SetRow(left.Children[1], 1);
        var right = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto,Auto"),
            RowSpacing = 10,
            Children =
            {
                new ScrollViewer { Content = selectedRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                comment,
                status,
                submit
            }
        };
        Grid.SetRow(comment, 1);
        Grid.SetRow(status, 2);
        Grid.SetRow(submit, 3);
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16, Children = { left, right } };
        Grid.SetColumn(right, 1);
        var shell = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 16, Children = { header, columns } };
        Grid.SetRow(columns, 1);
        dialog.Content = new Border
        {
            Background = Brush("#0d1217"),
            BorderBrush = Brush("#344154"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24),
            Child = shell
        };
        search.TextChanged += (_, _) => RenderCatalog();
        RenderCatalog();
        RenderSelected();
        await dialog.ShowDialog(this);
    }

    private async Task RefreshAdminCallsAsync(bool notify)
    {
        try
        {
            var incoming = await service.LoadAdminCallsAsync(config, CancellationToken.None);
            if (notify && adminCallsLoadedOnce)
            {
                foreach (var call in incoming.Where(x => !knownAdminCallHostIds.Contains(x.HostId)))
                    ShowWindowsNotification("Вызов администратора", call.HostAlias + " просит оператора", true);
            }

            adminCalls.Clear();
            adminCalls.AddRange(incoming);
            knownAdminCallHostIds.Clear();
            foreach (var call in incoming) knownAdminCallHostIds.Add(call.HostId);
            adminCallsLoadedOnce = true;
            UpdateAdminCallsButton();
            if (headerCallsPopup?.IsOpen == true) RenderAdminCallsPopup();
        }
        catch
        {
        }
    }

    private void UpdateAdminCallsButton()
    {
        if (headerCallsButton == null) return;
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Children =
            {
                DashboardIcon("event", adminCalls.Count > 0 ? "#ffb020" : "#2f8cff", 17),
                new TextBlock { Text = "Вызовы", Foreground = adminCalls.Count > 0 ? Brush("#ffb020") : Brush("#2f8cff"), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }
            }
        };
        if (adminCalls.Count > 0)
            content.Children.Add(new Border
            {
                MinWidth = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = Brush("#ff4d4f"),
                Child = new TextBlock { Text = adminCalls.Count.ToString(), Foreground = Brushes.White, FontSize = 11, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
        headerCallsButton.Content = content;
    }

    private void RenderAdminCallsPopup()
    {
        if (headerCallsRows == null) return;
        headerCallsRows.Children.Clear();
        if (adminCalls.Count == 0)
        {
            headerCallsRows.Children.Add(HeaderPopupMessage("Нет вызовов"));
            return;
        }

        foreach (var call in adminCalls)
        {
            var elapsed = DateTime.Now - call.CalledAtLocal;
            var done = HeaderToolButton("✓", "Подтвердить вызов", 38);
            done.Click += async (_, _) =>
            {
                try
                {
                    await service.CompleteAdminCallAsync(config, call.HostId, CancellationToken.None);
                    await RefreshAdminCallsAsync(false);
                }
                catch (Exception ex)
                {
                    ShowToast("Вызовы", ex.Message);
                }
            };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            grid.Children.Add(new StackPanel
            {
                Spacing = 3,
                Children =
                {
                    new TextBlock { Text = call.HostAlias, Foreground = Brush("#f5f7ff"), FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = string.Join(" • ", new[] { call.GroupTitle, "ждёт " + Math.Max(0, (int)elapsed.TotalMinutes) + " мин." }.Where(x => !string.IsNullOrWhiteSpace(x))), Foreground = Brush("#8f9aad"), FontSize = 12 }
                }
            });
            Grid.SetColumn(done, 1);
            grid.Children.Add(done);
            headerCallsRows.Children.Add(new Border
            {
                Background = Brush("#111820"),
                BorderBrush = Brush("#263244"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10),
                Child = grid
            });
        }
    }

    private void ApplyUiPreset(UiPresetOption preset)
    {
        UiLabAppearance.Select(preset.Key);
        ApplyAppearanceBrushes();
        Dispatcher.UIThread.Post(RebuildShell);
    }

    private void ApplyAppearanceBrushes()
    {
        PrimaryAccent.Color = Color.Parse(UiLabAppearance.EffectiveAccent);
        BrightAccent.Color = Color.Parse(UiLabAppearance.EffectiveBrightAccent);
        DeepAccent.Color = Color.Parse(UiLabAppearance.EffectiveDeepAccent);
        SoftAccent.Color = Color.Parse(UiLabAppearance.EffectiveSoftAccent);
        AccentBorder.Color = Color.Parse(UiLabAppearance.EffectiveAccentBorder);
        foreach (var pair in ThemeBrushes)
            pair.Value.Color = ResolvedThemeColor(pair.Key);
        var panelAlpha = (byte)Math.Round(255 * UiLabAppearance.PanelOpacity);
        foreach (var pair in ThemePanelBrushes)
        {
            var color = ResolvedThemeColor(pair.Key);
            pair.Brush.Color = Color.FromArgb(panelAlpha, color.R, color.G, color.B);
        }
        if (themeBackdropImage != null)
            themeBackdropImage.Opacity = UiLabAppearance.BackgroundOpacity;
        Background = Brush("#080d12");
        mapCanvas.Background = UiLabAppearance.HasBackdrop ? Brushes.Transparent : Brush("#080d12");
    }

    private void RebuildShell()
    {
        var target = screen;
        var sourceText = source.Text;
        var summaryText = summary.Text;
        var selectedTitleText = selectedTitle.Text;
        var selectedBodyText = selectedBody.Text;

        contentHost.PageTransition = null;
        contentHost.Content = null;
        if (mapScrollView != null)
            mapScrollView.Content = null;
        mapScrollView = null;
        sidePanel.Child = null;
        Content = null;
        navButtons.Clear();
        ThemePanelBrushes.Clear();
        themeBackdropImage = null;

        title = new TextBlock();
        source = new TextBlock { Text = sourceText };
        summary = new TextBlock { Text = summaryText };
        selectedTitle = new TextBlock { Text = selectedTitleText };
        selectedBody = new TextBlock { Text = selectedBodyText };
        contentHost = CreateContentHost();
        sidePanel = new Border();
        toastHost = new StackPanel();

        Content = BuildRoot();
        SwitchScreen(target);
        UpdateSelectedPanel();
    }

    private static string UiStyleDescription(string key)
    {
        return key switch
        {
            "smartshell" => "Классическая боковая панель · плавное затухание · знакомые иконки",
            "violet" => "Компактный rail · больше места карте · фиолетовые активные состояния",
            "semi" => "Верхняя командная лента · вертикальный переход экранов · минимальные иконки",
            "suki" => "Широкий dock · просторные элементы · горизонтальное движение экранов",
            "material" => "Navigation rail · строгая сетка · Material-иконки и состояния",
            "oled" => "Верхняя навигация · быстрый crossfade · минимальная яркость фона",
            _ => "Альтернативная компоновка интерфейса"
        };
    }

    private async Task ShowPaletteEditorAsync()
    {
        var preset = UiLabAppearance.Current;
        var custom = UiLabAppearance.CustomPalette;
        var originalPalette = custom;
        var motif = UiLabAppearance.ActiveMotif;
        var backgroundImage = custom?.BackgroundImage ?? "";
        var backgroundOpacity = custom?.BackgroundOpacity ?? 0;
        var panelOpacity = custom?.PanelOpacity ?? 1;
        var pcCardOpacity = UiLabAppearance.PcCardOpacity;
        Slider? backgroundOpacitySlider = null;
        Slider? panelTransparencySlider = null;
        Slider? pcCardTransparencySlider = null;
        TextBlock? backgroundFileText = null;
        TextBlock? backgroundOpacityText = null;
        TextBlock? panelTransparencyText = null;
        TextBlock? pcCardTransparencyText = null;
        var updatingBackdropControls = false;
        var colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            ["accent"] = Color.Parse(custom?.Accent ?? preset.Accent),
            ["background"] = Color.Parse(custom?.Background ?? preset.Colors["#080d12"]),
            ["surface"] = Color.Parse(custom?.Surface ?? preset.Colors["#111820"]),
            ["border"] = Color.Parse(custom?.Border ?? preset.Colors["#263244"]),
            ["text"] = Color.Parse(custom?.Text ?? preset.Colors["#f5f7ff"]),
            ["dashboardPanel"] = Color.Parse(custom?.DashboardPanel ?? UiLabAppearance.DashboardPanel),
            ["shopPanel"] = Color.Parse(custom?.ShopPanel ?? UiLabAppearance.ShopPanel),
            ["psTimerPanel"] = Color.Parse(custom?.PsTimerPanel ?? UiLabAppearance.PsTimerPanel),
            ["mapControlPanel"] = Color.Parse(custom?.MapControlPanel ?? UiLabAppearance.MapControlPanel),
            ["pcFree"] = Color.Parse(custom?.PcFree ?? UiLabAppearance.PcFree),
            ["pcLoggedIn"] = Color.Parse(custom?.PcLoggedIn ?? UiLabAppearance.PcLoggedIn),
            ["pcSession"] = Color.Parse(custom?.PcSession ?? UiLabAppearance.PcSession),
            ["pcCleaned"] = Color.Parse(custom?.PcCleaned ?? UiLabAppearance.PcCleaned),
            ["pcService"] = Color.Parse(custom?.PcService ?? UiLabAppearance.PcService),
            ["pcShellOff"] = Color.Parse(custom?.PcShellOff ?? UiLabAppearance.PcShellOff),
            ["pcConnectionLost"] = Color.Parse(custom?.PcConnectionLost ?? UiLabAppearance.PcConnectionLost),
            ["pcOffline"] = Color.Parse(custom?.PcOffline ?? UiLabAppearance.PcOffline)
        };
        var paletteButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        var paletteSwatches = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        var statusPreviewCards = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        var statusPreviewLabels = new Dictionary<string, TextBlock>(StringComparer.OrdinalIgnoreCase);
        var panelPreviewCards = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        var selectedKey = "accent";
        var updatingPicker = false;
        var pickerTitle = new TextBlock { Foreground = Brush("#f5f7ff"), FontSize = 18, FontWeight = FontWeight.SemiBold };
        var pickerHint = new TextBlock { Foreground = Brush("#8f9aad"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
        var spectrum = new ColorSpectrum
        {
            HsvColor = colors[selectedKey].ToHsv(),
            Height = 220,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Shape = ColorSpectrumShape.Box,
            Components = ColorSpectrumComponents.SaturationValue,
            CornerRadius = new CornerRadius(6)
        };
        var hueSlider = new Avalonia.Controls.Primitives.ColorSlider
        {
            HsvColor = colors[selectedKey].ToHsv(),
            ColorModel = ColorModel.Hsva,
            ColorComponent = ColorComponent.Component1,
            Orientation = Orientation.Horizontal,
            Height = 36,
            IsAlphaVisible = false,
            IsPerceptive = true
        };
        var redInput = FineColorInput(colors[selectedKey].R);
        var greenInput = FineColorInput(colors[selectedKey].G);
        var blueInput = FineColorInput(colors[selectedKey].B);
        var colorValue = new TextBlock
        {
            Foreground = Brush("#c9d3e6"),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        UiCustomPalette CurrentPalette() => new(
            ToHex(colors["accent"]),
            ToHex(colors["background"]),
            ToHex(colors["surface"]),
            ToHex(colors["border"]),
            ToHex(colors["text"]),
            ToHex(colors["dashboardPanel"]),
            ToHex(colors["shopPanel"]),
            ToHex(colors["psTimerPanel"]),
            ToHex(colors["mapControlPanel"]),
            ToHex(colors["pcFree"]),
            ToHex(colors["pcLoggedIn"]),
            ToHex(colors["pcSession"]),
            ToHex(colors["pcCleaned"]),
            ToHex(colors["pcService"]),
            ToHex(colors["pcShellOff"]),
            ToHex(colors["pcConnectionLost"]),
            ToHex(colors["pcOffline"]),
            motif,
            backgroundImage,
            backgroundOpacity,
            panelOpacity,
            pcCardOpacity);

        void ApplyPalette(UiCustomPalette palette)
        {
            colors["accent"] = Color.Parse(palette.Accent);
            colors["background"] = Color.Parse(palette.Background);
            colors["surface"] = Color.Parse(palette.Surface);
            colors["border"] = Color.Parse(palette.Border);
            colors["text"] = Color.Parse(palette.Text);
            colors["dashboardPanel"] = Color.Parse(palette.DashboardPanel);
            colors["shopPanel"] = Color.Parse(palette.ShopPanel);
            colors["psTimerPanel"] = Color.Parse(palette.PsTimerPanel);
            colors["mapControlPanel"] = Color.Parse(palette.MapControlPanel);
            colors["pcFree"] = Color.Parse(palette.PcFree);
            colors["pcLoggedIn"] = Color.Parse(palette.PcLoggedIn);
            colors["pcSession"] = Color.Parse(palette.PcSession);
            colors["pcCleaned"] = Color.Parse(palette.PcCleaned);
            colors["pcService"] = Color.Parse(palette.PcService);
            colors["pcShellOff"] = Color.Parse(palette.PcShellOff);
            colors["pcConnectionLost"] = Color.Parse(palette.PcConnectionLost);
            colors["pcOffline"] = Color.Parse(palette.PcOffline);
            motif = palette.Motif;
            backgroundImage = palette.BackgroundImage;
            backgroundOpacity = palette.BackgroundOpacity;
            panelOpacity = palette.PanelOpacity;
            pcCardOpacity = palette.PcCardOpacity is >= 0 and <= 1
                ? palette.PcCardOpacity
                : palette.Motif.Equals("dragon", StringComparison.OrdinalIgnoreCase) ? 224d / 255 : 46d / 255;
        }

        void RenderStatusPreviews()
        {
            foreach (var pair in statusPreviewCards)
            {
                var color = colors[pair.Key];
                pair.Value.BorderBrush = new SolidColorBrush(color);
                pair.Value.Background = new SolidColorBrush(Color.FromArgb(
                    (byte)Math.Round(255 * pcCardOpacity), color.R, color.G, color.B));
                statusPreviewLabels[pair.Key].Foreground = new SolidColorBrush(color);
            }
        }

        void RenderPanelPreviews()
        {
            foreach (var pair in panelPreviewCards)
                pair.Value.Background = new SolidColorBrush(colors[pair.Key]);
        }

        void RenderSelectedColor()
        {
            var color = colors[selectedKey];
            paletteSwatches[selectedKey].Background = new SolidColorBrush(color);
            colorValue.Text = $"RGB {color.R}, {color.G}, {color.B}  ·  {ToHex(color)}";
            redInput.Value = color.R;
            greenInput.Value = color.G;
            blueInput.Value = color.B;
            UiLabAppearance.PreviewCustomPalette(CurrentPalette());
            ApplyAppearanceBrushes();
            RenderStatusPreviews();
            RenderPanelPreviews();
            if (screen == "map") RefreshMap();
        }

        void ApplyHsv(HsvColor hsv)
        {
            if (updatingPicker) return;
            updatingPicker = true;
            spectrum.HsvColor = hsv;
            hueSlider.HsvColor = hsv;
            colors[selectedKey] = hsv.ToRgb();
            RenderSelectedColor();
            updatingPicker = false;
        }

        void SelectColor(string key, string titleText, string hint)
        {
            selectedKey = key;
            pickerTitle.Text = titleText;
            pickerHint.Text = hint;
            foreach (var pair in paletteButtons)
            {
                pair.Value.Background = pair.Key == key ? SoftAccent : Brush("#151d28");
                pair.Value.BorderBrush = pair.Key == key ? BrightAccent : Brush("#263244");
            }

            ApplyHsv(colors[key].ToHsv());
        }

        spectrum.ColorChanged += (_, _) =>
        {
            if (updatingPicker) return;
            ApplyHsv(spectrum.HsvColor);
        };
        hueSlider.ColorChanged += (_, _) =>
        {
            if (updatingPicker) return;
            var current = spectrum.HsvColor;
            ApplyHsv(new HsvColor(1, hueSlider.HsvColor.H, current.S, current.V));
        };
        void FineColorChanged()
        {
            if (updatingPicker || redInput.Value is not { } red || greenInput.Value is not { } green || blueInput.Value is not { } blue)
                return;
            ApplyHsv(Color.FromRgb((byte)red, (byte)green, (byte)blue).ToHsv());
        }
        redInput.ValueChanged += (_, _) => FineColorChanged();
        greenInput.ValueChanged += (_, _) => FineColorChanged();
        blueInput.ValueChanged += (_, _) => FineColorChanged();

        Button PaletteOption(string key, string titleText, string hint)
        {
            var swatch = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(colors[key]),
                BorderBrush = Brush("#334052"),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center
            };
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
            content.Children.Add(swatch);
            var labels = new StackPanel
            {
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = titleText, Foreground = Brush("#f5f7ff"), FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = hint, Foreground = Brush("#8f9aad"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis }
                }
            };
            Grid.SetColumn(labels, 1);
            content.Children.Add(labels);

            var button = new Button
            {
                Height = 58,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(10, 0),
                Background = Brush("#151d28"),
                BorderBrush = Brush("#263244"),
                Content = content
            };
            button.Click += (_, _) => SelectColor(key, titleText, hint);
            paletteButtons[key] = button;
            paletteSwatches[key] = swatch;
            return button;
        }

        Border StatusPreview(string key, string number, string title, string detail)
        {
            var colorText = new TextBlock
            {
                Text = number,
                Foreground = new SolidColorBrush(colors[key]),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            };
            var card = new Border
            {
                Height = 92,
                Margin = new Thickness(4),
                Padding = new Thickness(10, 8),
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(2),
                Child = new StackPanel
                {
                    Spacing = 5,
                    Children =
                    {
                        colorText,
                        new TextBlock { Text = title, Foreground = Brush("#f5f7ff"), FontSize = 12, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock { Text = detail, Foreground = Brush("#8f9aad"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis }
                    }
                }
            };
            statusPreviewCards[key] = card;
            statusPreviewLabels[key] = colorText;
            return card;
        }

        Border PanelPreview(string key, string title, string detail)
        {
            var card = new Border
            {
                Height = 72,
                Margin = new Thickness(4),
                Padding = new Thickness(12, 9),
                CornerRadius = new CornerRadius(7),
                BorderBrush = Brush("#263244"),
                BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    Spacing = 5,
                    Children =
                    {
                        new TextBlock { Text = title, Foreground = Brush("#f5f7ff"), FontSize = 12, FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = detail, Foreground = Brush("#8f9aad"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis }
                    }
                }
            };
            panelPreviewCards[key] = card;
            return card;
        }

        var panelPreviewGrid = new UniformGrid { Columns = 4, Rows = 1 };
        panelPreviewGrid.Children.Add(PanelPreview("dashboardPanel", "Дашборд", "Выручка и показатели"));
        panelPreviewGrid.Children.Add(PanelPreview("shopPanel", "Магазин", "Клиент и корзина"));
        panelPreviewGrid.Children.Add(PanelPreview("psTimerPanel", "Таймеры PS", "PS1 · осталось 02:15"));
        panelPreviewGrid.Children.Add(PanelPreview("mapControlPanel", "Карта клуба", "Управление выбранным ПК"));
        RenderPanelPreviews();

        var statusPreviewGrid = new UniformGrid { Columns = 4, Rows = 2 };
        statusPreviewGrid.Children.Add(StatusPreview("pcFree", "04", "Свободен", "ПК включён"));
        statusPreviewGrid.Children.Add(StatusPreview("pcLoggedIn", "31", "Клиент вошёл", "без тарифа"));
        statusPreviewGrid.Children.Add(StatusPreview("pcSession", "05", "Активная сессия", "до 19:55"));
        statusPreviewGrid.Children.Add(StatusPreview("pcCleaned", "20", "Убран", "готов к посадке"));
        statusPreviewGrid.Children.Add(StatusPreview("pcService", "14", "Обслуживание", "высокий доступ"));
        statusPreviewGrid.Children.Add(StatusPreview("pcShellOff", "24", "Shell отключён", "нужна проверка"));
        statusPreviewGrid.Children.Add(StatusPreview("pcConnectionLost", "09", "Нет связи", "сеанс прерван"));
        statusPreviewGrid.Children.Add(StatusPreview("pcOffline", "18", "Выключен", "офлайн"));
        RenderStatusPreviews();

        var interfaceOptions = new (string Key, string Title, string Hint)[]
        {
            ("accent", "Акцент", "Кнопки и активные состояния"),
            ("background", "Фон", "Основной фон приложения"),
            ("surface", "Поверхность", "Панели, таблицы и карточки"),
            ("border", "Граница", "Разделители и контуры"),
            ("text", "Текст", "Основной читаемый текст")
        };
        var panelOptions = new (string Key, string Title, string Hint)[]
        {
            ("dashboardPanel", "Панели дашборда", "Карточки показателей и таблиц дашборда"),
            ("shopPanel", "Панели магазина", "Клиент, ассортимент и корзина"),
            ("psTimerPanel", "Панели таймеров PS", "Карточки активных и завершённых таймеров"),
            ("mapControlPanel", "Панель карты клуба", "Информация и кнопки управления выбранным ПК")
        };
        var statusOptions = new (string Key, string Title, string Hint)[]
        {
            ("pcFree", "Включён и свободен", "ПК работает, клиент не авторизован"),
            ("pcLoggedIn", "Клиент авторизован", "Клиент вошёл, но тариф ещё не запущен"),
            ("pcSession", "Активная сессия", "Клиент использует оплаченный тариф"),
            ("pcCleaned", "ПК убран", "Место подготовлено для следующего клиента"),
            ("pcService", "Обслуживание", "Сервисный режим или высокий доступ"),
            ("pcShellOff", "Shell отключён", "Оболочка снята или заблокирована"),
            ("pcConnectionLost", "Потеря связи", "Связь пропала во время активной сессии"),
            ("pcOffline", "ПК выключен", "Компьютер находится офлайн")
        };
        var paletteList = new StackPanel { Spacing = 8 };
        var interfaceGroupButton = new Button { Content = "Интерфейс", Height = 40, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        var panelGroupButton = new Button { Content = "Панели", Height = 40, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        var statusGroupButton = new Button { Content = "Статусы ПК", Height = 40, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        var showingGroup = "interface";

        void RenderPaletteGroup(string group)
        {
            showingGroup = group;
            paletteButtons.Clear();
            paletteSwatches.Clear();
            paletteList.Children.Clear();
            var options = group switch
            {
                "panels" => panelOptions,
                "statuses" => statusOptions,
                _ => interfaceOptions
            };
            foreach (var option in options)
                paletteList.Children.Add(PaletteOption(option.Key, option.Title, option.Hint));

            foreach (var pair in new[]
                     {
                         (Button: interfaceGroupButton, Active: group == "interface"),
                         (Button: panelGroupButton, Active: group == "panels"),
                         (Button: statusGroupButton, Active: group == "statuses")
                     })
            {
                pair.Button.Background = pair.Active ? SoftAccent : Brush("#151d28");
                pair.Button.BorderBrush = pair.Active ? BrightAccent : Brush("#263244");
            }
            SelectColor(options[0].Key, options[0].Title, options[0].Hint);
        }

        interfaceGroupButton.Click += (_, _) => RenderPaletteGroup("interface");
        panelGroupButton.Click += (_, _) => RenderPaletteGroup("panels");
        statusGroupButton.Click += (_, _) => RenderPaletteGroup("statuses");
        var groupTabs = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 8,
            Children = { interfaceGroupButton, panelGroupButton, statusGroupButton }
        };
        Grid.SetColumn(panelGroupButton, 1);
        Grid.SetColumn(statusGroupButton, 2);
        var paletteLeft = new StackPanel { Spacing = 10, Children = { groupTabs, paletteList } };

        var dialog = new ModalWindow
        {
            Title = "Палитра интерфейса",
            Width = 1480,
            Height = 1000,
            MinWidth = 1100,
            MinHeight = 780,
            CanResize = true,
            SystemDecorations = SystemDecorations.None,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#080d12")
        };

        var savedPaletteBox = new ComboBox
        {
            Height = 40,
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550"),
            Padding = new Thickness(10, 0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var savedPaletteStatus = new TextBlock
        {
            Foreground = Brush("#8f9aad"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var refreshingSavedPaletteSelection = false;
        var activePaletteName = UiLabAppearance.ActivePaletteName;
        void RefreshBackdropControls()
        {
            if (backgroundOpacitySlider == null || panelTransparencySlider == null || pcCardTransparencySlider == null ||
                backgroundFileText == null || backgroundOpacityText == null || panelTransparencyText == null ||
                pcCardTransparencyText == null)
                return;
            updatingBackdropControls = true;
            backgroundOpacitySlider.Value = Math.Round(backgroundOpacity * 100);
            panelTransparencySlider.Value = Math.Round((1 - panelOpacity) * 100);
            pcCardTransparencySlider.Value = Math.Round((1 - pcCardOpacity) * 100);
            backgroundFileText.Text = string.IsNullOrWhiteSpace(backgroundImage)
                ? "Фон не выбран"
                : backgroundImage.StartsWith("avares://", StringComparison.OrdinalIgnoreCase)
                    ? "Встроенный фон темы"
                    : Path.GetFileName(backgroundImage);
            backgroundOpacityText.Text = $"Видимость фона: {backgroundOpacitySlider.Value:0}%";
            panelTransparencyText.Text = $"Прозрачность панелей: {panelTransparencySlider.Value:0}%";
            pcCardTransparencyText.Text = $"Прозрачность карточек ПК: {pcCardTransparencySlider.Value:0}%";
            updatingBackdropControls = false;
        }
        void RefreshSavedPalettes(string? selectedName = null)
        {
            refreshingSavedPaletteSelection = true;
            var names = UiLabAppearance.SavedPalettes.Select(x => x.Name).OrderBy(x => x).ToList();
            savedPaletteBox.ItemsSource = names;
            if (selectedName != null)
                savedPaletteBox.SelectedItem = names.FirstOrDefault(x => x.Equals(selectedName, StringComparison.OrdinalIgnoreCase));
            else
                savedPaletteBox.SelectedItem = names.FirstOrDefault(x => x.Equals(UiLabAppearance.ActivePaletteName, StringComparison.OrdinalIgnoreCase));
            refreshingSavedPaletteSelection = false;
        }
        void ActivateSavedPalette(string name)
        {
            var saved = UiLabAppearance.SavedPalettes.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (saved == null) return;
            ApplyPalette(saved.Palette);
            UiLabAppearance.ActivateNamedPalette(name);
            originalPalette = saved.Palette;
            RefreshBackdropControls();
            RenderPaletteGroup(showingGroup);
            ApplyAppearanceBrushes();
            Dispatcher.UIThread.Post(RebuildShell);
            activePaletteName = saved.Name;
            savedPaletteStatus.Text = "Активна: " + saved.Name;
        }

        async Task<bool> ConfirmPaletteSwitchAsync(string name)
        {
            var confirmation = new ModalWindow
            {
                Title = "Смена темы",
                Width = 500,
                Height = 230,
                CanResize = false,
                SystemDecorations = SystemDecorations.None,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#0d1217")
            };
            var yes = AccentButton("Переключить", () => confirmation.Close(true));
            var no = GhostButton("Остаться", () => confirmation.Close(false));
            var close = GhostButton("×", () => confirmation.Close(false));
            close.Width = 42;
            close.Height = 42;
            var header = new Grid
            {
                Height = 48,
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Background = Brush("#111820"),
                Children =
                {
                    new TextBlock
                    {
                        Text = "Смена темы",
                        Margin = new Thickness(18, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Brush("#f5f7ff"),
                        FontWeight = FontWeight.SemiBold
                    },
                    close
                }
            };
            Grid.SetColumn(close, 1);
            header.PointerPressed += (_, e) =>
            {
                if (e.Source is Button || (e.Source as Control)?.FindAncestorOfType<Button>() != null) return;
                if (e.GetCurrentPoint(header).Properties.IsLeftButtonPressed) confirmation.BeginMoveDrag(e);
            };
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { no, yes }
            };
            var content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 18,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Переключить тему на «{name}»? Несохранённые изменения текущей темы будут отменены.",
                        Foreground = Brush("#c9d3e6"),
                        TextWrapping = TextWrapping.Wrap
                    },
                    actions
                }
            };
            var root = new Grid { RowDefinitions = new RowDefinitions("48,*"), Children = { header, content } };
            Grid.SetRow(content, 1);
            confirmation.Content = root;
            return await confirmation.ShowDialog<bool>(dialog);
        }

        savedPaletteBox.SelectionChanged += async (_, _) =>
        {
            if (refreshingSavedPaletteSelection || savedPaletteBox.SelectedItem is not string name ||
                name.Equals(activePaletteName, StringComparison.OrdinalIgnoreCase))
                return;
            if (!await ConfirmPaletteSwitchAsync(name))
            {
                RefreshSavedPalettes(activePaletteName);
                return;
            }
            ActivateSavedPalette(name);
        };

        var savedPaletteLabel = new TextBlock
        {
            Text = "Сохранённая тема",
            Foreground = Brush("#f5f7ff"),
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var savedPaletteBar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,320,*"),
            ColumnSpacing = 12,
            Children = { savedPaletteLabel, savedPaletteBox, savedPaletteStatus }
        };
        Grid.SetColumn(savedPaletteBox, 1);
        Grid.SetColumn(savedPaletteStatus, 2);
        RefreshSavedPalettes();
        savedPaletteStatus.Text = activePaletteName.Length == 0 ? "Активна базовая палитра" : "Активна: " + activePaletteName;

        Button BackdropButton(string text) => new()
        {
            Content = text,
            Height = 38,
            Padding = new Thickness(14, 0),
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550"),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        backgroundFileText = new TextBlock
        {
            Foreground = Brush("#c9d3e6"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        backgroundOpacityText = new TextBlock { Foreground = Brush("#a9b2c5"), FontSize = 12 };
        panelTransparencyText = new TextBlock { Foreground = Brush("#a9b2c5"), FontSize = 12 };
        pcCardTransparencyText = new TextBlock { Foreground = Brush("#a9b2c5"), FontSize = 12 };
        backgroundOpacitySlider = new Slider { Minimum = 0, Maximum = 100, Height = 30 };
        panelTransparencySlider = new Slider { Minimum = 0, Maximum = 92, Height = 30 };
        pcCardTransparencySlider = new Slider { Minimum = 0, Maximum = 92, Height = 30 };
        var chooseBackground = BackdropButton("Выбрать фон");
        var removeBackground = BackdropButton("Убрать фон");

        void PreviewBackdrop(bool rebuild)
        {
            UiLabAppearance.PreviewCustomPalette(CurrentPalette());
            ApplyAppearanceBrushes();
            if (rebuild)
                Dispatcher.UIThread.Post(RebuildShell);
        }

        chooseBackground.Click += async (_, _) =>
        {
            var files = await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите фоновое изображение",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Изображения")
                    {
                        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"]
                    }
                ]
            });
            var file = files.FirstOrDefault();
            if (file == null) return;
            backgroundImage = file.Path.LocalPath;
            if (backgroundOpacity <= 0) backgroundOpacity = 0.24;
            RefreshBackdropControls();
            PreviewBackdrop(true);
        };
        removeBackground.Click += (_, _) =>
        {
            backgroundImage = "";
            backgroundOpacity = 0;
            RefreshBackdropControls();
            PreviewBackdrop(true);
        };
        backgroundOpacitySlider.ValueChanged += (_, _) =>
        {
            if (updatingBackdropControls) return;
            backgroundOpacity = backgroundOpacitySlider.Value / 100;
            backgroundOpacityText.Text = $"Видимость фона: {backgroundOpacitySlider.Value:0}%";
            PreviewBackdrop(false);
        };
        panelTransparencySlider.ValueChanged += (_, _) =>
        {
            if (updatingBackdropControls) return;
            panelOpacity = 1 - panelTransparencySlider.Value / 100;
            panelTransparencyText.Text = $"Прозрачность панелей: {panelTransparencySlider.Value:0}%";
            PreviewBackdrop(false);
        };
        pcCardTransparencySlider.ValueChanged += (_, _) =>
        {
            if (updatingBackdropControls) return;
            pcCardOpacity = 1 - pcCardTransparencySlider.Value / 100;
            pcCardTransparencyText.Text = $"Прозрачность карточек ПК: {pcCardTransparencySlider.Value:0}%";
            UiLabAppearance.PreviewCustomPalette(CurrentPalette());
            RenderStatusPreviews();
            if (screen == "map") RefreshMap();
        };
        RefreshBackdropControls();

        var backgroundActions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8,
            Children = { backgroundFileText, chooseBackground, removeBackground }
        };
        Grid.SetColumn(chooseBackground, 1);
        Grid.SetColumn(removeBackground, 2);
        var opacityControls = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 24
        };
        var backgroundOpacityControl = new StackPanel
        {
            Spacing = 2,
            Children = { backgroundOpacityText, backgroundOpacitySlider }
        };
        var panelOpacityControl = new StackPanel
        {
            Spacing = 2,
            Children = { panelTransparencyText, panelTransparencySlider }
        };
        var pcCardOpacityControl = new StackPanel
        {
            Spacing = 2,
            Children = { pcCardTransparencyText, pcCardTransparencySlider }
        };
        opacityControls.Children.Add(backgroundOpacityControl);
        Grid.SetColumn(panelOpacityControl, 1);
        opacityControls.Children.Add(panelOpacityControl);
        Grid.SetColumn(pcCardOpacityControl, 2);
        opacityControls.Children.Add(pcCardOpacityControl);
        var backdropSettings = new Border
        {
            Background = Brush("#111820"),
            BorderBrush = Brush("#263244"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Фон и прозрачность", Foreground = Brush("#f5f7ff"), FontWeight = FontWeight.SemiBold },
                    backgroundActions,
                    opacityControls
                }
            }
        };
        var save = new Button
        {
            Content = "Сохранить активную тему",
            Height = 42,
            Padding = new Thickness(18, 0),
            Background = PrimaryAccent,
            Foreground = Brush("#f5f7ff"),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var reset = new Button
        {
            Content = "Вернуть цвета стиля",
            Height = 42,
            Padding = new Thickness(18, 0),
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550"),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var paletteCommitted = false;
        var cancel = new Button
        {
            Content = "Отмена",
            Height = 42,
            Padding = new Thickness(18, 0),
            Background = Brushes.Transparent,
            Foreground = Brush("#a9b2c5"),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        save.Click += (_, _) =>
        {
            var palette = CurrentPalette();
            if (activePaletteName.Length == 0)
                UiLabAppearance.SetCustomPalette(palette);
            else
                UiLabAppearance.SaveNamedPalette(activePaletteName, palette);
            originalPalette = UiLabAppearance.CustomPalette;
            paletteCommitted = true;
            dialog.Close();
            ApplyAppearanceBrushes();
            Dispatcher.UIThread.Post(RebuildShell);
        };
        reset.Click += (_, _) =>
        {
            ApplyPalette(new UiCustomPalette(
                preset.Accent,
                preset.Colors["#080d12"],
                preset.Colors["#111820"],
                preset.Colors["#263244"],
                preset.Colors["#f5f7ff"],
                Motif: motif));
            RefreshBackdropControls();
            RenderPaletteGroup(showingGroup);
            UiLabAppearance.PreviewCustomPalette(CurrentPalette());
            ApplyAppearanceBrushes();
            Dispatcher.UIThread.Post(RebuildShell);
            savedPaletteStatus.Text = "Показаны цвета стиля; сохранённая тема не изменена";
        };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Closing += (_, _) =>
        {
            if (paletteCommitted) return;
            UiLabAppearance.PreviewCustomPalette(originalPalette);
            ApplyAppearanceBrushes();
            Dispatcher.UIThread.Post(RebuildShell);
        };

        var fineGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 10 };
        fineGrid.Children.Add(FineColorChannel("R", "#ff5252", redInput));
        var greenChannel = FineColorChannel("G", "#35d07f", greenInput);
        Grid.SetColumn(greenChannel, 1);
        fineGrid.Children.Add(greenChannel);
        var blueChannel = FineColorChannel("B", "#4ea1ff", blueInput);
        Grid.SetColumn(blueChannel, 2);
        fineGrid.Children.Add(blueChannel);

        var picker = new Border
        {
            Background = Brush("#111820"),
            BorderBrush = Brush("#263244"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    pickerTitle,
                    pickerHint,
                    spectrum,
                    hueSlider,
                    colorValue,
                    fineGrid
                }
            }
        };
        var statusPreview = new Border
        {
            Background = Brush("#111820"),
            BorderBrush = Brush("#263244"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Живой пример панелей", Foreground = Brush("#f5f7ff"), FontWeight = FontWeight.SemiBold },
                    panelPreviewGrid,
                    new TextBlock { Text = "Карта клуба", Foreground = Brush("#f5f7ff"), FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 4, 0, 0) },
                    statusPreviewGrid
                }
            }
        };
        var pickerColumn = new StackPanel { Spacing = 12, Children = { picker, statusPreview } };
        var paletteGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("320,*"), ColumnSpacing = 16 };
        paletteGrid.Children.Add(paletteLeft);
        Grid.SetColumn(pickerColumn, 1);
        paletteGrid.Children.Add(pickerColumn);

        var paletteHeading = new TextBlock
        {
            Text = "Своя палитра",
            Foreground = Brush("#f5f7ff"),
            FontSize = 22,
            FontWeight = FontWeight.SemiBold
        };
        var paletteDescription = new TextBlock
        {
            Text = "Настройте интерфейс, фон, прозрачность панелей и состояния компьютеров. Изменения сразу видны в приложении.",
            Foreground = Brush("#8f9aad"),
            TextWrapping = TextWrapping.Wrap
        };
        var paletteScroll = new ScrollViewer
        {
            Content = paletteGrid,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var paletteFooter = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { cancel, reset, save }
        };
        var paletteRoot = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto"),
            RowSpacing = 12,
            Children = { paletteHeading, paletteDescription, savedPaletteBar, backdropSettings, paletteScroll, paletteFooter }
        };
        Grid.SetRow(paletteDescription, 1);
        Grid.SetRow(savedPaletteBar, 2);
        Grid.SetRow(backdropSettings, 3);
        Grid.SetRow(paletteScroll, 4);
        Grid.SetRow(paletteFooter, 5);

        var paletteContent = new Border
        {
            Padding = new Thickness(24),
            Background = Brush("#0d1217"),
            Child = paletteRoot
        };
        Button PaletteWindowButton(string text) => new()
        {
            Content = text,
            Background = Brushes.Transparent,
            Foreground = Brush("#c9d3e6"),
            BorderBrush = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var minimizeWindow = PaletteWindowButton("—");
        var maximizeWindow = PaletteWindowButton("□");
        var closeWindow = PaletteWindowButton("×");
        foreach (var button in new[] { minimizeWindow, maximizeWindow, closeWindow })
        {
            button.Width = 48;
            button.Height = 48;
            button.Padding = new Thickness(0);
            button.BorderThickness = new Thickness(0);
            button.CornerRadius = new CornerRadius(0);
        }
        closeWindow.Foreground = Brush("#ff6b78");
        minimizeWindow.Click += (_, _) => dialog.WindowState = WindowState.Minimized;
        maximizeWindow.Click += (_, _) => dialog.WindowState = dialog.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        closeWindow.Click += (_, _) => dialog.Close();
        var windowControls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { minimizeWindow, maximizeWindow, closeWindow }
        };
        var windowTitle = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(16, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                AppMark(28),
                new TextBlock
                {
                    Text = "Палитра интерфейса",
                    Foreground = Brush("#f5f7ff"),
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
        var titleBar = new Grid
        {
            Height = 48,
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Background = Brush("#111820"),
            Children = { windowTitle, windowControls }
        };
        Grid.SetColumn(windowControls, 1);
        titleBar.PointerPressed += (_, e) =>
        {
            if (e.Source is Button || (e.Source as Control)?.FindAncestorOfType<Button>() != null) return;
            if (e.GetCurrentPoint(titleBar).Properties.IsLeftButtonPressed) dialog.BeginMoveDrag(e);
        };
        var windowRoot = new Grid
        {
            RowDefinitions = new RowDefinitions("48,*"),
            Children = { titleBar, paletteContent }
        };
        Grid.SetRow(paletteContent, 1);
        dialog.Content = windowRoot;
        RenderPaletteGroup("interface");
        await dialog.ShowDialog(this);
    }

    private static NumericUpDown FineColorInput(byte value) => new()
    {
        Value = value,
        Minimum = 0,
        Maximum = 255,
        Increment = 1,
        FormatString = "0",
        Height = 38,
        MinWidth = 118,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Background = Brush("#182231"),
        Foreground = Brush("#f5f7ff"),
        BorderBrush = Brush("#2b3550"),
        FontSize = 13,
        Padding = new Thickness(8, 0),
        TextAlignment = TextAlignment.Center
    };

    private static Control FineColorChannel(string label, string color, NumericUpDown input)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*"), ColumnSpacing = 8 };
        grid.Children.Add(new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.Parse(color)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.Parse("#ffffff")),
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        Grid.SetColumn(input, 1);
        grid.Children.Add(input);
        return new Border
        {
            Background = Brush("#151d28"),
            BorderBrush = Brush("#263244"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Child = grid
        };
    }

    private static AvaloniaBitmap? LoadDragonArtwork(string assetName)
    {
        try
        {
            return new AvaloniaBitmap(AssetLoader.Open(new Uri(
                "avares://SmartShellCleaner.Avalonia/Assets/" + assetName)));
        }
        catch
        {
            return null;
        }
    }

    private Border DragonArtworkLayer(
        AvaloniaBitmap? artwork,
        double width,
        double height,
        double opacity,
        Stretch stretch = Stretch.Uniform)
    {
        return new Border
        {
            Width = width,
            Height = height,
            Background = BrightAccent,
            Opacity = artwork is null ? 0 : opacity,
            OpacityMask = artwork is null
                ? null
                : new ImageBrush
                {
                    Source = artwork,
                    Stretch = stretch
                },
            IsHitTestVisible = false
        };
    }

    private Border DragonSilhouette(double width, double opacity) =>
        DragonArtworkLayer(DragonArtwork.Value, width, width * 0.72, opacity);

    private Border DragonHead(double width, double height, double opacity) =>
        DragonArtworkLayer(DragonHeadArtwork.Value, width, height, opacity);

    private static AvaloniaBitmap? LoadThemeBackground(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        if (ThemeBackgroundArtworks.TryGetValue(source, out var cached)) return cached;
        try
        {
            using var stream = source.StartsWith("avares://", StringComparison.OrdinalIgnoreCase)
                ? AssetLoader.Open(new Uri(source))
                : File.OpenRead(source);
            cached = new AvaloniaBitmap(stream);
        }
        catch
        {
            cached = null;
        }
        ThemeBackgroundArtworks[source] = cached;
        return cached;
    }

    private Grid BuildThemeBackdrop()
    {
        var layer = new Grid
        {
            ClipToBounds = true,
            IsHitTestVisible = false
        };

        var artwork = LoadThemeBackground(UiLabAppearance.BackgroundImage);
        if (artwork is not null)
        {
            themeBackdropImage = new AvaloniaImage
            {
                Source = artwork,
                Stretch = Stretch.Uniform,
                Opacity = UiLabAppearance.BackgroundOpacity,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = false
            };
            layer.Children.Add(themeBackdropImage);
        }

        return layer;
    }

    private Control WithDragonWatermark(Control content, double size, double opacity)
    {
        if (!UiLabAppearance.HasDragonMotif || DragonHeadArtwork.Value is null)
            return content;

        var layer = new Grid { ClipToBounds = true };
        var dragon = DragonHead(size * 0.86, size, opacity);
        dragon.HorizontalAlignment = HorizontalAlignment.Right;
        dragon.VerticalAlignment = VerticalAlignment.Top;
        dragon.Margin = new Thickness(0, 8, 8, 0);
        layer.Children.Add(dragon);
        layer.Children.Add(content);
        return layer;
    }

    private Border AppMark(double size)
    {
        Control mark;
        if (UiLabAppearance.HasDragonMotif && DragonHeadArtwork.Value is not null)
        {
            var dragon = DragonHead(size * 0.92, size * 0.94, 1);
            dragon.HorizontalAlignment = HorizontalAlignment.Center;
            dragon.VerticalAlignment = VerticalAlignment.Center;
            mark = dragon;
        }
        else if (UiLabAppearance.HasSpiderMotif || UiLabAppearance.HasCatMotif)
        {
            var icon = UiLabAppearance.HasSpiderMotif ? "spider" : "cat";
            var motif = DashboardIcon(icon, "#4ea1ff", size * 0.72);
            motif.HorizontalAlignment = HorizontalAlignment.Center;
            mark = motif;
        }
        else
        {
            mark = new TextBlock
            {
                Text = "S",
                Foreground = Brush("#4ea1ff"),
                FontWeight = FontWeight.Bold,
                FontSize = size * 0.56,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        var border = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(8),
            Background = Brush("#102a4d"),
            BorderBrush = Brush("#2f8cff"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 12, 0),
            Child = mark
        };
        if (UiLabAppearance.HasDragonMotif)
            ToolTip.SetTip(border, "Драконья палитра «Родион»");
        else if (UiLabAppearance.HasSpiderMotif)
            ToolTip.SetTip(border, "Паучья палитра «Соня»");
        else if (UiLabAppearance.HasCatMotif)
            ToolTip.SetTip(border, "Кошачья палитра «Артём»");
        return border;
    }

    private Button WindowButton(string text, Action action, bool danger = false)
    {
        var button = new Button
        {
            Content = text,
            Width = 48,
            Height = 38,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = danger ? Brush("#ff7b7b") : Brush("#c9d3e6"),
            FontSize = 16,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private StackPanel BuildSideContent()
    {
        selectedTitle.Foreground = Brush("#f5f7ff");
        selectedTitle.FontSize = 22;
        selectedTitle.FontWeight = FontWeight.SemiBold;

        selectedBody.Foreground = Brush("#a9b2c5");
        selectedBody.FontSize = 13;
        selectedBody.TextWrapping = TextWrapping.Wrap;
        selectedBody.Margin = new Thickness(0, 14, 0, 20);

        cleanedButton = AccentButton("Да, убран", () => _ = MarkSelectedCleanedAsync());
        singleSelectionActions = new StackPanel
        {
            Children =
            {
                cleanedButton,
                GhostButton("Панель управления", () => _ = OpenGroupedControlPanelAsync(selected)),
                GhostButton("Напомнить", () => ShowToast("Напоминание", "Оставил задачу уборки активной.")),
                GhostButton("Освободился", MarkSelectedFree),
                GhostButton("Занят", MarkSelectedBusy)
            }
        };
        bulkSelectionActions = new StackPanel { IsVisible = false };
        return new StackPanel
        {
            Children =
            {
                selectedTitle,
                selectedBody,
                singleSelectionActions,
                bulkSelectionActions,
                new TextBlock
                {
                    Text = "Зажмите ЛКМ на свободном месте и протяните рамку для выбора нескольких ПК. ПКМ открывает меню для всего выделения.",
                    Foreground = Brush("#778399"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 12, 0, 0)
                }
            }
        };
    }

    private void PopulateBulkSelectionActions(IReadOnlyCollection<PcRecord> hosts)
    {
        if (bulkSelectionActions == null) return;

        bulkSelectionActions.Children.Clear();
        bulkSelectionActions.Children.Add(new TextBlock
        {
            Text = "Уборка",
            Foreground = Brush("#a9b2c5"),
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        bulkSelectionActions.Children.Add(BulkActionButton("✓  Убран", hosts.Any(NeedsCleaning),
            () => _ = MarkSelectedCleanedAsync(), accent: true));
        bulkSelectionActions.Children.Add(BulkActionButton("◷  Напомнить", hosts.Any(x => x.CleaningStatus == "Нужно убрать"),
            () => ShowToast("Напоминание", "Задачи уборки для выбранных ПК оставлены активными.")));
        bulkSelectionActions.Children.Add(BulkActionButton("○  Освободился", true, MarkSelectedFree));
        bulkSelectionActions.Children.Add(BulkActionButton("●  Занят", true, MarkSelectedBusy));

        bulkSelectionActions.Children.Add(new TextBlock
        {
            Text = "Электропитание",
            Foreground = Brush("#a9b2c5"),
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 8, 0, 8)
        });
        var canPowerOn = hosts.Any(x => x.HostId > 0 && !x.Online && !x.Locked);
        var canControlOnline = hosts.Any(x => x.HostId > 0 && x.Online && !x.Locked);
        var canShutdown = hosts.Any(x => x.HostId > 0 && x.Online && !x.Locked);
        bulkSelectionActions.Children.Add(BulkActionButton("⏻  Включить", canPowerOn,
            () => _ = SendBulkHostCommandAsync(SelectedHosts(), "WOL")));
        bulkSelectionActions.Children.Add(BulkActionButton("⏼  Выключить", canShutdown,
            () => _ = SendBulkHostCommandAsync(SelectedHosts(), "SHUTDOWN"), danger: true));
        bulkSelectionActions.Children.Add(BulkActionButton("↻  Перезагрузить", canControlOnline,
            () => _ = SendBulkHostCommandAsync(SelectedHosts(), "REBOOT")));
        bulkSelectionActions.Children.Add(BulkActionButton("↪  Выйти из Windows", canControlOnline,
            () => _ = SendBulkHostCommandAsync(SelectedHosts(), "LOGOFF")));
    }

    private Button BulkActionButton(string text, bool enabled, Action action, bool accent = false, bool danger = false)
    {
        var button = accent ? AccentButton(text, action) : GhostButton(text, action);
        button.IsEnabled = enabled;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.Padding = new Thickness(14, 0);
        if (!enabled)
        {
            button.Opacity = 0.45;
            button.Foreground = Brush("#667085");
        }
        else if (danger)
        {
            button.Foreground = Brush("#ff7b7b");
        }
        return button;
    }

    private Button NavButton(string icon, string text, string target)
    {
        var rail = IsRailShell();
        var top = IsTopNavigationShell();
        var button = new Button
        {
            Height = rail ? (UiLabAppearance.CurrentKey == "material" ? 58 : 54) : top ? 44 : UiLabAppearance.CurrentKey == "suki" ? 44 : 38,
            Width = rail ? 68 : double.NaN,
            MinWidth = top ? 92 : 0,
            Padding = rail ? new Thickness(4, 5) : top ? new Thickness(14, 0) : new Thickness(14, 0, 10, 0),
            HorizontalAlignment = rail ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
            HorizontalContentAlignment = rail ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            BorderThickness = new Thickness(0),
            Content = SidebarNavLabel(icon, text),
            IsVisible = target != "cleaning-log" || CanViewCleaningAudit()
        };
        button.Click += (_, _) => SwitchScreen(target);
        navButtons[target] = button;
        ApplyNavButtonStyle(button, string.Equals(screen, target, StringComparison.OrdinalIgnoreCase));
        return button;
    }

    private StackPanel SidebarNavLabel(string icon, string text)
    {
        var rail = IsRailShell();
        var navIcon = DashboardIcon(icon, "#cbd3df", rail ? 20 : 18);
        navIcon.HorizontalAlignment = rail ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        return new StackPanel
        {
            Width = rail ? 60 : double.NaN,
            Orientation = rail ? Orientation.Vertical : Orientation.Horizontal,
            Spacing = rail ? 3 : 11,
            HorizontalAlignment = rail ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            Children =
            {
                navIcon,
                new TextBlock
                {
                    Text = text,
                    Width = rail ? 60 : double.NaN,
                    Foreground = Brush("#f5f7ff"),
                    FontWeight = rail ? FontWeight.Medium : FontWeight.SemiBold,
                    FontSize = rail ? 9.5 : 14,
                    TextAlignment = rail ? TextAlignment.Center : TextAlignment.Left,
                    HorizontalAlignment = rail ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
                    MaxWidth = rail ? 76 : double.PositiveInfinity,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
    }

    private void UpdateNavButtonStyles()
    {
        foreach (var item in navButtons)
            ApplyNavButtonStyle(item.Value, string.Equals(screen, item.Key, StringComparison.OrdinalIgnoreCase));

        if (!psTimerAttentionPending || !navButtons.TryGetValue("ps", out var psButton)) return;
        psButton.Background = Brush("#3b250f");
        psButton.BorderBrush = Brush("#ff9f1a");
        psButton.BorderThickness = new Thickness(1);
        if (psButton.Content is not StackPanel content) return;
        if (content.Children.OfType<PathIcon>().FirstOrDefault() is { } icon)
            icon.Foreground = Brush("#ff9f1a");
        if (content.Children.OfType<TextBlock>().FirstOrDefault() is { } label)
            label.Foreground = Brush("#ffb347");
    }

    private static void ApplyNavButtonStyle(Button button, bool active)
    {
        var theme = UiLabAppearance.CurrentKey;
        button.Background = theme switch
        {
            "semi" or "oled" => active ? Brush("#182231") : Brushes.Transparent,
            "suki" => active ? SoftAccent : Brushes.Transparent,
            "material" => active ? SoftAccent : Brushes.Transparent,
            "violet" => active ? DeepAccent : Brushes.Transparent,
            _ => Brush(active ? "#172b43" : "#111820")
        };
        button.BorderBrush = active ? BrightAccent : Brushes.Transparent;
        button.BorderThickness = theme switch
        {
            "semi" or "oled" when active => new Thickness(0, 0, 0, 2),
            "material" when active => new Thickness(3, 0, 0, 0),
            "suki" when active => new Thickness(1),
            _ => new Thickness(0)
        };
        if (button.Content is not StackPanel content) return;
        if (content.Children.OfType<PathIcon>().FirstOrDefault() is { } icon)
            icon.Foreground = active ? BrightAccent : Brush("#cbd3df");
        if (content.Children.OfType<TextBlock>().FirstOrDefault() is { } label)
            label.Foreground = active ? BrightAccent : Brush("#f5f7ff");
    }

    private Button SideButton(string icon, string text, Action action)
    {
        var button = new Button
        {
            Height = 36,
            Padding = new Thickness(14, 0, 10, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brush("#111820"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#111820"),
            Content = IconLabel(icon, text)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private Button AccentButton(string text, Action action)
    {
        var button = new Button
        {
            Height = 40,
            Margin = new Thickness(0, 0, 0, 10),
            Background = Brush("#2563eb"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3b82f6"),
            Content = text,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        button.Click += (_, _) => action();
        return button;
    }

    private Button GhostButton(string text, Action action)
    {
        var button = new Button
        {
            Height = 38,
            Margin = new Thickness(0, 0, 0, 10),
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550"),
            Content = text,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        button.Click += (_, _) => action();
        return button;
    }

    private StackPanel IconLabel(string icon, string text)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock { Text = icon, Width = 24, Foreground = Brush("#4ea1ff"), FontSize = 15, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }
            }
        };
    }

    private void OpenSmartShellMap()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://admin.smartshell.gg/map",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowToast("SmartShell", "Не удалось открыть сайт: " + ex.Message);
        }
    }

    private async Task<bool> OpenSmartShellClientRegistrationAsync()
    {
        var clientsUri = new Uri("https://admin.smartshell.gg/clients");
        var status = new TextBlock
        {
            Text = "Загрузка SmartShell...",
            Foreground = Brush("#8f9aad"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var webView = new WebView2
        {
            CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SmartShellCleaner",
                    "WebView2"),
                ProfileName = "SmartShellCleaner",
                EnabledDevTools = false
            },
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(8, 13, 18)
        };

        async Task OpenFormAsync()
        {
            if (webView.CoreWebView2 == null) return;
            try
            {
                const string script = """
                    (() => {
                      const visible = element => element && element.getClientRects().length > 0;
                      const title = Array.from(document.querySelectorAll('h1,h2,h3,[role="dialog"]'))
                        .find(element => visible(element) && element.textContent.includes('Создание клиента'));
                      if (title) return true;
                      const button = Array.from(document.querySelectorAll('button'))
                        .find(element => visible(element) && element.textContent.trim() === 'Создать клиента');
                      if (!button) return false;
                      button.click();
                      return true;
                    })()
                    """;
                var opened = await webView.ExecuteScriptAsync(script);
                status.Text = string.Equals(opened, "true", StringComparison.OrdinalIgnoreCase)
                    ? "Форма создания клиента"
                    : "SmartShell";
            }
            catch (Exception ex)
            {
                status.Text = "Ошибка страницы: " + ex.Message;
            }
        }

        webView.CoreWebView2InitializationCompleted += (_, e) =>
        {
            if (!e.IsSuccess)
                status.Text = "Не удалось запустить WebView2: " + e.InitializationException?.Message;
        };
        webView.NavigationCompleted += async (_, _) => await OpenFormAsync();

        var openForm = new Button
        {
            Content = "Открыть форму",
            Height = 36,
            Padding = new Thickness(14, 0),
            Background = Brush("#13243b"),
            BorderBrush = Brush("#183457"),
            Foreground = Brush("#4ea1ff"),
            FontWeight = FontWeight.SemiBold
        };
        openForm.Click += async (_, _) =>
        {
            if (webView.Source == clientsUri)
                await OpenFormAsync();
            else
                webView.Source = clientsUri;
        };

        var refresh = new Button
        {
            Content = "↻",
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brush("#2b3550"),
            Foreground = Brush("#4ea1ff"),
            FontSize = 17
        };
        ToolTip.SetTip(refresh, "Обновить страницу");
        refresh.Click += (_, _) => webView.Reload();

        var done = new Button
        {
            Content = "Готово",
            Height = 36,
            Padding = new Thickness(18, 0),
            Background = Brush("#2f8cff"),
            BorderBrush = Brush("#2f8cff"),
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold
        };
        var close = new Button
        {
            Content = "×",
            Width = 40,
            Height = 36,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = Brush("#c7d1df"),
            FontSize = 20
        };

        var titleBar = new Grid
        {
            Height = 58,
            ColumnDefinitions = new ColumnDefinitions("Auto,16,*,Auto,8,Auto,8,Auto"),
            Background = Brush("#111820"),
            Children =
            {
                new TextBlock
                {
                    Text = "Создание клиента",
                    Foreground = Brush("#f5f7ff"),
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(18, 0, 0, 0)
                },
                status,
                openForm,
                refresh,
                close
            }
        };
        Grid.SetColumn(status, 2);
        Grid.SetColumn(openForm, 3);
        Grid.SetColumn(refresh, 5);
        Grid.SetColumn(close, 7);

        var footer = new Border
        {
            Height = 58,
            Background = Brush("#111820"),
            BorderBrush = Brush("#26313d"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(14, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = done
        };
        done.HorizontalAlignment = HorizontalAlignment.Right;

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Background = Brush("#080d12"),
            Children = { titleBar, webView, footer }
        };
        Grid.SetRow(webView, 1);
        Grid.SetRow(footer, 2);

        var dialog = new ModalWindow
        {
            Title = "Создание клиента",
            Width = 1180,
            Height = 820,
            MinWidth = 900,
            MinHeight = 650,
            CanResize = true,
            SystemDecorations = SystemDecorations.None,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#080d12"),
            Content = root
        };
        titleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(titleBar).Properties.IsLeftButtonPressed)
                dialog.BeginMoveDrag(e);
        };
        close.Click += (_, _) => dialog.Close(false);
        done.Click += (_, _) => dialog.Close(true);
        webView.Source = clientsUri;

        try
        {
            return await dialog.ShowDialog<bool>(this);
        }
        finally
        {
            webView.Dispose();
        }
    }

    private async Task OpenSstvnAsync(PcRecord record, bool viewOnly)
    {
        if (!record.Online || record.Locked)
        {
            ShowToast(record.Name, "Удалённый доступ доступен только для включённого и не заблокированного ПК.");
            return;
        }

        if (string.IsNullOrWhiteSpace(record.MacAddress))
        {
            ShowToast(record.Name, "Нет MAC-адреса для удалённого доступа.");
            return;
        }

        try
        {
            var settings = await service.LoadRemoteControlSettingsAsync(config, CancellationToken.None);
            if (!settings.Enabled)
            {
                ShowToast(record.Name, "Удалённое управление отключено в настройках SmartShell.");
                return;
            }
            if (!settings.CcBoot && string.IsNullOrWhiteSpace(settings.Password))
            {
                ShowToast(record.Name, "В SmartShell не задан пароль удалённого управления.");
                return;
            }

            var target = !string.IsNullOrWhiteSpace(record.IpAddress)
                ? record.IpAddress!.Trim()
                : record.MacAddress.Trim().Replace(":", "-");
            var credential = settings.CcBoot ? "ccboot_mode" : settings.Password;
            var uri = "sstvn://" + target + "&" + credential + (viewOnly ? "&viewonly" : "");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowToast(record.Name, "Не удалось открыть удалённый доступ: " + ex.Message);
        }
    }

    private void AddPcFromSidebar()
    {
        var raw = (addPcBox?.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            ShowToast("Карта", "Введите номер или имя места.");
            return;
        }

        var name = NormalizeName(raw);
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (!records.TryGetValue(name, out var record))
        {
            record = new PcRecord { Name = name, SourceStatus = "offline" };
            records[name] = record;
        }

        selected = record;
        selectedHostKeys.Clear();
        selectedHostKeys.Add(record.Name);
        if (addPcBox != null) addPcBox.Text = "";
        RefreshMap();
        UpdateSelectedPanel();
        UpdateSummary("добавлено место " + name);
    }

    private void ClearCleaningHistory()
    {
        cleaningReminderMinutes.Clear();
        pendingCleaningAudits.Clear();
        foreach (var record in records.Values)
        {
            record.CleaningStatus = "Нет задачи";
            record.FreedAtUtc = null;
        }

        SaveCleaningAuditState();

        RefreshMap();
        if (screen == "details") RenderComputerGroups();
        UpdateSelectedPanel();
        UpdateSummary("текущие задачи уборки очищены");
        ShowToast("Уборка", "Текущие задачи очищены. Журнал сохранён.");
    }

    private void MarkSelectedFree()
    {
        var targets = SelectedHosts();
        if (targets.Count == 0 && selected != null)
            targets.Add(selected);
        if (targets.Count == 0)
        {
            ShowToast("Карта", "Сначала выберите ПК.");
            return;
        }

        foreach (var target in targets)
        {
            target.SourceStatus = "free";
            target.CleaningStatus = "Нужно убрать";
            target.FreedAtUtc = DateTime.UtcNow;
            SetPendingCleaning(target.Name, target.FreedAtUtc.Value);
            cleaningReminderMinutes.Remove(target.Name);
        }
        SaveCleaningAuditState();
        RefreshMap();
        UpdateSelectedPanel();
        UpdateSummary(targets.Count == 1
            ? targets[0].Name + " отмечен свободным"
            : "свободными отмечено: " + targets.Count);
    }

    private void MarkSelectedBusy()
    {
        var targets = SelectedHosts();
        if (targets.Count == 0 && selected != null)
            targets.Add(selected);
        if (targets.Count == 0)
        {
            ShowToast("Карта", "Сначала выберите ПК.");
            return;
        }

        foreach (var target in targets)
        {
            target.SourceStatus = "busy";
            target.CleaningStatus = "Нет задачи";
            target.FreedAtUtc = null;
            RemovePendingCleaning(target.Name);
            cleaningReminderMinutes.Remove(target.Name);
        }
        SaveCleaningAuditState();
        RefreshMap();
        UpdateSelectedPanel();
        UpdateSummary(targets.Count == 1
            ? targets[0].Name + " отмечен занятым"
            : "занятыми отмечено: " + targets.Count);
    }

    private void SwitchScreen(string target)
    {
        if (target == "cleaning-log" && !CanViewCleaningAudit())
        {
            ShowToast("Доступ", "Журнал уборки доступен только администраторам системы.");
            return;
        }

        if (target == "details" && screen != "details")
        {
            selectedHostKeys.Clear();
            selected = null;
        }

        if (target == "shop" && screen != "shop")
            PrepareShopEntry();

        if (target == "ps")
            psTimerAttentionPending = false;

        contentHost.IsTransitionReversed = ScreenIndex(target) < ScreenIndex(screen);
        screen = target;
        UpdateNavButtonStyles();
        title.Text = target switch
        {
            "dashboard" => "Дашборд",
            "shop" => "Магазин",
            "ps" => "Таймеры PlayStation",
            "bookings" => "Бронирование",
            "payments" => "Платежи",
            "clients" => "Клиенты",
            "logs" => "Логи",
            "cleaning-log" => "Журнал уборки",
            "details" => "Компьютеры",
            _ => "Карта клуба"
        };

        var fullWidth = target == "dashboard" || target == "shop" || target == "ps" || target == "bookings" || target == "payments" || target == "clients" || target == "logs" || target == "cleaning-log" || target == "details";
        SetInspectorVisibility(!fullWidth && selectedHostKeys.Any(records.ContainsKey));
        Grid.SetColumnSpan(contentHost, fullWidth ? 2 : 1);
        contentHost.Margin = ContentMargin(fullWidth);
        contentHost.Content = target switch
        {
            "dashboard" => BuildDashboardView(),
            "shop" => BuildShopView(),
            "ps" => BuildPsTimersView(),
            "bookings" => BuildBookingsView(),
            "payments" => BuildPaymentsView(),
            "clients" => BuildClientsView(),
            "logs" => BuildLogsView(),
            "cleaning-log" => BuildCleaningAuditView(),
            "details" => BuildDetailsView(),
            _ => BuildMapView()
        };
    }

    private static int ScreenIndex(string target)
    {
        return target switch
        {
            "dashboard" => 0,
            "details" => 1,
            "map" => 2,
            "bookings" => 3,
            "shop" => 4,
            "payments" => 5,
            "clients" => 6,
            "logs" => 7,
            "cleaning-log" => 8,
            "ps" => 9,
            _ => 0
        };
    }

    private Control BuildMapView()
    {
        if (mapScrollView == null)
        {
            mapScrollView = new ScrollViewer
            {
                Content = mapCanvas,
                Background = UiLabAppearance.HasBackdrop ? Brushes.Transparent : Brush("#080d12"),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            mapScrollView.SizeChanged += (_, e) =>
            {
                var width = Math.Max(960, e.NewSize.Width);
                var height = Math.Max(700, e.NewSize.Height);
                if (Math.Abs(mapCanvas.Width - width) < 0.5 && Math.Abs(mapCanvas.Height - height) < 0.5) return;
                mapCanvas.Width = width;
                mapCanvas.Height = height;
                if (screen == "map") RefreshMap();
            };
        }

        return mapScrollView;
    }

    private Control BuildDashboardView()
    {
        dashboardBody = new StackPanel { Spacing = 16, MinWidth = 1580 };
        Dispatcher.UIThread.Post(async () => await RefreshDashboardAsync(true));

        return new ScrollViewer
        {
            Content = dashboardBody,
            Background = PageBackground(),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private async Task RefreshDashboardAsync(bool force = false)
    {
        if (screen != "dashboard" || dashboardBody == null || dashboardLoading)
            return;
        if (!force && DateTime.UtcNow < dashboardRefreshAfterUtc)
            return;

        dashboardLoading = true;
        var body = dashboardBody;
        if (body.Children.Count == 0)
        {
            body.Children.Add(new ProgressBar
            {
                IsIndeterminate = true,
                Height = 3,
                Foreground = Brush("#2f8cff"),
                Background = Brush("#16202c")
            });
        }

        try
        {
            var dashboardTask = service.LoadDashboardAsync(config, CancellationToken.None);
            var paymentsTask = service.LoadActiveWorkShiftPaymentsAsync(config, CancellationToken.None);
            await Task.WhenAll(dashboardTask, paymentsTask);
            if (screen != "dashboard" || dashboardBody != body) return;

            var snapshot = await dashboardTask;
            body.Children.Clear();
            body.Children.Add(BuildDashboardContent(snapshot, await paymentsTask));
            source.Text = "обновлено " + DateTime.Now.ToString("HH:mm:ss");
            summary.Text = snapshot.WorkShiftId > 0 ? "смена № " + snapshot.WorkShiftId : "смена не открыта";
            dashboardRefreshAfterUtc = DateTime.UtcNow.AddSeconds(30);
        }
        catch (Exception ex)
        {
            dashboardRefreshAfterUtc = DateTime.UtcNow.AddSeconds(10);
            if (body.Children.Count <= 1)
            {
                body.Children.Clear();
                body.Children.Add(new TextBlock
                {
                    Text = "Не удалось загрузить дашборд: " + ex.Message,
                    Foreground = Brush("#ff8a91"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(12)
                });
            }
        }
        finally
        {
            dashboardLoading = false;
        }
    }

    private Control BuildDashboardContent(DashboardSnapshot snapshot, IReadOnlyCollection<WorkShiftPayment> payments)
    {
        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(DashboardGrid("250,420,420,210,210",
            DashboardWorkerPanel(snapshot),
            DashboardFinancePanel(snapshot),
            DashboardCategoriesPanel(snapshot),
            DashboardSecondaryFinancePanel(snapshot.Deposit, snapshot.OnlineDeposit),
            DashboardBonusPanel(snapshot.Bonus)));
        content.Children.Add(BuildDashboardHostStatusPanel());
        content.Children.Add(DashboardColumns(
            BuildDashboardTasksPanel(snapshot.Tasks),
            BuildDashboardSoldPanel(payments)));
        content.Children.Add(DashboardColumns(
            BuildDashboardClientsPanel(snapshot.ActiveClients),
            BuildDashboardGameAccountsPanel()));
        return content;
    }

    private Border DashboardWorkerPanel(DashboardSnapshot snapshot)
    {
        var avatar = new Border
        {
            Width = 50,
            Height = 50,
            CornerRadius = new CornerRadius(25),
            Background = Brush("#343b45"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = "♙",
                Foreground = Brush("#768195"),
                FontSize = 25,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var shiftStarted = snapshot.StartedAtLocal?.ToString("dd MMMM HH:mm", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) ?? "—";
        var shiftBadge = new Border
        {
            Background = Brush("#343b45"),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(9, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock { Text = "◉  " + shiftStarted, Foreground = Brush("#f5f7ff"), FontSize = 12, FontWeight = FontWeight.SemiBold }
        };
        return DashboardSurface(new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                avatar,
                new TextBlock { Text = ShortOperatorName(snapshot.WorkerName), Foreground = Brush("#f5f7ff"), FontSize = 16, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = string.IsNullOrWhiteSpace(snapshot.WorkerPhone) ? "—" : snapshot.WorkerPhone, Foreground = Brush("#8f9aad"), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center },
                new Separator { Background = Brush("#343d48"), Margin = new Thickness(8, 7) },
                shiftBadge
            }
        }, 16);
    }

    private Border DashboardFinancePanel(DashboardSnapshot snapshot)
    {
        var left = new StackPanel
        {
            Spacing = 9,
            Children =
            {
                DashboardAmount("Выручка", snapshot.Total, "#f5f7ff", true),
                new Separator { Background = Brush("#343d48"), Margin = new Thickness(0, 3) },
                DashboardAmount("Наличных в кассе", snapshot.CashInBox, "#f5f7ff"),
                DashboardAmount("На начало смены", snapshot.CashOnStart, "#f5f7ff")
            }
        };
        var right = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                DashboardPaymentMethod("cash", "Наличные", snapshot.Cash, snapshot.Total, "#25b96b"),
                new Separator { Background = Brush("#343d48"), Margin = new Thickness(0, 3) },
                DashboardPaymentMethod("card", "Карта", snapshot.Card, snapshot.Total, "#2f8cff")
            }
        };
        return DashboardSurface(DashboardGrid("*,*", left, right), 16);
    }

    private Control DashboardPaymentMethod(string icon, string titleText, decimal amount, decimal total, string color)
    {
        var percent = total > 0 ? amount / total * 100 : 0;
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                DashboardIconLabel(icon, titleText, color),
                new TextBlock { Text = DashboardMoneyText(amount), Foreground = Brush("#f5f7ff"), FontSize = 18, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = DashboardPercentText(percent), Foreground = Brush(color), FontSize = 13 }
            }
        };
    }

    private Border DashboardCategoriesPanel(DashboardSnapshot snapshot)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("*,*")
        };
        AddDashboardCategory(grid, 0, 0, "clock", "Тарифы", snapshot.Tariffs);
        AddDashboardCategory(grid, 1, 0, "goods", "Товары", snapshot.Goods);
        AddDashboardCategory(grid, 0, 1, "wallet", "Пополнения", snapshot.DepositTopUps);
        AddDashboardCategory(grid, 1, 1, "service", "Услуги", snapshot.Services);
        return DashboardSurface(grid, 16);
    }

    private void AddDashboardCategory(Grid grid, int column, int row, string icon, string titleText, decimal value)
    {
        var item = new StackPanel
        {
            Spacing = 7,
            Margin = new Thickness(column == 0 ? 0 : 14, row == 0 ? 0 : 14, column == 0 ? 14 : 0, row == 0 ? 14 : 0),
            Children =
            {
                DashboardIconLabel(icon, titleText, "#d8dfec"),
                new TextBlock { Text = DashboardMoneyText(value), Foreground = Brush("#f5f7ff"), FontSize = 18, FontWeight = FontWeight.SemiBold }
            }
        };
        Grid.SetColumn(item, column);
        Grid.SetRow(item, row);
        grid.Children.Add(item);
    }

    private Border DashboardSecondaryFinancePanel(decimal deposit, decimal online)
    {
        return DashboardSurface(new StackPanel
        {
            Spacing = 14,
            Children =
            {
                DashboardIconAmount("wallet", "Траты с депозита", deposit, "#8b5cf6"),
                new Separator { Background = Brush("#343d48") },
                DashboardIconAmount("online", "Онлайн-пополнения", online, "#8b5cf6")
            }
        }, 16);
    }

    private Border DashboardBonusPanel(decimal bonus)
    {
        return DashboardSurface(new StackPanel
        {
            Spacing = 14,
            Children =
            {
                DashboardIconAmount("bonus", "Бонусные пополнения", bonus, "#f97316"),
                new Separator { Background = Brush("#343d48"), Margin = new Thickness(0, 3) }
            }
        }, 16);
    }

    private Control DashboardIconAmount(string icon, string titleText, decimal value, string color)
    {
        return new StackPanel
        {
            Spacing = 7,
            Children =
            {
                DashboardIconLabel(icon, titleText, color),
                new TextBlock { Text = DashboardMoneyText(value), Foreground = Brush("#f5f7ff"), FontSize = 18, FontWeight = FontWeight.SemiBold }
            }
        };
    }

    private Control DashboardAmount(string titleText, decimal value, string color, bool large = false)
    {
        return new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = titleText, Foreground = Brush("#8f9aad"), FontSize = 12 },
                new TextBlock { Text = DashboardMoneyText(value), Foreground = Brush(color), FontSize = large ? 18 : 16, FontWeight = FontWeight.SemiBold }
            }
        };
    }

    private Border BuildDashboardHostStatusPanel()
    {
        var hosts = records.Values.ToList();
        var metrics = DashboardGrid("*,*,1.4*,*,*,*",
            DashboardStatusValue("computer", "Компьютеры:", hosts.Count, "#8993a4"),
            DashboardStatusValue("power", "Включены:", hosts.Count(x => x.Online), "#25b96b"),
            DashboardSessionStatusValue(hosts.Count(x => x.HasSession), hosts.Count(HasLoggedInClient)),
            DashboardStatusValue("serviceMode", "Обслуживание:", hosts.Count(x => IsServiceMode(x) && !IsHighAccessMode(x)), "#f59e0b"),
            DashboardStatusValue("high", "Высокий доступ:", hosts.Count(IsHighAccessMode), "#ef4444"),
            DashboardStatusValue("shellOff", "Шелл отключён:", hosts.Count(IsShellDisabled), "#ef4444"));
        return DashboardSurface(metrics, 12);
    }

    private Control DashboardStatusValue(string icon, string titleText, int value, string color)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                DashboardIcon(icon, color, 18),
                new TextBlock { Text = titleText, Foreground = Brush("#8f9aad"), FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = value.ToString(), Foreground = Brush("#f5f7ff"), FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }
            }
        };
    }

    private Control DashboardSessionStatusValue(int sessions, int loggedIn)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                DashboardIcon("session", "#2f8cff", 18),
                new TextBlock { Text = "Активный сеанс:", Foreground = Brush("#8f9aad"), FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = sessions.ToString(), Foreground = Brush("#f5f7ff"), FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                DashboardIcon("user", "#2f8cff", 17),
                new TextBlock { Text = loggedIn.ToString(), Foreground = Brush("#f5f7ff"), FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }
            }
        };
    }

    private Border BuildDashboardTasksPanel(IReadOnlyCollection<DashboardTaskInfo> tasks)
    {
        var rows = new StackPanel { Spacing = 2, MinHeight = 145 };
        var activeButton = FilterButton("Активные задачи", true);
        var completedButton = FilterButton("Завершенные задачи", false);
        var completed = false;

        void Render()
        {
            activeButton.Background = completed ? Brush("#182231") : Brush("#3b4251");
            completedButton.Background = completed ? Brush("#3b4251") : Brush("#182231");
            activeButton.Foreground = completed ? Brush("#a9b2c5") : Brush("#f5f7ff");
            completedButton.Foreground = completed ? Brush("#f5f7ff") : Brush("#a9b2c5");
            rows.Children.Clear();
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,Auto"), MinHeight = 54 };
            AddDashboardTableCell(header, "Задача  ↕", 0, true);
            AddDashboardTableCell(header, "Срок исполнения  ↕", 1, true);
            AddDashboardTableCell(header, "Исполнитель  ↕", 2, true);
            rows.Children.Add(header);
            rows.Children.Add(new Separator { Background = Brush("#343d48") });
            var selectedTasks = tasks.Where(x => completed == x.FinishedAtLocal.HasValue)
                .OrderBy(x => x.DeadlineLocal ?? DateTime.MaxValue)
                .Take(6)
                .ToList();
            foreach (var task in selectedTasks)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,Auto"), MinHeight = 42 };
                row.Children.Add(new TextBlock { Text = task.Title, Foreground = Brush("#f5f7ff"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                var worker = new TextBlock { Text = task.Worker, Foreground = Brush("#8f9aad"), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(worker, 1);
                row.Children.Add(worker);
                var date = new TextBlock
                {
                    Text = (completed ? task.FinishedAtLocal : task.DeadlineLocal)?.ToString("dd.MM HH:mm") ?? "—",
                    Foreground = Brush("#8f9aad"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(date, 2);
                row.Children.Add(date);
                rows.Children.Add(row);
            }
            if (selectedTasks.Count == 0)
                rows.Children.Add(DashboardEmpty(completed ? "Нет завершенных задач :)" : "Нет активных задач :)"));
        }

        activeButton.Click += (_, _) => { completed = false; Render(); };
        completedButton.Click += (_, _) => { completed = true; Render(); };
        var tabs = DashboardTabs(activeButton, completedButton);
        Render();
        return DashboardSurface(new StackPanel { Spacing = 12, Children = { tabs, rows } }, 16);
    }

    private Border BuildDashboardSoldPanel(IReadOnlyCollection<WorkShiftPayment> payments)
    {
        var rows = new StackPanel { Spacing = 2 };
        var buttons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase)
        {
            ["GOOD"] = FilterButton("Проданные товары", true),
            ["TARIFF"] = FilterButton("Проданные тарифы", false),
            ["SERVICE"] = FilterButton("Оказанные услуги", false)
        };
        var mode = "GOOD";

        void Render()
        {
            foreach (var pair in buttons)
            {
                var active = pair.Key == mode;
                pair.Value.Background = active ? Brush("#3b4251") : Brush("#182231");
                pair.Value.Foreground = active ? Brush("#f5f7ff") : Brush("#a9b2c5");
            }
            rows.Children.Clear();
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,*") , MinHeight = 54 };
            AddDashboardTableCell(header, "Название  ↕", 0, true);
            AddDashboardTableCell(header, "Количество  ↕", 1, true);
            AddDashboardTableCell(header, "Стоимость  ↕", 2, true);
            rows.Children.Add(header);
            rows.Children.Add(new Separator { Background = Brush("#343d48") });
            var sold = payments
                .Where(x => !x.IsRefunded)
                .SelectMany(x => x.Lines)
                .Where(x => x.Type.Equals(mode, StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    Title = group.Key,
                    Amount = group.Sum(x => x.Amount),
                    Sum = group.Sum(x => x.Sum)
                })
                .OrderByDescending(x => x.Sum)
                .Take(10)
                .ToList();
            foreach (var item in sold)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,*"), MinHeight = 44 };
                AddDashboardTableCell(row, item.Title, 0, false);
                AddDashboardTableCell(row, item.Amount + " шт.", 1, false);
                AddDashboardTableCell(row, DashboardMoneyText(item.Sum), 2, false);
                rows.Children.Add(row);
            }
            if (sold.Count == 0)
                rows.Children.Add(DashboardEmpty("Продаж в этой категории нет"));
        }

        foreach (var pair in buttons)
        {
            var key = pair.Key;
            pair.Value.Click += (_, _) => { mode = key; Render(); };
        }
        var tabSurface = DashboardTabs(buttons.Values.ToArray());
        Render();
        return DashboardSurface(new StackPanel { Spacing = 12, Children = { tabSurface, rows } }, 16);
    }

    private Border BuildDashboardClientsPanel(IReadOnlyCollection<DashboardActiveClient> clients)
    {
        var rows = new StackPanel { Spacing = 2 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,*,*") };
        AddDashboardTableCell(header, "Никнейм  ↕", 0, true);
        AddDashboardTableCell(header, "Траты  ↕", 1, true);
        AddDashboardTableCell(header, "Всего часов  ↕", 2, true);
        AddDashboardTableCell(header, "Посл. посещение  ↕", 3, true);
        rows.Children.Add(header);
        rows.Children.Add(new Separator { Background = Brush("#2b3442") });

        foreach (var client in clients)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,*,*"), MinHeight = 44 };
            AddDashboardTableCell(row, client.Nickname, 0, false);
            AddDashboardTableCell(row, DashboardMoneyText(client.Payments), 1, false);
            AddDashboardTableCell(row, (client.TotalSeconds / 3600d).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) + " ч.", 2, false);
            AddDashboardTableCell(row, client.LastActivityLocal?.ToString("dd MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) ?? "—", 3, false);
            rows.Children.Add(row);
        }
        if (clients.Count == 0)
            rows.Children.Add(DashboardEmpty("За текущую смену активных клиентов нет"));
        return DashboardSurface(new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Активные клиенты за смену", Foreground = Brush("#f5f7ff"), FontSize = 16, FontWeight = FontWeight.SemiBold },
                rows
            }
        }, 16);
    }

    private Border BuildDashboardGameAccountsPanel()
    {
        var groups = FilterButton("Группы аккаунтов", true);
        var busy = FilterButton("Занятые аккаунты", false);
        var rows = new StackPanel { MinHeight = 125 };

        void Render(bool showBusy)
        {
            groups.Background = showBusy ? Brush("#182231") : Brush("#3b4251");
            busy.Background = showBusy ? Brush("#3b4251") : Brush("#182231");
            rows.Children.Clear();
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,Auto"), MinHeight = 54 };
            AddDashboardTableCell(header, "Название ПК", 0, true);
            AddDashboardTableCell(header, "Группа аккаунтов", 1, true);
            AddDashboardTableCell(header, "Статус", 2, true);
            AddDashboardTableCell(header, "Управление", 3, true);
            rows.Children.Add(header);
            rows.Children.Add(new Separator { Background = Brush("#343d48") });
            rows.Children.Add(DashboardEmpty(showBusy ? "Пока нет занятых аккаунтов :(" : "Группы аккаунтов не назначены"));
        }

        groups.Click += (_, _) => Render(false);
        busy.Click += (_, _) => Render(true);
        Render(false);
        return DashboardSurface(new StackPanel { Spacing = 12, Children = { DashboardTabs(groups, busy), rows } }, 16);
    }

    private void AddDashboardTableCell(Grid grid, string text, int column, bool header)
    {
        var cell = new TextBlock
        {
            Text = text,
            Foreground = header ? Brush("#8f9aad") : Brush("#f5f7ff"),
            FontSize = header ? 12 : 14,
            FontWeight = header ? FontWeight.SemiBold : FontWeight.Normal,
            Margin = new Thickness(8, 10),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private Control DashboardEmpty(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = Brush("#667085"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 28),
            TextAlignment = TextAlignment.Center
        };
    }

    private StackPanel DashboardIconLabel(string icon, string text, string color)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                DashboardIcon(icon, color, 17),
                new TextBlock
                {
                    Text = text,
                    Foreground = Brush("#8f9aad"),
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
    }

    private PathIcon DashboardIcon(string icon, string color, double size)
    {
        var data = ThemedIconData(icon) ?? icon switch
        {
            "dashboard" => "M3,10.5 L12,3 L21,10.5 V21 H14 V15 H10 V21 H3 Z",
            "map" => "M3,4 H21 V6 H3 Z M4,9 H7 V12 H4 Z M10.5,9 H13.5 V12 H10.5 Z M17,9 H20 V12 H17 Z M4,15 H7 V18 H4 Z M10.5,15 H13.5 V18 H10.5 Z M17,15 H20 V18 H17 Z",
            "payments" => "M3,5 H21 C22.1,5 23,5.9 23,7 V17 C23,18.1 22.1,19 21,19 H3 C1.9,19 1,18.1 1,17 V7 C1,5.9 1.9,5 3,5 Z M3,8 H21 V10 H3 Z M5,14 H11 V16 H5 Z",
            "clients" => "M12,2 L21,6 V12 C21,17 17.5,21 12,23 C6.5,21 3,17 3,12 V6 Z M12,6 A3,3 0 1 0 12,12 A3,3 0 1 0 12,6 M7,18 C7.5,14.7 9.2,13 12,13 C14.8,13 16.5,14.7 17,18 Z",
            "logs" => "M3,4 H6 V7 H3 Z M8,4 H21 V7 H8 Z M3,10.5 H6 V13.5 H3 Z M8,10.5 H21 V13.5 H8 Z M3,17 H6 V20 H3 Z M8,17 H21 V20 H8 Z",
            "gamepad" => "M7,7 H17 C20.3,7 22,10 22,14.5 C22,18 20.2,20 18,20 C16.2,20 15,17 12,17 C9,17 7.8,20 6,20 C3.8,20 2,18 2,14.5 C2,10 3.7,7 7,7 Z M7,10 H9 V12 H11 V14 H9 V16 H7 V14 H5 V12 H7 Z M16,11 A1.5,1.5 0 1 0 16,14 A1.5,1.5 0 1 0 16,11 M19,14 A1.5,1.5 0 1 0 19,17 A1.5,1.5 0 1 0 19,14",
            "cash" => "M3,6 L21,6 L21,18 L3,18 Z M5,8 L5,16 L19,16 L19,8 Z M12,9 A3,3 0 1 0 12,15 A3,3 0 1 0 12,9",
            "card" => "M3,5 H21 C22.1,5 23,5.9 23,7 V17 C23,18.1 22.1,19 21,19 H3 C1.9,19 1,18.1 1,17 V7 C1,5.9 1.9,5 3,5 Z M3,8 H21 V10 H3 Z M5,15 H11 V17 H5 Z",
            "clock" => "M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 M13,6 H11 V13 L16,16 L17,14.5 L13,12 Z",
            "goods" => "M7,7 V6 C7,3.24 9.24,1 12,1 C14.76,1 17,3.24 17,6 V7 H20 L21,23 H3 L4,7 Z M9,7 H15 V6 C15,4.34 13.66,3 12,3 C10.34,3 9,4.34 9,6 Z",
            "wallet" => "M3,5 H19 C20.1,5 21,5.9 21,7 V9 H16 C14.34,9 13,10.34 13,12 C13,13.66 14.34,15 16,15 H21 V17 C21,18.1 20.1,19 19,19 H3 C1.9,19 1,18.1 1,17 V7 C1,5.9 1.9,5 3,5 M16,11 H22 V13 H16 C15.45,13 15,12.55 15,12 C15,11.45 15.45,11 16,11",
            "online" => "M3,5 H18 C19.1,5 20,5.9 20,7 V9 H15 C13.34,9 12,10.34 12,12 C12,13.66 13.34,15 15,15 H20 V17 C20,18.1 19.1,19 18,19 H3 C1.9,19 1,18.1 1,17 V7 C1,5.9 1.9,5 3,5 M15,11 H22 V13 H15 Z M18,8 V5 H20 V8 H23 V10 H20 V13 H18 V10 H15 V8 Z",
            "service" => "M6,9 V3 H18 V9 H20 C21.1,9 22,9.9 22,11 V17 H18 V21 H6 V17 H2 V11 C2,9.9 2.9,9 4,9 Z M8,5 V9 H16 V5 Z M8,15 V19 H16 V15 Z",
            "bonus" => "M12,2 L22,9 L12,22 L2,9 Z M12,5 L6,9 L12,17 L18,9 Z",
            "computer" => "M3,3 H21 C22.1,3 23,3.9 23,5 V16 C23,17.1 22.1,18 21,18 H14 V20 H17 V22 H7 V20 H10 V18 H3 C1.9,18 1,17.1 1,16 V5 C1,3.9 1.9,3 3,3 Z M3,5 V16 H21 V5 Z",
            "power" => "M11,2 H13 V12 H11 Z M7.05,4.81 L8.46,6.22 C6.97,7.29 6,9.04 6,11 C6,14.31 8.69,17 12,17 C15.31,17 18,14.31 18,11 C18,9.04 17.03,7.29 15.54,6.22 L16.95,4.81 C18.89,6.25 20,8.53 20,11 C20,15.42 16.42,19 12,19 C7.58,19 4,15.42 4,11 C4,8.53 5.11,6.25 7.05,4.81 Z",
            "session" => "M5,3 H19 C20.1,3 21,3.9 21,5 V19 C21,20.1 20.1,21 19,21 H5 C3.9,21 3,20.1 3,19 V5 C3,3.9 3.9,3 5,3 Z M6,6 V18 H18 V6 Z",
            "user" => "M12,2 A5,5 0 1 0 12,12 A5,5 0 1 0 12,2 M4,22 C4,17.58 7.58,14 12,14 C16.42,14 20,17.58 20,22 Z",
            "serviceMode" => "M8,3 H16 L18,17 H6 Z M5,19 H19 V22 H5 Z M9,8 H15 L14,5 H10 Z M8,14 H16 L15.4,12 H8.6 Z",
            "high" => "M4.3,3 L21,19.7 L19.7,21 L3,4.3 Z M19.7,3 L21,4.3 L4.3,21 L3,19.7 Z",
            "shellOff" => "M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 M11,6 H13 V14 H11 Z M11,16 H13 V18 H11 Z",
            "search" => "M10,3 A7,7 0 1 0 10,17 A7,7 0 1 0 10,3 M15,15 L22,22 L20,24 L13,17 Z",
            "calendar" => "M4,4 H7 V2 H9 V4 H15 V2 H17 V4 H20 C21.1,4 22,4.9 22,6 V20 C22,21.1 21.1,22 20,22 H4 C2.9,22 2,21.1 2,20 V6 C2,4.9 2.9,4 4,4 Z M4,9 V20 H20 V9 Z M6,12 H10 V16 H6 Z",
            "event" => "M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 M11,6 H13 V13 H11 Z M11,16 H13 V18 H11 Z",
            "audit" => "M7,3 H9 C9.4,1.8 10.5,1 12,1 C13.5,1 14.6,1.8 15,3 H17 C18.1,3 19,3.9 19,5 V21 H5 V5 C5,3.9 5.9,3 7,3 Z M9,5 H15 V3 H9 Z M8,12 L11,15 L16,9 L17.5,10.5 L11,18 L6.5,13.5 Z",
            "refresh" => "M12,3 C16.4,3 20,6.6 20,11 H23 L19,16 L15,11 H18 C18,7.7 15.3,5 12,5 C9.4,5 7.2,6.7 6.4,9 L4.3,9 C5.2,5.5 8.3,3 12,3 M6,13 C6,16.3 8.7,19 12,19 C14.6,19 16.8,17.3 17.6,15 H19.7 C18.8,18.5 15.7,21 12,21 C7.6,21 4,17.4 4,13 H1 L5,8 L9,13 Z",
            "external" => "M5,4 H11 V6 H7 V18 H19 V14 H21 V20 H5 Z M13,3 H22 V12 H20 V6.4 L11.7,14.7 L10.3,13.3 L18.6,5 H13 Z",
            "settings" => "M10.8,2 H13.2 L14,5 C14.8,5.3 15.5,5.7 16.2,6.2 L19.1,5.3 L20.3,7.4 L18.1,9.5 C18.2,10 18.3,10.5 18.3,11 C18.3,11.5 18.2,12 18.1,12.5 L20.3,14.6 L19.1,16.7 L16.2,15.8 C15.5,16.3 14.8,16.7 14,17 L13.2,20 H10.8 L10,17 C9.2,16.7 8.5,16.3 7.8,15.8 L4.9,16.7 L3.7,14.6 L5.9,12.5 C5.8,12 5.7,11.5 5.7,11 C5.7,10.5 5.8,10 5.9,9.5 L3.7,7.4 L4.9,5.3 L7.8,6.2 C8.5,5.7 9.2,5.3 10,5 Z M12,8 A3,3 0 1 0 12,14 A3,3 0 1 0 12,8",
            "add" => "M11,3 H13 V11 H21 V13 H13 V21 H11 V13 H3 V11 H11 Z",
            "edit" => "M4,17.25 V21 H7.75 L18.81,9.94 L15.06,6.19 Z M20.71,8.04 C21.1,7.65 21.1,7 20.71,6.63 L18.37,4.29 C18,3.9 17.35,3.9 16.96,4.29 L15.13,6.12 L18.88,9.87 Z",
            "clear" => "M8,3 H16 L18,6 H21 V8 H3 V6 H6 Z M5,10 H19 L18,22 H6 Z M9,12 V19 H11 V12 Z M13,12 V19 H15 V12 Z",
            "dragon" => "M2,20 C4,14 5,9 10,6 L7,2 C12,3 16,5 18,8 L22,6 L20,12 C22,15 20,20 17,22 L14,18 L11,22 C8,19 6,17 2,20 Z M8,10 L12,7 L16,10 L13,13 H10 Z",
            "spider" => "M12,2 C10.3,2 9,3.3 9,5 C9,5.8 9.3,6.5 9.8,7 C8.7,8.2 8,10 8,12 C8,16.2 9.8,19.4 12,22 C14.2,19.4 16,16.2 16,12 C16,10 15.3,8.2 14.2,7 C14.7,6.5 15,5.8 15,5 C15,3.3 13.7,2 12,2 Z M8.5,8.5 L3,4 L2,5.5 L7.2,11 Z M7.8,11.5 L1,9.5 L1,11.5 L7.8,14 Z M8.5,14.5 L3,20 L2,18.5 L7.2,12.8 Z M15.5,8.5 L21,4 L22,5.5 L16.8,11 Z M16.2,11.5 L23,9.5 L23,11.5 L16.2,14 Z M15.5,14.5 L21,20 L22,18.5 L16.8,12.8 Z",
            "cat" => "M4,9 L5,2 L10,6 C11.3,5.6 12.7,5.6 14,6 L19,2 L20,9 C21.4,10.5 22,12.3 22,14.5 C22,19 18,22 12,22 C6,22 2,19 2,14.5 C2,12.3 2.6,10.5 4,9 Z M7,12 L10,13.5 L7,15 Z M17,12 L14,13.5 L17,15 Z M10,17 H14 L12,19 Z",
            _ => "M4,4 H20 V20 H4 Z"
        };
        return new PathIcon
        {
            Data = Geometry.Parse(data),
            Foreground = Brush(color),
            Width = size,
            Height = size,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static string? ThemedIconData(string icon)
    {
        return UiLabAppearance.CurrentKey switch
        {
            "material" => icon switch
            {
                "dashboard" => "M3,3 H10 V10 H3 Z M14,3 H21 V10 H14 Z M3,14 H10 V21 H3 Z M14,14 H21 V21 H14 Z",
                "map" => "M4,3 H20 C21.1,3 22,3.9 22,5 V19 C22,20.1 21.1,21 20,21 H4 C2.9,21 2,20.1 2,19 V5 C2,3.9 2.9,3 4,3 Z M5,7 H9 V11 H5 Z M10,7 H14 V11 H10 Z M15,7 H19 V11 H15 Z M5,13 H9 V17 H5 Z M10,13 H14 V17 H10 Z M15,13 H19 V17 H15 Z",
                "calendar" => "M5,2 H7 V5 H17 V2 H19 V5 H21 V22 H3 V5 H5 Z M5,9 V20 H19 V9 Z",
                "goods" => "M5,7 H19 L20,22 H4 Z M8,7 C8,4.8 9.8,3 12,3 C14.2,3 16,4.8 16,7 H14 C14,5.9 13.1,5 12,5 C10.9,5 10,5.9 10,7 Z",
                "payments" => "M3,5 H21 C22.1,5 23,5.9 23,7 V17 C23,18.1 22.1,19 21,19 H3 C1.9,19 1,18.1 1,17 V7 C1,5.9 1.9,5 3,5 Z M3,9 H21 V12 H3 Z",
                "clients" => "M8,4 A4,4 0 1 0 8,12 A4,4 0 1 0 8,4 M16,6 A3,3 0 1 0 16,12 A3,3 0 1 0 16,6 M1,21 C1,16.6 4.1,14 8,14 C11.9,14 15,16.6 15,21 Z M15,14 C19,14 23,16.1 23,20 H17 C17,17.8 16.3,15.8 15,14 Z",
                "logs" => "M5,3 H19 C20.1,3 21,3.9 21,5 V21 H3 V5 C3,3.9 3.9,3 5,3 Z M7,7 H17 V9 H7 Z M7,11 H17 V13 H7 Z M7,15 H14 V17 H7 Z",
                "audit" => "M8,2 H16 V4 H20 V22 H4 V4 H8 Z M10,4 V6 H14 V4 Z M8,13 L11,16 L17,9 L18.5,10.5 L11,19 L6.5,14.5 Z",
                "gamepad" => "M7,7 H17 C20,7 22,10 22,15 C22,18 20.3,20 18.2,20 C16,20 15.4,17 12,17 C8.6,17 8,20 5.8,20 C3.7,20 2,18 2,15 C2,10 4,7 7,7 Z M7,10 V12 H5 V14 H7 V16 H9 V14 H11 V12 H9 V10 Z",
                _ => null
            },
            "semi" => icon switch
            {
                "dashboard" => "M3,4 H21 V7 H3 Z M3,10 H14 V13 H3 Z M3,16 H18 V19 H3 Z",
                "map" => "M3,4 H9 V10 H3 Z M11,4 H21 V10 H11 Z M3,12 H14 V20 H3 Z M16,12 H21 V20 H16 Z",
                "calendar" => "M4,5 H20 V21 H4 Z M7,2 H9 V7 H7 Z M15,2 H17 V7 H15 Z M6,10 H18 V12 H6 Z",
                "goods" => "M4,7 H20 V21 H4 Z M8,7 C8,4.8 9.8,3 12,3 C14.2,3 16,4.8 16,7 H14 C14,5.9 13.1,5 12,5 C10.9,5 10,5.9 10,7 Z",
                "payments" => "M3,5 H21 V19 H3 Z M3,8 H21 V10 H3 Z M6,14 H12 V16 H6 Z",
                "clients" => "M7,4 H11 V8 H7 Z M14,4 H18 V8 H14 Z M4,11 H14 V20 H4 Z M15,11 H21 V20 H15 Z",
                "logs" => "M3,4 H6 V7 H3 Z M8,4 H21 V7 H8 Z M3,10 H6 V13 H3 Z M8,10 H21 V13 H8 Z M3,16 H6 V19 H3 Z M8,16 H21 V19 H8 Z",
                _ => null
            },
            "suki" => icon switch
            {
                "dashboard" => "M2,3 H11 V12 H2 Z M13,3 H22 V8 H13 Z M13,10 H22 V21 H13 Z M2,14 H11 V21 H2 Z",
                "map" => "M2,4 L8,2 L16,5 L22,3 V20 L16,22 L8,19 L2,21 Z M8,5 V16 L16,19 V8 Z",
                "calendar" => "M3,5 H21 V22 H3 Z M6,2 H9 V8 H6 Z M15,2 H18 V8 H15 Z M6,11 H11 V16 H6 Z",
                "goods" => "M3,8 L6,3 H18 L21,8 V21 H3 Z M8,8 C8,5.8 9.8,4 12,4 C14.2,4 16,5.8 16,8 H14 C14,6.9 13.1,6 12,6 C10.9,6 10,6.9 10,8 Z",
                "clients" => "M12,2 L21,7 V17 L12,22 L3,17 V7 Z M12,6 A3,3 0 1 0 12,12 A3,3 0 1 0 12,6 M7,18 C7.5,14.8 9.2,13 12,13 C14.8,13 16.5,14.8 17,18 Z",
                "logs" => "M3,3 H21 V8 H3 Z M3,10 H16 V15 H3 Z M3,17 H19 V22 H3 Z",
                _ => null
            },
            "oled" => icon switch
            {
                "dashboard" => "M2,11 H11 V2 H2 Z M13,2 H22 V11 H13 Z M2,13 H11 V22 H2 Z M13,13 H22 V22 H13 Z",
                "map" => "M2,3 H22 V6 H2 Z M2,9 H22 V12 H2 Z M2,15 H22 V18 H2 Z",
                "logs" => "M4,3 H20 V5 H4 Z M4,9 H20 V11 H4 Z M4,15 H20 V17 H4 Z M4,21 H14 V23 H4 Z",
                _ => null
            },
            _ => null
        };
    }

    private Border DashboardTabs(params Button[] buttons)
    {
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        foreach (var button in buttons)
        {
            button.BorderBrush = Brushes.Transparent;
            button.BorderThickness = new Thickness(0);
            tabs.Children.Add(button);
        }
        return new Border
        {
            Background = Brush("#303640"),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = tabs
        };
    }

    private Border DashboardSurface(Control child, double padding)
    {
        return new Border
        {
            Background = PanelBrush("#151c25", 188),
            BorderBrush = Brush("#18212b"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(padding),
            Child = child
        };
    }

    private Grid DashboardColumns(params Control[] controls)
    {
        return DashboardGrid(string.Join(",", Enumerable.Repeat("*", controls.Length)), controls);
    }

    private Grid DashboardGrid(string columns, params Control[] controls)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(columns)
        };
        for (var index = 0; index < controls.Length; index++)
        {
            controls[index].Margin = new Thickness(index == 0 ? 0 : 7, 0, index == controls.Length - 1 ? 0 : 7, 0);
            Grid.SetColumn(controls[index], index);
            grid.Children.Add(controls[index]);
        }
        return grid;
    }

    private static string DashboardMoneyText(decimal value)
    {
        return value
            .ToString("#,0.##", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))
            .Replace('\u00a0', ' ') + " ₽";
    }

    private static string ShortOperatorName(string? fullName)
    {
        var parts = (fullName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return string.IsNullOrWhiteSpace(fullName) ? "—" : fullName.Trim();
        var initials = parts.Skip(1).Take(2).Select(x => char.ToUpperInvariant(x[0]) + ".");
        return parts[0] + " " + string.Join(" ", initials);
    }

    private static string DashboardPercentText(decimal value)
    {
        return value.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) + "%";
    }

    private Control BuildDetailsView()
    {
        computerGroupsHost = new StackPanel
        {
            MinWidth = 1481,
            Spacing = 20,
            Margin = new Thickness(0, 0, 0, 82)
        };

        var infoButton = new Button
        {
            Content = "ⓘ",
            Width = 44,
            Height = 44,
            Padding = new Thickness(0),
            Background = Brush("#111820"),
            Foreground = Brush("#2f8cff"),
            BorderBrush = Brush("#111820"),
            FontSize = 17,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(infoButton, "Выберите компьютеры для бронирования или отправки уведомления");

        var scroll = new ScrollViewer
        {
            Content = computerGroupsHost,
            Background = PageBackground(),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(scroll, 1);

        computerBulkText = new TextBlock
        {
            Foreground = Brush("#f5f7ff"),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0)
        };
        computerBulkBar = BuildComputerBulkBar();
        Grid.SetRow(computerBulkBar, 1);

        var root = new Grid
        {
            Background = PageBackground(),
            RowDefinitions = new RowDefinitions("Auto,*")
        };
        root.Children.Add(infoButton);
        root.Children.Add(scroll);
        root.Children.Add(computerBulkBar);

        RenderComputerGroups();
        return root;
    }

    private Border BuildComputerBulkBar()
    {
        computerTariffButton = ComputerBulkButton("◷  Выбрать тариф", async () =>
        {
            var hosts = SelectedHosts();
            var host = hosts.FirstOrDefault(x => x.HostId > 0);
            if (host != null)
                await OpenTariffSaleDialogAsync(host, hosts);
        });
        var book = ComputerBulkButton("⊕  Забронировать", async () =>
        {
            var hosts = SelectedHosts();
            var first = hosts.FirstOrDefault(x => x.HostId > 0);
            if (first == null) return;
            await OpenBookingDialogAsync(first, hosts.Where(x => x.HostId > 0).Select(x => x.HostId));
        });
        var notify = ComputerBulkButton("▣  Уведомление", async () => await PromptBulkMessageAsync(SelectedHosts()));
        computerPowerButton = ComputerBulkButton("⏻  Включить", async () =>
            await SendBulkHostCommandAsync(SelectedHosts(), "WOL"));
        var close = ComputerBulkButton("×", () =>
        {
            selectedHostKeys.Clear();
            selected = null;
            RenderComputerGroups();
        });
        close.Width = 42;
        close.MinWidth = 42;
        close.Padding = new Thickness(0);

        return new Border
        {
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 18),
            Padding = new Thickness(16, 10),
            Background = PanelBrush("#101722", 214),
            BorderBrush = Brush("#2b3550"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 24,
                OffsetY = 8,
                Color = Color.Parse("#99000000")
            }),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { computerBulkText!, computerTariffButton, book, notify, computerPowerButton, close }
            }
        };
    }

    private Button ComputerBulkButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Height = 36,
            MinWidth = 138,
            Padding = new Thickness(14, 0),
            Background = Brush("#13243b"),
            Foreground = Brush("#4ea1ff"),
            BorderBrush = Brush("#183457"),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Click += (_, _) => action();
        return button;
    }

    private Button ComputerBulkButton(string text, Func<Task> action)
    {
        Action startAction = () => { _ = action(); };
        return ComputerBulkButton(text, startAction);
    }

    private void RenderComputerGroups()
    {
        if (computerGroupsHost == null) return;

        computerRemainingBlocks.Clear();
        computerGroupsHost.Children.Clear();

        var groups = records.Values
            .GroupBy(ComputerGroupTitle)
            .OrderBy(x => ComputerGroupOrder(x.Key))
            .ThenBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        foreach (var group in groups)
            computerGroupsHost.Children.Add(ComputerGroupPanel(group.Key, SortComputers(group).ToList()));

        if (groups.Count == 0)
        {
            computerGroupsHost.Children.Add(new TextBlock
            {
                Text = "Компьютеры ещё не загружены.",
                Foreground = Brush("#8f9aad"),
                FontSize = 14,
                Margin = new Thickness(12, 24)
            });
        }

        UpdateComputerBulkBar();
    }

    private Border ComputerGroupPanel(string groupTitle, IReadOnlyList<PcRecord> hosts)
    {
        var rows = new StackPanel();
        rows.Children.Add(ComputerTableHeader());
        foreach (var host in hosts)
            rows.Children.Add(ComputerTableRow(host));

        var allSelected = hosts.Count > 0 && hosts.All(x => selectedHostKeys.Contains(x.Name));
        var selectAll = new CheckBox
        {
            Content = "Выбрать все",
            IsChecked = allSelected,
            Foreground = Brush("#f5f7ff"),
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        selectAll.Click += (_, _) =>
        {
            foreach (var host in hosts)
            {
                if (selectAll.IsChecked == true) selectedHostKeys.Add(host.Name);
                else selectedHostKeys.Remove(host.Name);
            }
            selected = SelectedHosts().FirstOrDefault();
            RenderComputerGroups();
        };

        var groupHeader = new Grid
        {
            Height = 64,
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(28, 0)
        };
        groupHeader.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = ComputerGroupBrush(groupTitle)
                },
                new TextBlock
                {
                    Text = groupTitle,
                    Foreground = Brush("#f5f7ff"),
                    FontSize = 16,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        });
        Grid.SetColumn(selectAll, 1);
        groupHeader.Children.Add(selectAll);

        return new Border
        {
            Background = PanelBrush("#121a24", 188),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = new StackPanel { Children = { groupHeader, rows } }
        };
    }

    private Grid ComputerTableHeader()
    {
        var grid = ComputerTableGrid();
        grid.Height = 58;
        grid.Background = PanelBrush("#121a24", 200);
        AddComputerCell(grid, new Border(), 0);
        AddComputerCell(grid, ComputerSortHeader("Название", "name"), 1);
        AddComputerCell(grid, ComputerSortHeader("Статус", "status"), 2);
        AddComputerCell(grid, ComputerSortHeader("Бронь", "booking"), 3);
        AddComputerCell(grid, ComputerSortHeader("Клиент", "client"), 4);
        AddComputerCell(grid, ComputerSortHeader("Сеанс", "session"), 5);
        AddComputerCell(grid, ComputerSortHeader("Старт", "start"), 6);
        AddComputerCell(grid, ComputerSortHeader("Окончание", "end"), 7);
        AddComputerCell(grid, ComputerSortHeader("Остаток", "remaining"), 8);
        AddComputerCell(grid, ComputerSortHeader("Приложение", "application"), 9);
        return grid;
    }

    private Button ComputerSortHeader(string text, string column)
    {
        var active = computerSortColumn == column;
        var arrow = active ? (computerSortDescending ? "↓" : "↑") : "↕";
        var button = new Button
        {
            Content = text + "  " + arrow,
            Padding = new Thickness(16, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = active ? Brush("#4ea1ff") : Brush("#f5f7ff"),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold
        };
        button.Click += (_, _) =>
        {
            if (computerSortColumn == column) computerSortDescending = !computerSortDescending;
            else
            {
                computerSortColumn = column;
                computerSortDescending = false;
            }
            RenderComputerGroups();
        };
        return button;
    }

    private Border ComputerTableRow(PcRecord record)
    {
        var grid = ComputerTableGrid();
        grid.Height = 56;

        var selectedRow = selectedHostKeys.Contains(record.Name);
        var check = new CheckBox
        {
            IsChecked = selectedRow,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        check.Click += (_, _) =>
        {
            if (check.IsChecked == true) selectedHostKeys.Add(record.Name);
            else selectedHostKeys.Remove(record.Name);
            selected = SelectedHosts().FirstOrDefault();
            RenderComputerGroups();
        };
        AddComputerCell(grid, check, 0);
        AddComputerCell(grid, ComputerText(record.Name, "#f5f7ff", FontWeight.SemiBold), 1);
        AddComputerCell(grid, ComputerStatusBadge(record), 2);
        AddComputerCell(grid, ComputerBookingCell(record), 3);
        AddComputerCell(grid, ComputerClientCell(record), 4);
        AddComputerCell(grid, ComputerText(record.HasSession && !string.IsNullOrWhiteSpace(record.ActiveTariffTitle) ? record.ActiveTariffTitle! : "—"), 5);
        AddComputerCell(grid, ComputerText(record.SessionStartedAtUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "—"), 6);
        AddComputerCell(grid, ComputerText(record.SessionEndsAtUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "—"), 7);

        var remaining = ComputerText(record.HasSession ? SessionRemainingText(record) : "—");
        if (record.HasSession) computerRemainingBlocks[record.Name] = remaining;
        AddComputerCell(grid, remaining, 8);
        AddComputerCell(grid, ComputerText(ComputerApplicationText(record)), 9);

        var row = new Border
        {
            Background = selectedRow ? Brush("#1d3b63") : PanelBrush("#151d28", 214),
            BorderBrush = Brush("#263244"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = grid,
            ContextMenu = HostMenu(record)
        };
        row.PointerPressed += (_, e) =>
        {
            var point = e.GetCurrentPoint(row);
            if (point.Properties.IsRightButtonPressed && !selectedHostKeys.Contains(record.Name))
            {
                selectedHostKeys.Clear();
                selectedHostKeys.Add(record.Name);
                selected = record;
                row.Background = Brush("#1d3b63");
                check.IsChecked = true;
                UpdateComputerBulkBar();
            }
            else if (point.Properties.IsLeftButtonPressed && e.ClickCount == 2)
            {
                _ = OpenTariffSaleDialogAsync(record);
                e.Handled = true;
            }
        };
        return row;
    }

    private static Grid ComputerTableGrid()
    {
        return new Grid { ColumnDefinitions = new ColumnDefinitions("72,170,152,120,212,170,120,125,120,220") };
    }

    private static void AddComputerCell(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static TextBlock ComputerText(string text, string color = "#c8d0df", FontWeight? weight = null)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = Brush(color),
            FontSize = 13,
            FontWeight = weight ?? FontWeight.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0)
        };
    }

    private Border ComputerStatusBadge(PcRecord record)
    {
        var (text, foreground, background) = ComputerStatus(record);
        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0),
            Padding = new Thickness(9, 4),
            CornerRadius = new CornerRadius(8),
            Background = Brush(background),
            Child = new TextBlock { Text = text, Foreground = Brush(foreground), FontSize = 12 }
        };
    }

    private Control ComputerBookingCell(PcRecord record)
    {
        if (record.HostId > 0 && upcomingBookingsByHost.TryGetValue(record.HostId, out var booking))
        {
            var button = ComputerLink("сегодня " + booking.FromLocal.ToString("HH:mm"));
            button.Click += (_, _) => _ = OpenBookingDialogAsync(record, booking.HostIds, booking);
            return button;
        }

        return ComputerText(record.BookingCount > 0 ? record.BookingCount.ToString() : "—");
    }

    private Control ComputerClientCell(PcRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.ClientName) && string.IsNullOrWhiteSpace(record.ClientPhone))
            return ComputerText("—");

        var button = ComputerLink(!string.IsNullOrWhiteSpace(record.ClientName) ? record.ClientName! : record.ClientPhone!);
        button.Click += (_, _) => _ = OpenHoverClientAsync(record);
        return button;
    }

    private static Button ComputerLink(string text)
    {
        return new Button
        {
            Content = new TextBlock
            {
                Text = text,
                TextDecorations = TextDecorations.Underline,
                TextTrimming = TextTrimming.CharacterEllipsis
            },
            Padding = new Thickness(16, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = Brush("#4ea1ff"),
            FontSize = 13
        };
    }

    private IEnumerable<PcRecord> SortComputers(IEnumerable<PcRecord> hosts)
    {
        Func<PcRecord, object?> key = computerSortColumn switch
        {
            "status" => x => ComputerStatus(x).Text,
            "booking" => x => x.BookingCount,
            "client" => x => x.ClientName ?? x.ClientPhone ?? "",
            "session" => x => x.ActiveTariffTitle ?? "",
            "start" => x => x.SessionStartedAtUtc ?? DateTime.MinValue,
            "end" => x => x.SessionEndsAtUtc ?? DateTime.MinValue,
            "remaining" => x => x.SessionEndsAtUtc ?? DateTime.MinValue,
            "application" => ComputerApplicationText,
            _ => x => NaturalKey(x.Name)
        };
        return computerSortDescending ? hosts.OrderByDescending(key) : hosts.OrderBy(key);
    }

    private void UpdateComputerBulkBar()
    {
        if (computerBulkBar == null || computerBulkText == null) return;
        var hosts = SelectedHosts();
        var count = hosts.Count;
        computerBulkBar.IsVisible = count > 0;
        computerBulkText.Text = "Выбрано хостов: " + count;

        if (computerTariffButton != null)
        {
            var sameGroup = count > 0 && hosts.All(x => x.GroupId == hosts[0].GroupId)
                && hosts.All(x => string.Equals(ComputerGroupTitle(x), ComputerGroupTitle(hosts[0]), StringComparison.OrdinalIgnoreCase));
            computerTariffButton.IsVisible = sameGroup
                && hosts.All(x => x.HostId > 0 && !x.InService && !x.Locked && !x.HasSession && !HasLoggedInClient(x));
        }

        if (computerPowerButton != null)
            computerPowerButton.IsVisible = count > 0 && hosts.All(x => x.HostId > 0 && !x.Online && !x.Locked);
    }

    private static string ComputerGroupTitle(PcRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.GroupTitle)) return record.GroupTitle!;
        if (record.Name.StartsWith("PS", StringComparison.OrdinalIgnoreCase)) return "PlayStation";

        var digits = new string(record.Name.Where(char.IsDigit).ToArray());
        if (!int.TryParse(digits, out var number)) return "Без группы";
        if (number >= 16 && number <= 29) return "Standart";
        if (number <= 5) return "VIP";
        if (number <= 15) return "Premium";
        if (number <= 31) return "Dual's";
        return "Без группы";
    }

    private static int ComputerGroupOrder(string title)
    {
        return title.ToLowerInvariant() switch
        {
            "standart" => 0,
            "vip" => 1,
            "premium" => 2,
            "dual's" => 3,
            "playstation" => 4,
            _ => 10
        };
    }

    private static IBrush ComputerGroupBrush(string title)
    {
        return title.ToLowerInvariant() switch
        {
            "standart" => Brush("#2f8cff"),
            "vip" => Brush("#8b5cf6"),
            "premium" => Brush("#d946ef"),
            "dual's" => Brush("#17c964"),
            "playstation" => Brush("#f5a524"),
            _ => Brush("#667085")
        };
    }

    private static (string Text, string Foreground, string Background) ComputerStatus(PcRecord record)
    {
        if (!record.Online) return ("Нет связи", "#9aa5b8", "#293342");
        if (record.HasSession) return ("Занят", "#4ea1ff", "#12345b");
        if (IsShellDisabled(record)) return ("Shell снят", "#ff6b73", "#3a171e");
        if (IsServiceMode(record)) return ("Обслуживание", "#f5b942", "#3a2a11");
        return ("Свободен", "#23d47c", "#123524");
    }

    private static string ComputerApplicationText(PcRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.ActiveWindow)) return record.ActiveWindow!;
        if (string.IsNullOrWhiteSpace(record.ShellVersion)) return record.Online ? "SmartShell" : "—";
        return "SmartShell [v." + record.ShellVersion!.Trim().TrimStart('v', 'V') + "]";
    }

    private Control BuildLogsView()
    {
        const int pageSize = 50;
        var currentPage = 1;
        var lastPage = 1;
        var loadVersion = 0;
        var startDate = DateTime.Today.AddDays(-30);
        var finishDate = DateTime.Today;
        var loadedEntries = new List<EventLogEntry>();
        var filters = new List<LogCategoryFilter>
        {
            new("Все категории", Array.Empty<string>()),
            new("Авторизация", new[] { "CLIENT_AUTHORIZED_FROM_HOST", "WORKER_AUTHORIZED_FROM_HOST", "WORKER_AUTHORIZED_FROM_BILLING", "USER_VERIFIED" }),
            new("Клиенты", new[] { "USER_REGISTERED", "USER_BANNED", "USER_UNBANNED", "USER_RESET_PASSWORD", "USER_DISCOUNT_UPDATED", "BONUS_UPDATED", "DEPOSIT_UPDATED" }),
            new("Отправка кодов", new[] { "SEND_VERIFY_CODE", "SEND_RESET_CODE" }),
            new("Сотрудники", new[] { "WORKER_CREATED", "WORKER_UPDATED", "WORKER_DELETED", "WORKER_ENABLED", "TASK_ADDED" }),
            new("Смены", new[] { "WORK_SHIFT_STARTED", "WORK_SHIFT_FINISHED" }),
            new("Оборудование", new[] { "HOST_GROUP_CREATED", "HOST_GROUP_UPDATED", "HOST_GROUP_DELETED", "HOST_CREATED", "HOST_UPDATED", "HOST_DELETED", "HOST_SWITCHED_GROUP", "HOST_HARDWARE_CHANGED", "HOST_HARDWARE_APPLIED", "SHELL_HIGH_ACCESS_ENABLED", "SHELL_HIGH_ACCESS_DISABLED", "SHELL_DISABLED", "SHELL_ENABLED", "HOST_QR_GENERATE" }),
            new("Сеансы", new[] { "CLIENT_SESSION_STARTED", "CLIENT_SESSION_PROLONGED", "CLIENT_SESSION_FINISHED", "CLIENT_SESSION_HOST_CHANGED", "HOST_PUNISHED", "CLIENT_SESSION_PAUSED", "CLIENT_SESSION_UNPAUSED", "CLIENT_SESSION_UNPAUSED_AUTO" }),
            new("Продажи", new[] { "PAYMENT_CREATED", "PAYMENT_REFUND", "POST_PAYMENT_CANCELED" }),
            new("Бонусные пополнения", new[] { "BONUS_PAYMENT_CREATED" }),
            new("Тарифы", new[] { "TARIFF_CREATED", "TARIFF_UPDATED", "TARIFF_DELETED" }),
            new("Приложения", new[] { "SHORTCUT_CREATED", "SHORTCUT_UPDATED", "SHORTCUT_DELETED" }),
            new("Товары", new[] { "GOOD_CREATED", "GOOD_UPDATED", "GOOD_DELETED", "WAREHOUSE_GOODS_ADDED", "WAREHOUSE_GOODS_DISPOSED", "GOOD_LOW_STOCK_THRESHOLD_REACHED" }),
            new("Группы товаров", new[] { "GOOD_CATEGORY_CREATED", "GOOD_CATEGORY_UPDATED", "GOOD_CATEGORY_DELETED" }),
            new("Услуги", new[] { "SERVICE_CREATED", "SERVICE_UPDATED", "SERVICE_DELETED" }),
            new("Скидки", new[] { "DISCOUNT_CREATED", "DISCOUNT_UPDATED", "DISCOUNT_DELETED" }),
            new("Промокоды", new[] { "PROMO_CODE_CREATED", "PROMO_CODE_UPDATED", "PROMO_CODE_DELETED", "PROMO_CODE_USED", "PROMO_CODE_EXPIRED" }),
            new("Группы промокодов", new[] { "PROMO_CAMPAIGN_CREATED", "PROMO_CAMPAIGN_UPDATED", "PROMO_CAMPAIGN_DELETED", "PROMO_CAMPAIGN_CODES_GENERATED", "PROMO_CAMPAIGN_EXPIRED" }),
            new("Ордеры", new[] { "CASH_ORDER_CREATED" }),
            new("Конфигурация", new[] { "SETTINGS_UPDATED", "SETTINGS_COPIED" }),
            new("Telegram", new[] { "TELEGRAM_LINKED", "TELEGRAM_UNLINKED" }),
            new("Вызов", new[] { "HOST_ADMIN_CALLED", "HOST_ADMIN_CALL_CHECKED", "HOST_ADMIN_CALL_FAILED" }),
            new("Отзывы клиентов", new[] { "CLUB_COMMENT_CREATED" }),
            new("Достижения", new[] { "ACHIEVEMENT_CREATED", "ACHIEVEMENT_DELETED", "ACHIEVEMENT_ASSIGNED" }),
            new("Бронирование", new[] { "BOOKING_CREATED", "BOOKING_CANCELED", "BOOKING_FAILED_TO_START", "BOOKING_UPDATED", "BOOKING_CONFIRMED" }),
            new("Комбо-наборы", new[] { "COMBO_CREATED", "COMBO_UPDATED", "COMBO_DELETED" }),
            new("Онлайн-пополнение", new[] { "DEPOSIT_ADDED_ONLINE" }),
            new("Управление", new[] { "DEBT_ACTIVATED" }),
            new("Группы аккаунтов", new[] { "USER_GROUP_CREATED", "USER_GROUP_UPDATED", "USER_GROUP_DELETED", "USER_CHANGED_GROUP" }),
            new("Межклубный перевод депозита", new[] { "USER_DEPOSIT_TRANSFERRED" }),
            new("Объявления", new[] { "ADVERTISEMENT_MANUAL_COMPLETED", "ADVERTISEMENT_APPROVED", "ADVERTISEMENT_REJECTED" }),
            new("Новости", new[] { "NEWS_CREATED", "NEWS_UPDATED", "NEWS_DELETED" }),
            new("Смена владельца клуба", new[] { "OWNER_CHANGED" })
        };
        var selectedFilter = filters[0];
        var rows = new StackPanel();
        var topPager = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 24, 0)
        };
        var bottomPager = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16) };
        var status = new TextBlock
        {
            Foreground = Brush("#8f9aad"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var search = new TextBox
        {
            Width = 250,
            Height = 48,
            Watermark = "Найти событие",
            Background = Brush("#151d28"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#263244"),
            Padding = new Thickness(14, 0)
        };
        var searchButton = new Button
        {
            Width = 50,
            Height = 48,
            Padding = new Thickness(0),
            Background = Brush("#2f8cff"),
            BorderBrush = Brush("#2f8cff"),
            Content = DashboardIcon("search", "#ffffff", 19)
        };
        var rangeText = new TextBlock
        {
            Foreground = Brush("#f5f7ff"),
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var rangeButton = new Button
        {
            Height = 48,
            MinWidth = 320,
            Padding = new Thickness(14, 0),
            Background = Brush("#151d28"),
            BorderBrush = Brush("#263244"),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children = { DashboardIcon("calendar", "#2f8cff", 19), rangeText }
            }
        };
        var categoryText = new TextBlock
        {
            Text = filters[0].Title,
            Foreground = Brush("#f5f7ff"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var categoryButtonContent = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        categoryButtonContent.Children.Add(categoryText);
        var categoryArrow = new TextBlock
        {
            Text = "⌄",
            Foreground = Brush("#8f9aad"),
            FontSize = 17,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        Grid.SetColumn(categoryArrow, 1);
        categoryButtonContent.Children.Add(categoryArrow);
        var categoryButton = new Button
        {
            Height = 48,
            MinWidth = 340,
            Background = Brush("#151d28"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#263244"),
            Padding = new Thickness(14, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = categoryButtonContent
        };
        var categorySearch = new TextBox
        {
            Height = 42,
            Watermark = "Найти категорию",
            Background = Brush("#111820"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3748"),
            Padding = new Thickness(12, 0),
            Margin = new Thickness(10, 10, 10, 8)
        };
        var categoryRows = new StackPanel { Spacing = 2 };
        var categoryPopup = new Popup
        {
            PlacementTarget = categoryButton,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            VerticalOffset = 5,
            IsLightDismissEnabled = true,
            Topmost = true,
            Child = new Border
            {
                Width = 430,
                MaxHeight = 540,
                CornerRadius = new CornerRadius(7),
                Background = Brush("#151d28"),
                BorderBrush = Brush("#344154"),
                BorderThickness = new Thickness(1),
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 24, OffsetY = 10, Color = Color.Parse("#99000000") }),
                Child = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,*"),
                    Children =
                    {
                        categorySearch,
                        new ScrollViewer
                        {
                            Content = categoryRows,
                            MaxHeight = 470,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                        }
                    }
                }
            }
        };
        Grid.SetRow(((Grid)((Border)categoryPopup.Child).Child!).Children[1], 1);
        var categoryHost = new Grid
        {
            MinWidth = 340,
            Children = { categoryButton, categoryPopup }
        };
        var expandedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Button CategoryMenuButton(string text, bool nested, bool selected)
        {
            return new Button
            {
                Height = 38,
                Padding = new Thickness(nested ? 30 : 14, 0, 10, 0),
                Background = selected ? Brush("#172f4c") : Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Foreground = selected ? Brush("#4ea1ff") : Brush(nested ? "#c7d0de" : "#f5f7ff"),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = text
            };
        }

        void SelectCategory(LogCategoryFilter filter)
        {
            selectedFilter = filter;
            categoryText.Text = filter.Title;
            categoryPopup.IsOpen = false;
            categorySearch.Text = "";
            _ = LoadAsync();
        }

        void RenderCategoryMenu()
        {
            categoryRows.Children.Clear();
            var query = (categorySearch.Text ?? "").Trim();
            var all = CategoryMenuButton("Все категории", false, selectedFilter.Types.Length == 0);
            all.Click += (_, _) => SelectCategory(filters[0]);
            categoryRows.Children.Add(all);
            categoryRows.Children.Add(new Separator { Background = Brush("#2b3748"), Margin = new Thickness(10, 5) });

            foreach (var group in filters.Skip(1))
            {
                var matchingTypes = group.Types
                    .Where(type => query.Length == 0
                        || group.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                        || LogEventTitle(type).Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    .ToArray();
                if (matchingTypes.Length == 0) continue;

                var groupSelected = selectedFilter.Types.Length == group.Types.Length
                    && selectedFilter.Types.SequenceEqual(group.Types, StringComparer.OrdinalIgnoreCase);
                var expanded = query.Length > 0 || expandedCategories.Contains(group.Title);
                var groupRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,42") };
                var groupButton = CategoryMenuButton(group.Title, false, groupSelected);
                groupButton.FontWeight = FontWeight.SemiBold;
                groupButton.Click += (_, _) => SelectCategory(group);
                groupRow.Children.Add(groupButton);

                var expandButton = new Button
                {
                    Content = expanded ? "⌃" : "⌄",
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Transparent,
                    Foreground = Brush("#8f9aad"),
                    Padding = new Thickness(0),
                    IsVisible = group.Types.Length > 1
                };
                expandButton.Click += (_, _) =>
                {
                    if (!expandedCategories.Add(group.Title)) expandedCategories.Remove(group.Title);
                    RenderCategoryMenu();
                };
                Grid.SetColumn(expandButton, 1);
                groupRow.Children.Add(expandButton);
                categoryRows.Children.Add(groupRow);

                if (!expanded || group.Types.Length <= 1) continue;
                foreach (var type in matchingTypes)
                {
                    var title = LogEventTitle(type);
                    var typeSelected = selectedFilter.Types.Length == 1
                        && string.Equals(selectedFilter.Types[0], type, StringComparison.OrdinalIgnoreCase);
                    var typeButton = CategoryMenuButton(title, true, typeSelected);
                    typeButton.Click += (_, _) => SelectCategory(new LogCategoryFilter(title, new[] { type }));
                    categoryRows.Children.Add(typeButton);
                }
            }
        }

        categoryButton.Click += (_, _) =>
        {
            RenderCategoryMenu();
            categoryPopup.IsOpen = !categoryPopup.IsOpen;
        };
        categorySearch.TextChanged += (_, _) => RenderCategoryMenu();

        void UpdateRangeText()
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
            rangeText.Text = startDate.ToString("d MMMM yyyy", culture) + " — " + finishDate.ToString("d MMMM yyyy", culture) + ", 23:59";
        }

        string EventColor(EventLogEntry entry)
        {
            var type = entry.Type.ToUpperInvariant();
            if (type.Contains("DELETED") || type.Contains("CANCELED") || type.Contains("FAILED") || type.Contains("DISABLED") || type.Contains("FINISHED")) return "#ff5c66";
            if (type.Contains("CREATED") || type.Contains("STARTED") || type.Contains("ENABLED") || type.Contains("AUTHORIZED")) return "#f5f7ff";
            if (type.Contains("UPDATED") || type.Contains("PROLONGED") || type.Contains("PAUSED")) return "#f2b84b";
            return "#4ea1ff";
        }

        string EventIcon(EventLogEntry entry)
        {
            var type = entry.Type.ToUpperInvariant();
            if (type.Contains("SESSION")) return "clock";
            if (type.Contains("PAYMENT") || type.Contains("GOOD") || type.Contains("SERVICE")) return "goods";
            if (type.Contains("HOST") || type.Contains("SHELL")) return "computer";
            if (type.Contains("USER") || type.Contains("WORKER") || type.Contains("AUTHORIZED")) return "user";
            if (type.Contains("BOOKING")) return "calendar";
            return "event";
        }

        Grid LogGrid() => new()
        {
            ColumnDefinitions = new ColumnDefinitions("160,285,180,220,3*,220"),
            ColumnSpacing = 14,
            MinWidth = 1500
        };

        TextBlock CellText(string text, string color = "#f5f7ff", bool bold = false, bool wrap = false) => new()
        {
            Text = string.IsNullOrWhiteSpace(text) ? "" : text,
            Foreground = Brush(color),
            FontSize = 13,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            MaxWidth = wrap ? 560 : double.PositiveInfinity,
            HorizontalAlignment = wrap ? HorizontalAlignment.Left : HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };

        void AddCell(Grid grid, Control cell, int column)
        {
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        async Task OpenClientAsync(string uuid)
        {
            if (string.IsNullOrWhiteSpace(uuid)) return;
            try
            {
                var client = await service.LoadClientDetailsAsync(config, uuid, CancellationToken.None);
                if (client != null) await ShowClientCardDialogAsync(client, this);
            }
            catch (Exception ex)
            {
                ShowToast("Логи", "Не удалось открыть клиента: " + ex.Message);
            }
        }

        Control PersonCell(string text, string uuid)
        {
            if (string.IsNullOrWhiteSpace(text)) return CellText("");
            if (string.IsNullOrWhiteSpace(uuid)) return CellText(text, "#4ea1ff");
            var button = new Button
            {
                Content = text,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Foreground = Brush("#4ea1ff"),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Left,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            button.Click += (_, _) => _ = OpenClientAsync(uuid);
            return button;
        }

        Border BuildHeader()
        {
            var grid = LogGrid();
            var headers = new[] { "Дата и время", "Категория", "Устройство", "Субъект  ↕", "Событие", "Объект" };
            for (var i = 0; i < headers.Length; i++) AddCell(grid, CellText(headers[i], "#f5f7ff", true), i);
            return new Border
            {
                Height = 54,
                Background = PanelBrush("#151d28", 200),
                BorderBrush = Brush("#343d48"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(18, 0),
                Child = grid
            };
        }

        void RenderRows()
        {
            rows.Children.Clear();
            rows.Children.Add(BuildHeader());
            foreach (var entry in loadedEntries)
            {
                var grid = LogGrid();
                var date = new StackPanel
                {
                    Spacing = 2,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        CellText(entry.CreatedAtLocal.ToString("d MMMM", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")), "#f5f7ff", true),
                        CellText(entry.CreatedAtLocal.ToString("HH:mm:ss"), "#f5f7ff", true)
                    }
                };
                var category = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 9,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { DashboardIcon(EventIcon(entry), EventColor(entry), 17), CellText(entry.Category, "#f5f7ff", true) }
                };
                Control host = string.IsNullOrWhiteSpace(entry.Host)
                    ? CellText("")
                    : new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children = { DashboardIcon("computer", "#cbd3df", 17), CellText(entry.Host) }
                    };
                AddCell(grid, date, 0);
                AddCell(grid, category, 1);
                AddCell(grid, host, 2);
                AddCell(grid, PersonCell(entry.Subject, entry.SubjectClientUuid), 3);
                AddCell(grid, CellText(entry.Description, "#f5f7ff", true, true), 4);
                AddCell(grid, PersonCell(entry.Object, entry.ObjectClientUuid), 5);
                rows.Children.Add(new Border
                {
                    MinHeight = 72,
                    Background = PanelBrush("#151d28", 214),
                    Padding = new Thickness(18, 12),
                    Child = grid
                });
            }
            if (loadedEntries.Count == 0)
                rows.Children.Add(DashboardEmpty("За выбранный период событий нет"));
        }

        Button PageButton(string text, int targetPage, bool active = false)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 40,
                Height = 38,
                Padding = new Thickness(10, 0),
                Background = active ? Brush("#172f4c") : Brush("#151d28"),
                BorderBrush = active ? Brush("#2f8cff") : Brush("#263244"),
                Foreground = active ? Brush("#f5f7ff") : Brush("#cbd3df"),
                IsEnabled = targetPage >= 1 && targetPage <= lastPage && targetPage != currentPage
            };
            button.Click += (_, _) => _ = LoadAsync(targetPage);
            return button;
        }

        void RenderPager(StackPanel panel)
        {
            panel.Children.Clear();
            if (lastPage <= 1) return;
            panel.Children.Add(PageButton("‹", currentPage - 1));
            var first = Math.Max(1, currentPage - 2);
            var last = Math.Min(lastPage, first + 4);
            first = Math.Max(1, last - 4);
            for (var page = first; page <= last; page++) panel.Children.Add(PageButton(page.ToString(), page, page == currentPage));
            panel.Children.Add(PageButton("›", currentPage + 1));
        }

        async Task LoadAsync(int requestedPage = 1)
        {
            var version = ++loadVersion;
            status.Text = "Загружаю журнал...";
            searchButton.IsEnabled = false;
            try
            {
                var result = await service.LoadEventLogsAsync(
                    config,
                    requestedPage,
                    pageSize,
                    search.Text ?? "",
                    startDate.Date,
                    finishDate.Date.AddDays(1).AddTicks(-1),
                    selectedFilter.Types,
                    CancellationToken.None);
                if (version != loadVersion || screen != "logs") return;
                currentPage = result.CurrentPage;
                lastPage = result.LastPage;
                loadedEntries = result.Entries;
                RenderRows();
                RenderPager(topPager);
                RenderPager(bottomPager);
                status.Text = result.WorkShiftId > 0
                    ? "Смена №" + result.WorkShiftId + " · страница " + currentPage + " из " + lastPage + " · " + result.Entries.Count + " событий"
                    : "Активная смена не найдена";
                source.Text = "обновлено " + DateTime.Now.ToString("HH:mm:ss");
                summary.Text = result.WorkShiftId > 0 ? "смена №" + result.WorkShiftId : selectedFilter.Title;
            }
            catch (Exception ex)
            {
                if (version != loadVersion || screen != "logs") return;
                loadedEntries.Clear();
                RenderRows();
                status.Text = "Не удалось загрузить логи: " + ex.Message;
            }
            finally
            {
                if (version == loadVersion) searchButton.IsEnabled = true;
            }
        }

        searchButton.Click += (_, _) => _ = LoadAsync();
        search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) _ = LoadAsync();
        };
        rangeButton.Click += async (_, _) =>
        {
            var range = await ShowLogsDateRangeDialogAsync(startDate, finishDate);
            if (!range.HasValue) return;
            startDate = range.Value.Start;
            finishDate = range.Value.Finish;
            UpdateRangeText();
            await LoadAsync();
        };
        UpdateRangeText();

        var searchPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { search, searchButton } };
        var filtersGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            ColumnSpacing = 14,
            Children = { searchPanel, rangeButton, categoryHost, topPager }
        };
        Grid.SetColumn(rangeButton, 1);
        Grid.SetColumn(categoryHost, 2);
        Grid.SetColumn(topPager, 3);
        var table = new Border
        {
            Margin = new Thickness(0, 18, 0, 0),
            Background = PanelBrush("#151d28", 178),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = rows
        };
        var tableScroll = new ScrollViewer
        {
            Content = table,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children = { filtersGrid, status, tableScroll, bottomPager }
        };
        status.Margin = new Thickness(0, 10, 0, 0);
        Dispatcher.UIThread.Post(async () => await LoadAsync());
        return new ScrollViewer
        {
            Content = content,
            Background = PageBackground(),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private Control BuildCleaningAuditView()
    {
        const int pageSize = 50;
        var canEdit = CanEditCleaningAudit();
        var currentPage = 1;
        var search = new TextBox
        {
            Width = 320,
            Height = 44,
            Watermark = "Поиск по ПК или оператору",
            Background = Brush("#151d28"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#263244"),
            Padding = new Thickness(14, 0)
        };
        ComboBox AuditFilter(IEnumerable<string> items, double width)
        {
            return new ComboBox
            {
                Width = width,
                Height = 44,
                ItemsSource = items.ToList(),
                SelectedIndex = 0,
                Background = Brush("#151d28"),
                Foreground = Brush("#f5f7ff"),
                BorderBrush = Brush("#263244"),
                Padding = new Thickness(12, 0)
            };
        }

        var operatorItems = new[] { "Все операторы" }
            .Concat(cleaningAuditEntries
                .Select(x => string.IsNullOrWhiteSpace(x.Operator) ? "Оператор не определён" : x.Operator)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(x => x))
            .ToList();
        var dateItems = new[] { "Все даты" }
            .Concat(cleaningAuditEntries
                .Select(x => x.ClientLeftAtUtc.ToLocalTime().ToString("dd.MM.yyyy"))
                .Distinct()
                .OrderByDescending(x => DateTime.ParseExact(x, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();
        var operatorFilter = AuditFilter(operatorItems, 230);
        var dateFilter = AuditFilter(dateItems, 170);
        var kindFilter = AuditFilter(new[]
        {
            "Все уборки",
            "Только просроченные",
            "Только подозрительные",
            "Просроченные или подозрительные"
        }, 260);
        var sortFilter = AuditFilter(new[]
        {
            "Сначала новые",
            "Сначала старые",
            "По оператору",
            "Долгая уборка сначала"
        }, 230);
        var summaryText = new TextBlock { Foreground = Brush("#8f9aad"), VerticalAlignment = VerticalAlignment.Center };
        var rows = new StackPanel { Spacing = 1 };
        var pager = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16)
        };

        Grid AuditGrid() => new()
        {
            ColumnDefinitions = new ColumnDefinitions(canEdit
                ? "110,190,190,160,260,*,92"
                : "110,190,190,160,260,*"),
            ColumnSpacing = 16,
            MinWidth = canEdit ? 1320 : 1210
        };

        static void AddCell(Grid grid, Control cell, int column)
        {
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        TextBlock Cell(string value, string color = "#f5f7ff", bool bold = false) => new()
        {
            Text = value,
            Foreground = Brush(color),
            FontSize = 13,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };

        var header = AuditGrid();
        AddCell(header, Cell("ПК", "#cbd3df", true), 0);
        AddCell(header, Cell("Клиент ушёл", "#cbd3df", true), 1);
        AddCell(header, Cell("ПК убран", "#cbd3df", true), 2);
        AddCell(header, Cell("Время уборки", "#cbd3df", true), 3);
        AddCell(header, Cell("Контроль", "#cbd3df", true), 4);
        AddCell(header, Cell("Оператор смены", "#cbd3df", true), 5);
        if (canEdit) AddCell(header, Cell("", "#cbd3df", true), 6);
        rows.Children.Add(new Border
        {
            Background = PanelBrush("#111820", 200),
            Padding = new Thickness(18, 14),
            Child = header
        });

        List<CleaningAuditEntry> FilteredEntries()
        {
            var query = (search.Text ?? "").Trim();
            var selectedOperator = operatorFilter.SelectedItem as string ?? "Все операторы";
            var selectedDate = dateFilter.SelectedItem as string ?? "Все даты";
            var filtered = cleaningAuditEntries
                .Where(x => query.Length == 0
                    || x.HostName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || (x.Operator ?? "").Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || (x.SuspiciousReason ?? "").Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .Where(x => selectedOperator == "Все операторы"
                    || string.Equals(string.IsNullOrWhiteSpace(x.Operator) ? "Оператор не определён" : x.Operator,
                        selectedOperator, StringComparison.CurrentCultureIgnoreCase))
                .Where(x => selectedDate == "Все даты"
                    || x.ClientLeftAtUtc.ToLocalTime().ToString("dd.MM.yyyy") == selectedDate)
                .Where(x =>
                {
                    var duration = (x.CleanedAtUtc ?? DateTime.UtcNow) - x.ClientLeftAtUtc;
                    var overdue = duration > TimeSpan.FromMinutes(20);
                    var suspicious = !string.IsNullOrWhiteSpace(x.SuspiciousReason);
                    return kindFilter.SelectedIndex switch
                    {
                        1 => overdue,
                        2 => suspicious,
                        3 => overdue || suspicious,
                        _ => true
                    };
                });

            return sortFilter.SelectedIndex switch
            {
                1 => filtered.OrderBy(x => x.CleanedAtUtc ?? x.ClientLeftAtUtc).ToList(),
                2 => filtered.OrderBy(x => x.Operator ?? "").ThenByDescending(x => x.CleanedAtUtc ?? x.ClientLeftAtUtc).ToList(),
                3 => filtered.OrderByDescending(x => (x.CleanedAtUtc ?? DateTime.UtcNow) - x.ClientLeftAtUtc).ToList(),
                _ => filtered.OrderByDescending(x => x.CleanedAtUtc ?? x.ClientLeftAtUtc).ToList()
            };
        }

        void Render()
        {
            while (rows.Children.Count > 1) rows.Children.RemoveAt(1);
            pager.Children.Clear();
            var filtered = FilteredEntries();
            var pages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)pageSize));
            currentPage = Math.Clamp(currentPage, 1, pages);
            foreach (var entry in filtered.Skip((currentPage - 1) * pageSize).Take(pageSize))
            {
                var cleaningTime = (entry.CleanedAtUtc ?? DateTime.UtcNow) - entry.ClientLeftAtUtc;
                if (cleaningTime < TimeSpan.Zero) cleaningTime = TimeSpan.Zero;
                var overdue = cleaningTime - TimeSpan.FromMinutes(20);
                var timingText = entry.CleanedAtUtc.HasValue
                    ? overdue > TimeSpan.Zero
                        ? "Просрочка +" + FormatCleaningDelay(overdue)
                        : "Убрано за " + FormatCleaningDelay(cleaningTime)
                    : overdue > TimeSpan.Zero
                        ? "Просрочено на " + FormatCleaningDelay(overdue)
                        : "Ожидает " + FormatCleaningDelay(cleaningTime);
                var grid = AuditGrid();
                AddCell(grid, Cell(entry.HostName, "#4ea1ff", true), 0);
                AddCell(grid, Cell(entry.ClientLeftAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"), "#cbd3df"), 1);
                AddCell(grid, Cell(entry.CleanedAtUtc.HasValue
                    ? entry.CleanedAtUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss")
                    : "Ожидает уборки", entry.CleanedAtUtc.HasValue ? "#cbd3df" : "#ff5c66", true), 2);
                AddCell(grid, Cell(timingText, overdue > TimeSpan.Zero ? "#ff5c66" : "#cbd3df", true), 3);
                AddCell(grid, Cell(string.IsNullOrWhiteSpace(entry.SuspiciousReason)
                    ? "—"
                    : "Подозрительно: " + entry.SuspiciousReason,
                    string.IsNullOrWhiteSpace(entry.SuspiciousReason) ? "#667085" : "#ffb547", true), 4);
                AddCell(grid, Cell(string.IsNullOrWhiteSpace(entry.Operator) ? "Оператор не определён" : entry.Operator), 5);
                if (canEdit)
                {
                    var editButton = new Button
                    {
                        Width = 38,
                        Height = 38,
                        Padding = new Thickness(0),
                        Background = Brushes.Transparent,
                        BorderBrush = Brush("#315d89"),
                        Content = DashboardIcon("edit", "#4ea1ff", 16)
                    };
                    ToolTip.SetTip(editButton, "Редактировать запись");
                    editButton.Click += async (_, _) =>
                    {
                        if (!CanEditCleaningAudit()) return;
                        if (await ShowCleaningAuditEditDialogAsync(entry))
                            Render();
                    };
                    var deleteButton = new Button
                    {
                        Width = 38,
                        Height = 38,
                        Padding = new Thickness(0),
                        Background = Brushes.Transparent,
                        BorderBrush = Brush("#6d2832"),
                        Content = DashboardIcon("clear", "#ff5c66", 16)
                    };
                    ToolTip.SetTip(deleteButton, "Удалить запись");
                    deleteButton.Click += (_, _) =>
                    {
                        if (!CanEditCleaningAudit()) return;
                        DeleteCleaningAuditEntry(entry);
                        Render();
                    };
                    AddCell(grid, new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Children = { editButton, deleteButton }
                    }, 6);
                }
                rows.Children.Add(new Border
                {
                    Background = PanelBrush("#151d28", 214),
                    Padding = new Thickness(18, 14),
                    Child = grid
                });
            }

            if (filtered.Count == 0)
                rows.Children.Add(DashboardEmpty("Уборок за последние 31 день нет"));

            summaryText.Text = "Показано: " + filtered.Count + " из " + cleaningAuditEntries.Count
                + " · ожидают уборки: " + pendingCleaningAudits.Count
                + " · подозрительных: " + cleaningAuditEntries.Count(x => !string.IsNullOrWhiteSpace(x.SuspiciousReason))
                + " · просрочено сейчас: " + pendingCleaningAudits.Values.Count(x => DateTime.UtcNow - x.ClientLeftAtUtc > TimeSpan.FromMinutes(20));

            if (pages <= 1) return;
            for (var page = 1; page <= pages; page++)
            {
                var targetPage = page;
                var button = new Button
                {
                    Content = page.ToString(),
                    MinWidth = 40,
                    Height = 38,
                    Background = page == currentPage ? Brush("#172f4c") : Brush("#151d28"),
                    BorderBrush = page == currentPage ? Brush("#2f8cff") : Brush("#263244"),
                    Foreground = Brush("#f5f7ff"),
                    IsEnabled = page != currentPage
                };
                button.Click += (_, _) => { currentPage = targetPage; Render(); };
                pager.Children.Add(button);
            }
        }

        search.TextChanged += (_, _) => { currentPage = 1; Render(); };
        operatorFilter.SelectionChanged += (_, _) => { currentPage = 1; Render(); };
        dateFilter.SelectionChanged += (_, _) => { currentPage = 1; Render(); };
        kindFilter.SelectionChanged += (_, _) => { currentPage = 1; Render(); };
        sortFilter.SelectionChanged += (_, _) => { currentPage = 1; Render(); };
        var searchRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 18,
            Children = { search, summaryText }
        };
        Grid.SetColumn(summaryText, 1);
        var filtersRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { operatorFilter, dateFilter, kindFilter, sortFilter }
        };
        var top = new StackPanel { Spacing = 10, Children = { searchRow, filtersRow } };
        var table = new Border
        {
            Margin = new Thickness(0, 18, 0, 0),
            Background = PanelBrush("#151d28", 178),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = rows
        };
        var fileText = new TextBlock
        {
            Text = "Локальный файл: " + CleaningAuditPath,
            Foreground = Brush("#667085"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 12)
        };
        Render();
        return new ScrollViewer
        {
            Background = PageBackground(),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel { Children = { top, table, pager, fileText } }
        };
    }

    private async Task<bool> ShowCleaningAuditEditDialogAsync(CleaningAuditEntry entry)
    {
        var dialog = new ModalWindow
        {
            Title = "Редактирование уборки",
            Width = 620,
            Height = 650,
            MinWidth = 520,
            MinHeight = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#0b1118")
        };
        TextBox Field(string text, string watermark) => new()
        {
            Text = text,
            Watermark = watermark,
            Height = 42,
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550")
        };
        TextBlock Caption(string text) => new()
        {
            Text = text,
            Foreground = Brush("#8f9aad"),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold
        };

        var hostBox = Field(entry.HostName, "ПК");
        var operatorBox = Field(entry.Operator, "Оператор смены");
        var leftBox = Field(entry.ClientLeftAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"), "дд.мм.гггг чч:мм:сс");
        var cleanedBox = Field(entry.CleanedAtUtc?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") ?? "", "Оставьте пустым, если уборка ожидается");
        var reasonBox = Field(entry.SuspiciousReason, "Причина отметки");
        var error = new TextBlock { Foreground = Brush("#ffb547"), TextWrapping = TextWrapping.Wrap };
        var save = AccentButton("Сохранить", () => { });
        var cancel = GhostButton("Отмена", () => dialog.Close(false));

        static bool TryLocalDate(string value, out DateTime utc)
        {
            if (DateTime.TryParse(value, System.Globalization.CultureInfo.CurrentCulture,
                    System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var local))
            {
                utc = DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
                return true;
            }
            utc = default;
            return false;
        }

        save.Click += (_, _) =>
        {
            var hostName = NormalizeName(hostBox.Text ?? "");
            if (string.IsNullOrWhiteSpace(hostName) || !TryLocalDate(leftBox.Text ?? "", out var leftUtc))
            {
                error.Text = "Укажите ПК и корректное время ухода клиента.";
                return;
            }
            DateTime? cleanedUtc = null;
            if (!string.IsNullOrWhiteSpace(cleanedBox.Text))
            {
                if (!TryLocalDate(cleanedBox.Text, out var parsedCleaned) || parsedCleaned < leftUtc)
                {
                    error.Text = "Время уборки должно быть корректным и не раньше ухода клиента.";
                    return;
                }
                cleanedUtc = parsedCleaned;
            }

            var oldHost = entry.HostName;
            var oldLeft = entry.ClientLeftAtUtc;
            var wasPending = TryGetPendingCleaning(oldHost, out var oldPending)
                && Math.Abs((oldPending.ClientLeftAtUtc - oldLeft).TotalSeconds) < 1;
            if (wasPending)
            {
                RemovePendingCleaning(oldHost);
                cleaningReminderMinutes.Remove(oldHost);
                if (records.TryGetValue(oldHost, out var oldRecord))
                {
                    oldRecord.CleaningStatus = "Нет задачи";
                    oldRecord.FreedAtUtc = null;
                }
            }

            entry.HostName = hostName;
            entry.Operator = (operatorBox.Text ?? "").Trim();
            entry.ClientLeftAtUtc = leftUtc;
            entry.CleanedAtUtc = cleanedUtc;
            entry.SuspiciousReason = (reasonBox.Text ?? "").Trim();
            if (!cleanedUtc.HasValue)
            {
                pendingCleaningAudits[hostName] = new PendingCleaningAudit
                {
                    HostName = hostName,
                    Operator = entry.Operator,
                    ClientLeftAtUtc = leftUtc,
                    LastReminderAtUtc = entry.LastReminderAtUtc
                };
                if (records.TryGetValue(hostName, out var record))
                {
                    record.CleaningStatus = "Нужно убрать";
                    record.FreedAtUtc = leftUtc;
                }
            }
            else if (records.TryGetValue(hostName, out var cleanedRecord) && wasPending)
            {
                cleanedRecord.CleaningStatus = "Убрано";
                cleanedRecord.FreedAtUtc = null;
            }

            SaveCleaningAuditState();
            RefreshMap();
            UpdateSelectedPanel();
            dialog.Close(true);
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Children = { cancel, save }
        };
        dialog.Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Данные уборки", Foreground = Brush("#f5f7ff"), FontSize = 22, FontWeight = FontWeight.SemiBold },
                    Caption("Компьютер"), hostBox,
                    Caption("Оператор смены"), operatorBox,
                    Caption("Клиент ушёл"), leftBox,
                    Caption("ПК убран"), cleanedBox,
                    Caption("Контрольная отметка"), reasonBox,
                    error,
                    buttons
                }
            }
        };
        return await dialog.ShowDialog<bool>(this);
    }

    private void DeleteCleaningAuditEntry(CleaningAuditEntry entry)
    {
        if (TryGetPendingCleaning(entry.HostName, out var pending)
            && Math.Abs((pending.ClientLeftAtUtc - entry.ClientLeftAtUtc).TotalSeconds) < 1)
        {
            RemovePendingCleaning(entry.HostName);
            cleaningReminderMinutes.Remove(entry.HostName);
            if (records.TryGetValue(entry.HostName, out var record))
            {
                record.CleaningStatus = "Нет задачи";
                record.FreedAtUtc = null;
            }
        }
        cleaningAuditEntries.Remove(entry);
        SaveCleaningAuditState();
        RefreshMap();
        UpdateSelectedPanel();
    }

    private static string FormatCleaningDelay(TimeSpan delay)
    {
        if (delay.TotalHours >= 1)
            return ((int)delay.TotalHours) + " ч " + delay.Minutes.ToString("00") + " мин";
        return ((int)delay.TotalMinutes) + " мин " + delay.Seconds.ToString("00") + " сек";
    }

    private static string CleaningSuspicion(DateTime clientLeftAtUtc, DateTime cleanedAtUtc, DateTime? reminderAtUtc)
    {
        var afterLeaving = cleanedAtUtc - clientLeftAtUtc;
        if (afterLeaving >= TimeSpan.Zero && afterLeaving <= TimeSpan.FromSeconds(30))
            return "убран через " + (int)Math.Round(afterLeaving.TotalSeconds) + " сек. после ухода клиента";

        if (reminderAtUtc.HasValue)
        {
            var afterReminder = cleanedAtUtc - reminderAtUtc.Value;
            if (afterReminder >= TimeSpan.Zero && afterReminder <= TimeSpan.FromSeconds(10))
                return "убран через " + (int)Math.Round(afterReminder.TotalSeconds) + " сек. после уведомления";
        }

        return "";
    }

    private static string CleaningAuditPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmartShellCleaner",
        "cleaning-audit.json");

    private void LoadCleaningAuditState()
    {
        cleaningAuditEntries.Clear();
        pendingCleaningAudits.Clear();
        try
        {
            if (!File.Exists(CleaningAuditPath)) return;
            var document = JsonSerializer.Deserialize<CleaningAuditDocument>(File.ReadAllText(CleaningAuditPath));
            if (document == null) return;
            cleaningAuditEntries.AddRange(document.Entries.Where(x => !string.IsNullOrWhiteSpace(x.HostName)));
            foreach (var pending in document.Pending.Where(x => !string.IsNullOrWhiteSpace(x.HostName)))
            {
                pendingCleaningAudits[NormalizeName(pending.HostName)] = pending;
                if (FindCleaningAuditEntry(pending.HostName, pending.ClientLeftAtUtc) == null)
                    cleaningAuditEntries.Add(new CleaningAuditEntry
                    {
                        Operator = pending.Operator,
                        HostName = NormalizeName(pending.HostName),
                        ClientLeftAtUtc = pending.ClientLeftAtUtc,
                        LastReminderAtUtc = pending.LastReminderAtUtc
                    });
            }
            PruneCleaningAuditState();
            SaveCleaningAuditState();
        }
        catch
        {
            cleaningAuditEntries.Clear();
            pendingCleaningAudits.Clear();
        }
    }

    private void SaveCleaningAuditState()
    {
        try
        {
            PruneCleaningAuditState();
            var path = CleaningAuditPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tempPath = path + ".tmp";
            var document = new CleaningAuditDocument
            {
                Entries = cleaningAuditEntries.OrderByDescending(x => x.CleanedAtUtc ?? x.ClientLeftAtUtc.AddMinutes(20)).ToList(),
                Pending = pendingCleaningAudits.Values.OrderBy(x => x.ClientLeftAtUtc).ToList()
            };
            File.WriteAllText(tempPath, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tempPath, path, true);
        }
        catch
        {
        }
    }

    private void PruneCleaningAuditState()
    {
        var cutoff = DateTime.UtcNow.AddDays(-31);
        cleaningAuditEntries.RemoveAll(x => (x.CleanedAtUtc ?? x.ClientLeftAtUtc.AddMinutes(20)) < cutoff);
        foreach (var key in pendingCleaningAudits.Where(x => x.Value.ClientLeftAtUtc < cutoff).Select(x => x.Key).ToList())
            pendingCleaningAudits.Remove(key);
    }

    private void SetPendingCleaning(string hostName, DateTime clientLeftAtUtc)
    {
        var normalized = NormalizeName(hostName);
        var pending = new PendingCleaningAudit
        {
            HostName = normalized,
            Operator = apiAccessInfo?.DisplayName ?? "",
            ClientLeftAtUtc = clientLeftAtUtc
        };
        pendingCleaningAudits[normalized] = pending;
        if (FindCleaningAuditEntry(normalized, clientLeftAtUtc) == null)
            cleaningAuditEntries.Add(new CleaningAuditEntry
            {
                Operator = pending.Operator,
                HostName = normalized,
                ClientLeftAtUtc = clientLeftAtUtc
            });
    }

    private bool TryGetPendingCleaning(string hostName, out PendingCleaningAudit pending) =>
        pendingCleaningAudits.TryGetValue(NormalizeName(hostName), out pending!);

    private bool RemovePendingCleaning(string hostName) => pendingCleaningAudits.Remove(NormalizeName(hostName));

    private CleaningAuditEntry? FindCleaningAuditEntry(string hostName, DateTime clientLeftAtUtc) =>
        cleaningAuditEntries.FirstOrDefault(x =>
            string.Equals(NormalizeName(x.HostName), NormalizeName(hostName), StringComparison.OrdinalIgnoreCase)
            && Math.Abs((x.ClientLeftAtUtc - clientLeftAtUtc).TotalSeconds) < 1);

    private static string NormalizeOperatorPhone(string? phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 10) digits = "7" + digits;
        if (digits.Length == 11 && digits[0] == '8') digits = "7" + digits[1..];
        return digits;
    }

    private static string OperatorPaletteName(ApiAccessInfo info)
    {
        var phone = NormalizeOperatorPhone(info.Phone);
        if (phone == CleaningAuditEditorPhone) return "Родион";
        if (phone == SonyaOperatorPhone) return "Соня";
        if (phone == ArtemOperatorPhone) return "Артём";
        if (phone == KirillOperatorPhone) return "Кирилл";

        var name = (info.DisplayName ?? "").ToLowerInvariant().Replace('ё', 'е');
        if (name.Contains("внуков") || name.Contains("родион")) return "Родион";
        if (name.Contains("соколов") || name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(x => x.StartsWith("сон") || x.StartsWith("соф"))) return "Соня";
        if (name.Contains("коваленко") || name.Contains("артем")) return "Артём";
        if (name.Contains("кирил")) return "Кирилл";
        return "";
    }

    private void ApplyOperatorPalette(ApiAccessInfo info)
    {
        var phone = NormalizeOperatorPhone(info.Phone);
        var identity = phone.Length > 0 ? phone : info.DisplayName.Trim().ToLowerInvariant().Replace('ё', 'е');
        if (identity.Length == 0 || identity == appliedOperatorPaletteIdentity) return;
        appliedOperatorPaletteIdentity = identity;

        var paletteName = OperatorPaletteName(info);
        if (paletteName.Length == 0)
            UiLabAppearance.ResetCustomPalette();
        else
            UiLabAppearance.ActivateNamedPalette(paletteName);

        ApplyAppearanceBrushes();
        RebuildShell();
    }

    private bool CanViewCleaningAudit() =>
        apiAccessInfo != null && CleaningAuditAccessPhones.Contains(NormalizeOperatorPhone(apiAccessInfo.Phone));

    private bool CanEditCleaningAudit() =>
        apiAccessInfo != null && NormalizeOperatorPhone(apiAccessInfo.Phone) == CleaningAuditEditorPhone;

    private void UpdateCleaningAuditAccess()
    {
        if (navButtons.TryGetValue("cleaning-log", out var button))
            button.IsVisible = CanViewCleaningAudit();
        if (screen == "cleaning-log" && !CanViewCleaningAudit())
            SwitchScreen("map");
        else if (screen == "cleaning-log")
            contentHost.Content = BuildCleaningAuditView();
    }

    private async Task RefreshCurrentOperatorAccessAsync()
    {
        try
        {
            apiAccessInfo = await service.LoadApiAccessInfoAsync(config, CancellationToken.None);
            ApplyOperatorPalette(apiAccessInfo);
        }
        catch
        {
            apiAccessInfo = null;
        }
        UpdateCleaningAuditAccess();
    }

    private async Task<string> LoadCurrentCleaningOperatorAsync()
    {
        try
        {
            apiAccessInfo = await service.LoadApiAccessInfoAsync(config, CancellationToken.None);
        }
        catch
        {
        }

        UpdateCleaningAuditAccess();

        return string.IsNullOrWhiteSpace(apiAccessInfo?.DisplayName)
            ? "Оператор не определён"
            : apiAccessInfo.DisplayName;
    }

    private static string LogEventTitle(string type)
    {
        return type.ToUpperInvariant() switch
        {
            "CLIENT_AUTHORIZED_FROM_HOST" => "Авторизация клиента на ПК",
            "WORKER_AUTHORIZED_FROM_HOST" => "Авторизация сотрудника на ПК",
            "WORKER_AUTHORIZED_FROM_BILLING" => "Авторизация сотрудника в панели",
            "USER_VERIFIED" => "Подтверждение клиента",
            "USER_REGISTERED" => "Регистрация клиента",
            "USER_BANNED" => "Блокировка клиента",
            "USER_UNBANNED" => "Разблокировка клиента",
            "USER_RESET_PASSWORD" => "Сброс пароля клиента",
            "USER_DISCOUNT_UPDATED" => "Изменение скидки клиента",
            "BONUS_UPDATED" => "Изменение бонусов",
            "DEPOSIT_UPDATED" => "Изменение депозита",
            "SEND_VERIFY_CODE" => "Отправка кода подтверждения",
            "SEND_RESET_CODE" => "Отправка кода сброса пароля",
            "WORKER_CREATED" => "Создание сотрудника",
            "WORKER_UPDATED" => "Изменение сотрудника",
            "WORKER_DELETED" => "Удаление сотрудника",
            "WORKER_ENABLED" => "Включение сотрудника",
            "TASK_ADDED" => "Создание задачи",
            "WORK_SHIFT_STARTED" => "Открытие смены",
            "WORK_SHIFT_FINISHED" => "Закрытие смены",
            "HOST_GROUP_CREATED" => "Создание группы компьютеров",
            "HOST_GROUP_UPDATED" => "Изменение группы компьютеров",
            "HOST_GROUP_DELETED" => "Удаление группы компьютеров",
            "HOST_CREATED" => "Создание компьютера",
            "HOST_UPDATED" => "Изменение компьютера",
            "HOST_DELETED" => "Удаление компьютера",
            "HOST_SWITCHED_GROUP" => "Перенос компьютера в группу",
            "HOST_HARDWARE_CHANGED" => "Изменение конфигурации компьютера",
            "HOST_HARDWARE_APPLIED" => "Применение конфигурации компьютера",
            "SHELL_HIGH_ACCESS_ENABLED" => "Включение высокого доступа",
            "SHELL_HIGH_ACCESS_DISABLED" => "Выключение высокого доступа",
            "SHELL_DISABLED" => "Снятие Shell",
            "SHELL_ENABLED" => "Включение Shell",
            "HOST_QR_GENERATE" => "Создание QR-кода компьютера",
            "CLIENT_SESSION_STARTED" => "Старт клиентского сеанса",
            "CLIENT_SESSION_PROLONGED" => "Продление клиентского сеанса",
            "CLIENT_SESSION_FINISHED" => "Завершение клиентского сеанса",
            "CLIENT_SESSION_HOST_CHANGED" => "Перенос сеанса",
            "HOST_PUNISHED" => "Штраф сеанса",
            "CLIENT_SESSION_PAUSED" => "Приостановка сеанса",
            "CLIENT_SESSION_UNPAUSED" => "Возобновление сеанса",
            "CLIENT_SESSION_UNPAUSED_AUTO" => "Автоматическое возобновление сеанса",
            "PAYMENT_CREATED" => "Создание продажи",
            "PAYMENT_REFUND" => "Возврат продажи",
            "POST_PAYMENT_CANCELED" => "Отмена постоплаты",
            "BONUS_PAYMENT_CREATED" => "Бонусное пополнение",
            "TARIFF_CREATED" => "Создание тарифа",
            "TARIFF_UPDATED" => "Изменение тарифа",
            "TARIFF_DELETED" => "Удаление тарифа",
            "SHORTCUT_CREATED" => "Создание приложения",
            "SHORTCUT_UPDATED" => "Изменение приложения",
            "SHORTCUT_DELETED" => "Удаление приложения",
            "GOOD_CREATED" => "Создание товара",
            "GOOD_UPDATED" => "Изменение товара",
            "GOOD_DELETED" => "Удаление товара",
            "WAREHOUSE_GOODS_ADDED" => "Внесение товара на склад",
            "WAREHOUSE_GOODS_DISPOSED" => "Списание товара",
            "GOOD_LOW_STOCK_THRESHOLD_REACHED" => "Заканчивается товар",
            "GOOD_CATEGORY_CREATED" => "Создание группы товаров",
            "GOOD_CATEGORY_UPDATED" => "Изменение группы товаров",
            "GOOD_CATEGORY_DELETED" => "Удаление группы товаров",
            "SERVICE_CREATED" => "Создание услуги",
            "SERVICE_UPDATED" => "Изменение услуги",
            "SERVICE_DELETED" => "Удаление услуги",
            "DISCOUNT_CREATED" => "Создание скидки",
            "DISCOUNT_UPDATED" => "Изменение скидки",
            "DISCOUNT_DELETED" => "Удаление скидки",
            "PROMO_CODE_CREATED" => "Создание промокода",
            "PROMO_CODE_UPDATED" => "Изменение промокода",
            "PROMO_CODE_DELETED" => "Удаление промокода",
            "PROMO_CODE_USED" => "Использование промокода",
            "PROMO_CODE_EXPIRED" => "Истечение срока промокода",
            "PROMO_CAMPAIGN_CREATED" => "Создание группы промокодов",
            "PROMO_CAMPAIGN_UPDATED" => "Изменение группы промокодов",
            "PROMO_CAMPAIGN_DELETED" => "Удаление группы промокодов",
            "PROMO_CAMPAIGN_CODES_GENERATED" => "Генерация промокодов",
            "PROMO_CAMPAIGN_EXPIRED" => "Истечение срока группы промокодов",
            "CASH_ORDER_CREATED" => "Создание кассового ордера",
            "SETTINGS_UPDATED" => "Изменение конфигурации",
            "SETTINGS_COPIED" => "Копирование конфигурации",
            "TELEGRAM_LINKED" => "Подключение Telegram",
            "TELEGRAM_UNLINKED" => "Отключение Telegram",
            "HOST_ADMIN_CALLED" => "Вызов администратора",
            "HOST_ADMIN_CALL_CHECKED" => "Принятие вызова",
            "HOST_ADMIN_CALL_FAILED" => "Пропущенный вызов",
            "CLUB_COMMENT_CREATED" => "Отзыв клиента",
            "ACHIEVEMENT_CREATED" => "Создание достижения",
            "ACHIEVEMENT_DELETED" => "Удаление достижения",
            "ACHIEVEMENT_ASSIGNED" => "Выдача достижения",
            "BOOKING_CREATED" => "Создание бронирования",
            "BOOKING_CANCELED" => "Отмена бронирования",
            "BOOKING_FAILED_TO_START" => "Ошибка запуска бронирования",
            "BOOKING_UPDATED" => "Изменение бронирования",
            "BOOKING_CONFIRMED" => "Подтверждение бронирования",
            "COMBO_CREATED" => "Создание комбо-набора",
            "COMBO_UPDATED" => "Изменение комбо-набора",
            "COMBO_DELETED" => "Удаление комбо-набора",
            "DEPOSIT_ADDED_ONLINE" => "Онлайн-пополнение депозита",
            "DEBT_ACTIVATED" => "Активация задолженности",
            "USER_GROUP_CREATED" => "Создание группы аккаунтов",
            "USER_GROUP_UPDATED" => "Изменение группы аккаунтов",
            "USER_GROUP_DELETED" => "Удаление группы аккаунтов",
            "USER_CHANGED_GROUP" => "Перенос клиента в группу",
            "USER_DEPOSIT_TRANSFERRED" => "Межклубный перевод депозита",
            "ADVERTISEMENT_MANUAL_COMPLETED" => "Ручное завершение объявления",
            "ADVERTISEMENT_APPROVED" => "Одобрение объявления",
            "ADVERTISEMENT_REJECTED" => "Отклонение объявления",
            "NEWS_CREATED" => "Создание новости",
            "NEWS_UPDATED" => "Изменение новости",
            "NEWS_DELETED" => "Удаление новости",
            "OWNER_CHANGED" => "Смена владельца клуба",
            _ => type.Replace('_', ' ')
        };
    }

    private async Task<(DateTime Start, DateTime Finish)?> ShowLogsDateRangeDialogAsync(DateTime start, DateTime finish)
    {
        var startPicker = new CalendarDatePicker
        {
            SelectedDate = start,
            DisplayDate = start,
            FirstDayOfWeek = DayOfWeek.Monday,
            Height = 48,
            Background = Brush("#151d28"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2f8cff")
        };
        var finishPicker = new CalendarDatePicker
        {
            SelectedDate = finish,
            DisplayDate = finish,
            FirstDayOfWeek = DayOfWeek.Monday,
            Height = 48,
            Background = Brush("#151d28"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2f8cff")
        };
        var dialog = new ModalWindow
        {
            Title = "Период логов",
            Width = 620,
            Height = 285,
            CanResize = false,
            SystemDecorations = SystemDecorations.None,
            Background = Brush("#0d141c"),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var apply = AccentButton("Применить", () => { });
        var cancel = GhostButton("Отмена", () => dialog.Close(false));
        apply.Click += (_, _) => dialog.Close(true);
        var pickers = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 14 };
        var startBlock = new StackPanel { Spacing = 8, Children = { CellLabel("Начало периода"), startPicker } };
        var finishBlock = new StackPanel { Spacing = 8, Children = { CellLabel("Конец периода"), finishPicker } };
        Grid.SetColumn(finishBlock, 1);
        pickers.Children.Add(startBlock);
        pickers.Children.Add(finishBlock);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, apply }
        };
        dialog.Content = new Border
        {
            BorderBrush = Brush("#344152"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Spacing = 22,
                Children =
                {
                    new TextBlock { Text = "Период журнала", Foreground = Brush("#f5f7ff"), FontSize = 20, FontWeight = FontWeight.SemiBold },
                    pickers,
                    actions
                }
            }
        };
        var accepted = await dialog.ShowDialog<bool>(this);
        if (!accepted) return null;
        var selectedStart = startPicker.SelectedDate?.Date ?? start.Date;
        var selectedFinish = finishPicker.SelectedDate?.Date ?? finish.Date;
        if (selectedFinish < selectedStart) (selectedStart, selectedFinish) = (selectedFinish, selectedStart);
        return (selectedStart, selectedFinish);
    }

    private TextBlock CellLabel(string text) => new()
    {
        Text = text,
        Foreground = Brush("#8f9aad"),
        FontSize = 12,
        FontWeight = FontWeight.SemiBold
    };

    private Control BuildClientsView()
    {
        const int clientsPerPage = 25;
        var currentPage = 1;
        var loadVersion = 0;
        var searchVersion = 0;
        var openingClient = false;
        var loadedClients = new List<ClientInfo>();
        var sortColumn = "lastActivity";
        var sortDescending = true;
        var sortButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        var rows = new StackPanel();
        var pagination = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var countText = new TextBlock
        {
            Text = "Клиенты",
            Foreground = Brush("#f5f7ff"),
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var status = new TextBlock
        {
            Foreground = Brush("#8f9aad"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var search = new TextBox
        {
            Width = 330,
            Height = 42,
            Watermark = "Найти клиента",
            Background = Brush("#151d28"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550"),
            Padding = new Thickness(14, 0)
        };

        TextBlock CellText(string text, IBrush? color = null, bool bold = false, double size = 13) => new()
        {
            Text = string.IsNullOrWhiteSpace(text) ? "—" : text,
            Foreground = color ?? Brush("#f5f7ff"),
            FontSize = size,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        void AddCell(Grid grid, Control cell, int column)
        {
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        Grid ClientGrid() => new()
        {
            ColumnDefinitions = new ColumnDefinitions("210,170,190,100,150,170,120,190"),
            ColumnSpacing = 12
        };

        Border HeaderRow()
        {
            var grid = ClientGrid();
            var columns = new[]
            {
                ("Никнейм", "nickname"),
                ("Телефон", "phone"),
                ("Посл. посещение", "lastActivity"),
                ("Скидка", "discount"),
                ("Группа", "group"),
                ("Депозит", "deposit"),
                ("Статус", "status"),
                ("Регистрация", "created")
            };
            for (var index = 0; index < columns.Length; index++)
                AddCell(grid, SortHeader(columns[index].Item1, columns[index].Item2), index);
            return new Border
            {
                Height = 52,
                Background = Brush("#151d28"),
                BorderBrush = Brush("#303a48"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 0),
                Child = grid
            };
        }

        async Task OpenClientAsync(ClientInfo client)
        {
            if (openingClient || string.IsNullOrWhiteSpace(client.Uuid)) return;
            openingClient = true;
            try
            {
                status.Text = "Загружаю профиль клиента...";
                var details = await service.LoadClientDetailsAsync(config, client.Uuid, CancellationToken.None) ?? client;
                await ShowClientCardDialogAsync(details, this);
                if (screen == "clients") await LoadAsync(currentPage);
            }
            catch (Exception ex)
            {
                ShowToast("Клиенты", "Не удалось открыть профиль: " + ex.Message);
            }
            finally
            {
                openingClient = false;
            }
        }

        Border ClientTableRow(ClientInfo client)
        {
            var grid = ClientGrid();
            AddCell(grid, CellText(string.IsNullOrWhiteSpace(client.Nickname) ? client.Phone : client.Nickname, null, true), 0);
            AddCell(grid, CellText(client.Phone, Brush("#c3ccda")), 1);
            AddCell(grid, CellText(client.LastActivityText, Brush("#c3ccda")), 2);
            AddCell(grid, CellText(client.DiscountText == "Нет скидки" ? "0%" : client.DiscountText), 3);
            AddCell(grid, CellText(client.GroupTitle, Brush("#c3ccda")), 4);

            var balance = new StackPanel
            {
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    CellText(client.BalanceText, null, true),
                    CellText("★  " + client.Bonus.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), Brush("#f5b942"), false, 12)
                }
            };
            AddCell(grid, balance, 5);
            var statusPalette = client.StatusText switch
            {
                "Заблокирован" => (Background: "#3a171e", Foreground: "#ff6b73"),
                "Не подтверждён" => (Background: "#3a2a11", Foreground: "#f5b942"),
                _ => (Background: "#123524", Foreground: "#35d48a")
            };
            AddCell(grid, new Border
            {
                Background = Brush(statusPalette.Background),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(9, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = CellText(client.StatusText, Brush(statusPalette.Foreground), true, 12)
            }, 6);
            AddCell(grid, CellText(client.CreatedAtText, Brush("#c3ccda")), 7);

            var row = new Border
            {
                MinHeight = 64,
                Background = Brushes.Transparent,
                BorderBrush = Brush("#263244"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 8),
                Child = grid
            };
            row.PointerEntered += (_, _) => row.Background = Brush("#1b2532");
            row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
            row.PointerPressed += async (_, e) =>
            {
                if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
                    await OpenClientAsync(client);
            };
            return row;
        }

        decimal DiscountValue(ClientInfo client)
        {
            var match = Regex.Match(client.DiscountText, @"\d+(?:[.,]\d+)?");
            return decimal.TryParse(
                match.Value.Replace(',', '.'),
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value)
                ? value
                : 0;
        }

        IEnumerable<ClientInfo> SortBy<T>(Func<ClientInfo, T> selector) => sortDescending
            ? loadedClients.OrderByDescending(selector)
            : loadedClients.OrderBy(selector);

        IEnumerable<ClientInfo> SortedClients() => sortColumn switch
        {
            "nickname" => SortBy(x => (string.IsNullOrWhiteSpace(x.Nickname) ? x.Phone : x.Nickname).ToUpperInvariant()),
            "phone" => SortBy(x => x.Phone),
            "discount" => SortBy(DiscountValue),
            "group" => SortBy(x => x.GroupTitle.ToUpperInvariant()),
            "deposit" => SortBy(x => x.Deposit),
            "status" => SortBy(x => x.StatusText.ToUpperInvariant()),
            "created" => SortBy(x => x.CreatedAtUtc ?? DateTime.MinValue),
            _ => SortBy(x => x.LastActivityAtUtc ?? DateTime.MinValue)
        };

        void RenderRows()
        {
            rows.Children.Clear();
            foreach (var client in SortedClients()
                         .Skip((currentPage - 1) * clientsPerPage)
                         .Take(clientsPerPage))
                rows.Children.Add(ClientTableRow(client));
            if (loadedClients.Count == 0)
            {
                rows.Children.Add(new TextBlock
                {
                    Text = "Клиенты не найдены.",
                    Foreground = Brush("#8f9aad"),
                    Margin = new Thickness(20),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }
        }

        void RefreshSortHeaders()
        {
            foreach (var item in sortButtons)
            {
                var title = item.Key switch
                {
                    "nickname" => "Никнейм",
                    "phone" => "Телефон",
                    "lastActivity" => "Посл. посещение",
                    "discount" => "Скидка",
                    "group" => "Группа",
                    "deposit" => "Депозит",
                    "status" => "Статус",
                    "created" => "Регистрация",
                    _ => item.Key
                };
                var active = item.Key.Equals(sortColumn, StringComparison.OrdinalIgnoreCase);
                item.Value.Content = title + (active ? sortDescending ? "  ↓" : "  ↑" : "");
                item.Value.Foreground = active ? Brush("#60a5fa") : Brush("#a8b3c7");
            }
        }

        Button SortHeader(string text, string column)
        {
            var button = new Button
            {
                Content = text,
                Height = 52,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Foreground = Brush("#a8b3c7"),
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            button.Click += (_, _) =>
            {
                if (sortColumn.Equals(column, StringComparison.OrdinalIgnoreCase))
                    sortDescending = !sortDescending;
                else
                {
                    sortColumn = column;
                    sortDescending = false;
                }
                RefreshSortHeaders();
                ShowPage(1);
            };
            sortButtons[column] = button;
            RefreshSortHeaders();
            return button;
        }

        Button PageButton(string text, int page, bool active = false, bool enabled = true)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 40,
                Height = 40,
                Padding = new Thickness(10, 0),
                IsEnabled = enabled,
                Background = active ? Brush("#132b4a") : Brushes.Transparent,
                BorderBrush = active ? Brush("#2f8cff") : Brushes.Transparent,
                Foreground = active ? Brush("#f5f7ff") : Brush("#c0cad8"),
                FontWeight = FontWeight.SemiBold
            };
            button.Click += (_, _) => ShowPage(page);
            return button;
        }

        void RefreshPagination(ClientListPage result)
        {
            pagination.Children.Clear();
            pagination.IsVisible = result.LastPage > 1;
            if (result.LastPage <= 1) return;

            pagination.Children.Add(PageButton("‹", Math.Max(1, result.CurrentPage - 1), enabled: result.CurrentPage > 1));
            var pages = new SortedSet<int> { 1, result.LastPage };
            if (result.CurrentPage <= 4)
                for (var page = 1; page <= Math.Min(6, result.LastPage); page++) pages.Add(page);
            else if (result.CurrentPage >= result.LastPage - 3)
                for (var page = Math.Max(1, result.LastPage - 5); page <= result.LastPage; page++) pages.Add(page);
            else
                for (var page = result.CurrentPage - 2; page <= result.CurrentPage + 2; page++) pages.Add(page);

            var previous = 0;
            foreach (var page in pages)
            {
                if (previous > 0 && page - previous > 1)
                    pagination.Children.Add(CellText("…", Brush("#8f9aad"), false, 14));
                pagination.Children.Add(PageButton(page.ToString(), page, page == result.CurrentPage));
                previous = page;
            }
            pagination.Children.Add(PageButton("›", Math.Min(result.LastPage, result.CurrentPage + 1), enabled: result.CurrentPage < result.LastPage));
        }

        void ShowPage(int page)
        {
            var lastPage = Math.Max(1, (int)Math.Ceiling(loadedClients.Count / (double)clientsPerPage));
            currentPage = Math.Clamp(page, 1, lastPage);
            RenderRows();

            var firstShown = loadedClients.Count == 0 ? 0 : (currentPage - 1) * clientsPerPage + 1;
            var lastShown = Math.Min(loadedClients.Count, firstShown + clientsPerPage - 1);
            status.Text = loadedClients.Count == 0
                ? "Нет записей"
                : $"Показано {firstShown}–{lastShown} из {loadedClients.Count}";
            RefreshPagination(new ClientListPage
            {
                CurrentPage = currentPage,
                LastPage = lastPage,
                Total = loadedClients.Count
            });
        }

        async Task LoadAsync(int page = 1)
        {
            var version = ++loadVersion;
            currentPage = Math.Max(1, page);
            rows.Children.Clear();
            pagination.IsVisible = false;
            status.Text = "Загружаю клиентов...";
            rows.Children.Add(new TextBlock
            {
                Text = "Загружаю клиентов...",
                Foreground = Brush("#8f9aad"),
                Margin = new Thickness(20),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            try
            {
                if (!HasSmartShellAccess()) throw new InvalidOperationException("Подтяните токен SmartShell из браузера.");
                var result = await service.LoadAllClientsAsync(config, search.Text ?? "", CancellationToken.None);
                if (version != loadVersion || screen != "clients") return;

                loadedClients.Clear();
                loadedClients.AddRange(result.Entries);
                countText.Text = "Клиенты  " + result.Total;
                ShowPage(page);
            }
            catch (Exception ex)
            {
                if (version != loadVersion || screen != "clients") return;
                rows.Children.Clear();
                rows.Children.Add(new TextBlock
                {
                    Text = "Не удалось загрузить клиентов: " + ex.Message,
                    Foreground = Brush("#f5a524"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(20)
                });
                status.Text = "Ошибка загрузки";
            }
        }

        search.TextChanged += async (_, _) =>
        {
            var version = ++searchVersion;
            await Task.Delay(350);
            if (version == searchVersion && screen == "clients") await LoadAsync(1);
        };
        search.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            searchVersion++;
            await LoadAsync(1);
            e.Handled = true;
        };

        var refresh = new Button
        {
            Content = "↻",
            Width = 42,
            Height = 42,
            Background = Brush("#151d28"),
            BorderBrush = Brush("#2b3550"),
            Foreground = Brush("#4ea1ff"),
            FontSize = 17
        };
        ToolTip.SetTip(refresh, "Обновить клиентов");
        refresh.Click += async (_, _) => await LoadAsync(currentPage);

        var createClient = new Button
        {
            Content = "+  Создать клиента",
            Height = 42,
            Padding = new Thickness(16, 0),
            Background = Brush("#2f8cff"),
            BorderBrush = Brush("#2f8cff"),
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold
        };
        ToolTip.SetTip(createClient, "Открыть защищённую форму регистрации SmartShell");
        createClient.Click += async (_, _) =>
        {
            if (await OpenSmartShellClientRegistrationAsync())
                await LoadAsync(1);
        };

        var activeTab = new Border
        {
            Background = Brush("#24313f"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 10),
            Child = countText
        };
        var tabs = new Border
        {
            Background = Brush("#151d28"),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(4),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    activeTab,
                    new Border
                    {
                        Padding = new Thickness(16, 10),
                        Child = CellText("Группы  0", Brush("#8f9aad"), true)
                    }
                }
            }
        };

        var toolbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,24,Auto,10,Auto,10,Auto,*"),
            Margin = new Thickness(0, 0, 0, 16)
        };
        toolbar.Children.Add(tabs);
        Grid.SetColumn(search, 2);
        toolbar.Children.Add(search);
        Grid.SetColumn(refresh, 4);
        toolbar.Children.Add(refresh);
        Grid.SetColumn(createClient, 6);
        toolbar.Children.Add(createClient);
        Grid.SetColumn(status, 7);
        toolbar.Children.Add(status);

        var tableStack = new StackPanel { MinWidth = 1270, Children = { HeaderRow(), rows } };
        var tableScroll = new ScrollViewer
        {
            Content = tableStack,
            Background = Brush("#151d28"),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(tableScroll, 1);
        var paginationBorder = new Border
        {
            MinHeight = 58,
            Background = Brush("#111820"),
            BorderBrush = Brush("#2b3442"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = pagination
        };
        Grid.SetRow(paginationBorder, 2);
        var table = new Grid
        {
            RowDefinitions = new RowDefinitions("0,*,Auto"),
            Children = { tableScroll, paginationBorder }
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Background = PageBackground(),
            Children =
            {
                toolbar,
                new Border
                {
                    Background = Brush("#151d28"),
                    BorderBrush = Brush("#202a38"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(7),
                    Child = table
                }
            }
        };
        Grid.SetRow(root.Children[1], 1);
        Dispatcher.UIThread.Post(async () => await LoadAsync());
        return root;
    }

    private Control BuildPaymentsView()
    {
        var payments = new List<WorkShiftPayment>();
        WorkShiftPayment? selectedPayment = null;
        var postPaymentsOnly = false;
        var sortColumn = "date";
        var sortDescending = true;
        var sortButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        const int paymentsPerPage = 50;
        var currentPage = 1;
        var refundsEnabled = true;
        var cashboxEnabled = true;
        var refundWindow = TimeSpan.FromMinutes(30);
        var refundInProgress = new HashSet<int>();
        var rowRefundControls = new List<(Button Button, TextBlock Timer, WorkShiftPayment Payment)>();
        var detailRefundControls = new List<(Button Button, TextBlock Timer, WorkShiftPayment Payment)>();

        var search = new TextBox
        {
            Width = 300,
            Height = 42,
            Watermark = "Поиск по платежам",
            Background = Brush("#151d28"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550"),
            Padding = new Thickness(14, 0)
        };
        var status = new TextBlock
        {
            Foreground = Brush("#8f9aad"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var pageRange = new TextBlock
        {
            Foreground = Brush("#8f9aad"),
            VerticalAlignment = VerticalAlignment.Center
        };
        var pageInfo = new TextBlock
        {
            Foreground = Brush("#d8dfec"),
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var previousPage = new Button
        {
            Content = "‹",
            Width = 38,
            Height = 34,
            Padding = new Thickness(0),
            Background = Brush("#182231"),
            BorderBrush = Brush("#2b3550"),
            Foreground = Brush("#f5f7ff"),
            FontSize = 18
        };
        var nextPage = new Button
        {
            Content = "›",
            Width = 38,
            Height = 34,
            Padding = new Thickness(0),
            Background = Brush("#182231"),
            BorderBrush = Brush("#2b3550"),
            Foreground = Brush("#f5f7ff"),
            FontSize = 18
        };
        var paginationPanel = new Border
        {
            Height = 52,
            Background = PanelBrush("#111820", 188),
            BorderBrush = Brush("#2b3442"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(14, 0),
            IsVisible = false
        };
        var rows = new StackPanel();
        var detailContent = new StackPanel { Margin = new Thickness(18), Spacing = 8 };
        var detailPanel = new Border
        {
            Margin = new Thickness(16, 0, 0, 0),
            Background = PanelBrush("#151d28", 188),
            BorderBrush = Brush("#253044"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Child = new ScrollViewer { Content = detailContent },
            IsVisible = false
        };
        var detailColumn = new ColumnDefinition(0, GridUnitType.Pixel);
        var allButton = new Button();
        var postButton = new Button();

        TextBlock Cell(string text, string color = "#f5f7ff", FontWeight? weight = null)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brush(color),
                FontWeight = weight ?? FontWeight.Normal,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                TextAlignment = TextAlignment.Left,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(12, 0)
            };
        }

        Grid TableGrid()
        {
            return new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("130,155,1.35*,1.1*,165,90,130"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
        }

        void AddCell(Grid grid, Control cell, int column)
        {
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        string PaymentColor(WorkShiftPayment payment)
        {
            return payment.MethodCode.ToUpperInvariant() switch
            {
                "CASH" => "#21c979",
                "CARD" => "#4ea1ff",
                "DEPOSIT" => "#9b7cff",
                "BONUS" => "#f2b84b",
                _ => "#a9b2c5"
            };
        }

        string PaymentBackground(WorkShiftPayment payment)
        {
            return payment.MethodCode.ToUpperInvariant() switch
            {
                "CASH" => "#12372a",
                "CARD" => "#172f4c",
                "DEPOSIT" => "#2b2148",
                "BONUS" => "#3b3018",
                _ => "#252d38"
            };
        }

        Border PaymentBadge(WorkShiftPayment payment)
        {
            var color = payment.IsRefunded ? "#ff7b83" : PaymentColor(payment);
            return new Border
            {
                Background = Brush(payment.IsRefunded ? "#451820" : PaymentBackground(payment)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 3),
                Margin = new Thickness(12, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = payment.Method, Foreground = Brush(color), FontSize = 12 }
            };
        }

        TimeSpan RefundRemaining(WorkShiftPayment payment)
        {
            return payment.CreatedAtLocal.Add(refundWindow) - DateTime.Now;
        }

        bool RefundIsAvailable(WorkShiftPayment payment)
        {
            return refundsEnabled && !payment.IsRefunded && !payment.NotRefundable && RefundRemaining(payment) > TimeSpan.Zero;
        }

        bool RefundIsSafeInApp(WorkShiftPayment payment)
        {
            return !cashboxEnabled
                || payment.Total == 0
                || payment.MethodCode.Equals("DEPOSIT", StringComparison.OrdinalIgnoreCase)
                || payment.MethodCode.Equals("BONUS", StringComparison.OrdinalIgnoreCase);
        }

        bool RefundIsLatestTariff(WorkShiftPayment payment)
        {
            if (!payment.Category.Equals("Покупка тарифов", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(payment.ClientUuid))
                return true;

            var latest = payments
                .Where(x => !x.IsRefunded
                    && x.Category.Equals("Покупка тарифов", StringComparison.OrdinalIgnoreCase)
                    && x.ClientUuid.Equals(payment.ClientUuid, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.CreatedAtLocal)
                .ThenByDescending(x => x.Id)
                .FirstOrDefault();
            return latest == null || latest.Id == payment.Id;
        }

        void UpdateRefundControls(List<(Button Button, TextBlock Timer, WorkShiftPayment Payment)> controls)
        {
            foreach (var control in controls)
            {
                var remaining = RefundRemaining(control.Payment);
                control.Button.IsVisible = RefundIsAvailable(control.Payment);
                control.Button.IsEnabled = RefundIsSafeInApp(control.Payment)
                    && RefundIsLatestTariff(control.Payment)
                    && !refundInProgress.Contains(control.Payment.Id);
                control.Timer.Text = remaining > TimeSpan.Zero
                    ? ((int)remaining.TotalMinutes).ToString("00") + ":" + remaining.Seconds.ToString("00")
                    : "00:00";
            }
        }

        async Task RefundAsync(WorkShiftPayment payment)
        {
            if (!RefundIsAvailable(payment))
            {
                ShowToast("Платежи", "Время отмены этого платежа истекло.");
                return;
            }
            if (!RefundIsSafeInApp(payment))
            {
                ShowToast("Платежи", "В клубе включена касса. Этот платёж нужно отменить через SmartShell вместе с фискальным чеком.");
                return;
            }
            if (!RefundIsLatestTariff(payment))
            {
                ShowToast("Платежи", "Сначала отмените самый последний тариф этого клиента.");
                return;
            }

            var confirmed = await ConfirmAsync(
                "Отмена платежа",
                "Отменить платёж № " + payment.Id + " на сумму " + payment.TotalText + "?",
                true);
            if (!confirmed) return;

            refundInProgress.Add(payment.Id);
            UpdateRefundControls(rowRefundControls);
            UpdateRefundControls(detailRefundControls);
            try
            {
                await service.RefundPaymentAsync(config, payment.Id, CancellationToken.None);
                ShowToast("Платежи", "Платёж № " + payment.Id + " отменён.");
                await LoadAsync();
            }
            catch (Exception ex)
            {
                ShowToast(
                    "Ошибка отмены",
                    ex.Message.Contains("seance is not last", StringComparison.OrdinalIgnoreCase)
                        ? "Сначала отмените самый последний приобретённый тариф этого клиента."
                        : ex.Message);
            }
            finally
            {
                refundInProgress.Remove(payment.Id);
                UpdateRefundControls(rowRefundControls);
                UpdateRefundControls(detailRefundControls);
            }
        }

        Button RefundButton(
            WorkShiftPayment payment,
            List<(Button Button, TextBlock Timer, WorkShiftPayment Payment)> controls,
            bool wide = false)
        {
            var timerText = new TextBlock
            {
                Foreground = Brush("#ff7b83"),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            var button = new Button
            {
                Height = 42,
                Margin = wide ? new Thickness(0, 10, 0, 4) : new Thickness(4, 7),
                Padding = new Thickness(8, 0),
                Background = Brushes.Transparent,
                BorderBrush = Brush("#e54855"),
                BorderThickness = new Thickness(1),
                Foreground = Brush("#ff6b73"),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Content = new StackPanel
                {
                    Spacing = 1,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "◔  Отменить",
                            Foreground = Brush("#ff6b73"),
                            FontSize = 12,
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        timerText
                    }
                }
            };
            timerText.HorizontalAlignment = HorizontalAlignment.Center;
            if (wide) button.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (!RefundIsLatestTariff(payment))
                ToolTip.SetTip(button, "Сначала отмените самый последний тариф этого клиента.");
            else if (!RefundIsSafeInApp(payment))
                ToolTip.SetTip(button, "Касса включена: нужен фискальный возврат через SmartShell.");
            button.Click += async (_, e) =>
            {
                e.Handled = true;
                await RefundAsync(payment);
            };
            controls.Add((button, timerText, payment));
            UpdateRefundControls(controls);
            return button;
        }

        async Task OpenClientAsync(WorkShiftPayment payment)
        {
            if (string.IsNullOrWhiteSpace(payment.ClientUuid)) return;
            try
            {
                var client = await service.LoadClientDetailsAsync(config, payment.ClientUuid, CancellationToken.None);
                if (client == null)
                {
                    ShowToast("Клиент", "SmartShell не вернул данные клиента.");
                    return;
                }
                await ShowClientCardDialogAsync(client, this);
            }
            catch (Exception ex)
            {
                ShowToast("Клиент", ex.Message);
            }
        }

        void RenderDetail()
        {
            detailContent.Children.Clear();
            detailRefundControls.Clear();
            detailPanel.IsVisible = selectedPayment != null;
            detailColumn.Width = new GridLength(selectedPayment == null ? 0 : 360, GridUnitType.Pixel);
            if (selectedPayment == null) return;

            var payment = selectedPayment;
            var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            heading.Children.Add(new TextBlock
            {
                Text = "Платёж № " + payment.Id,
                Foreground = Brush("#f5f7ff"),
                FontSize = 18,
                FontWeight = FontWeight.SemiBold
            });
            var close = new Button
            {
                Content = "×",
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Foreground = Brush("#d8dfec")
            };
            close.Click += (_, _) =>
            {
                selectedPayment = null;
                RenderDetail();
                RenderRows();
            };
            Grid.SetColumn(close, 1);
            heading.Children.Add(close);
            detailContent.Children.Add(heading);
            detailContent.Children.Add(Cell(payment.DateText, "#8f9aad"));

            if (RefundIsAvailable(payment))
                detailContent.Children.Add(RefundButton(payment, detailRefundControls, true));

            if (payment.IsRefunded)
            {
                detailContent.Children.Add(new TextBlock
                {
                    Text = "Платёж отменён",
                    Foreground = Brush("#ff6b73"),
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 10, 0, 4)
                });
            }

            detailContent.Children.Add(new Separator { Background = Brush("#2b3442"), Margin = new Thickness(0, 10) });
            detailContent.Children.Add(new TextBlock
            {
                Text = "Состав чека",
                Foreground = Brush("#f5f7ff"),
                FontSize = 17,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            foreach (var line in payment.Lines)
            {
                var lineGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 12) };
                var lineText = new StackPanel { Spacing = 3 };
                lineText.Children.Add(Cell(line.Title, "#f5f7ff", FontWeight.SemiBold));
                var unit = line.Amount > 0 ? line.Sum / line.Amount : line.Sum;
                lineText.Children.Add(Cell("Продано " + line.Amount + " × " + unit.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " ₽", "#8f9aad"));
                lineGrid.Children.Add(lineText);
                var lineSum = Cell(line.Sum.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " ₽", "#f5f7ff", FontWeight.SemiBold);
                Grid.SetColumn(lineSum, 1);
                lineGrid.Children.Add(lineSum);
                detailContent.Children.Add(lineGrid);
            }

            if (payment.Lines.Count == 0)
                detailContent.Children.Add(Cell("Данные о составе чека отсутствуют", "#8f9aad"));

            detailContent.Children.Add(new Separator { Background = Brush("#2b3442"), Margin = new Thickness(0, 10) });
            if (!string.IsNullOrWhiteSpace(payment.ClientUuid))
            {
                var clientButton = new Button
                {
                    Content = payment.ClientDisplay,
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Transparent,
                    Foreground = Brush("#4ea1ff"),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(0),
                    Height = 32
                };
                clientButton.Click += async (_, _) => await OpenClientAsync(payment);
                detailContent.Children.Add(clientButton);
            }

            var totalGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 10, 0, 0) };
            totalGrid.Children.Add(new TextBlock
            {
                Text = "Итого",
                Foreground = Brush("#f5f7ff"),
                FontSize = 17,
                FontWeight = FontWeight.SemiBold
            });
            var total = new TextBlock
            {
                Text = payment.TotalText,
                Foreground = Brush("#f5f7ff"),
                FontSize = 17,
                FontWeight = FontWeight.SemiBold
            };
            Grid.SetColumn(total, 1);
            totalGrid.Children.Add(total);
            detailContent.Children.Add(totalGrid);
            detailContent.Children.Add(PaymentBadge(payment));
            if (payment.Bonus > 0)
                detailContent.Children.Add(Cell("★ " + payment.Bonus.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), "#ff6b73"));
            UpdateRefundControls(detailRefundControls);
        }

        void RenderRows()
        {
            rows.Children.Clear();
            rowRefundControls.Clear();
            var query = (search.Text ?? "").Trim();
            IEnumerable<WorkShiftPayment> filtered = payments.Where(payment =>
                (!postPaymentsOnly || payment.IsPostPayment)
                && (string.IsNullOrWhiteSpace(query)
                    || payment.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
                    || payment.DateText.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || payment.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || payment.ClientDisplay.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || payment.Method.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || payment.Lines.Any(line => line.Title.Contains(query, StringComparison.OrdinalIgnoreCase))));

            filtered = sortColumn switch
            {
                "number" => sortDescending ? filtered.OrderByDescending(x => x.Id) : filtered.OrderBy(x => x.Id),
                "category" => sortDescending ? filtered.OrderByDescending(x => x.Category) : filtered.OrderBy(x => x.Category),
                "client" => sortDescending ? filtered.OrderByDescending(x => x.ClientDisplay) : filtered.OrderBy(x => x.ClientDisplay),
                "method" => sortDescending ? filtered.OrderByDescending(x => x.Method) : filtered.OrderBy(x => x.Method),
                "total" => sortDescending ? filtered.OrderByDescending(x => x.Total) : filtered.OrderBy(x => x.Total),
                _ => sortDescending ? filtered.OrderByDescending(x => x.CreatedAtLocal) : filtered.OrderBy(x => x.CreatedAtLocal)
            };

            var filteredPayments = filtered.ToList();
            var lastPage = Math.Max(1, (int)Math.Ceiling(filteredPayments.Count / (double)paymentsPerPage));
            currentPage = Math.Clamp(currentPage, 1, lastPage);
            var firstVisible = filteredPayments.Count == 0 ? 0 : (currentPage - 1) * paymentsPerPage + 1;
            var lastVisible = Math.Min(currentPage * paymentsPerPage, filteredPayments.Count);
            pageRange.Text = "Показано " + firstVisible + "–" + lastVisible + " из " + filteredPayments.Count;
            pageInfo.Text = "Страница " + currentPage + " из " + lastPage;
            previousPage.IsEnabled = currentPage > 1;
            nextPage.IsEnabled = currentPage < lastPage;
            paginationPanel.IsVisible = lastPage > 1;

            foreach (var payment in filteredPayments.Skip((currentPage - 1) * paymentsPerPage).Take(paymentsPerPage))
            {
                var rowColor = payment.IsRefunded ? "#ff8a91" : "#f5f7ff";
                var grid = TableGrid();
                AddCell(grid, Cell(payment.Id.ToString(), rowColor, FontWeight.SemiBold), 0);
                AddCell(grid, Cell(payment.DateText, rowColor), 1);
                AddCell(grid, Cell(payment.Category, rowColor), 2);
                AddCell(grid, Cell(
                    payment.ClientDisplay,
                    payment.IsRefunded
                        ? rowColor
                        : string.IsNullOrWhiteSpace(payment.ClientUuid) ? "#8f9aad" : "#c8d0df"), 3);
                AddCell(grid, PaymentBadge(payment), 4);
                AddCell(grid, Cell(payment.TotalText, rowColor, FontWeight.SemiBold), 5);
                if (payment.IsRefunded)
                {
                    AddCell(grid, new TextBlock
                    {
                        Text = "Отменено",
                        Foreground = Brush("#ff6b73"),
                        FontWeight = FontWeight.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }, 6);
                }
                else if (RefundIsAvailable(payment))
                    AddCell(grid, RefundButton(payment, rowRefundControls), 6);

                var row = new Button
                {
                    Height = 58,
                    Padding = new Thickness(0),
                    Background = payment.IsRefunded
                        ? Brush(selectedPayment?.Id == payment.Id ? "#4a1c25" : "#32141a")
                        : selectedPayment?.Id == payment.Id ? Brush("#1d385e") : Brushes.Transparent,
                    BorderBrush = payment.IsRefunded ? Brush("#7d2835") : Brushes.Transparent,
                    BorderThickness = payment.IsRefunded ? new Thickness(1, 0) : new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Content = grid
                };
                row.Click += (_, _) =>
                {
                    selectedPayment = payment;
                    RenderDetail();
                    RenderRows();
                };
                rows.Children.Add(row);
            }

            if (rows.Children.Count == 0)
                rows.Children.Add(new TextBlock
                {
                    Text = postPaymentsOnly ? "Постоплат за текущую смену нет" : "Платежи не найдены",
                    Foreground = Brush("#8f9aad"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 50)
                });
            UpdateRefundControls(rowRefundControls);
        }

        void RefreshSortHeaders()
        {
            foreach (var item in sortButtons)
            {
                var title = item.Key switch
                {
                    "number" => "Номер",
                    "date" => "Дата и время",
                    "category" => "Категория",
                    "client" => "Клиент",
                    "method" => "Оплата",
                    "total" => "Итого",
                    _ => item.Key
                };
                var active = item.Key.Equals(sortColumn, StringComparison.OrdinalIgnoreCase);
                item.Value.Content = title + (active ? sortDescending ? "  ↓" : "  ↑" : "");
                item.Value.Foreground = active ? Brush("#60a5fa") : Brush("#f5f7ff");
            }
        }

        Button SortHeader(string text, string column)
        {
            var button = new Button
            {
                Height = 52,
                Padding = new Thickness(12, 0),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Foreground = Brush("#f5f7ff"),
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            button.Click += (_, _) =>
            {
                currentPage = 1;
                if (sortColumn.Equals(column, StringComparison.OrdinalIgnoreCase))
                    sortDescending = !sortDescending;
                else
                {
                    sortColumn = column;
                    sortDescending = false;
                }
                RefreshSortHeaders();
                RenderRows();
            };
            sortButtons[column] = button;
            RefreshSortHeaders();
            return button;
        }

        void RefreshModeButtons()
        {
            allButton.Background = postPaymentsOnly ? Brushes.Transparent : Brush("#3b414b");
            postButton.Background = postPaymentsOnly ? Brush("#3b414b") : Brushes.Transparent;
        }

        Button ModeButton(string text, bool postMode)
        {
            var button = new Button
            {
                Content = text,
                Height = 40,
                Padding = new Thickness(16, 0),
                Foreground = Brush("#f5f7ff"),
                BorderBrush = Brushes.Transparent,
                FontWeight = FontWeight.SemiBold
            };
            button.Click += (_, _) =>
            {
                postPaymentsOnly = postMode;
                currentPage = 1;
                RefreshModeButtons();
                RenderRows();
            };
            return button;
        }

        async Task LoadAsync()
        {
            status.Text = "Загрузка...";
            try
            {
                try
                {
                    var policy = await service.LoadRefundPolicyAsync(config, CancellationToken.None);
                    refundsEnabled = policy.Enabled;
                    cashboxEnabled = policy.CashboxEnabled;
                    refundWindow = TimeSpan.FromMinutes(policy.HeadroomMinutes);
                }
                catch
                {
                    refundsEnabled = true;
                    cashboxEnabled = true;
                    refundWindow = TimeSpan.FromMinutes(30);
                }
                payments = await service.LoadActiveWorkShiftPaymentsAsync(config, CancellationToken.None);
                selectedPayment = selectedPayment == null ? null : payments.FirstOrDefault(x => x.Id == selectedPayment.Id);
                status.Text = "платежей в смене: " + payments.Count;
                RenderDetail();
                RenderRows();
            }
            catch (Exception ex)
            {
                status.Text = "не удалось загрузить";
                ShowToast("Платежи", ex.Message);
            }
        }

        allButton = ModeButton("▱  Все платежи", false);
        postButton = ModeButton("◴  Постоплаты", true);
        RefreshModeButtons();
        search.TextChanged += (_, _) =>
        {
            currentPage = 1;
            RenderRows();
        };
        previousPage.Click += (_, _) =>
        {
            if (currentPage <= 1) return;
            currentPage--;
            RenderRows();
        };
        nextPage.Click += (_, _) =>
        {
            currentPage++;
            RenderRows();
        };

        var paginationGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
        paginationGrid.Children.Add(pageRange);
        var pageControls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { previousPage, pageInfo, nextPage }
        };
        Grid.SetColumn(pageControls, 1);
        paginationGrid.Children.Add(pageControls);
        paginationPanel.Child = paginationGrid;

        var modePanel = new Border
        {
            Background = PanelBrush("#252b34", 196),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(4),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { allButton, postButton } }
        };
        var refresh = new Button
        {
            Content = "↻",
            Width = 42,
            Height = 42,
            Background = Brush("#151d28"),
            BorderBrush = Brush("#2b3550"),
            Foreground = Brush("#4ea1ff"),
            FontSize = 17
        };
        ToolTip.SetTip(refresh, "Обновить платежи");
        refresh.Click += async (_, _) => await LoadAsync();

        var toolbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,30,Auto,10,Auto,*"),
            Margin = new Thickness(0, 0, 0, 16)
        };
        toolbar.Children.Add(modePanel);
        Grid.SetColumn(search, 2);
        toolbar.Children.Add(search);
        Grid.SetColumn(refresh, 4);
        toolbar.Children.Add(refresh);
        Grid.SetColumn(status, 5);
        toolbar.Children.Add(status);

        var header = TableGrid();
        header.Height = 52;
        AddCell(header, SortHeader("Номер", "number"), 0);
        AddCell(header, SortHeader("Дата и время", "date"), 1);
        AddCell(header, SortHeader("Категория", "category"), 2);
        AddCell(header, SortHeader("Клиент", "client"), 3);
        AddCell(header, SortHeader("Оплата", "method"), 4);
        AddCell(header, SortHeader("Итого", "total"), 5);

        var table = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        table.Children.Add(new Border
        {
            Background = PanelBrush("#151d28", 188),
            BorderBrush = Brush("#2b3442"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = header
        });
        var rowsScroll = new ScrollViewer
        {
            Content = rows,
            Background = PanelBrush("#151d28", 174),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(rowsScroll, 1);
        table.Children.Add(rowsScroll);
        Grid.SetRow(paginationPanel, 2);
        table.Children.Add(paginationPanel);

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions
            {
                new ColumnDefinition(1, GridUnitType.Star),
                detailColumn
            }
        };
        body.Children.Add(new Border
        {
            Background = PanelBrush("#151d28", 188),
            BorderBrush = Brush("#202a38"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Child = table
        });
        Grid.SetColumn(detailPanel, 1);
        body.Children.Add(detailPanel);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Background = PageBackground()
        };
        root.Children.Add(toolbar);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var refundTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        refundTimer.Tick += (_, _) =>
        {
            UpdateRefundControls(rowRefundControls);
            UpdateRefundControls(detailRefundControls);
        };
        refundTimer.Start();
        root.DetachedFromVisualTree += (_, _) => refundTimer.Stop();

        Dispatcher.UIThread.Post(async () => await LoadAsync());
        return root;
    }

    private Control BuildBookingsView()
    {
        var datePicker = new CalendarDatePicker
        {
            SelectedDate = DateTime.Today,
            DisplayDate = DateTime.Today,
            FirstDayOfWeek = DayOfWeek.Monday,
            IsTodayHighlighted = true,
            Width = 170,
            Height = 42,
            Background = Brush("#151d28"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        datePicker.PointerPressed += (_, _) => datePicker.IsDropDownOpen = true;

        var groupBox = new ComboBox
        {
            Width = 220,
            Height = 42,
            Background = Brush("#151d28"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        var status = new TextBlock
        {
            Foreground = Brush("#8f9aad"),
            FontSize = 12,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var timelineHost = new ContentControl
        {
            Content = new TextBlock
            {
                Text = "Загружаю бронирования...",
                Foreground = Brush("#8f9aad"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var overview = new BookingOverview();
        var bookings = new List<BookingInfo>();
        var loading = false;

        void Render()
        {
            var day = datePicker.SelectedDate?.Date ?? DateTime.Today;
            var groupId = (groupBox.SelectedItem as BookingHostGroupInfo)?.Id ?? 0;
            timelineHost.Content = BuildBookingsTimeline(overview, bookings, day, groupId, LoadAsync);
        }

        async Task LoadAsync()
        {
            if (loading) return;
            loading = true;
            status.Text = "Загружаю залы, сеансы и брони...";
            try
            {
                var day = datePicker.SelectedDate?.Date ?? DateTime.Today;
                var selectedGroupId = (groupBox.SelectedItem as BookingHostGroupInfo)?.Id ?? 0;
                var overviewTask = service.LoadBookingOverviewAsync(config, CancellationToken.None);
                var bookingsTask = service.LoadBookingsAsync(config, day, day.AddDays(1).AddHours(12), CancellationToken.None);
                await Task.WhenAll(overviewTask, bookingsTask);
                overview = await overviewTask;
                bookings = await bookingsTask;

                var choices = new List<BookingHostGroupInfo>
                {
                    new() { Id = 0, Title = "Все залы" }
                };
                choices.AddRange(overview.Groups.OrderBy(x => x.Title));
                groupBox.ItemsSource = choices;
                groupBox.SelectedItem = choices.FirstOrDefault(x => x.Id == selectedGroupId) ?? choices[0];
                Render();
                status.Text = $"ПК: {overview.Hosts.Count} · броней: {bookings.Count} · обновлено {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                status.Text = "Не удалось загрузить бронирования: " + ex.Message;
                timelineHost.Content = new TextBlock
                {
                    Text = status.Text,
                    Foreground = Brush("#ff7b7b"),
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            finally
            {
                loading = false;
            }
        }

        var refreshButton = GhostButton("↻  Обновить", () => _ = LoadAsync());
        refreshButton.Width = 132;
        var createButton = AccentButton("＋  Забронировать", async () =>
        {
            var groupId = (groupBox.SelectedItem as BookingHostGroupInfo)?.Id ?? 0;
            var visibleHostIds = overview.Hosts
                .Where(x => groupId == 0 || x.GroupId == groupId)
                .Select(x => x.Id)
                .ToHashSet();
            var record = records.Values.FirstOrDefault(x => x.HostId > 0 && visibleHostIds.Contains(x.HostId))
                ?? records.Values.FirstOrDefault(x => x.HostId > 0);
            if (record == null)
            {
                ShowToast("Бронирование", "Нет доступных компьютеров SmartShell.");
                return;
            }
            await OpenBookingDialogAsync(record);
            await LoadAsync();
        });
        createButton.Width = 178;

        groupBox.SelectionChanged += (_, _) => Render();
        datePicker.SelectedDateChanged += (_, _) => _ = LoadAsync();

        var controls = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"),
            ColumnSpacing = 12
        };
        controls.Children.Add(new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new TextBlock { Text = "Дата брони", Foreground = Brush("#8f9aad"), FontSize = 12 },
                datePicker
            }
        });
        var groupField = new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new TextBlock { Text = "Отображать", Foreground = Brush("#8f9aad"), FontSize = 12 },
                groupBox
            }
        };
        Grid.SetColumn(groupField, 1);
        controls.Children.Add(groupField);
        Grid.SetColumn(refreshButton, 3);
        refreshButton.VerticalAlignment = VerticalAlignment.Bottom;
        controls.Children.Add(refreshButton);
        Grid.SetColumn(createButton, 4);
        createButton.VerticalAlignment = VerticalAlignment.Bottom;
        controls.Children.Add(createButton);

        var header = new Border
        {
            BorderBrush = Brush("#263244"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 16),
            Child = new StackPanel { Children = { controls, status } }
        };
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 16,
            Background = PageBackground()
        };
        root.Children.Add(header);
        Grid.SetRow(timelineHost, 1);
        root.Children.Add(timelineHost);

        Dispatcher.UIThread.Post(async () => await LoadAsync());
        return root;
    }

    private Control BuildBookingsTimeline(BookingOverview overview, List<BookingInfo> bookings, DateTime day, int groupId, Func<Task> refresh)
    {
        const double labelWidth = 190;
        const double halfHourWidth = 48;
        const double headerHeight = 52;
        const double groupHeight = 34;
        const double hostHeight = 56;
        const int halfHours = 72;
        var timelineWidth = halfHours * halfHourWidth;
        var rangeStart = day.Date;
        var rangeEnd = rangeStart.AddHours(36);
        var groupTitles = overview.Groups.ToDictionary(x => x.Id, x => x.Title);
        var hosts = overview.Hosts
            .Where(x => groupId == 0 || x.GroupId == groupId)
            .OrderBy(x => groupTitles.TryGetValue(x.GroupId, out var titleText) ? titleText : "")
            .ThenBy(x => x.Position)
            .ThenBy(x => NaturalKey(NormalizeName(x.Alias)))
            .ToList();

        if (hosts.Count == 0)
        {
            return new TextBlock
            {
                Text = "В выбранном зале нет компьютеров.",
                Foreground = Brush("#8f9aad"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        var rows = new List<(BookingHostInfo? Host, double Top, double Height)>();
        var y = headerHeight;
        foreach (var group in hosts.GroupBy(x => x.GroupId))
        {
            rows.Add((null, y, groupHeight));
            y += groupHeight;
            foreach (var host in group)
            {
                rows.Add((host, y, hostHeight));
                y += hostHeight;
            }
        }
        var totalHeight = y;
        var leftCanvas = new Canvas { Width = labelWidth, Height = totalHeight, Background = PanelBrush("#111820", 188) };
        var rightCanvas = new Canvas { Width = timelineWidth, Height = totalHeight, Background = PanelBrush("#0d131a", 168) };

        var leftHeader = new Border
        {
            Width = labelWidth,
            Height = headerHeight,
            Background = PanelBrush("#151d28", 200),
            BorderBrush = Brush("#2a3544"),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(16, 0),
            Child = new TextBlock
            {
                Text = "Компьютеры",
                Foreground = Brush("#f5f7ff"),
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        leftCanvas.Children.Add(leftHeader);

        for (var slot = 0; slot <= halfHours; slot++)
        {
            var x = slot * halfHourWidth;
            var line = new Border
            {
                Width = 1,
                Height = totalHeight,
                Background = Brush(slot % 2 == 0 ? "#283341" : "#1c2632")
            };
            Canvas.SetLeft(line, x);
            rightCanvas.Children.Add(line);
            if (slot == halfHours) continue;
            var label = new TextBlock
            {
                Text = rangeStart.AddMinutes(slot * 30).ToString("HH:mm"),
                Width = halfHourWidth,
                Foreground = Brush("#8f9aad"),
                FontSize = 11,
                TextAlignment = TextAlignment.Center
            };
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, 17);
            rightCanvas.Children.Add(label);
        }
        var headerLine = new Border { Width = timelineWidth, Height = 1, Background = Brush("#2a3544") };
        Canvas.SetTop(headerLine, headerHeight - 1);
        rightCanvas.Children.Add(headerLine);

        var hostTop = new Dictionary<int, double>();
        var groupIndex = 0;
        foreach (var row in rows)
        {
            if (row.Host == null)
            {
                var group = hosts.GroupBy(x => x.GroupId).ElementAt(groupIndex++);
                var groupLabel = new Border
                {
                    Width = labelWidth,
                    Height = groupHeight,
                    Background = PanelBrush("#0d141c", 204),
                    BorderBrush = Brush("#263244"),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(16, 0),
                    Child = new TextBlock
                    {
                        Text = groupTitles.TryGetValue(group.Key, out var groupTitle) ? groupTitle : "Без зала",
                        Foreground = Brush("#8f9aad"),
                        FontSize = 12,
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                Canvas.SetTop(groupLabel, row.Top);
                leftCanvas.Children.Add(groupLabel);
                var groupBand = new Border { Width = timelineWidth, Height = groupHeight, Background = PanelBrush("#0a1016", 188) };
                Canvas.SetTop(groupBand, row.Top);
                rightCanvas.Children.Add(groupBand);
                continue;
            }

            var host = row.Host;
            hostTop[host.Id] = row.Top;
            var normalizedName = NormalizeName(host.Alias);
            var record = records.Values.FirstOrDefault(x => x.HostId == host.Id);
            var hostLabel = new Border
            {
                Width = labelWidth,
                Height = hostHeight,
                Background = PanelBrush("#151d28", 214),
                BorderBrush = Brush("#263244"),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(16, 0),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 3,
                    Children =
                    {
                        new TextBlock { Text = normalizedName, Foreground = Brush("#f5f7ff"), FontWeight = FontWeight.SemiBold },
                        new TextBlock
                        {
                            Text = record?.HasSession == true ? "Активный сеанс" : record?.Online == true ? "Включён" : "Выключен",
                            Foreground = record?.HasSession == true ? Brush("#c987ff") : Brush("#8f9aad"),
                            FontSize = 11
                        }
                    }
                }
            };
            Canvas.SetTop(hostLabel, row.Top);
            leftCanvas.Children.Add(hostLabel);
            var rowBand = new Border
            {
                Width = timelineWidth,
                Height = hostHeight,
                Background = PanelBrush("#111820", 196),
                BorderBrush = Brush("#263244"),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            Canvas.SetTop(rowBand, row.Top);
            rightCanvas.Children.Add(rowBand);

            hostLabel.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(hostLabel).Properties.IsLeftButtonPressed || record == null) return;
                selected = record;
                selectedHostKeys.Clear();
                selectedHostKeys.Add(record.Name);
                UpdateSelectedPanel();
            };
        }

        void AddTimelineBar(int hostId, DateTime from, DateTime to, string titleText, string subtitleText, bool session, BookingInfo? booking = null)
        {
            if (!hostTop.TryGetValue(hostId, out var top) || from == DateTime.MinValue || to == DateTime.MinValue) return;
            var visibleFrom = from < rangeStart ? rangeStart : from;
            var visibleTo = to > rangeEnd ? rangeEnd : to;
            if (visibleTo <= visibleFrom) return;
            var left = (visibleFrom - rangeStart).TotalMinutes / 30d * halfHourWidth;
            var width = Math.Max(34, (visibleTo - visibleFrom).TotalMinutes / 30d * halfHourWidth);
            var bar = new Border
            {
                Width = width,
                Height = hostHeight - 16,
                Background = Brush(session ? "#30203d" : "#123052"),
                BorderBrush = Brush(session ? "#b14cff" : "#2f8cff"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 4),
                Child = new StackPanel
                {
                    Spacing = 1,
                    Children =
                    {
                        new TextBlock { Text = titleText, Foreground = Brush("#f5f7ff"), FontWeight = FontWeight.SemiBold, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock { Text = subtitleText, Foreground = session ? Brush("#d7a5ff") : Brush("#91c5ff"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis }
                    }
                }
            };
            Canvas.SetLeft(bar, left);
            Canvas.SetTop(bar, top + 8);
            rightCanvas.Children.Add(bar);
            ToolTip.SetTip(bar, titleText + "\n" + from.ToString("dd.MM.yyyy HH:mm") + " - " + to.ToString("dd.MM.yyyy HH:mm") + "\n" + subtitleText);
            if (booking != null)
            {
                var record = records.Values.FirstOrDefault(x => x.HostId == hostId);
                bar.PointerPressed += async (_, e) =>
                {
                    if (e.GetCurrentPoint(bar).Properties.IsLeftButtonPressed && record != null)
                    {
                        e.Handled = true;
                        await OpenBookingDialogAsync(record, booking.HostIds, booking);
                        await refresh();
                    }
                };
            }
        }

        foreach (var host in hosts)
        {
            var client = !string.IsNullOrWhiteSpace(host.ClientName) ? host.ClientName : host.ClientPhone;
            foreach (var session in host.Sessions)
                AddTimelineBar(host.Id, session.StartedLocal, session.FinishedLocal, string.IsNullOrWhiteSpace(client) ? "Активный сеанс" : client, "Сеанс", true);
        }
        foreach (var booking in bookings.Where(x => !IsInactiveBookingStatus(x.Status)))
        {
            foreach (var hostId in booking.HostIds)
            {
                var subtitleText = string.IsNullOrWhiteSpace(booking.Comment) ? "Бронирование" : booking.Comment;
                AddTimelineBar(hostId, booking.FromLocal, booking.ToLocal, booking.ClientDisplay, subtitleText, false, booking);
            }
        }

        if (DateTime.Now >= rangeStart && DateTime.Now <= rangeEnd)
        {
            var currentLeft = (DateTime.Now - rangeStart).TotalMinutes / 30d * halfHourWidth;
            var nowLine = new Border { Width = 2, Height = totalHeight, Background = Brush("#d24cff"), IsHitTestVisible = false };
            Canvas.SetLeft(nowLine, currentLeft);
            rightCanvas.Children.Add(nowLine);
        }

        var timelineScroll = new ScrollViewer
        {
            Content = rightCanvas,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = PanelBrush("#0d131a", 168)
        };
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions(labelWidth + ",*") };
        body.Children.Add(leftCanvas);
        Grid.SetColumn(timelineScroll, 1);
        body.Children.Add(timelineScroll);

        var horizontalBar = new Avalonia.Controls.Primitives.ScrollBar
        {
            Orientation = Orientation.Horizontal,
            Minimum = 0,
            SmallChange = halfHourWidth,
            LargeChange = halfHourWidth * 4,
            Margin = new Thickness(labelWidth, 6, 0, 0)
        };
        var syncingScroll = false;
        void SyncHorizontalBar()
        {
            syncingScroll = true;
            horizontalBar.Maximum = Math.Max(0, timelineScroll.Extent.Width - timelineScroll.Viewport.Width);
            horizontalBar.ViewportSize = timelineScroll.Viewport.Width;
            horizontalBar.Value = Math.Min(horizontalBar.Maximum, timelineScroll.Offset.X);
            syncingScroll = false;
        }
        horizontalBar.ValueChanged += (_, _) =>
        {
            if (!syncingScroll)
                timelineScroll.Offset = new Vector(horizontalBar.Value, 0);
        };
        timelineScroll.ScrollChanged += (_, _) => SyncHorizontalBar();
        timelineScroll.LayoutUpdated += (_, _) => SyncHorizontalBar();

        var verticalScroll = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = PageBackground()
        };
        var result = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Background = PageBackground(),
            Children = { verticalScroll, horizontalBar }
        };
        Grid.SetRow(horizontalBar, 1);

        Dispatcher.UIThread.Post(() =>
        {
            var minutes = day.Date == DateTime.Today ? Math.Max(0, (DateTime.Now - rangeStart).TotalMinutes - 120) : 0;
            var offset = Math.Floor(minutes / 30d) * halfHourWidth;
            timelineScroll.Offset = new Vector(offset, 0);
            SyncHorizontalBar();
        });
        return result;
    }

    private string ShopTabsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmartShellCleaner",
        "open-shop-tabs.json");

    private ShopTabState CreateEmptyShopTab(string? title = null)
    {
        return new ShopTabState
        {
            Title = string.IsNullOrWhiteSpace(title) ? NextShopTabTitle() : title.Trim(),
            AutoTitle = string.IsNullOrWhiteSpace(title)
        };
    }

    private string NextShopTabTitle()
    {
        var number = 1;
        while (shopTabs.Any(x => x.Title.Equals("Счёт " + number, StringComparison.OrdinalIgnoreCase)))
            number++;
        return "Счёт " + number;
    }

    private void LoadShopTabs()
    {
        try
        {
            if (File.Exists(ShopTabsPath))
            {
                var loaded = JsonSerializer.Deserialize<List<ShopTabState>>(File.ReadAllText(ShopTabsPath, Encoding.UTF8));
                if (loaded != null)
                    shopTabs.AddRange(loaded.Where(x => !string.IsNullOrWhiteSpace(x.Id)));
            }
        }
        catch
        {
            shopTabs.Clear();
        }

        if (shopTabs.Count == 0)
            shopTabs.Add(CreateEmptyShopTab());
        activeShopTabId = shopTabs[0].Id;
        ApplyShopTabToWorkingState(shopTabs[0]);
    }

    private void SaveShopTabsFile()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ShopTabsPath)!);
            File.WriteAllText(
                ShopTabsPath,
                JsonSerializer.Serialize(shopTabs, new JsonSerializerOptions { WriteIndented = true }),
                Encoding.UTF8);
        }
        catch
        {
        }
    }

    private void SaveActiveShopTab()
    {
        if (shopTabSyncing) return;
        var tab = shopTabs.FirstOrDefault(x => x.Id == activeShopTabId);
        if (tab == null) return;
        tab.Items = cartItems.Select(x => x.Copy()).ToList();
        tab.PaymentMethod = paymentMethod;
        tab.ClientUuid = selectedClient?.Uuid ?? "";
        tab.ClientDisplay = selectedClient?.Display ?? "";
        SaveShopTabsFile();
        RenderShopTabs();
    }

    private void ApplyShopTabToWorkingState(ShopTabState tab)
    {
        shopTabSyncing = true;
        try
        {
            cartItems.Clear();
            cartItems.AddRange((tab.Items ?? new List<CartItem>()).Select(x => x.Copy()));
            paymentMethod = tab.PaymentMethod is "CASH" or "CARD" or "COMPOSITE" ? tab.PaymentMethod : "";
            selectedClient = string.IsNullOrWhiteSpace(tab.ClientUuid)
                ? null
                : new ClientInfo { Uuid = tab.ClientUuid, Nickname = tab.ClientDisplay };
        }
        finally
        {
            shopTabSyncing = false;
        }
    }

    private void SwitchShopTab(string id)
    {
        if (id == activeShopTabId) return;
        SaveActiveShopTab();
        var tab = shopTabs.FirstOrDefault(x => x.Id == id);
        if (tab == null) return;
        activeShopTabId = tab.Id;
        ApplyShopTabToWorkingState(tab);
        clientResults.Clear();
        if (clientSearchBox != null) clientSearchBox.Text = "";
        RenderClientResults();
        UpdateSelectedClientText();
        RefreshPaymentButtons();
        RenderCart();
        RenderShopTabs();
    }

    private void AddShopTab()
    {
        SaveActiveShopTab();
        var tab = CreateEmptyShopTab();
        shopTabs.Add(tab);
        SwitchShopTab(tab.Id);
    }

    private void PrepareShopEntry()
    {
        SaveActiveShopTab();
        var empty = shopTabs.FirstOrDefault(x =>
            (x.Items?.Count ?? 0) == 0
            && string.IsNullOrWhiteSpace(x.ClientUuid)
            && string.IsNullOrWhiteSpace(x.ClientDisplay));
        if (empty == null)
        {
            empty = CreateEmptyShopTab();
            shopTabs.Add(empty);
        }
        empty.PaymentMethod = "";
        activeShopTabId = empty.Id;
        ApplyShopTabToWorkingState(empty);
        SaveShopTabsFile();
    }

    private async Task RenameShopTabAsync(ShopTabState tab)
    {
        var titleText = await AskTextAsync("Переименовать счёт", "Название счёта", tab.Title);
        if (string.IsNullOrWhiteSpace(titleText)) return;
        tab.Title = titleText.Trim();
        tab.AutoTitle = false;
        SaveShopTabsFile();
        RenderShopTabs();
    }

    private async Task CloseShopTabAsync(ShopTabState tab)
    {
        SaveActiveShopTab();
        if (tab.Items.Count > 0)
        {
            var debt = tab.Items.Where(x => x.Type == "DEPOSIT_DEBT").Sum(x => x.Total);
            var message = debt > 0
                ? "В счёте есть неоплаченный уже зачисленный депозит " + MoneyText(debt) + ". Закрыть счёт без оплаты?"
                : "В счёте остались позиции на " + MoneyText(tab.Items.Sum(x => x.Total)) + ". Закрыть его?";
            if (!await ConfirmAsync("Закрыть счёт", message, true)) return;
        }
        RemoveShopTab(tab.Id);
    }

    private void RemoveShopTab(string id)
    {
        var index = shopTabs.FindIndex(x => x.Id == id);
        if (index < 0) return;
        var wasActive = activeShopTabId == id;
        shopTabs.RemoveAt(index);
        if (shopTabs.Count == 0)
            shopTabs.Add(CreateEmptyShopTab());
        if (wasActive)
        {
            var next = shopTabs[Math.Min(index, shopTabs.Count - 1)];
            activeShopTabId = next.Id;
            ApplyShopTabToWorkingState(next);
            clientResults.Clear();
            RenderClientResults();
            UpdateSelectedClientText();
            RefreshPaymentButtons();
            RenderCart();
        }
        SaveShopTabsFile();
        RenderShopTabs();
    }

    private Border BuildShopTabsBar()
    {
        shopTabsHost = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        RenderShopTabs();
        return new Border
        {
            Background = Brush("#101720"),
            BorderBrush = Brush("#263244"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new ScrollViewer
            {
                Content = shopTabsHost,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            }
        };
    }

    private void RenderShopTabs()
    {
        if (shopTabsHost == null) return;
        shopTabsHost.Children.Clear();
        foreach (var tab in shopTabs)
        {
            var active = tab.Id == activeShopTabId;
            var total = tab.Items.Sum(x => x.Total);
            var debt = tab.Items.Where(x => x.Type == "DEPOSIT_DEBT").Sum(x => x.Total);
            var titleStack = new StackPanel
            {
                Spacing = 1,
                Children =
                {
                    new TextBlock { Text = tab.Title, Foreground = Brush(active ? "#ffffff" : "#d7deea"), FontWeight = FontWeight.SemiBold, MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = MoneyText(total) + (debt > 0 ? "  •  долг " + MoneyText(debt) : ""), Foreground = Brush(debt > 0 ? "#f5b942" : "#8f9aad"), FontSize = 11 }
                }
            };
            var select = new Button
            {
                Height = 48,
                MinWidth = 126,
                Padding = new Thickness(12, 4),
                Background = active ? Brush("#17345a") : Brush("#182231"),
                BorderBrush = active ? Brush("#2f8cff") : Brush("#2b3748"),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = titleStack
            };
            select.Click += (_, _) => SwitchShopTab(tab.Id);
            var close = new Button
            {
                Content = "×",
                Width = 30,
                Height = 48,
                Padding = new Thickness(0),
                Background = active ? Brush("#17345a") : Brush("#182231"),
                BorderBrush = active ? Brush("#2f8cff") : Brush("#2b3748"),
                Foreground = Brush("#9aa5b8")
            };
            close.Click += (_, _) => _ = CloseShopTabAsync(tab);
            var rename = new Button
            {
                Content = "✎",
                Width = 30,
                Height = 48,
                Padding = new Thickness(0),
                Background = active ? Brush("#17345a") : Brush("#182231"),
                BorderBrush = active ? Brush("#2f8cff") : Brush("#2b3748"),
                Foreground = Brush("#8f9aad"),
                FontSize = 16
            };
            ToolTip.SetTip(rename, "Переименовать счёт");
            rename.Click += (_, _) => _ = RenameShopTabAsync(tab);
            shopTabsHost.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { select, rename, close } });
        }
        var add = new Button
        {
            Content = "+  Новый счёт",
            Height = 48,
            Padding = new Thickness(14, 0),
            Background = Brush("#13243b"),
            BorderBrush = Brush("#245487"),
            Foreground = Brush("#4ea1ff")
        };
        add.Click += (_, _) => AddShopTab();
        shopTabsHost.Children.Add(add);
    }

    private Control BuildShopView()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("300,*,430"),
            Background = PageBackground()
        };

        paymentButtons.Clear();
        debtPaymentButtons.Clear();
        clientSearchBox = new TextBox
        {
            Watermark = "Логин, телефон или последние 4 цифры",
            Height = 40,
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550")
        };
        clientSearchBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
                await SearchClientsFromUiAsync();
        };
        clientResultsList = new StackPanel { Spacing = 8 };
        selectedClientText = new TextBlock
        {
            Text = "Клиент не выбран. Для пополнения баланса клиент обязателен.",
            Foreground = Brush("#8f9aad"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        };
        depositAmountBox = new TextBox
        {
            Watermark = "Сумма пополнения",
            Height = 40,
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550")
        };
        bonusAmountBox = new TextBox
        {
            Watermark = "Начислить бонусов (необязательно)",
            Height = 40,
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550")
        };
        bonusCommentBox = new TextBox
        {
            Text = "ЛОТ",
            Watermark = "Комментарий к бонусам",
            MaxLength = 255,
            Height = 40,
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550")
        };
        depositAlreadyCreditedBox = new CheckBox
        {
            Content = new TextBlock
            {
                Text = "Депозит уже зачислен — клиент оплатит позже",
                TextWrapping = TextWrapping.Wrap
            },
            Foreground = Brush("#f5b942"),
            FontSize = 12
        };
        storeStatusText = new TextBlock
        {
            Foreground = Brush("#8f9aad"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        };

        var creditBalanceButton = AccentButton("Положить на баланс и записать в счёт", () => _ = CreditDepositAndAddDebtAsync());
        creditBalanceButton.Height = 58;
        creditBalanceButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        creditBalanceButton.Content = new TextBlock
        {
            Text = "Положить на баланс и записать в счёт",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var debtPaymentGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8
        };
        AddDebtPaymentButton(debtPaymentGrid, 0, "Наличными", "CASH");
        AddDebtPaymentButton(debtPaymentGrid, 1, "Картой", "CARD");

        var clientPanel = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                clientSearchBox,
                AccentButton("Найти клиента", () => _ = SearchClientsFromUiAsync()),
                new ScrollViewer
                {
                    Content = clientResultsList,
                    MaxHeight = 190,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = PageBackground()
                },
                selectedClientText,
                depositAmountBox,
                bonusAmountBox,
                bonusCommentBox,
                depositAlreadyCreditedBox,
                new TextBlock
                {
                    Text = "Как клиент оплатит при выходе",
                    Foreground = Brush("#8f9aad"),
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold
                },
                debtPaymentGrid,
                creditBalanceButton,
                GhostButton("Добавить в корзину", AddDepositToCart),
                storeStatusText
            }
        };
        var client = Panel("Клиент", clientPanel);
        Grid.SetColumn(client, 0);
        grid.Children.Add(client);

        shopModeButtons.Clear();
        shopSearchBox = new TextBox
        {
            Watermark = "Поиск",
            Height = 42,
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550")
        };
        shopSearchBox.TextChanged += (_, _) => RenderShopCatalog();
        shopCatalogList = new StackPanel { Spacing = 8 };

        var catalogStack = new StackPanel { Spacing = 12 };
        catalogStack.Children.Add(shopSearchBox);
        catalogStack.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                ShopModeButton("Товары", "GOOD"),
                ShopModeButton("Услуги", "SERVICE"),
                ShopModeButton("Комбо-наборы", "COMBO")
            }
        });
        catalogStack.Children.Add(CatalogHeader());
        catalogStack.Children.Add(new ScrollViewer
        {
            Content = shopCatalogList,
            MaxHeight = 570,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = PageBackground()
        });
        var catalog = Panel("Ассортимент", catalogStack);
        Grid.SetColumn(catalog, 1);
        grid.Children.Add(catalog);

        cartItemsList = new StackPanel { Spacing = 8 };
        cartTotalText = new TextBlock
        {
            Text = "Итого: 0 ₽",
            Foreground = Brush("#f5f7ff"),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 6, 0, 0)
        };
        cashPartBox = SplitAmountBox("Наличными");
        cardPartBox = SplitAmountBox("Картой");
        cashPartBox.TextChanged += (_, _) => SyncSplitFromCash();
        cardPartBox.TextChanged += (_, _) => SyncSplitFromCard();
        cashReceivedBox = SplitAmountBox("Сколько дал клиент");
        cashReceivedBox.TextChanged += (_, _) => RefreshCashPaymentPanel();
        cashChangeText = new TextBlock
        {
            Text = "Сдача: 0 ₽",
            Foreground = Brush("#22c55e"),
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        cashChangePanel = new StackPanel
        {
            Spacing = 8,
            IsVisible = false,
            Children =
            {
                new TextBlock
                {
                    Text = "Получено наличными",
                    Foreground = Brush("#8f9aad"),
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold
                },
                cashReceivedBox,
                cashChangeText
            }
        };

        var paymentGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 8
        };
        AddPaymentButton(paymentGrid, 0, "Наличные", "CASH");
        AddPaymentButton(paymentGrid, 1, "Карта", "CARD");
        AddPaymentButton(paymentGrid, 2, "Разделить", "COMPOSITE");

        var splitGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8,
            Children = { cashPartBox, cardPartBox }
        };
        Grid.SetColumn(cardPartBox, 1);

        var cartScroll = new ScrollViewer
        {
            Content = cartItemsList,
            MinHeight = 280,
            MaxHeight = 430,
            VerticalAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = PageBackground()
        };
        checkoutButton = AccentButton("Оплатить", () => _ = CreatePaymentFromCartAsync());
        checkoutButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        var clearCartButton = GhostButton("Очистить", ClearCart);
        clearCartButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        var cartFooter = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                cartTotalText,
                paymentGrid,
                splitGrid,
                cashChangePanel,
                checkoutButton,
                clearCartButton
            }
        };
        var cartPanel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 16,
            Children = { cartScroll, cartFooter }
        };
        Grid.SetRow(cartFooter, 1);
        var cart = Panel("Корзина", cartPanel);
        Grid.SetColumn(cart, 2);
        grid.Children.Add(cart);

        RefreshShopModeButtons();
        RefreshPaymentButtons();
        RefreshDebtPaymentButtons();
        RenderClientResults();
        RenderCart();
        RenderShopCatalog();
        _ = EnsureShopItemsLoadedAsync();

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Background = PageBackground()
        };
        root.Children.Add(BuildShopTabsBar());
        Grid.SetRow(grid, 1);
        root.Children.Add(grid);
        return root;
    }

    private TextBox SplitAmountBox(string watermark)
    {
        return new TextBox
        {
            Watermark = watermark,
            Height = 40,
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550")
        };
    }

    private void AddPaymentButton(Grid grid, int column, string text, string method)
    {
        var button = PaymentButton(text, method);
        button.Click += (_, _) => SetPaymentMethod(method);
        paymentButtons[method] = button;
        Grid.SetColumn(button, column);
        grid.Children.Add(button);
    }

    private void AddDebtPaymentButton(Grid grid, int column, string text, string method)
    {
        var button = PaymentButton(text, method);
        button.Click += (_, _) =>
        {
            debtPaymentMethod = method;
            RefreshDebtPaymentButtons();
        };
        debtPaymentButtons[method] = button;
        Grid.SetColumn(button, column);
        grid.Children.Add(button);
    }

    private async Task SearchClientsFromUiAsync()
    {
        var query = (clientSearchBox?.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            SetStoreStatus("Введите логин, телефон или последние 4 цифры номера.");
            return;
        }
        if (!HasSmartShellAccess())
        {
            SetStoreStatus("Введите SmartShell API ключи.");
            return;
        }

        SetStoreStatus("Ищу клиента...");
        try
        {
            var found = await service.SearchClientsAsync(config, query, CancellationToken.None);
            clientResults.Clear();
            clientResults.AddRange(found);
            selectedClient = found.Count == 1 ? found[0] : selectedClient;
            RenderClientResults();
            UpdateSelectedClientText();
            SaveActiveShopTab();
            SetStoreStatus("Найдено клиентов: " + found.Count);
        }
        catch (Exception ex)
        {
            SetStoreStatus("Ошибка поиска клиента: " + ex.Message);
            ShowToast("Клиент", "Ошибка поиска: " + ex.Message);
        }
    }

    private void RenderClientResults()
    {
        if (clientResultsList == null) return;
        clientResultsList.Children.Clear();
        if (clientResults.Count == 0)
        {
            clientResultsList.Children.Add(CatalogStatus("Начните вводить никнейм или телефон авторизованного клиента."));
            return;
        }

        foreach (var client in clientResults)
            clientResultsList.Children.Add(ClientRow(client));
    }

    private Border ClientRow(ClientInfo client)
    {
        var active = selectedClient?.Uuid == client.Uuid;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock
        {
            Text = client.Display,
            Foreground = Brush("#f5f7ff"),
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Баланс: " + client.BalanceText + " | Бонус: " + client.BonusText,
            Foreground = Brush("#8f9aad"),
            FontSize = 12
        });
        row.Children.Add(stack);
        var pick = new TextBlock
        {
            Text = active ? "✓" : "+",
            Foreground = active ? Brush("#22c55e") : Brush("#4ea1ff"),
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(pick, 1);
        row.Children.Add(pick);

        var border = new Border
        {
            Background = active ? Brush("#132944") : Brush("#151d28"),
            BorderBrush = active ? Brush("#3b82f6") : Brush("#263244"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 10),
            Child = row
        };
        border.PointerPressed += (_, _) =>
        {
            selectedClient = client;
            RenderClientResults();
            UpdateSelectedClientText();
            SaveActiveShopTab();
        };
        return border;
    }

    private void UpdateSelectedClientText()
    {
        if (selectedClientText == null) return;
        selectedClientText.Text = selectedClient == null
            ? "Клиент не выбран. Для пополнения баланса клиент обязателен."
            : "Выбран: " + selectedClient.Display + "\nБаланс: " + selectedClient.BalanceText + " | Бонус: " + selectedClient.BonusText;
    }

    private void SetStoreStatus(string text)
    {
        if (storeStatusText != null) storeStatusText.Text = text;
        source.Text = text;
    }

    private void AddShopItemToCart(ShopItem item)
    {
        var existing = cartItems.FirstOrDefault(x => x.Type == item.Type && x.EntityId == item.EntityId);
        if (existing != null)
            existing.Amount += 1;
        else
            cartItems.Add(new CartItem
            {
                Type = item.Type,
                EntityId = item.EntityId,
                Title = item.Title,
                Amount = 1,
                UnitCost = item.Cost
            });
        RenderCart();
        SetStoreStatus("Добавлено в корзину: " + item.Title);
    }

    private void AddDepositToCart()
    {
        if (selectedClient == null || string.IsNullOrWhiteSpace(selectedClient.Uuid))
        {
            SetStoreStatus("Для пополнения выберите клиента.");
            return;
        }
        var sum = 0m;
        if (!string.IsNullOrWhiteSpace(depositAmountBox?.Text)
            && (!TryParseMoney(depositAmountBox.Text, out sum) || sum <= 0))
        {
            SetStoreStatus("Сумма пополнения должна быть больше нуля.");
            return;
        }
        var bonus = 0m;
        if (!string.IsNullOrWhiteSpace(bonusAmountBox?.Text)
            && (!TryParseMoney(bonusAmountBox.Text, out bonus) || bonus < 0 || bonus > 16000000m))
        {
            SetStoreStatus("Бонусы должны быть числом от 0 до 16 000 000.");
            return;
        }
        if (sum <= 0 && bonus <= 0)
        {
            SetStoreStatus("Введите сумму пополнения или количество бонусов.");
            return;
        }
        var comment = string.IsNullOrWhiteSpace(bonusCommentBox?.Text)
            ? "ЛОТ"
            : bonusCommentBox.Text.Trim();

        if (sum > 0)
        {
            var alreadyCredited = depositAlreadyCreditedBox?.IsChecked == true;
            cartItems.Add(new CartItem
            {
                Type = alreadyCredited ? "DEPOSIT_DEBT" : "DEPOSIT",
                Title = alreadyCredited ? "Депозит уже зачислен — ждём оплату" : "Пополнение баланса",
                Amount = 1,
                UnitCost = sum,
                ClientUuid = selectedClient.Uuid,
                ClientDisplay = selectedClient.Display,
                PaymentMethod = alreadyCredited ? debtPaymentMethod : ""
            });
        }
        if (bonus > 0)
        {
            cartItems.Add(new CartItem
            {
                Type = "BONUS",
                Title = "Начисление бонусов",
                Amount = 1,
                BonusAmount = bonus,
                ClientUuid = selectedClient.Uuid,
                ClientDisplay = selectedClient.Display,
                Comment = comment
            });
        }
        if (depositAmountBox != null) depositAmountBox.Text = "";
        if (bonusAmountBox != null) bonusAmountBox.Text = "";
        if (bonusCommentBox != null) bonusCommentBox.Text = "ЛОТ";
        if (depositAlreadyCreditedBox != null) depositAlreadyCreditedBox.IsChecked = false;
        RenderCart();
        var parts = new List<string>();
        if (sum > 0) parts.Add(MoneyText(sum));
        if (bonus > 0) parts.Add("★ +" + BonusText(bonus) + " (" + comment + ")");
        SetStoreStatus("Добавлено для " + selectedClient.Display + ": " + string.Join(" и ", parts));
    }

    private async Task CreditDepositAndAddDebtAsync()
    {
        if (creditDepositInProgress) return;
        if (selectedClient == null || string.IsNullOrWhiteSpace(selectedClient.Uuid))
        {
            SetStoreStatus("Для пополнения выберите клиента.");
            return;
        }
        if (!TryParseMoney(depositAmountBox?.Text, out var sum) || sum <= 0)
        {
            SetStoreStatus("Сумма пополнения должна быть больше нуля.");
            return;
        }
        var client = selectedClient;
        var method = debtPaymentMethod == "CARD" ? "CARD" : "CASH";
        try
        {
            creditDepositInProgress = true;
            SetStoreStatus("Зачисляю " + MoneyText(sum) + " клиенту " + client.Display + "...");
            var deposit = new CartItem
            {
                Type = "DEPOSIT",
                Title = "Пополнение баланса",
                Amount = 1,
                UnitCost = sum,
                ClientUuid = client.Uuid,
                ClientDisplay = client.Display
            };
            var input = BuildPaymentInput(new List<CartItem> { deposit }, client.Uuid, method, 0m);
            await service.ValidateAndCreatePaymentAsync(config, input, CancellationToken.None);

            cartItems.Add(new CartItem
            {
                Type = "DEPOSIT_DEBT",
                Title = "Депозит уже зачислен — ждём оплату",
                Amount = 1,
                UnitCost = sum,
                ClientUuid = client.Uuid,
                ClientDisplay = client.Display,
                PaymentMethod = method
            });
            if (depositAmountBox != null) depositAmountBox.Text = "";
            if (depositAlreadyCreditedBox != null) depositAlreadyCreditedBox.IsChecked = false;
            RenderCart();
            SetStoreStatus("Зачислено " + MoneyText(sum) + " клиенту " + client.Display
                + ". Сумма записана в счёт; способ в SmartShell: " + PaymentMethodTitle(method) + ".");
            ShowToast("Пополнение записано в счёт", client.Display + " • " + MoneyText(sum));
        }
        catch (Exception ex)
        {
            SetStoreStatus("Не удалось зачислить баланс: " + ex.Message);
        }
        finally
        {
            creditDepositInProgress = false;
        }
    }

    private void RenderCart()
    {
        if (cartItemsList == null) return;
        cartItemsList.Children.Clear();

        if (cartItems.Count == 0)
        {
            cartItemsList.Children.Add(CatalogStatus("Выберите товары, услуги или пополнение, чтобы добавить их в корзину."));
        }
        else
        {
            foreach (var item in cartItems.ToList())
                cartItemsList.Children.Add(CartRow(item));
        }

        UpdateCartTotal();
        SaveActiveShopTab();
    }

    private Border CartRow(CartItem item)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock
        {
            Text = item.Title,
            Foreground = Brush("#f5f7ff"),
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        var sub = item.Type switch
        {
            "DEPOSIT" => item.ClientDisplay,
            "DEPOSIT_DEBT" => item.ClientDisplay + "  •  уже зачислен, оплатит позже: "
                + PaymentMethodTitle(string.IsNullOrWhiteSpace(item.PaymentMethod) ? "CASH" : item.PaymentMethod),
            "BONUS" => item.ClientDisplay + "  •  " + (string.IsNullOrWhiteSpace(item.Comment) ? "ЛОТ" : item.Comment),
            _ => item.Amount + " × " + MoneyText(item.UnitCost)
        };
        stack.Children.Add(new TextBlock { Text = sub, Foreground = Brush("#8f9aad"), FontSize = 12 });
        rowAddTotal(stack, item.Type == "BONUS" ? "★ +" + BonusText(item.BonusTotal) : item.TotalText);
        grid.Children.Add(stack);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        if (item.Type != "DEPOSIT_DEBT")
        {
            controls.Children.Add(SmallCartButton("-", () =>
            {
                if (item.Amount > 1) item.Amount -= 1;
                else cartItems.Remove(item);
                RenderCart();
            }));
            controls.Children.Add(SmallCartButton("+", () =>
            {
                item.Amount += 1;
                RenderCart();
            }));
        }
        controls.Children.Add(SmallCartButton("×", () =>
        {
            cartItems.Remove(item);
            RenderCart();
        }));
        Grid.SetColumn(controls, 1);
        grid.Children.Add(controls);

        return new Border
        {
            Background = Brush("#151d28"),
            BorderBrush = Brush("#263244"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 10),
            Child = grid
        };

        static void rowAddTotal(StackPanel stack, string total)
        {
            stack.Children.Add(new TextBlock
            {
                Text = total,
                Foreground = Brush("#f5f7ff"),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold
            });
        }
    }

    private Button SmallCartButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Background = Brush("#202938"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#334155")
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void ClearCart()
    {
        cartItems.Clear();
        paymentMethod = "";
        if (cashReceivedBox != null) cashReceivedBox.Text = "";
        RefreshPaymentButtons();
        RenderCart();
        SetStoreStatus("Корзина очищена.");
    }

    private void SetPaymentMethod(string method)
    {
        paymentMethod = method;
        if (paymentMethod == "COMPOSITE")
            ResetSplitAmounts();
        RefreshPaymentButtons();
        RefreshCashPaymentPanel(true);
        SaveActiveShopTab();
    }

    private void RefreshPaymentButtons()
    {
        foreach (var pair in paymentButtons)
        {
            var active = string.Equals(pair.Key, paymentMethod, StringComparison.OrdinalIgnoreCase);
            pair.Value.Background = active ? Brush("#26384f") : Brush("#202938");
            pair.Value.BorderBrush = active ? Brush("#3b82f6") : Brush("#334155");
        }

        var splitVisible = paymentMethod == "COMPOSITE";
        if (cashPartBox != null) cashPartBox.IsVisible = splitVisible;
        if (cardPartBox != null) cardPartBox.IsVisible = splitVisible;
        RefreshCashPaymentPanel();
    }

    private void RefreshDebtPaymentButtons()
    {
        foreach (var pair in debtPaymentButtons)
        {
            var active = string.Equals(pair.Key, debtPaymentMethod, StringComparison.OrdinalIgnoreCase);
            pair.Value.Background = active ? Brush("#26384f") : Brush("#202938");
            pair.Value.BorderBrush = active ? Brush("#3b82f6") : Brush("#334155");
        }
    }

    private void ResetSplitAmounts()
    {
        splitPaymentSyncing = true;
        try
        {
            if (cashPartBox == null || cardPartBox == null) return;
            cashPartBox.Text = "";
            cardPartBox.Text = "";
        }
        finally
        {
            splitPaymentSyncing = false;
        }
    }

    private void SyncSplitFromCash()
    {
        if (splitPaymentSyncing || paymentMethod != "COMPOSITE") return;
        if (!TryParseMoney(cashPartBox?.Text, out var cash)) return;
        var total = cartItems.Sum(x => x.Total);
        var card = total - cash;
        if (card < 0 || cardPartBox == null) return;
        splitPaymentSyncing = true;
        try { cardPartBox.Text = card.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); }
        finally { splitPaymentSyncing = false; }
        RefreshCashPaymentPanel();
    }

    private void SyncSplitFromCard()
    {
        if (splitPaymentSyncing || paymentMethod != "COMPOSITE") return;
        if (!TryParseMoney(cardPartBox?.Text, out var card)) return;
        var total = cartItems.Sum(x => x.Total);
        var cash = total - card;
        if (cash < 0 || cashPartBox == null) return;
        splitPaymentSyncing = true;
        try { cashPartBox.Text = cash.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); }
        finally { splitPaymentSyncing = false; }
        RefreshCashPaymentPanel();
    }

    private void UpdateCartTotal()
    {
        if (cartTotalText != null)
        {
            var money = cartItems.Sum(x => x.Total);
            var bonuses = cartItems.Sum(x => x.BonusTotal);
            var creditedDebt = cartItems.Where(x => x.Type == "DEPOSIT_DEBT").Sum(x => x.Total);
            cartTotalText.Text = "Итого: " + MoneyText(money)
                + (bonuses > 0 ? "  |  ★ " + BonusText(bonuses) : "")
                + (creditedDebt > 0 ? "\nУже зачислено, ждём оплату: " + MoneyText(creditedDebt) : "");
        }
        if (paymentMethod == "COMPOSITE")
            ResetSplitAmounts();
        RefreshCashPaymentPanel(paymentMethod == "CASH");
    }

    private decimal CurrentCashDue()
    {
        var debtCash = cartItems
            .Where(x => x.Type == "DEPOSIT_DEBT"
                && !string.Equals(x.PaymentMethod, "CARD", StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.Total);
        var apiTotal = cartItems.Where(x => x.Type != "DEPOSIT_DEBT").Sum(x => x.Total);
        if (paymentMethod == "CASH") return apiTotal + debtCash;
        if (paymentMethod != "COMPOSITE") return debtCash;
        return TryParseMoney(cashPartBox?.Text, out var cash) ? cash + debtCash : debtCash;
    }

    private void RefreshCashPaymentPanel(bool resetReceived = false)
    {
        if (cashChangePanel == null || cashReceivedBox == null || cashChangeText == null) return;

        var methodSelected = paymentMethod is "CASH" or "CARD" or "COMPOSITE";
        var cashDue = paymentMethod == "CASH" ? CurrentCashDue() : 0m;
        cashChangePanel.IsVisible = cashDue > 0;
        if (resetReceived && cashDue > 0)
            cashReceivedBox.Text = cashDue.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        var cashValid = cashDue <= 0
            || TryParseMoney(cashReceivedBox.Text, out var received) && received >= cashDue;
        var change = cashValid && cashDue > 0 && TryParseMoney(cashReceivedBox.Text, out var paid)
            ? paid - cashDue
            : 0m;
        cashChangeText.Text = cashValid
            ? "Сдача: " + MoneyText(change)
            : "Недостаточно наличных";
        cashChangeText.Foreground = cashValid ? Brush("#22c55e") : Brush("#ff5c66");

        var splitValid = paymentMethod != "COMPOSITE"
            || TryParseMoney(cashPartBox?.Text, out var cash)
            && TryParseMoney(cardPartBox?.Text, out var card)
            && cash > 0 && card > 0
            && Math.Abs(cash + card - cartItems.Where(x => x.Type != "DEPOSIT_DEBT").Sum(x => x.Total)) <= 0.01m;
        if (checkoutButton != null)
        {
            checkoutButton.IsEnabled = cartItems.Count > 0 && methodSelected && cashValid && splitValid;
            checkoutButton.Opacity = checkoutButton.IsEnabled ? 1 : 0.45;
        }
    }

    private async Task CreatePaymentFromCartAsync()
    {
        if (cartItems.Count == 0)
        {
            SetStoreStatus("Корзина пустая.");
            return;
        }
        if (paymentMethod is not ("CASH" or "CARD" or "COMPOSITE"))
        {
            SetStoreStatus("Выберите способ оплаты.");
            RefreshCashPaymentPanel();
            return;
        }
        if (cartItems.Any(x => (x.Type == "DEPOSIT" || x.Type == "DEPOSIT_DEBT" || x.Type == "BONUS") && string.IsNullOrWhiteSpace(x.ClientUuid)))
        {
            SetStoreStatus("В корзине есть пополнение или бонусы без клиента.");
            return;
        }

        var total = cartItems.Sum(x => x.Total);
        var debtItems = cartItems.Where(x => x.Type == "DEPOSIT_DEBT").ToList();
        var creditedDebt = debtItems.Sum(x => x.Total);
        var apiItems = cartItems.Where(x => x.Type != "DEPOSIT_DEBT").ToList();
        var apiTotal = apiItems.Sum(x => x.Total);
        var bonusTotal = cartItems.Sum(x => x.BonusTotal);
        if (apiItems.Count > 0 && !HasSmartShellAccess())
        {
            SetStoreStatus("Введите SmartShell API ключи.");
            return;
        }
        if (creditedDebt > 0 && paymentMethod == "COMPOSITE")
        {
            SetStoreStatus("Счёт с уже зачисленным депозитом закройте наличными или картой. Для раздельной оплаты используйте отдельные счета.");
            return;
        }
        var cardSum = 0m;
        if (apiTotal > 0 && !TryGetCardSum(apiTotal, out cardSum)) return;
        var cashDue = CurrentCashDue();
        if (paymentMethod == "CASH" && cashDue > 0
            && (!TryParseMoney(cashReceivedBox?.Text, out var received) || received < cashDue))
        {
            SetStoreStatus("Укажите полученную от клиента сумму наличными.");
            RefreshCashPaymentPanel();
            return;
        }

        var snapshot = apiItems.Select(x => x.Copy()).ToList();
        try
        {
            SetStoreStatus(apiItems.Count > 0 ? "Создаю платёж..." : "Закрываю отложенный счёт...");
            if (snapshot.Count > 0)
            {
                var drafts = BuildPaymentDrafts(snapshot, selectedClient, paymentMethod, cardSum);
                foreach (var draft in drafts)
                {
                    var input = BuildPaymentInput(draft.Items, draft.ClientUuid, draft.Method, draft.CardSum, draft.Comment);
                    await service.ValidateAndCreatePaymentAsync(config, input, CancellationToken.None);
                }
            }

            StartPurchasedPsTimers(snapshot, selectedClient?.Display ?? "");

            var successText = total > 0 ? "Счёт закрыт: " + MoneyText(total) : "Бонусы начислены";
            if (bonusTotal > 0)
                successText += " | ★ +" + BonusText(bonusTotal);
            if (creditedDebt > 0)
                successText += " | долг по депозиту погашен";
            RemoveShopTab(activeShopTabId);
            SetStoreStatus(successText);
            ShowToast("Магазин", successText);
        }
        catch (Exception ex)
        {
            var prefix = total > 0 ? "Ошибка оплаты: " : "Ошибка начисления бонусов: ";
            SetStoreStatus(prefix + ex.Message);
            ShowToast("Оплата", prefix + ex.Message);
        }
    }

    private bool TryGetCardSum(decimal total, out decimal cardSum)
    {
        cardSum = 0m;
        if (paymentMethod == "CARD")
        {
            cardSum = total;
            return true;
        }
        if (paymentMethod == "CASH")
            return true;

        if (!TryParseMoney(cashPartBox?.Text, out var cash) || !TryParseMoney(cardPartBox?.Text, out cardSum))
        {
            SetStoreStatus("Введите суммы для раздельной оплаты.");
            return false;
        }
        if (cash <= 0 || cardSum <= 0 || Math.Abs((cash + cardSum) - total) > 0.01m)
        {
            SetStoreStatus("Наличка и карта должны вместе давать итог корзины.");
            return false;
        }
        return true;
    }

    private async Task<bool> ShowCashPaymentDialogAsync(decimal total, decimal cashDue, decimal cardSum)
    {
        var dialog = new ModalWindow
        {
            Title = "Оплата наличными",
            Width = 460,
            Height = cardSum > 0 ? 430 : 390,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = Brush("#0b1118")
        };

        var receivedBox = new TextBox
        {
            Height = 48,
            Watermark = "Сколько дал клиент",
            FontSize = 18,
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#3b82f6")
        };
        var changeText = new TextBlock
        {
            Text = "Сдача: —",
            Foreground = Brush("#f5f7ff"),
            FontSize = 22,
            FontWeight = FontWeight.SemiBold
        };
        var hintText = new TextBlock
        {
            Text = "Введите полученную сумму.",
            Foreground = Brush("#8f9aad"),
            FontSize = 13
        };
        var pay = new Button
        {
            Content = "Оплатить",
            Width = 130,
            Height = 42,
            IsEnabled = false,
            Background = Brush("#2563eb"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3b82f6")
        };
        pay.Click += (_, _) => dialog.Close(true);
        receivedBox.TextChanged += (_, _) =>
        {
            var valid = TryParseMoney(receivedBox.Text, out var received) && received >= cashDue;
            pay.IsEnabled = valid;
            if (valid)
            {
                changeText.Text = "Сдача: " + MoneyText(received - cashDue);
                changeText.Foreground = Brush("#22c55e");
                hintText.Text = "Суммы достаточно для оплаты.";
                hintText.Foreground = Brush("#7dd3a7");
            }
            else
            {
                changeText.Text = "Сдача: —";
                changeText.Foreground = Brush("#f5f7ff");
                hintText.Text = received > 0
                    ? "Не хватает " + MoneyText(cashDue - received) + "."
                    : "Введите полученную сумму.";
                hintText.Foreground = received > 0 ? Brush("#f5a524") : Brush("#8f9aad");
            }
        };

        var cancel = GhostButton("Отмена", () => dialog.Close(false));
        cancel.Width = 110;
        cancel.Margin = new Thickness(0, 0, 10, 0);
        var summary = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                CashSummaryRow("Сумма покупки", MoneyText(total)),
                CashSummaryRow("Наличными", MoneyText(cashDue))
            }
        };
        if (cardSum > 0)
            summary.Children.Insert(1, CashSummaryRow("Картой", MoneyText(cardSum)));

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = "Расчёт сдачи",
                    Foreground = Brush("#f5f7ff"),
                    FontSize = 22,
                    FontWeight = FontWeight.SemiBold
                },
                summary,
                new TextBlock { Text = "Получено от клиента", Foreground = Brush("#8f9aad") },
                receivedBox,
                changeText,
                hintText,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, pay }
                }
            }
        };
        dialog.Opened += (_, _) => receivedBox.Focus();
        return await dialog.ShowDialog<bool>(this);
    }

    private static Grid CashSummaryRow(string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new TextBlock { Text = label, Foreground = Brush("#8f9aad") });
        var amount = new TextBlock
        {
            Text = value,
            Foreground = Brush("#f5f7ff"),
            FontWeight = FontWeight.SemiBold
        };
        Grid.SetColumn(amount, 1);
        grid.Children.Add(amount);
        return grid;
    }

    private List<PaymentDraft> BuildPaymentDrafts(List<CartItem> items, ClientInfo? client, string method, decimal cardSum)
    {
        var drafts = new List<PaymentDraft>();
        var shopItems = items.Where(x => x.Type != "DEPOSIT" && x.Type != "BONUS").ToList();
        if (shopItems.Count > 0)
            drafts.Add(new PaymentDraft { Items = shopItems, ClientUuid = client?.Uuid ?? "" });

        foreach (var group in items.Where(x => x.Type == "DEPOSIT").GroupBy(x => x.ClientUuid ?? ""))
            drafts.Add(new PaymentDraft { Items = group.ToList(), ClientUuid = group.Key });

        foreach (var bonusItem in items.Where(x => x.Type == "BONUS"))
        {
            drafts.Add(new PaymentDraft
            {
                Items = new List<CartItem> { bonusItem },
                ClientUuid = bonusItem.ClientUuid,
                Method = "BONUS",
                Comment = string.IsNullOrWhiteSpace(bonusItem.Comment) ? "ЛОТ" : bonusItem.Comment.Trim()
            });
        }

        var remainingCard = cardSum;
        foreach (var draft in drafts.Where(x => x.Method != "BONUS"))
        {
            var sum = draft.Items.Sum(x => x.Total);
            if (method == "CARD")
            {
                draft.Method = "CARD";
            }
            else if (method == "CASH")
            {
                draft.Method = "CASH";
            }
            else
            {
                var draftCard = Math.Min(sum, remainingCard);
                remainingCard -= draftCard;
                if (draftCard <= 0)
                    draft.Method = "CASH";
                else if (draftCard >= sum)
                    draft.Method = "CARD";
                else
                {
                    draft.Method = "COMPOSITE";
                    draft.CardSum = draftCard;
                }
            }
        }
        return drafts;
    }

    private Dictionary<string, object?> BuildPaymentInput(
        List<CartItem> items,
        string clientUuid,
        string method,
        decimal cardSum,
        string? comment = null)
    {
        var paymentItems = new List<object?>();
        foreach (var item in items)
        {
            var dict = new Dictionary<string, object?>
            {
                ["type"] = item.Type,
                ["amount"] = item.Amount,
                ["sum"] = DecimalToDouble(item.Type == "BONUS" ? item.BonusTotal : item.Total)
            };
            if (item.EntityId > 0) dict["entity_id"] = item.EntityId;
            if (item.HostId > 0) dict["host_id"] = item.HostId;
            paymentItems.Add(dict);
        }

        var input = new Dictionary<string, object?>
        {
            ["method"] = method,
            ["items"] = paymentItems,
            ["comment"] = string.IsNullOrWhiteSpace(comment) ? "SmartShellCleaner" : comment.Trim()
        };
        if (method == "COMPOSITE")
            input["card_sum"] = DecimalToDouble(cardSum);
        if (!string.IsNullOrWhiteSpace(clientUuid))
            input["client_uuid"] = clientUuid;
        return input;
    }

    private bool TryParseMoney(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        return decimal.TryParse(raw.Trim().Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private double DecimalToDouble(decimal value)
    {
        return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private string MoneyText(decimal value)
    {
        return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " ₽";
    }

    private static string BonusText(decimal value)
    {
        return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    private string PaymentMethodTitle(string method)
    {
        if (method == "CARD") return "Карта";
        if (method == "COMPOSITE") return "Раздельно";
        if (method == "DEPOSIT") return "Депозит";
        return "Наличные";
    }

    private bool HasSmartShellAccess()
    {
        return config.SmartShellEnabled && !string.IsNullOrWhiteSpace(config.AccessToken);
    }

    private Button ShopModeButton(string text, string mode)
    {
        var button = FilterButton(text, string.Equals(shopMode, mode, StringComparison.OrdinalIgnoreCase));
        button.Click += (_, _) =>
        {
            shopMode = mode;
            RefreshShopModeButtons();
            RenderShopCatalog();
        };
        shopModeButtons[mode] = button;
        return button;
    }

    private void RefreshShopModeButtons()
    {
        foreach (var pair in shopModeButtons)
        {
            var active = string.Equals(shopMode, pair.Key, StringComparison.OrdinalIgnoreCase);
            pair.Value.Background = active ? Brush("#3b4251") : Brush("#182231");
            pair.Value.BorderBrush = active ? Brush("#3b82f6") : Brush("#2b3550");
        }
    }

    private async Task EnsureShopItemsLoadedAsync()
    {
        if (shopLoading)
            return;
        if (allShopItems.Count > 0)
        {
            RenderShopCatalog();
            return;
        }
        if (!config.SmartShellEnabled || string.IsNullOrWhiteSpace(config.AccessToken))
        {
            RenderShopCatalog("Введите SmartShell API ключи, чтобы загрузить магазин.");
            return;
        }

        shopLoading = true;
        RenderShopCatalog("Загружаю каталог из SmartShell...");
        try
        {
            var items = await service.LoadShopItemsAsync(config, CancellationToken.None);
            allShopItems.Clear();
            allShopItems.AddRange(items);
            source.Text = "магазин: загружено позиций " + items.Count;
            ShowToast("Магазин", "Загружено позиций: " + items.Count);
        }
        catch (Exception ex)
        {
            RenderShopCatalog("Не удалось загрузить каталог: " + ex.Message);
            ShowToast("Магазин", "Ошибка загрузки каталога: " + ex.Message);
            return;
        }
        finally
        {
            shopLoading = false;
        }

        RenderShopCatalog();
    }

    private void RenderShopCatalog(string? statusText = null)
    {
        if (shopCatalogList == null)
            return;

        shopCatalogList.Children.Clear();

        if (!string.IsNullOrWhiteSpace(statusText))
        {
            shopCatalogList.Children.Add(CatalogStatus(statusText));
            return;
        }

        var query = (shopSearchBox?.Text ?? "").Trim();
        var filteredItems = allShopItems
            .Where(x => string.Equals(x.Type, shopMode, StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Type.Equals("GOOD", StringComparison.OrdinalIgnoreCase)
                || x.AvailableToBuy && x.Stock.GetValueOrDefault() > 0)
            .Where(x => string.IsNullOrWhiteSpace(query)
                || x.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || x.GroupTitle.Contains(query, StringComparison.OrdinalIgnoreCase));
        var items = shopSortColumn switch
        {
            "stock" => (shopSortDescending
                ? filteredItems.OrderBy(x => x.Stock.HasValue ? 0 : 1).ThenByDescending(x => x.Stock)
                : filteredItems.OrderBy(x => x.Stock.HasValue ? 0 : 1).ThenBy(x => x.Stock)).ToList(),
            "cost" => (shopSortDescending
                ? filteredItems.OrderByDescending(x => x.Cost)
                : filteredItems.OrderBy(x => x.Cost)).ToList(),
            _ => (shopSortDescending
                ? filteredItems.OrderByDescending(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
                : filteredItems.OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)).ToList()
        };

        if (items.Count == 0)
        {
            var text = shopLoading
                ? "Загружаю каталог из SmartShell..."
                : allShopItems.Count == 0
                    ? "Каталог пока пуст. Если ключи уже введены, нажмите вкладку магазина еще раз."
                    : "По этому фильтру ничего не найдено.";
            shopCatalogList.Children.Add(CatalogStatus(text));
            return;
        }

        foreach (var item in items)
            shopCatalogList.Children.Add(CatalogRow(item));
    }

    private Border CatalogHeader()
    {
        shopSortButtons.Clear();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,90,100") };
        grid.Children.Add(CatalogSortButton("Название", "title"));
        var stock = CatalogSortButton("Кол-во", "stock");
        Grid.SetColumn(stock, 1);
        grid.Children.Add(stock);
        var cost = CatalogSortButton("Стоимость", "cost");
        Grid.SetColumn(cost, 2);
        grid.Children.Add(cost);

        return new Border
        {
            Background = Brush("#101722"),
            BorderBrush = Brush("#263244"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 10),
            Child = grid
        };
    }

    private Button CatalogSortButton(string title, string column)
    {
        var button = new Button
        {
            Content = title + (shopSortColumn == column ? (shopSortDescending ? "  ↓" : "  ↑") : ""),
            Height = 28,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            Foreground = shopSortColumn == column ? Brush("#60a5fa") : Brush("#8f9aad"),
            BorderThickness = new Thickness(0),
            FontWeight = FontWeight.SemiBold
        };
        shopSortButtons[column] = button;
        button.Click += (_, _) =>
        {
            if (shopSortColumn == column)
                shopSortDescending = !shopSortDescending;
            else
            {
                shopSortColumn = column;
                shopSortDescending = false;
            }
            foreach (var pair in shopSortButtons)
            {
                var label = pair.Key switch
                {
                    "stock" => "Кол-во",
                    "cost" => "Стоимость",
                    _ => "Название"
                };
                pair.Value.Content = label + (shopSortColumn == pair.Key ? (shopSortDescending ? "  ↓" : "  ↑") : "");
                pair.Value.Foreground = shopSortColumn == pair.Key ? Brush("#60a5fa") : Brush("#8f9aad");
            }
            RenderShopCatalog();
        };
        return button;
    }

    private Border CatalogStatus(string text)
    {
        return new Border
        {
            Background = Brush("#101722"),
            BorderBrush = Brush("#263244"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(14),
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brush("#8f9aad"),
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    private Control BuildPsTimersView()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("340,*"),
            Background = PageBackground()
        };

        psDeviceBox = new ComboBox
        {
            Height = 40,
            ItemsSource = new[] { "PS1", "PS2" },
            SelectedIndex = 0,
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550")
        };
        psMinutesBox = new TextBox
        {
            Height = 40,
            Text = "60",
            Watermark = "Минуты",
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550")
        };
        psClientBox = new TextBox
        {
            Height = 40,
            Watermark = "Клиент или комментарий",
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550")
        };

        var quick = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
        foreach (var minutes in new[] { 30, 60, 90, 120, 180, 240 })
        {
            var value = minutes;
            var button = FilterButton(minutes >= 60 ? minutes / 60 + " ч" : minutes + " мин", false);
            button.Margin = new Thickness(0, 0, 8, 8);
            button.Click += (_, _) => psMinutesBox.Text = value.ToString();
            quick.Children.Add(button);
        }

        var startPanel = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Запуск таймера", Foreground = Brush("#f5f7ff"), FontSize = 18, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "Когда в магазине проходит оплата услуги PlayStation, запускай таймер здесь или из PS-блока в магазине.", Foreground = Brush("#8f9aad"), TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "Приставка", Foreground = Brush("#8f9aad"), FontSize = 12 },
                psDeviceBox,
                new TextBlock { Text = "Длительность", Foreground = Brush("#8f9aad"), FontSize = 12 },
                psMinutesBox,
                quick,
                new TextBlock { Text = "Клиент", Foreground = Brush("#8f9aad"), FontSize = 12 },
                psClientBox,
                AccentButton("Запустить таймер", () => StartPsTimerFromInputs("Ручной запуск")),
                GhostButton("Открыть магазин", () => SwitchScreen("shop"))
            }
        };

        var startCard = Panel("PlayStation", startPanel);
        Grid.SetColumn(startCard, 0);
        grid.Children.Add(startCard);

        psTimersList = new StackPanel { Spacing = 10 };
        var activeCard = Panel("Активные таймеры", new ScrollViewer
        {
            Content = psTimersList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        Grid.SetColumn(activeCard, 1);
        grid.Children.Add(activeCard);

        RebuildPsTimersList();
        return grid;
    }

    private void StartPurchasedPsTimers(IEnumerable<CartItem> purchasedItems, string client)
    {
        var missed = 0;
        foreach (var item in purchasedItems.Where(x => x.Type.Equals("SERVICE", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TryGetPsServiceMinutes(item.Title, out var minutes)) continue;
            for (var unit = 0; unit < item.Amount; unit++)
            {
                var device = new[] { "PS1", "PS2" }
                    .FirstOrDefault(name => !psTimers.Any(x => x.Device == name && !x.Completed && x.Remaining > TimeSpan.Zero));
                if (device == null)
                {
                    missed++;
                    continue;
                }
                StartPsTimer(device, minutes, client, "Продажа услуги: " + item.Title);
            }
        }

        if (missed == 0) return;
        var message = "Не запущено таймеров: " + missed + ". Обе приставки уже заняты.";
        ShowToast("Таймер PS требует внимания", message);
        ShowWindowsNotification("Таймер PS требует внимания", message, true);
    }

    private static bool TryGetPsServiceMinutes(string title, out int minutes)
    {
        minutes = 0;
        var normalized = Regex.Replace(title.Trim().ToLowerInvariant(), @"\s+", " ");
        if (!normalized.StartsWith("пс ", StringComparison.Ordinal)) return false;
        if (normalized.Contains("пол часа", StringComparison.Ordinal)) minutes = 30;
        else if (normalized.Contains("ночн", StringComparison.Ordinal)) minutes = 480;
        else
        {
            var match = Regex.Match(normalized, @"\b(1|3|5)\s*ч\b");
            if (match.Success) minutes = int.Parse(match.Groups[1].Value) * 60;
        }
        return minutes > 0;
    }

    private void StartPsTimerFromInputs(string sourceText)
    {
        var device = Convert.ToString(psDeviceBox?.SelectedItem) ?? "PS1";
        if (!int.TryParse(psMinutesBox?.Text, out var minutes) || minutes <= 0)
        {
            ShowToast("Таймер PS", "Введите длительность в минутах.");
            return;
        }

        StartPsTimer(device, minutes, psClientBox?.Text ?? "", sourceText);
    }

    private void StartPsTimer(string device, int minutes, string client, string sourceText)
    {
        var activeOnDevice = psTimers.Any(x => x.Device == device && !x.Completed && x.Remaining > TimeSpan.Zero);
        if (activeOnDevice)
        {
            ShowToast("Таймер уже запущен", device + " уже имеет активный таймер. Остановите его или дождитесь завершения.");
            return;
        }

        var timerSession = new PsTimerSession
        {
            Device = device,
            Client = string.IsNullOrWhiteSpace(client) ? "без клиента" : client.Trim(),
            Source = sourceText,
            StartedAt = DateTime.Now,
            EndsAt = DateTime.Now.AddMinutes(minutes)
        };
        psTimers.Add(timerSession);
        RebuildPsTimersList();
        ShowToast("Таймер запущен", device + " на " + minutes + " мин.");
        if (screen != "ps")
            source.Text = "Запущен таймер " + device + " на " + minutes + " мин. Все таймеры во вкладке «Таймеры PS».";
    }

    private void UpdatePsTimers()
    {
        var completedNow = new List<PsTimerSession>();
        foreach (var timerSession in psTimers)
        {
            var wasCompleted = timerSession.Completed;
            timerSession.Tick();
            if (!wasCompleted && timerSession.Completed)
                completedNow.Add(timerSession);
        }

        if (psTimersList != null)
            RebuildPsTimersList();

        foreach (var completed in completedNow)
        {
            var message = completed.Device + " можно освобождать.";
            ShowToast("Таймер завершён", message);
            ShowWindowsNotification("Таймер PS завершён", message, true);
        }

        if (completedNow.Count > 0 && screen != "ps")
        {
            psTimerAttentionPending = true;
            UpdateNavButtonStyles();
        }
    }

    private void UpdateHostCountdownCards()
    {
        if (screen == "map")
        {
            foreach (var pair in hostHoverRemainingBlocks)
            {
                if (records.TryGetValue(pair.Key, out var record) && record.HasSession)
                    pair.Value.Text = SessionRemainingText(record);
            }
            foreach (var pair in hostBottomBlocks)
            {
                if (records.TryGetValue(pair.Key, out var record))
                    pair.Value.Text = HostBottomText(record);
            }
        }

        if (screen == "details")
        {
            foreach (var pair in computerRemainingBlocks)
            {
                if (records.TryGetValue(pair.Key, out var record) && record.HasSession)
                    pair.Value.Text = SessionRemainingText(record);
            }
        }
    }

    private void UpdateSelectedElapsedPanel()
    {
        if (selected?.FreedAtUtc != null || selected?.HasSession == true)
            UpdateSelectedPanel();
    }

    private void RebuildPsTimersList()
    {
        if (psTimersList == null) return;
        psTimersList.Children.Clear();

        var ordered = psTimers
            .OrderBy(x => x.Completed)
            .ThenBy(x => x.EndsAt)
            .ToList();

        if (ordered.Count == 0)
        {
            psTimersList.Children.Add(new TextBlock
            {
                Text = "Активных PS-таймеров пока нет.",
                Foreground = Brush("#8f9aad"),
                FontSize = 14,
                Margin = new Thickness(0, 8, 0, 0)
            });
            return;
        }

        foreach (var timerSession in ordered)
            psTimersList.Children.Add(PsTimerCard(timerSession));
    }

    private Border PsTimerCard(PsTimerSession timerSession)
    {
        var statusBrush = timerSession.Completed ? Brush("#667085") : Brush("#17c964");
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = timerSession.Device + " · " + timerSession.RemainingText,
            Foreground = statusBrush,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold
        });
        stack.Children.Add(new TextBlock
        {
            Text = timerSession.TimeRange + " · " + timerSession.Client,
            Foreground = Brush("#a9b2c5"),
            FontSize = 13
        });
        stack.Children.Add(new TextBlock
        {
            Text = timerSession.Source,
            Foreground = Brush("#778399"),
            FontSize = 12
        });
        row.Children.Add(stack);

        var stop = GhostButton(timerSession.Completed ? "Убрать" : "Стоп", () =>
        {
            psTimers.Remove(timerSession);
            RebuildPsTimersList();
            ShowToast("Таймер убран", timerSession.Device + " удалён из списка.");
        });
        stop.Width = 92;
        stop.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(stop, 1);
        row.Children.Add(stop);

        return new Border
        {
            Background = PanelBrush("#101f1b", 188),
            BorderBrush = timerSession.Completed ? Brush("#263244") : Brush("#1f8f4d"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Child = row
        };
    }

    private Border Panel(string titleText, Control content)
    {
        var panelContent = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = titleText, Foreground = Brush("#f5f7ff"), FontSize = 18, FontWeight = FontWeight.SemiBold },
                content
            }
        };

        return new Border
        {
            Background = PanelBrush("#121a25", 150),
            BorderBrush = Brush("#263244"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 14, 0),
            Child = WithDragonWatermark(panelContent, 112, 0.06)
        };
    }

    private Button FilterButton(string text, bool active)
    {
        return new Button
        {
            Height = 38,
            MinWidth = 92,
            Background = active ? Brush("#3b4251") : Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = active ? Brush("#3b82f6") : Brush("#2b3550"),
            Content = text
        };
    }

    private Button PaymentButton(string text, string method)
    {
        return new Button
        {
            Height = 56,
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = Brush("#202938"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#334155"),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    PaymentIcon(method),
                    new TextBlock
                    {
                        Text = text,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontWeight = FontWeight.SemiBold
                    }
                }
            }
        };
    }

    private Border CatalogRow(ShopItem item)
    {
        var row = CatalogRow(item.Title, item.StockText, item.CostText);
        row.PointerPressed += (_, _) => AddShopItemToCart(item);
        return row;
    }

    private Border CatalogRow(string name, string stock, string cost)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,90,100") };
        grid.Children.Add(new TextBlock
        {
            Text = name,
            Foreground = Brush("#f5f7ff"),
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var stockText = new TextBlock { Text = stock, Foreground = Brush("#f5f7ff") };
        Grid.SetColumn(stockText, 1);
        grid.Children.Add(stockText);
        var costText = new TextBlock { Text = cost, Foreground = Brush("#f5f7ff"), FontWeight = FontWeight.SemiBold };
        Grid.SetColumn(costText, 2);
        grid.Children.Add(costText);

        return new Border
        {
            Background = Brush("#151d28"),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(14, 12),
            Child = grid
        };
    }

    private void RefreshMap()
    {
        if (activeHostHoverPopup != null)
            activeHostHoverPopup.IsOpen = false;
        activeHostHoverPopup = null;
        activeHostHoverPointerInside = false;
        hostHoverRemainingBlocks.Clear();
        hostBottomBlocks.Clear();
        mapCanvas.Children.Clear();
        var offset = MapLayoutOffset();
        foreach (var record in records.Values.OrderBy(x => NaturalKey(x.Name)))
        {
            var card = HostCard(record);
            var placed = layout.TryGetValue(record.Name, out var p) ? p : new Point(30, 30);
            var point = new Point(placed.X + offset.X, placed.Y + offset.Y);
            Canvas.SetLeft(card, point.X);
            Canvas.SetTop(card, point.Y);
            mapCanvas.Children.Add(card);
        }
        mapCanvas.Children.Add(mapSelectionRectangle);
    }

    private void ConfigureMapSelection()
    {
        mapCanvas.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(mapCanvas).Properties.IsLeftButtonPressed) return;
            mapSelectionStart = e.GetPosition(mapCanvas);
            mapSelectionDragging = false;
            mapSelectionAdditive = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        };

        mapCanvas.PointerMoved += (_, e) =>
        {
            if (mapSelectionStart == null || !e.GetCurrentPoint(mapCanvas).Properties.IsLeftButtonPressed) return;

            var current = e.GetPosition(mapCanvas);
            var start = mapSelectionStart.Value;
            if (!mapSelectionDragging && Math.Abs(current.X - start.X) < 6 && Math.Abs(current.Y - start.Y) < 6) return;

            mapSelectionDragging = true;
            UpdateMapSelectionRectangle(start, current);
            e.Pointer.Capture(mapCanvas);
            e.Handled = true;
        };

        mapCanvas.PointerReleased += (_, e) =>
        {
            if (mapSelectionStart == null) return;

            if (mapSelectionDragging)
            {
                SelectHostsInRectangle(mapSelectionStart.Value, e.GetPosition(mapCanvas), mapSelectionAdditive);
                e.Handled = true;
            }
            else
            {
                selectedHostKeys.Clear();
                selected = null;
                RefreshMap();
                UpdateSelectedPanel();
                e.Handled = true;
            }

            mapSelectionStart = null;
            mapSelectionDragging = false;
            mapSelectionRectangle.IsVisible = false;
            e.Pointer.Capture(null);
        };
    }

    private void UpdateMapSelectionRectangle(Point start, Point current)
    {
        var left = Math.Min(start.X, current.X);
        var top = Math.Min(start.Y, current.Y);
        Canvas.SetLeft(mapSelectionRectangle, left);
        Canvas.SetTop(mapSelectionRectangle, top);
        mapSelectionRectangle.Width = Math.Abs(current.X - start.X);
        mapSelectionRectangle.Height = Math.Abs(current.Y - start.Y);
        mapSelectionRectangle.IsVisible = true;
    }

    private void SelectHostsInRectangle(Point start, Point end, bool additive)
    {
        var selectionRect = new Rect(
            Math.Min(start.X, end.X),
            Math.Min(start.Y, end.Y),
            Math.Abs(end.X - start.X),
            Math.Abs(end.Y - start.Y));

        if (!additive)
            selectedHostKeys.Clear();

        var offset = MapLayoutOffset();
        foreach (var record in records.Values)
        {
            var placed = layout.TryGetValue(record.Name, out var p) ? p : new Point(30, 30);
            var point = new Point(placed.X + offset.X, placed.Y + offset.Y);
            var cardRect = new Rect(point.X, point.Y, 73, 73);
            if (selectionRect.Intersects(cardRect))
                selectedHostKeys.Add(record.Name);
        }

        selected = records.Values
            .Where(x => selectedHostKeys.Contains(x.Name))
            .OrderBy(x => NaturalKey(x.Name))
            .FirstOrDefault();
        RefreshMap();
        UpdateSelectedPanel();
    }

    private Point MapLayoutOffset() => new(
        Math.Max(0, (mapCanvas.Width - 960) / 2),
        Math.Max(0, (mapCanvas.Height - 700) / 2));

    private Border HostCard(PcRecord record)
    {
        var color = StatusBrush(record);
        var groupSelected = selectedHostKeys.Contains(record.Name);
        var topControl = HostTopControl(record, out var setTopControlHovered);
        var bottomText = new TextBlock
        {
            Text = HostBottomText(record),
            Foreground = color,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold
        };
        hostBottomBlocks[record.Name] = bottomText;
        var card = new Border
        {
            Width = 73,
            Height = 73,
            Background = StatusFill(record),
            BorderBrush = groupSelected ? Brush("#f5f7ff") : color,
            BorderThickness = new Thickness(groupSelected ? 3 : IsConnectionLost(record) || record.Online || record.SourceStatus is "busy" or "free" ? 2 : 1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Child = new Grid
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = ShortName(record.Name),
                        Foreground = color,
                        FontWeight = FontWeight.SemiBold,
                        FontSize = 14
                    },
                    topControl,
                    bottomText
                }
            }
        };

        upcomingBookingsByHost.TryGetValue(record.HostId, out var upcomingBooking);
        var hoverPopup = new Popup
        {
            PlacementTarget = card,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            VerticalOffset = -1,
            IsLightDismissEnabled = false,
            Topmost = true
        };
        void CloseHoverPopup()
        {
            hoverPopup.IsOpen = false;
            if (ReferenceEquals(activeHostHoverPopup, hoverPopup))
            {
                activeHostHoverPopup = null;
                activeHostHoverPointerInside = false;
            }
        }
        hoverPopup.Child = HostHoverTip(
            record,
            upcomingBooking,
            () =>
            {
                CloseHoverPopup();
                _ = OpenHoverClientAsync(record);
            },
            () =>
            {
                CloseHoverPopup();
                if (upcomingBooking != null)
                    _ = OpenBookingDialogAsync(record, upcomingBooking.HostIds, upcomingBooking);
            });
        ((Grid)card.Child!).Children.Add(hoverPopup);

        var normalFill = StatusFill(record);
        var normalThickness = card.BorderThickness;
        var contextMenuPending = false;
        var cardHovered = false;
        var popupHovered = false;
        var hoverVersion = 0;
        void SchedulePopupClose()
        {
            var version = ++hoverVersion;
            DispatcherTimer.RunOnce(() =>
            {
                if (version == hoverVersion && !cardHovered && !popupHovered)
                    CloseHoverPopup();
            }, TimeSpan.FromMilliseconds(180));
        }
        hoverPopup.Child.PointerEntered += (_, _) =>
        {
            popupHovered = true;
            activeHostHoverPointerInside = true;
            hoverVersion++;
        };
        hoverPopup.Child.PointerExited += (_, _) =>
        {
            popupHovered = false;
            if (ReferenceEquals(activeHostHoverPopup, hoverPopup))
                activeHostHoverPointerInside = false;
            SchedulePopupClose();
        };
        card.PointerEntered += (_, _) =>
        {
            if (activeHostHoverPointerInside && activeHostHoverPopup != null && !ReferenceEquals(activeHostHoverPopup, hoverPopup))
                return;
            cardHovered = true;
            hoverVersion++;
            if (activeHostHoverPopup != null && !ReferenceEquals(activeHostHoverPopup, hoverPopup))
                activeHostHoverPopup.IsOpen = false;
            activeHostHoverPopup = hoverPopup;
            hoverPopup.IsOpen = true;
            card.Background = Brushes.Transparent;
            card.BorderThickness = new Thickness(2);
            setTopControlHovered(true);
        };
        card.PointerExited += (_, _) =>
        {
            cardHovered = false;
            card.Background = normalFill;
            card.BorderThickness = normalThickness;
            setTopControlHovered(false);
            SchedulePopupClose();
        };

        card.PointerPressed += (_, e) =>
        {
            var pointer = e.GetCurrentPoint(card).Properties;
            if (pointer.IsLeftButtonPressed)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    if (!selectedHostKeys.Add(record.Name))
                        selectedHostKeys.Remove(record.Name);
                }
                else
                {
                    selectedHostKeys.Clear();
                    selectedHostKeys.Add(record.Name);
                }

                selected = selectedHostKeys.Contains(record.Name)
                    ? record
                    : records.Values.FirstOrDefault(x => selectedHostKeys.Contains(x.Name));
                card.BorderBrush = Brush("#f5f7ff");
                card.BorderThickness = new Thickness(3);
                DispatcherTimer.RunOnce(() =>
                {
                    if (screen == "map") RefreshMap();
                }, TimeSpan.FromMilliseconds(550));
            }
            else if (pointer.IsRightButtonPressed && !selectedHostKeys.Contains(record.Name))
            {
                selectedHostKeys.Clear();
                selectedHostKeys.Add(record.Name);
                selected = record;
                contextMenuPending = true;
                card.BorderBrush = Brush("#f5f7ff");
                card.BorderThickness = new Thickness(3);
            }
            else if (pointer.IsRightButtonPressed)
            {
                contextMenuPending = true;
            }
            UpdateSelectedPanel();
            e.Handled = true;
        };
        card.PointerReleased += (_, _) =>
        {
            if (!contextMenuPending) return;
            contextMenuPending = false;
            if (!mapSelectionDragging)
                HostMenu(record).Open(card);
        };
        card.DoubleTapped += (_, _) => _ = OpenTariffSaleDialogAsync(record);
        return card;
    }

    private Control HostHoverTip(PcRecord record, BookingInfo? booking, Action openClient, Action openBooking)
    {
        var accent = StatusBrush(record);
        var body = new StackPanel { Spacing = 12 };
        Button HoverLink(Control content, Action action)
        {
            var button = new Button
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = content
            };
            button.Click += (_, _) => action();
            return button;
        }
        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new Border
                {
                    Width = 24,
                    Height = 24,
                    CornerRadius = new CornerRadius(12),
                    Background = HasLoggedInClient(record) ? Brush("#103c2c") : Brush("#26313d"),
                    Child = new TextBlock
                    {
                        Text = HostHoverIcon(record),
                        Foreground = HasLoggedInClient(record) ? Brush("#17c964") : Brush("#cbd5e1"),
                        FontSize = 14,
                        FontWeight = FontWeight.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                },
                new TextBlock
                {
                    Text = HostHoverStatusText(record),
                    Foreground = Brush("#f5f7ff"),
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        });

        if (record.HasSession)
        {
            var remaining = new TextBlock
            {
                Text = SessionRemainingText(record),
                Foreground = accent,
                FontWeight = FontWeight.SemiBold
            };
            hostHoverRemainingBlocks[record.Name] = remaining;
            body.Children.Add(new Border
            {
                Background = Brush("#16243a"),
                BorderBrush = Brush("#24466f"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(10, 6),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = remaining
            });

            if (!string.IsNullOrWhiteSpace(record.ActiveTariffTitle)
                || record.SessionStartedAtUtc.HasValue
                || record.SessionEndsAtUtc.HasValue)
            {
                var sessionPeriod = record.SessionStartedAtUtc.HasValue && record.SessionEndsAtUtc.HasValue
                    ? record.SessionStartedAtUtc.Value.ToLocalTime().ToString("HH:mm")
                        + " → "
                        + record.SessionEndsAtUtc.Value.ToLocalTime().ToString("HH:mm")
                    : record.SessionStartedAtUtc.HasValue
                        ? "с " + record.SessionStartedAtUtc.Value.ToLocalTime().ToString("HH:mm")
                        : record.SessionEndsAtUtc.HasValue
                            ? "до " + record.SessionEndsAtUtc.Value.ToLocalTime().ToString("HH:mm")
                            : "активный пакет";
                body.Children.Add(new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "◷  " + (string.IsNullOrWhiteSpace(record.ActiveTariffTitle) ? "Активный сеанс" : record.ActiveTariffTitle),
                            Foreground = Brush("#f5f7ff"),
                            FontWeight = FontWeight.SemiBold,
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock { Text = sessionPeriod, Foreground = Brush("#8f9aad"), FontSize = 12 }
                    }
                });
            }

        }

        if ((record.HasSession || HasLoggedInClient(record))
            && (!string.IsNullOrWhiteSpace(record.ClientName) || !string.IsNullOrWhiteSpace(record.ClientPhone)))
        {
            var clientText = !string.IsNullOrWhiteSpace(record.ClientName)
                ? record.ClientName
                : record.ClientPhone;
            var phoneText = !string.IsNullOrWhiteSpace(record.ClientPhone) && record.ClientPhone != clientText
                ? record.ClientPhone
                : "";
            var clientContent = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "◉", Foreground = Brush("#f5f7ff"), FontSize = 16, VerticalAlignment = VerticalAlignment.Center },
                    new StackPanel
                    {
                        Spacing = 2,
                        Children =
                        {
                            new TextBlock { Text = clientText ?? "", Foreground = Brush("#2f8cff"), FontWeight = FontWeight.SemiBold, TextDecorations = TextDecorations.Underline, TextTrimming = TextTrimming.CharacterEllipsis },
                            new TextBlock { Text = phoneText, Foreground = Brush("#8f9aad"), FontSize = 12, IsVisible = !string.IsNullOrWhiteSpace(phoneText), TextTrimming = TextTrimming.CharacterEllipsis }
                        }
                    }
                }
            };
            body.Children.Add(HoverLink(clientContent, openClient));
        }

        if (booking != null)
        {
            var hours = Math.Max(0, (booking.ToLocal - booking.FromLocal).TotalHours);
            var when = booking.FromLocal.Date == DateTime.Today
                ? "Бронь сегодня в " + booking.FromLocal.ToString("HH:mm")
                : "Бронь " + booking.FromLocal.ToString("dd.MM в HH:mm");
            var bookingTitle = when + " (" + hours.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " ч.)";
            body.Children.Add(new Border { Height = 1, Background = Brush("#334052") });
            body.Children.Add(HoverLink(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    BookingGlyph(),
                    new StackPanel
                    {
                        Spacing = 3,
                        Children =
                        {
                            new TextBlock { Text = bookingTitle, Foreground = Brush("#2f8cff"), FontWeight = FontWeight.SemiBold, TextDecorations = TextDecorations.Underline },
                            new TextBlock { Text = booking.HostIds.Count + " ПК", Foreground = Brush("#8f9aad"), FontSize = 12 }
                        }
                    }
                }
            }, openBooking));
        }

        var metaRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = "▣",
                    Foreground = Brush("#cbd5e1"),
                    FontSize = 16,
                    VerticalAlignment = VerticalAlignment.Center
                },
                new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = accent,
                    VerticalAlignment = VerticalAlignment.Center
                },
                new TextBlock
                {
                    Text = HostHoverPlaceText(record),
                    Foreground = Brush("#f5f7ff"),
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
        body.Children.Add(new Border { Height = 1, Background = Brush("#334052") });
        body.Children.Add(metaRow);

        return new Border
        {
            Width = 264,
            Background = Brush("#161c23"),
            BorderBrush = Brush("#334052"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16, 14),
            BoxShadow = new BoxShadows(new BoxShadow { Color = Color.Parse("#99000000"), Blur = 22, OffsetY = 10 }),
            Child = body
        };
    }

    private async Task OpenHoverClientAsync(PcRecord record)
    {
        var query = !string.IsNullOrWhiteSpace(record.ClientUuid) ? record.ClientUuid : ActiveClientQuery(record);
        if (string.IsNullOrWhiteSpace(query)) return;

        try
        {
            var client = !string.IsNullOrWhiteSpace(record.ClientUuid)
                ? await service.LoadClientDetailsAsync(config, record.ClientUuid!, CancellationToken.None)
                : await service.FindFirstClientAsync(config, query, CancellationToken.None);
            if (client == null)
                throw new InvalidOperationException("Клиент не найден.");
            if (string.IsNullOrWhiteSpace(record.ClientUuid) && !string.IsNullOrWhiteSpace(client.Uuid))
                client = await service.LoadClientDetailsAsync(config, client.Uuid, CancellationToken.None) ?? client;
            await ShowClientCardDialogAsync(client, this);
        }
        catch (Exception ex)
        {
            ShowToast(record.Name, "Не удалось открыть клиента: " + ex.Message);
        }
    }

    private async Task ShowClientCardDialogAsync(ClientInfo client, Window owner)
    {
        var dialog = new ModalWindow
        {
            Title = "Клиент " + client.Display,
            Width = 1250,
            Height = 760,
            MinWidth = 1040,
            MinHeight = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#0d1217"),
            SystemDecorations = SystemDecorations.None
        };

        var historyRows = new StackPanel();
        var paginationPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(12, 10, 12, 14)
        };
        var historyStatus = new TextBlock
        {
            Foreground = Brush("#8f9aad"),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(16)
        };
        var tabButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        var activeMode = "PAYMENTS";
        var activePage = 1;

        TextBlock HistoryText(string text, IBrush? color = null, bool bold = false) => new()
        {
            Text = text,
            Foreground = color ?? Brush("#f5f7ff"),
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        Border HistoryBadge(string text, string background, string foreground)
        {
            return new Border
            {
                Background = Brush(background),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = HistoryText(text, Brush(foreground), true)
            };
        }

        Control PaymentBadge(string payment)
        {
            var value = string.IsNullOrWhiteSpace(payment) ? "—" : payment;
            var normalized = value.ToUpperInvariant();

            Control VectorBadge(string icon, string background, string foreground) => new Border
            {
                Background = Brush(background),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        DashboardIcon(icon, foreground, 16),
                        HistoryText(value, Brush(foreground), true)
                    }
                }
            };

            if (normalized.Contains("НАЛИЧ")) return VectorBadge("cash", "#14392d", "#35d48a");
            if (normalized.Contains("КАРТ")) return VectorBadge("card", "#15324a", "#4aa9ff");
            if (normalized.Contains("ДЕПОЗ")) return VectorBadge("wallet", "#2b2148", "#a77cff");
            if (normalized.Contains("БОНУС")) return HistoryBadge("★  " + value, "#402d17", "#ffb84d");
            if (normalized.Contains("РАЗДЕЛ")) return HistoryBadge("÷  " + value, "#26313d", "#b9c6d8");
            return HistoryBadge(value, "#202a35", "#b9c6d8");
        }

        Border HistoryRow(ClientHistoryEntry entry, bool header = false)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,170,*,150,120"), ColumnSpacing = 12 };
            var values = header
                ? activeMode switch
                {
                    "BONUS" => new[] { "Дата", "Оператор", "Операция", "Изменение", "Баланс" },
                    "TRANSFERS" => new[] { "Дата", "Клуб", "Тип", "Источник", "Сумма" },
                    _ => new[] { "Дата", "Оператор", "Категория", "Оплата", "Итого" }
                }
                : new[] { entry.DateText, entry.Operator, entry.Category, entry.Payment, entry.Total };

            for (var i = 0; i < values.Length; i++)
            {
                Control cell;
                if (!header && activeMode == "PAYMENTS" && i == 2)
                {
                    var category = HistoryText(values[i], entry.Category.Contains("Покуп", StringComparison.OrdinalIgnoreCase) ? Brush("#2f8cff") : null, true);
                    if (entry.Category.Contains("Покуп", StringComparison.OrdinalIgnoreCase))
                        category.TextDecorations = TextDecorations.Underline;
                    cell = category;
                }
                else if (!header && activeMode == "PAYMENTS" && i == 3)
                {
                    cell = PaymentBadge(values[i]);
                }
                else if (!header && activeMode == "PAYMENTS" && i == 4)
                {
                    var total = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                    total.Children.Add(HistoryText(values[i], null, true));
                    if (!string.IsNullOrWhiteSpace(entry.BonusText))
                        total.Children.Add(HistoryBadge(entry.BonusText, "#431f26", "#ff6674"));
                    if (entry.IsRefunded)
                        total.Children.Add(HistoryBadge("Возврат", "#402d17", "#ffb84d"));
                    cell = total;
                }
                else
                {
                    var color = header ? Brush("#a8b3c7") : i == 1 ? Brush("#8f9aad") : null;
                    cell = HistoryText(values[i], color, header || i == 0);
                }
                Grid.SetColumn(cell, i);
                grid.Children.Add(cell);
            }

            return new Border
            {
                Background = header ? Brush("#121a24") : Brushes.Transparent,
                BorderBrush = Brush("#303a48"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, header ? 14 : 16),
                Child = grid
            };
        }

        void RefreshTabs()
        {
            foreach (var pair in tabButtons)
            {
                var active = pair.Key == activeMode;
                pair.Value.Background = active ? Brush("#36404d") : Brushes.Transparent;
                pair.Value.Foreground = active ? Brush("#f5f7ff") : Brush("#a8b3c7");
            }
        }

        Button PageButton(string text, int targetPage, bool selected = false, bool enabled = true)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 40,
                Height = 40,
                Padding = new Thickness(10, 0),
                IsEnabled = enabled,
                Background = selected ? Brush("#132b4a") : Brushes.Transparent,
                BorderBrush = selected ? Brush("#2f8cff") : Brushes.Transparent,
                Foreground = selected ? Brush("#f5f7ff") : Brush("#c0cad8"),
                FontWeight = selected ? FontWeight.Bold : FontWeight.SemiBold
            };
            button.Click += async (_, _) => await LoadHistoryAsync(activeMode, targetPage);
            return button;
        }

        void RefreshPagination(int currentPage, int lastPage)
        {
            paginationPanel.Children.Clear();
            paginationPanel.IsVisible = lastPage > 1;
            if (lastPage <= 1) return;

            paginationPanel.Children.Add(PageButton("‹", Math.Max(1, currentPage - 1), enabled: currentPage > 1));
            var pages = new SortedSet<int> { 1, lastPage };
            if (currentPage <= 4)
                for (var page = 1; page <= Math.Min(6, lastPage); page++) pages.Add(page);
            else if (currentPage >= lastPage - 3)
                for (var page = Math.Max(1, lastPage - 5); page <= lastPage; page++) pages.Add(page);
            else
                for (var page = currentPage - 2; page <= currentPage + 2; page++) pages.Add(page);

            var previous = 0;
            foreach (var page in pages)
            {
                if (previous > 0 && page - previous > 1)
                    paginationPanel.Children.Add(new TextBlock { Text = "…", Foreground = Brush("#8f9aad"), Width = 32, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                paginationPanel.Children.Add(PageButton(page.ToString(), page, page == currentPage));
                previous = page;
            }
            paginationPanel.Children.Add(PageButton("›", Math.Min(lastPage, currentPage + 1), enabled: currentPage < lastPage));
        }

        async Task LoadHistoryAsync(string mode, int page = 1)
        {
            activeMode = mode;
            activePage = page;
            RefreshTabs();
            paginationPanel.IsVisible = false;
            historyRows.Children.Clear();
            historyStatus.Text = "Загружаю историю...";
            historyRows.Children.Add(historyStatus);

            try
            {
                var result = mode switch
                {
                    "BONUS" => await service.LoadClientBonusHistoryAsync(config, client.Uuid, page, CancellationToken.None),
                    "TRANSFERS" => await service.LoadClientTransferHistoryAsync(config, client.Uuid, page, CancellationToken.None),
                    _ => await service.LoadClientPaymentHistoryAsync(config, client.Uuid, page, CancellationToken.None)
                };
                activePage = result.CurrentPage;
                historyRows.Children.Clear();
                historyRows.Children.Add(HistoryRow(new ClientHistoryEntry(), true));
                foreach (var entry in result.Entries)
                    historyRows.Children.Add(HistoryRow(entry));
                if (result.Entries.Count == 0)
                {
                    historyStatus.Text = "История пока пуста.";
                    historyRows.Children.Add(historyStatus);
                }
                RefreshPagination(result.CurrentPage, result.LastPage);
            }
            catch (Exception ex)
            {
                paginationPanel.IsVisible = false;
                historyRows.Children.Clear();
                historyStatus.Text = "Не удалось загрузить историю: " + ex.Message;
                historyRows.Children.Add(historyStatus);
            }
        }

        Button HistoryTab(string text, string mode)
        {
            var button = new Button
            {
                Content = text,
                Height = 40,
                Padding = new Thickness(16, 0),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                FontWeight = FontWeight.SemiBold
            };
            button.Click += async (_, _) => await LoadHistoryAsync(mode, 1);
            tabButtons[mode] = button;
            return button;
        }

        var tabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Children =
            {
                HistoryTab("Платежи", "PAYMENTS"),
                HistoryTab("Система лояльности", "BONUS"),
                HistoryTab("Переводы", "TRANSFERS")
            }
        };
        var left = BuildDepositClientPanel(client, topUpClient: async () =>
        {
            await ShowDepositTopUpDialogAsync(client, dialog);
            await LoadHistoryAsync(activeMode, activePage);
        });
        var rightContent = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children =
            {
                new Border { Background = Brush("#1AFFFFFF"), CornerRadius = new CornerRadius(8), Padding = new Thickness(4), Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Left, Child = tabs },
                new ScrollViewer { Content = historyRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto },
                paginationPanel
            }
        };
        Grid.SetRow(rightContent.Children[1], 1);
        Grid.SetRow(rightContent.Children[2], 2);
        var right = SalePanel(rightContent, new Thickness(0));

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("318,*"),
            ColumnSpacing = 16,
            Children = { left, right }
        };
        Grid.SetColumn(right, 1);

        var close = new Button
        {
            Content = "×",
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = Brush("#f5f7ff"),
            FontSize = 20
        };
        close.Click += (_, _) => dialog.Close(false);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock { Text = "Профиль клиента", Foreground = Brush("#f5f7ff"), FontSize = 18, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var shell = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 20, Children = { header, body } };
        Grid.SetRow(body, 1);
        dialog.Content = new Border
        {
            Background = Brush("#0d1217"),
            BorderBrush = Brush("#33FFFFFF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(28),
            Child = shell
        };

        dialog.Opened += async (_, _) => await LoadHistoryAsync("PAYMENTS", 1);
        await dialog.ShowDialog<bool?>(owner);
    }

    private Control HostTopControl(PcRecord record, out Action<bool> setHovered)
    {
        setHovered = _ => { };
        var host = new Grid
        {
            Width = 22,
            Height = 22,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };

        if (IsConnectionLost(record))
        {
            var warning = new TextBlock
            {
                Text = "⚠",
                Foreground = Brush("#ff3b4d"),
                FontSize = 17,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(warning, "Потеряна связь с ПК");
            host.Children.Add(warning);
            return host;
        }

        if (record.HasSession)
            return host;

        if (IsShellDisabled(record))
        {
            host.Children.Add(new TextBlock
            {
                Text = "×",
                Foreground = Brush("#ff4d4f"),
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            return host;
        }

        if (IsServiceMode(record))
        {
            host.Children.Add(new TextBlock
            {
                Text = "▲",
                Foreground = Brush("#f5a524"),
                FontSize = 17,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            return host;
        }

        if (!record.Online && record.SourceStatus == "offline")
        {
            var button = new Button
            {
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Content = PowerGlyph()
            };
            button.Click += (_, _) => _ = SendHostCommandAsync(record, "WOL", "включить");
            if (record.BookingCount > 0)
            {
                var booking = BookingGlyph();
                button.IsVisible = false;
                host.Children.Add(booking);
                host.Children.Add(button);
                setHovered = hovered =>
                {
                    booking.IsVisible = !hovered;
                    button.IsVisible = hovered;
                };
            }
            else
            {
                host.Children.Add(button);
            }
        }
        else if (record.BookingCount > 0)
        {
            host.Children.Add(BookingGlyph());
        }

        return host;
    }

    private Control BookingGlyph()
    {
        var icon = DashboardIcon("calendar", "#2f8cff", 17);
        ToolTip.SetTip(icon, "Есть бронирование");
        return icon;
    }

    private static Control PowerGlyph()
    {
        return new Grid
        {
            Width = 20,
            Height = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(8),
                    BorderBrush = Brush("#8f9aad"),
                    BorderThickness = new Thickness(2),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0.9
                },
                new Border
                {
                    Width = 3,
                    Height = 9,
                    CornerRadius = new CornerRadius(2),
                    Background = Brush("#8f9aad"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 1, 0, 0)
                }
            }
        };
    }

    private static string HostBottomText(PcRecord record)
    {
        if (!record.HasSession) return "";
        var session = SessionDisplayText(record);
        return string.IsNullOrWhiteSpace(session) ? "" : "◷ " + session;
    }

    private static string SessionDisplayText(PcRecord record)
    {
        if (record.SessionEndsAtUtc.HasValue)
        {
            var left = record.SessionEndsAtUtc.Value - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) return "00:00";
            if (left <= TimeSpan.FromMinutes(5))
                return ((int)left.TotalMinutes).ToString("00") + ":" + left.Seconds.ToString("00");
            return record.SessionEndsAtUtc.Value.ToLocalTime().ToString("HH:mm");
        }

        if (!string.IsNullOrWhiteSpace(record.SessionTimeLeft))
            return record.SessionTimeLeft!;

        return "";
    }

    private static string SessionRemainingText(PcRecord record)
    {
        if (!record.SessionEndsAtUtc.HasValue)
            return string.IsNullOrWhiteSpace(record.SessionTimeLeft) ? "00:00:00" : record.SessionTimeLeft!;

        var left = record.SessionEndsAtUtc.Value - DateTime.UtcNow;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        return ((int)left.TotalHours).ToString("00") + ":" + left.Minutes.ToString("00") + ":" + left.Seconds.ToString("00");
    }

    private ContextMenu HostMenu(PcRecord record)
    {
        var onlineControl = record.Online && !record.Locked;
        var canRemote = onlineControl && !string.IsNullOrWhiteSpace(record.MacAddress);
        var canServiceToggle = !record.Locked && (!record.HasSession || record.InService);
        var targets = selectedHostKeys.Contains(record.Name)
            ? SelectedHosts()
            : new List<PcRecord> { record };

        if (targets.Count > 1)
            return GroupHostMenu(targets);

        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("◷", "Выбрать тариф", true, () => OpenTariffSaleDialogAsync(record)));
        menu.Items.Add(MenuItem("▣", "Пополнить депозит", record.HasSession, () => OpenShopForRecordAsync(record)));
        menu.Items.Add(SubMenuItem("▦", "Бронирование", true,
            MenuItem("+", "Забронировать", true, () => OpenBookingDialogAsync(record)),
            MenuItem("≡", "Список бронирований", true, () => OpenBookingsListAsync(record)),
            MenuItem("✓", "Клиент пришёл", record.BookingCount > 0, () => RedeemFirstBookingAsync(record))
        ));
        menu.Items.Add(SubMenuItem("!", "Штраф", record.HasSession,
            MenuItem("5", "5 минут", record.HasSession, () => AddPenaltyPresetAsync(record, 5)),
            MenuItem("10", "10 минут", record.HasSession, () => AddPenaltyPresetAsync(record, 10)),
            MenuItem("15", "15 минут", record.HasSession, () => AddPenaltyPresetAsync(record, 15)),
            MenuItem("30", "30 минут", record.HasSession, () => AddPenaltyPresetAsync(record, 30)),
            MenuItem("…", "Другое время", record.HasSession, () => OpenPenaltyDialogAsync(record))
        ));
        menu.Items.Add(MenuItem("⇄", "Смена места", record.HasSession, () => OpenChangeHostDialogAsync(record)));
        menu.Items.Add(MenuItem("■", "Завершить сеанс", record.HasSession, () => StopSessionAsync(record), true));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("✉", "Уведомление", true, () => SendMessageToHostAsync(record)));
        menu.Items.Add(SubMenuItem("⚡", "Электропитание", !record.Locked,
            MenuItem("⏻", "Включить", !record.Online, () => SendHostCommandAsync(record, "WOL", "включить")),
            MenuItem("⏼", "Выключить", onlineControl, () => SendHostCommandAsync(record, "SHUTDOWN", "выключить"), true),
            MenuItem("↻", "Перезагрузить", onlineControl, () => SendHostCommandAsync(record, "REBOOT", "перезагрузить")),
            MenuItem("↪", "Выйти из Windows", onlineControl, () => SendHostCommandAsync(record, "LOGOFF", "выйти из Windows"))
        ));
        menu.Items.Add(SubMenuItem("⚙", "Управление ПК", true,
            MenuItem("▵", record.InService ? "Выкл. режим обслуживания" : "Вкл. режим обслуживания", canServiceToggle, () => SetServiceModeAsync(record, !record.InService)),
            MenuItem("⌁", "Подключиться к ПК", canRemote, () => OpenSstvnAsync(record, false)),
            MenuItem("▣", "Просмотр экрана", canRemote, () => OpenSstvnAsync(record, true)),
            MenuItem(">", "Выполнить", onlineControl, () => SendRunCommandAsync(record)),
            MenuItem("✎", "Заметка о ПК", !record.Locked, () => AddHostCommentAsync(record))
        ));
        menu.Items.Add(SubMenuItem("S", "Shell", true,
            MenuItem("⇪", "Поделиться логами", record.Online, () => SendHostCommandAsync(record, "SEND_LOGS", "запросить логи")),
            MenuItem("H", "High access", onlineControl, () => SendHighAccessAsync(record)),
            MenuItem("×", "Снять Shell", onlineControl, () => SendProtectedHostCommandAsync(record, "DISABLE_SHELL", "выключить Shell"), true),
            MenuItem("↻", "Обновить Shell", onlineControl, () => SendHostCommandAsync(record, "SHELL_UPDATE", "обновить Shell"))
        ));
        return menu;
    }

    private ContextMenu GroupHostMenu(IReadOnlyCollection<PcRecord> hosts)
    {
        var ordered = hosts.OrderBy(x => NaturalKey(x.Name)).ToList();
        var anyOffline = ordered.Any(x => !x.Online && !x.Locked);
        var anyOnline = ordered.Any(x => x.Online && !x.Locked);
        var anyShutdown = ordered.Any(x => x.Online && !x.Locked);
        var menu = new ContextMenu();

        menu.Items.Add(new MenuItem
        {
            Header = MenuHeader("✓", "Выбрано компьютеров: " + ordered.Count, false, false),
            IsEnabled = false
        });
        menu.Items.Add(SubMenuItem("▦", "Бронирование", true,
            MenuItem("+", "Забронировать выбранные", true,
                () => OpenBookingDialogAsync(ordered[0], ordered.Select(x => x.HostId).ToList()))
        ));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("✉", "Уведомление всем", anyOnline,
            () => PromptBulkMessageAsync(ordered)));
        menu.Items.Add(SubMenuItem("⚡", "Электропитание", anyOffline || anyOnline,
            MenuItem("⏻", "Включить", anyOffline, () => SendBulkHostCommandAsync(ordered, "WOL")),
            MenuItem("⏼", "Выключить", anyShutdown, () => SendBulkHostCommandAsync(ordered, "SHUTDOWN"), true),
            MenuItem("↻", "Перезагрузить", anyOnline, () => SendBulkHostCommandAsync(ordered, "REBOOT")),
            MenuItem("↪", "Выйти из Windows", anyOnline, () => SendBulkHostCommandAsync(ordered, "LOGOFF"))
        ));
        return menu;
    }

    private MenuItem SubMenuItem(string icon, string text, bool enabled, params MenuItem[] children)
    {
        var item = new MenuItem
        {
            Header = MenuHeader(icon, text, enabled, false),
            IsEnabled = enabled,
            Foreground = enabled ? Brush("#f5f7ff") : Brush("#667085")
        };
        foreach (var child in children)
            item.Items.Add(child);
        return item;
    }

    private MenuItem MenuItem(string icon, string text, bool enabled, Func<Task> action, bool danger = false)
    {
        var foreground = !enabled ? Brush("#667085") : danger ? Brush("#ff4d4f") : Brush("#f5f7ff");
        var item = new MenuItem
        {
            Header = MenuHeader(icon, text, enabled, danger),
            IsEnabled = enabled,
            Foreground = foreground
        };
        item.Click += async (_, _) =>
        {
            try { await action(); }
            catch (Exception ex) { ShowToast("SmartShell", ex.Message); }
        };
        return item;
    }

    private static Control MenuHeader(string icon, string text, bool enabled, bool danger)
    {
        var foreground = !enabled ? Brush("#667085") : danger ? Brush("#ff4d4f") : Brush("#f5f7ff");
        var iconForeground = !enabled ? Brush("#667085") : danger ? Brush("#ff4d4f") : Brush("#2f8cff");
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9,
            Children =
            {
                new TextBlock
                {
                    Text = icon,
                    Width = 20,
                    Foreground = iconForeground,
                    FontSize = 14,
                    FontWeight = FontWeight.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                new TextBlock
                {
                    Text = text,
                    Foreground = foreground,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
    }

    private async Task OpenShopForRecordAsync(PcRecord record)
    {
        selected = record;
        SwitchScreen("shop");
        var value = !string.IsNullOrWhiteSpace(record.ClientPhone) ? record.ClientPhone : record.ClientName;
        if (!string.IsNullOrWhiteSpace(value) && clientSearchBox != null)
        {
            clientSearchBox.Text = value;
            await SearchClientsFromUiAsync();
        }
    }

    private async Task OpenTariffSaleDialogAsync(PcRecord record, IReadOnlyCollection<PcRecord>? targetHosts = null)
    {
        if (!EnsureSmartShellHost(record)) return;
        var saleHosts = (targetHosts ?? new[] { record })
            .Where(x => x.HostId > 0)
            .DistinctBy(x => x.HostId)
            .OrderBy(x => NaturalKey(x.Name))
            .ToList();
        if (saleHosts.Count == 0) return;

        try
        {
            source.Text = record.Name + ": загружаю тарифы...";

            ClientInfo? activeClient = null;
            var activeClientQuery = ActiveClientQuery(record);
            if (!string.IsNullOrWhiteSpace(activeClientQuery))
            {
                try
                {
                    activeClient = await service.FindFirstClientAsync(config, activeClientQuery, CancellationToken.None);
                    if (activeClient != null)
                        selectedClient = activeClient;
                }
                catch
                {
                    activeClient = null;
                }
            }

            var shiftSeconds = record.HasSession && record.SessionTimeLeftSeconds > 0
                ? record.SessionTimeLeftSeconds
                : (int?)null;
            var tariffs = await service.LoadTariffsAsync(config, record.HostId, activeClient?.Uuid ?? record.ClientUuid, shiftSeconds, CancellationToken.None);
            if (tariffs.Count == 0)
            {
                ShowToast(record.Name, "SmartShell не вернул активные тарифы.");
                return;
            }

            var shopItems = allShopItems.ToList();
            if (shopItems.Count == 0)
            {
                try
                {
                    source.Text = record.Name + ": загружаю товары и услуги...";
                    shopItems = await service.LoadShopItemsAsync(config, CancellationToken.None);
                    allShopItems.Clear();
                    allShopItems.AddRange(shopItems);
                }
                catch (Exception ex)
                {
                    ShowToast("Ассортимент", "Тарифы откроются, но товары/услуги не загрузились: " + ex.Message);
                }
            }

            var sale = await ShowTariffSaleDialogAsync(record, tariffs, shopItems, activeClient);
            if (sale == null || sale.Items.Count == 0) return;
            await CreateTariffPaymentAsync(record, sale, saleHosts);
        }
        catch (Exception ex)
        {
            ShowToast(record.Name, "Ошибка тарифов: " + ex.Message);
            source.Text = record.Name + ": ошибка тарифов - " + ex.Message;
        }
    }

    private static string ActiveClientQuery(PcRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.ClientPhone)) return record.ClientPhone!;
        if (!string.IsNullOrWhiteSpace(record.ClientName)) return record.ClientName!;
        if (!string.IsNullOrWhiteSpace(record.ClientUuid)) return record.ClientUuid!;
        return "";
    }

    private async Task<TariffSaleResult?> ShowTariffSaleDialogAsync(PcRecord record, List<TariffInfo> tariffs, List<ShopItem> shopItems, ClientInfo? activeClient)
    {
        var dialog = new ModalWindow
        {
            Title = "Продажа для " + record.Name,
            Width = 1500,
            Height = 720,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#0d1217"),
            SystemDecorations = SystemDecorations.None
        };

        TariffInfo? pickedTariff = null;
        var availableTariffs = tariffs.ToList();
        var initialTariffShiftSeconds = record.HasSession && record.SessionTimeLeftSeconds > 0
            ? record.SessionTimeLeftSeconds
            : 0;
        var tariffRefreshVersion = 0;
        var saleMode = "TARIFF";
        var saleCartItems = new List<CartItem>();
        var method = "CASH";
        var canUseDeposit = activeClient != null && !string.IsNullOrWhiteSpace(activeClient.Uuid);
        var activeQuery = ActiveClientQuery(record);
        var clientBox = SaleInput("Найти клиента");

        var searchBox = SaleInput("Поиск");
        var saleRows = new StackPanel();
        var cartContent = new Grid();
        var saleStatus = new TextBlock
        {
            Foreground = Brush("#f5a524"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var discountValue = new TextBlock { Text = "—", Foreground = Brush("#9aa6ba"), FontWeight = FontWeight.SemiBold };
        var finishValue = new TextBlock { Text = "—", Foreground = Brush("#9aa6ba"), FontWeight = FontWeight.SemiBold };
        var saleCashBox = SaleInput("Наличными");
        var saleCardBox = SaleInput("Картой");
        var saleSplitGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8,
            IsVisible = false,
            Children = { saleCashBox, saleCardBox }
        };
        Grid.SetColumn(saleCardBox, 1);
        var saleSplitSyncing = false;
        var payButton = SalePrimaryButton("Оплатить 0 ₽");
        payButton.IsEnabled = false;
        var methodButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        var modeButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);

        Button TabButton(string text, string mode)
        {
            var button = new Button
            {
                Content = text,
                Height = 40,
                Padding = new Thickness(18, 0),
                Background = Brushes.Transparent,
                Foreground = Brush("#f5f7ff"),
                BorderBrush = Brushes.Transparent,
                FontWeight = FontWeight.SemiBold
            };
            button.Click += (_, _) =>
            {
                saleMode = mode;
                RefreshModeButtons();
                RenderSaleRows();
            };
            modeButtons[mode] = button;
            return button;
        }

        void RefreshModeButtons()
        {
            foreach (var pair in modeButtons)
            {
                var active = string.Equals(pair.Key, saleMode, StringComparison.OrdinalIgnoreCase);
                pair.Value.Background = active ? Brush("#1AFFFFFF") : Brushes.Transparent;
                pair.Value.BorderBrush = Brushes.Transparent;
                pair.Value.Foreground = Brush("#f5f7ff");
            }
        }

        Button MethodButton(string text, string value, bool enabled = true)
        {
            var button = new Button
            {
                Height = 60,
                Padding = new Thickness(8, 6),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Foreground = enabled ? Brush("#f5f7ff") : Brush("#667085"),
                IsEnabled = enabled,
                Content = new StackPanel
                {
                    Spacing = 2,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        PaymentIcon(value, enabled),
                        new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11, FontWeight = FontWeight.SemiBold }
                    }
                }
            };
            button.Click += (_, _) =>
            {
                method = value;
                RefreshMethodButtons();
                RefreshCart();
            };
            methodButtons[value] = button;
            return button;
        }

        void RefreshMethodButtons()
        {
            foreach (var pair in methodButtons)
            {
                var active = string.Equals(pair.Key, method, StringComparison.OrdinalIgnoreCase);
                pair.Value.Background = active ? Brush("#1AFFFFFF") : Brushes.Transparent;
                pair.Value.BorderBrush = Brushes.Transparent;
            }
            saleSplitGrid.IsVisible = method == "COMPOSITE";
            if (method == "COMPOSITE" && SaleTotal() > 0)
                SeedSaleSplitAmounts(SaleTotal());
        }

        decimal SaleTotal() => saleCartItems.Sum(x => x.Total);

        string SaleAmountText(decimal value)
        {
            return Math.Round(value, 2).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        void SeedSaleSplitAmounts(decimal total)
        {
            if (!string.IsNullOrWhiteSpace(saleCashBox.Text) || !string.IsNullOrWhiteSpace(saleCardBox.Text))
                return;

            saleSplitSyncing = true;
            try
            {
                var card = Math.Round(total / 2m, 2);
                var cash = total - card;
                saleCashBox.Text = SaleAmountText(cash);
                saleCardBox.Text = SaleAmountText(card);
            }
            finally
            {
                saleSplitSyncing = false;
            }
        }

        void SyncSaleSplitFromCash()
        {
            var total = SaleTotal();
            if (saleSplitSyncing || method != "COMPOSITE" || total <= 0) return;
            if (!TryParseMoney(saleCashBox.Text, out var cash)) return;

            saleSplitSyncing = true;
            try
            {
                var card = Math.Max(0m, total - cash);
                saleCardBox.Text = SaleAmountText(card);
            }
            finally
            {
                saleSplitSyncing = false;
            }
        }

        void SyncSaleSplitFromCard()
        {
            var total = SaleTotal();
            if (saleSplitSyncing || method != "COMPOSITE" || total <= 0) return;
            if (!TryParseMoney(saleCardBox.Text, out var card)) return;

            saleSplitSyncing = true;
            try
            {
                var cash = Math.Max(0m, total - card);
                saleCashBox.Text = SaleAmountText(cash);
            }
            finally
            {
                saleSplitSyncing = false;
            }
        }

        saleCashBox.TextChanged += (_, _) => SyncSaleSplitFromCash();
        saleCardBox.TextChanged += (_, _) => SyncSaleSplitFromCard();

        void RefreshCart()
        {
            cartContent.Children.Clear();
            if (saleCartItems.Count == 0)
            {
                cartContent.Children.Add(new TextBlock
                {
                    Text = "Выберите тарифы, товары или услуги,\nчтобы добавить их в корзину",
                    Foreground = Brush("#8f9aad"),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
                discountValue.Text = activeClient?.DiscountText ?? "—";
                finishValue.Text = SaleFinishText();
                payButton.Content = "Оплатить 0 ₽";
                payButton.IsEnabled = false;
                saleCashBox.Text = "";
                saleCardBox.Text = "";
                return;
            }

            var cartStack = new StackPanel { Spacing = 10 };
            var lastTariffItem = saleCartItems.LastOrDefault(x => x.Type == "TARIFF");
            foreach (var item in saleCartItems.ToList())
                cartStack.Children.Add(SaleCartRow(item, item.Type != "TARIFF" || ReferenceEquals(item, lastTariffItem)));

            cartContent.Children.Add(new ScrollViewer
            {
                Content = cartStack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            });

            discountValue.Text = activeClient?.DiscountText ?? "—";
            finishValue.Text = SaleFinishText();
            payButton.Content = "Оплатить " + MoneyText(SaleTotal());
            payButton.IsEnabled = true;

            if (method == "COMPOSITE")
            {
                saleCashBox.Text = "";
                saleCardBox.Text = "";
                SeedSaleSplitAmounts(SaleTotal());
            }
        }

        string SaleFinishText()
        {
            var tariffSeconds = saleCartItems
                .Where(x => x.Type == "TARIFF")
                .Sum(x => Math.Max(0, x.DurationSeconds) * Math.Max(1, x.Amount));

            if (tariffSeconds <= 0)
                return record.HasSession ? ActiveSessionDetailedEndText(record) : "—";

            var baseTime = record.HasSession && record.SessionEndsAtUtc.HasValue
                ? record.SessionEndsAtUtc.Value.ToLocalTime()
                : DateTime.Now;
            return baseTime.AddSeconds(tariffSeconds).ToString("d MMMM, HH:mm:ss", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
        }

        Border SaleCartRow(CartItem item, bool canRemove)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var details = item.Type == "TARIFF"
                ? FormatTariffDuration(item.DurationSeconds)
                : item.Amount + " × " + MoneyText(item.UnitCost);
            grid.Children.Add(new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = item.Title, Foreground = Brush("#f5f7ff"), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = details, Foreground = Brush("#80FFFFFF"), FontSize = 12 }
                }
            });

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };
            right.Children.Add(new TextBlock { Text = MoneyText(item.Total), Foreground = Brush("#f5f7ff"), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            if (canRemove)
                right.Children.Add(SaleIconButton("×", async () => await RemoveSaleCartItemAsync(item)));
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            return new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Brush("#303a48"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 14),
                Child = grid
            };
        }

        Button SaleIconButton(string text, Func<Task> action)
        {
            var button = new Button
            {
                Content = text,
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Foreground = Brush("#8f9aad"),
                FontWeight = FontWeight.SemiBold
            };
            button.Click += async (_, _) => await action();
            return button;
        }

        int SelectedTariffSeconds()
        {
            return saleCartItems
                .Where(x => x.Type == "TARIFF")
                .Sum(x => Math.Max(0, x.DurationSeconds) * Math.Max(1, x.Amount));
        }

        async Task RefreshAvailableTariffsAsync()
        {
            var refreshVersion = ++tariffRefreshVersion;
            var selectedSeconds = SelectedTariffSeconds();
            var shiftSeconds = initialTariffShiftSeconds + selectedSeconds;
            saleStatus.Text = "Обновляю тарифы на рассчитанное время окончания...";

            try
            {
                var refreshed = await service.LoadTariffsAsync(
                    config,
                    record.HostId,
                    activeClient?.Uuid ?? record.ClientUuid,
                    shiftSeconds > 0 ? shiftSeconds : null,
                    CancellationToken.None);

                if (refreshVersion != tariffRefreshVersion)
                    return;

                availableTariffs.Clear();
                availableTariffs.AddRange(refreshed);
                saleStatus.Text = "";
                RenderSaleRows();
            }
            catch (Exception ex)
            {
                if (refreshVersion != tariffRefreshVersion)
                    return;
                saleStatus.Text = "Не удалось обновить доступные тарифы: " + ex.Message;
            }
        }

        async Task RemoveSaleCartItemAsync(CartItem item)
        {
            if (item.Type == "TARIFF")
            {
                var lastTariff = saleCartItems.LastOrDefault(x => x.Type == "TARIFF");
                if (!ReferenceEquals(item, lastTariff))
                    return;
            }

            saleCartItems.Remove(item);
            var lastSelectedTariff = saleCartItems.LastOrDefault(x => x.Type == "TARIFF");
            pickedTariff = lastSelectedTariff == null
                ? null
                : new TariffInfo
                {
                    Id = lastSelectedTariff.EntityId,
                    Title = lastSelectedTariff.Title,
                    Cost = lastSelectedTariff.UnitCost,
                    Duration = lastSelectedTariff.DurationSeconds
                };
            saleCashBox.Text = "";
            saleCardBox.Text = "";
            RefreshCart();

            if (item.Type == "TARIFF")
                await RefreshAvailableTariffsAsync();
            else
                RenderSaleRows();
        }

        Border TariffHeader()
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,120,128,128,130") };
            AddSaleHeaderCell(grid, 0, "Название");
            AddSaleHeaderCell(grid, 1, "Стоимость");
            AddSaleHeaderCell(grid, 2, "Действует с");
            AddSaleHeaderCell(grid, 3, "Действует по");
            AddSaleHeaderCell(grid, 4, "Длительность");
            return new Border
            {
                Background = Brush("#161c23"),
                BorderBrush = Brush("#334052"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(28, 16),
                Child = grid
            };
        }

        Border TariffRow(TariffInfo tariff)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,120,128,128,130") };
            AddSaleCell(grid, 0, tariff.Title, Brush("#f5f7ff"), true);
            AddSaleCell(grid, 1, MoneyText(tariff.Cost), Brush("#f5f7ff"), true);
            AddSaleCell(grid, 2, string.IsNullOrWhiteSpace(tariff.TimeFrom) ? "—" : tariff.TimeFrom, Brush("#f5f7ff"), true);
            AddSaleCell(grid, 3, string.IsNullOrWhiteSpace(tariff.TimeTo) ? "—" : tariff.TimeTo, Brush("#f5f7ff"), true);
            AddSaleCell(grid, 4, tariff.DurationText, Brush("#f5f7ff"), true);

            var row = new Border
            {
                Background = Brush("#161c23"),
                BorderBrush = Brush("#161c23"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(28, 15),
                Child = grid
            };
            row.PointerPressed += async (_, _) => await AddTariffToSaleCartAsync(tariff);
            return row;
        }

        async Task AddTariffToSaleCartAsync(TariffInfo tariff)
        {
            saleCartItems.Add(new CartItem
            {
                Type = "TARIFF",
                EntityId = tariff.Id,
                HostId = record.HostId,
                DurationSeconds = tariff.Duration,
                Title = tariff.Title,
                Amount = 1,
                UnitCost = tariff.Cost
            });
            pickedTariff = tariff;
            saleCashBox.Text = "";
            saleCardBox.Text = "";
            RefreshCart();
            await RefreshAvailableTariffsAsync();
        }

        void AddShopItemToSaleCart(ShopItem item)
        {
            var existing = saleCartItems.FirstOrDefault(x => x.Type == item.Type && x.EntityId == item.EntityId);
            if (existing != null)
                existing.Amount += 1;
            else
                saleCartItems.Add(new CartItem
                {
                    Type = item.Type,
                    EntityId = item.EntityId,
                    Title = item.Title,
                    Amount = 1,
                    UnitCost = item.Cost
                });
            saleCashBox.Text = "";
            saleCardBox.Text = "";
            RenderSaleRows();
            RefreshCart();
        }

        Border ShopHeader(string mode)
        {
            var grid = mode switch
            {
                "GOOD" => new Grid { ColumnDefinitions = new ColumnDefinitions("*,150,110,110") },
                "COMBO" => new Grid { ColumnDefinitions = new ColumnDefinitions("*,110,110") },
                _ => new Grid { ColumnDefinitions = new ColumnDefinitions("*,130") }
            };

            AddSaleHeaderCell(grid, 0, "Название");
            if (mode == "GOOD")
            {
                AddSaleHeaderCell(grid, 1, "Группа");
                AddSaleHeaderCell(grid, 2, "Количество");
                AddSaleHeaderCell(grid, 3, "Стоимость");
            }
            else if (mode == "COMBO")
            {
                AddSaleHeaderCell(grid, 1, "Количество");
                AddSaleHeaderCell(grid, 2, "Стоимость");
            }
            else
            {
                AddSaleHeaderCell(grid, 1, "Стоимость");
            }

            return new Border
            {
                Background = Brush("#161c23"),
                BorderBrush = Brush("#334052"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(28, 16),
                Child = grid
            };
        }

        Border ShopRow(ShopItem item)
        {
            var active = saleCartItems.Any(x => x.Type == item.Type && x.EntityId == item.EntityId);
            var grid = item.Type switch
            {
                "GOOD" => new Grid { ColumnDefinitions = new ColumnDefinitions("*,150,110,110") },
                "COMBO" => new Grid { ColumnDefinitions = new ColumnDefinitions("*,110,110") },
                _ => new Grid { ColumnDefinitions = new ColumnDefinitions("*,130") }
            };

            AddSaleCell(grid, 0, item.Title, Brush("#f5f7ff"), true);
            if (item.Type == "GOOD")
            {
                AddSaleCell(grid, 1, string.IsNullOrWhiteSpace(item.GroupTitle) ? "—" : item.GroupTitle, Brush("#8f9aad"), true);
                AddSaleCell(grid, 2, item.StockText, Brush("#f5f7ff"), true);
                AddSaleCell(grid, 3, MoneyText(item.Cost), Brush("#f5f7ff"), true);
            }
            else if (item.Type == "COMBO")
            {
                AddSaleCell(grid, 1, item.StockText, Brush("#f5f7ff"), true);
                AddSaleCell(grid, 2, MoneyText(item.Cost), Brush("#f5f7ff"), true);
            }
            else
            {
                AddSaleCell(grid, 1, MoneyText(item.Cost), Brush("#f5f7ff"), true);
            }

            var row = new Border
            {
                Background = active ? Brush("#0FFFFFFF") : Brush("#161c23"),
                BorderBrush = active ? Brush("#2f8cff") : Brush("#161c23"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(28, 15),
                Child = grid
            };
            row.PointerPressed += (_, _) => AddShopItemToSaleCart(item);
            return row;
        }

        void RenderSaleRows()
        {
            saleRows.Children.Clear();
            var query = (searchBox.Text ?? "").Trim();
            if (saleMode == "TARIFF")
            {
                saleRows.Children.Add(TariffHeader());
                var filtered = availableTariffs
                    .Where(x => string.IsNullOrWhiteSpace(query) || x.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (filtered.Count == 0)
                {
                    saleRows.Children.Add(new Border
                    {
                        Padding = new Thickness(28, 20),
                        Background = Brush("#161c23"),
                        Child = new TextBlock { Text = "Тарифы не найдены", Foreground = Brush("#8f9aad") }
                    });
                    return;
                }

                foreach (var tariff in filtered)
                    saleRows.Children.Add(TariffRow(tariff));
                return;
            }

            saleRows.Children.Add(ShopHeader(saleMode));
            var shopFiltered = shopItems
                .Where(x => string.Equals(x.Type, saleMode, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.IsNullOrWhiteSpace(query) || x.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || x.GroupTitle.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Title)
                .ToList();
            if (shopFiltered.Count == 0)
            {
                saleRows.Children.Add(new Border
                {
                    Padding = new Thickness(28, 20),
                    Background = Brush("#161c23"),
                    Child = new TextBlock { Text = SaleModeEmptyText(saleMode), Foreground = Brush("#8f9aad") }
                });
                return;
            }

            foreach (var item in shopFiltered)
                saleRows.Children.Add(ShopRow(item));
        }

        string SaleModeEmptyText(string mode)
        {
            return mode switch
            {
                "GOOD" => "Товары не найдены",
                "SERVICE" => "Услуги не найдены",
                "COMBO" => "Комбо-наборы не найдены",
                _ => "Поиск ничего не нашёл"
            };
        }

        payButton.Click += (_, _) =>
        {
            if (saleCartItems.Count == 0)
                return;

            var total = SaleTotal();
            var cardSum = 0m;
            if (method == "COMPOSITE")
            {
                var hasCash = TryParseMoney(saleCashBox.Text, out var cashSum);
                var hasCard = TryParseMoney(saleCardBox.Text, out cardSum);
                var sum = Math.Round(cashSum + cardSum, 2);
                var expected = Math.Round(total, 2);
                if (!hasCash || !hasCard || cashSum <= 0 || cardSum <= 0 || sum != expected)
                {
                    saleStatus.Text = "Для разделённой оплаты заполните наличные и карту так, чтобы сумма была " + MoneyText(total) + ".";
                    return;
                }
            }

            var clientQuery = activeClient != null || HasLoggedInClient(record) || record.HasSession
                ? activeQuery
                : (clientBox.Text ?? "");
            dialog.Close(new TariffSaleResult
            {
                Tariff = pickedTariff,
                Items = saleCartItems.Select(x => x.Copy()).ToList(),
                ClientQuery = clientQuery,
                ClientUuid = activeClient?.Uuid ?? record.ClientUuid ?? (clientBox.Text == selectedClient?.Display ? selectedClient?.Uuid ?? "" : ""),
                Method = method,
                CardSum = cardSum
            });
        };

        searchBox.TextChanged += (_, _) => RenderSaleRows();

        var tabs = new Border
        {
            Background = Brush("#1AFFFFFF"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    TabButton("Тарифы", "TARIFF"),
                    TabButton("Товары", "GOOD"),
                    TabButton("Услуги", "SERVICE"),
                    TabButton("Комбо-наборы", "COMBO")
                }
            }
        };

        var centerTop = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,290") };
        centerTop.Children.Add(tabs);
        Grid.SetColumn(searchBox, 2);
        centerTop.Children.Add(searchBox);

        var tariffTable = new Border
        {
            Background = Brush("#161c23"),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = new ScrollViewer
            {
                Content = saleRows,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            }
        };

        var center = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 14,
            Children = { centerTop, tariffTable }
        };
        Grid.SetRow(tariffTable, 1);

        var paymentGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = 8,
            Children =
            {
                MethodButton("Наличные", "CASH"),
                MethodButton("Карта", "CARD"),
                MethodButton("Депозит", "DEPOSIT", canUseDeposit),
                MethodButton("Разделить", "COMPOSITE")
            }
        };
        Grid.SetColumn(paymentGrid.Children[1], 1);
        Grid.SetColumn(paymentGrid.Children[2], 2);
        Grid.SetColumn(paymentGrid.Children[3], 3);

        var paymentTabs = new Border
        {
            Background = Brush("#1AFFFFFF"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = paymentGrid
        };

        var rightGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children =
            {
                cartContent,
                new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        SaleInfoLine("Скидка", discountValue),
                        SaleInfoLine("Время окончания", finishValue),
                        new Border { Height = 1, Background = Brush("#303a48") },
                        paymentTabs,
                        saleSplitGrid,
                        payButton,
                        saleStatus
                    }
                }
            }
        };
        Grid.SetRow(rightGrid.Children[1], 1);
        var right = SalePanel(rightGrid, new Thickness(14));

        var left = BuildSaleLeftPanel(record, activeClient, clientBox, dialog);
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("318,*,324"),
            ColumnSpacing = 16,
            Children = { left, center, right }
        };
        Grid.SetColumn(center, 1);
        Grid.SetColumn(right, 2);

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock
        {
            Text = "Продажа для " + record.Name,
            Foreground = Brush("#f5f7ff"),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var close = new Button
        {
            Content = "×",
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = Brush("#f5f7ff"),
            FontSize = 20
        };
        close.Click += (_, _) => dialog.Close(null);
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var shell = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 16,
            Children = { header, body }
        };
        Grid.SetRow(body, 1);

        dialog.Content = new Border
        {
            Background = Brush("#0d1217"),
            BorderBrush = Brush("#33FFFFFF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(32),
            Child = shell
        };

        RefreshModeButtons();
        RenderSaleRows();
        RefreshMethodButtons();
        RefreshCart();

        return await dialog.ShowDialog<TariffSaleResult?>(this);
    }

    private Control BuildSaleLeftPanel(PcRecord record, ClientInfo? activeClient, TextBox clientBox, Window dialog)
    {
        var discountBox = new ComboBox
        {
            Height = 44,
            ItemsSource = new[] { "Скидка" },
            SelectedIndex = 0,
            Background = Brush("#171f29"),
            Foreground = Brush("#8f9aad"),
            BorderBrush = Brush("#253143")
        };
        var promoBox = SaleInput("Промокод");
        var promoButton = new Button
        {
            Content = "Применить",
            Height = 44,
            Background = Brush("#171f29"),
            BorderBrush = Brush("#253143"),
            Foreground = Brush("#6f7a8f"),
            FontWeight = FontWeight.SemiBold
        };
        promoButton.Click += (_, _) => ShowToast("Промокод", "Промокоды подключим отдельным модулем SmartShell API.");

        var bottom = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                discountBox,
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,128"),
                    ColumnSpacing = 8,
                    Children = { promoBox, promoButton }
                }
            }
        };
        Grid.SetColumn(promoButton, 1);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        if (record.HasSession || HasLoggedInClient(record) || activeClient != null)
        {
            root.Children.Add(BuildSaleActiveClientCard(record, activeClient, dialog));
        }
        else
        {
            var emptyClient = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    clientBox,
                    new TextBlock
                    {
                        Text = "Начните вводить никнейм или телефон\nавторизованного клиента.",
                        Foreground = Brush("#8f9aad"),
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(12, 2, 12, 0)
                    }
                }
            };
            root.Children.Add(emptyClient);
        }

        Grid.SetRow(bottom, 2);
        root.Children.Add(bottom);
        return root;
    }

    private Control BuildSaleActiveClientCard(PcRecord record, ClientInfo? activeClient, Window dialog)
    {
        var display = activeClient?.Nickname;
        if (string.IsNullOrWhiteSpace(display)) display = activeClient?.Phone;
        if (string.IsNullOrWhiteSpace(display)) display = record.ClientName;
        if (string.IsNullOrWhiteSpace(display)) display = record.ClientPhone;
        if (string.IsNullOrWhiteSpace(display)) display = "Клиент";

        var sub = activeClient?.Phone;
        if (string.IsNullOrWhiteSpace(sub)) sub = record.ClientPhone;
        if (string.IsNullOrWhiteSpace(sub)) sub = "Телефон не указан";
        var balanceText = CopyableClientText(activeClient?.BalanceText ?? "—", "#a9b2c5", 14, true);
        var bonusText = CopyableClientText(
            activeClient == null || activeClient.Bonus <= 0 ? "Нет бонусов" : activeClient.BonusText,
            "#a9b2c5",
            14,
            true);

        var depositButton = SalePrimaryButton("₽  Пополнить депозит");
        depositButton.Height = 44;
        depositButton.Click += async (_, _) =>
        {
            var client = activeClient;
            if (client == null || string.IsNullOrWhiteSpace(client.Uuid))
            {
                var query = ActiveClientQuery(record);
                if (!string.IsNullOrWhiteSpace(query))
                    client = await service.FindFirstClientAsync(config, query, CancellationToken.None);
            }

            if (client == null || string.IsNullOrWhiteSpace(client.Uuid))
            {
                ShowToast("Пополнение", "Не удалось определить клиента для пополнения.");
                return;
            }

            await ShowDepositTopUpDialogAsync(client, dialog);
            activeClient ??= client;
            balanceText.Text = client.BalanceText;
            bonusText.Text = client.Bonus <= 0 ? "Нет бонусов" : client.BonusText;
        };

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                SalePanel(new StackPanel
                {
                    Spacing = 14,
                    Children =
                    {
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 12,
                            Children =
                            {
                                new Border
                                {
                                    Width = 42,
                                    Height = 42,
                                    CornerRadius = new CornerRadius(21),
                                    Background = Brush("#2a303a"),
                                    Child = new TextBlock
                                    {
                                        Text = "♙",
                                        Foreground = Brush("#8f9aad"),
                                        HorizontalAlignment = HorizontalAlignment.Center,
                                        VerticalAlignment = VerticalAlignment.Center,
                                        FontSize = 18
                                    }
                                },
                                new StackPanel
                                {
                                    VerticalAlignment = VerticalAlignment.Center,
                                    Children =
                                    {
                                        CopyableClientText(display, "#f5f7ff", 16, true),
                                        CopyableClientText(sub, "#8f9aad", 13, true)
                                    }
                                }
                            }
                        },
                        depositButton,
                        new Border
                        {
                            Background = Brush("#102a4d"),
                            CornerRadius = new CornerRadius(10),
                            Padding = new Thickness(8, 4),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            Child = new TextBlock { Text = "Клиент", Foreground = Brush("#2f8cff"), FontSize = 12, FontWeight = FontWeight.SemiBold }
                        },
                        new Border { Height = 1, Background = Brush("#303a48") },
                        SaleMetricLine("₽", balanceText),
                        SaleMetricLine("★", bonusText),
                        SaleMetricLine("%", activeClient?.DiscountText ?? "Нет скидки")
                    }
                }, new Thickness(14)),
                SaleAccordionPanel("▣", "Подробнее о клиенте", ClientDetailsText(activeClient)),
                SaleAccordionPanel("▤", "Комментарий о клиенте", ClientCommentText(activeClient), activeClient?.LastCommentImportant == true),
                SaleAccordionPanel("◎", "Абонемент", activeClient?.SubscriptionText ?? "Нет активного абонемента")
            }
        };
    }

    private async Task ShowDepositTopUpDialogAsync(ClientInfo client, Window owner)
    {
        if (!string.IsNullOrWhiteSpace(client.Uuid))
        {
            try
            {
                var loaded = await service.LoadClientDetailsAsync(config, client.Uuid, CancellationToken.None);
                if (loaded != null)
                {
                    client.Deposit = loaded.Deposit;
                    client.Bonus = loaded.Bonus;
                    client.PersonalDiscount = loaded.PersonalDiscount;
                    client.DiscountText = loaded.DiscountText;
                }
            }
            catch
            {
                // Основные данные клиента уже загружены; окно пополнения остаётся доступным.
            }
        }

        var dialog = new ModalWindow
        {
            Title = "Пополнение счёта",
            Width = 731,
            Height = 744,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#0d1217"),
            SystemDecorations = SystemDecorations.None
        };

        var method = "CASH";
        var amountBox = SaleInput("Сумма пополнения");
        var bonusBox = SaleInput("Сумма бонусов (необязательно)");
        var bonusCommentBox = SaleInput("Комментарий к бонусам");
        bonusCommentBox.Text = "ЛОТ";
        bonusCommentBox.MaxLength = 255;
        var promoBox = SaleInput("Промокод");
        var cashBox = SaleInput("Наличными");
        var cardBox = SaleInput("Картой");
        var status = new TextBlock
        {
            Foreground = Brush("#f5a524"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        var payButton = SalePrimaryButton("Оплатить 0 ₽");
        payButton.IsEnabled = false;
        var splitGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8,
            IsVisible = false,
            Children = { cashBox, cardBox }
        };
        Grid.SetColumn(cardBox, 1);

        var methodButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        var splitSyncing = false;

        decimal Amount()
        {
            return TryParseMoney(amountBox.Text, out var value) && value > 0 ? value : 0m;
        }

        decimal BonusAmount()
        {
            return TryParseMoney(bonusBox.Text, out var value) && value > 0 ? value : 0m;
        }

        string AmountText(decimal value)
        {
            return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        void SeedSplit()
        {
            var total = Amount();
            if (total <= 0 || !string.IsNullOrWhiteSpace(cashBox.Text) || !string.IsNullOrWhiteSpace(cardBox.Text))
                return;

            splitSyncing = true;
            try
            {
                var card = Math.Round(total / 2m, 2);
                cashBox.Text = AmountText(total - card);
                cardBox.Text = AmountText(card);
            }
            finally
            {
                splitSyncing = false;
            }
        }

        void RefreshPay()
        {
            var total = Amount();
            var bonus = BonusAmount();
            payButton.Content = total > 0
                ? "Пополнить " + MoneyText(total) + (bonus > 0 ? "  +  ★ " + BonusText(bonus) : "")
                : bonus > 0 ? "Начислить ★ " + BonusText(bonus) : "Пополнить 0 ₽";
            payButton.IsEnabled = total > 0 || bonus > 0;
            splitGrid.IsVisible = total > 0 && method == "COMPOSITE";
            foreach (var pair in methodButtons)
            {
                var active = string.Equals(pair.Key, method, StringComparison.OrdinalIgnoreCase);
                pair.Value.Background = active ? Brush("#1AFFFFFF") : Brushes.Transparent;
                pair.Value.BorderBrush = Brushes.Transparent;
            }

            if (method == "COMPOSITE")
                SeedSplit();
            else
            {
                cashBox.Text = "";
                cardBox.Text = "";
            }
        }

        void SyncSplitFromCash()
        {
            if (splitSyncing || method != "COMPOSITE") return;
            var total = Amount();
            if (total <= 0 || !TryParseMoney(cashBox.Text, out var cash)) return;
            var card = total - cash;
            if (card < 0) return;
            splitSyncing = true;
            try { cardBox.Text = AmountText(card); }
            finally { splitSyncing = false; }
        }

        void SyncSplitFromCard()
        {
            if (splitSyncing || method != "COMPOSITE") return;
            var total = Amount();
            if (total <= 0 || !TryParseMoney(cardBox.Text, out var card)) return;
            var cash = total - card;
            if (cash < 0) return;
            splitSyncing = true;
            try { cashBox.Text = AmountText(cash); }
            finally { splitSyncing = false; }
        }

        Button DepositMethodButton(string text, string value)
        {
            var button = new Button
            {
                Height = 60,
                Padding = new Thickness(8, 6),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Foreground = Brush("#f5f7ff"),
                Content = new StackPanel
                {
                    Spacing = 2,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        PaymentIcon(value),
                        new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11, FontWeight = FontWeight.SemiBold }
                    }
                }
            };
            button.Click += (_, _) =>
            {
                method = value;
                status.Text = "";
                RefreshPay();
            };
            methodButtons[value] = button;
            return button;
        }

        amountBox.TextChanged += (_, _) =>
        {
            cashBox.Text = "";
            cardBox.Text = "";
            RefreshPay();
        };
        bonusBox.TextChanged += (_, _) => RefreshPay();
        cashBox.TextChanged += (_, _) => SyncSplitFromCash();
        cardBox.TextChanged += (_, _) => SyncSplitFromCard();

        payButton.Click += async (_, _) =>
        {
            var amount = Amount();
            var bonusAmount = BonusAmount();
            if (amount <= 0 && bonusAmount <= 0)
            {
                status.Text = "Введите сумму пополнения или количество бонусов.";
                return;
            }
            var bonusComment = string.IsNullOrWhiteSpace(bonusCommentBox.Text)
                ? "ЛОТ"
                : bonusCommentBox.Text.Trim();

            var cardSum = 0m;
            if (amount > 0 && method == "COMPOSITE")
            {
                var hasCash = TryParseMoney(cashBox.Text, out var cash);
                var hasCard = TryParseMoney(cardBox.Text, out cardSum);
                if (!hasCash || !hasCard || cash <= 0 || cardSum <= 0 || Math.Abs(cash + cardSum - amount) > 0.01m)
                {
                    status.Text = "Наличка и карта должны вместе давать сумму пополнения.";
                    return;
                }
            }

            try
            {
                payButton.IsEnabled = false;
                status.Text = amount > 0 ? "Создаю пополнение..." : "Начисляю бонусы...";
                if (amount > 0)
                {
                    var item = new CartItem
                    {
                        Type = "DEPOSIT",
                        Title = "Пополнение баланса",
                        Amount = 1,
                        UnitCost = amount,
                        ClientUuid = client.Uuid,
                        ClientDisplay = client.Display
                    };
                    var input = BuildPaymentInput(new List<CartItem> { item }, client.Uuid, method, cardSum);
                    await service.ValidateAndCreatePaymentAsync(config, input, CancellationToken.None);
                    client.Deposit += amount;
                }
                if (bonusAmount > 0)
                {
                    var bonusItem = new CartItem
                    {
                        Type = "BONUS",
                        Title = "Начисление бонусов",
                        Amount = 1,
                        BonusAmount = bonusAmount,
                        ClientUuid = client.Uuid,
                        ClientDisplay = client.Display,
                        Comment = bonusComment
                    };
                    var bonusInput = BuildPaymentInput(
                        new List<CartItem> { bonusItem },
                        client.Uuid,
                        "BONUS",
                        0m,
                        bonusComment);
                    await service.ValidateAndCreatePaymentAsync(config, bonusInput, CancellationToken.None);
                    client.Bonus += bonusAmount;
                }

                var resultText = client.Display;
                if (amount > 0)
                    resultText += " на " + MoneyText(amount);
                if (bonusAmount > 0)
                    resultText += (amount > 0 ? ", " : ": ") + "бонусы ★ +" + BonusText(bonusAmount) + " (" + bonusComment + ")";
                ShowToast("Пополнение", resultText);
                dialog.Close(true);
            }
            catch (Exception ex)
            {
                status.Text = "Ошибка пополнения: " + ex.Message;
                payButton.IsEnabled = Amount() > 0 || BonusAmount() > 0;
            }
        };

        var close = new Button
        {
            Content = "×",
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = Brush("#f5f7ff"),
            FontSize = 20
        };
        close.Click += (_, _) => dialog.Close(false);

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock
        {
            Text = "Пополнение счёта",
            Foreground = Brush("#f5f7ff"),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var clientPanel = BuildDepositClientPanel(client, async () =>
        {
            await ShowClientEditDialogAsync(client, dialog);
        });

        var promoGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,142"),
            ColumnSpacing = 8,
            Children =
            {
                promoBox,
                new Button
                {
                    Content = "Применить",
                    Height = 48,
                    Background = Brush("#1A2C87FD"),
                    BorderBrush = Brushes.Transparent,
                    Foreground = Brush("#2f8cff"),
                    FontWeight = FontWeight.SemiBold
                }
            }
        };
        Grid.SetColumn(promoGrid.Children[1], 1);

        var paymentGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 8,
            Children =
            {
                DepositMethodButton("Наличные", "CASH"),
                DepositMethodButton("Карта", "CARD"),
                DepositMethodButton("Разделить", "COMPOSITE")
            }
        };
        Grid.SetColumn(paymentGrid.Children[1], 1);
        Grid.SetColumn(paymentGrid.Children[2], 2);

        var paymentTabs = new Border
        {
            Background = Brush("#1AFFFFFF"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = paymentGrid
        };

        var right = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto"),
            RowSpacing = 12,
            Children =
            {
                amountBox,
                bonusBox,
                bonusCommentBox,
                promoGrid,
                new Border(),
                new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        paymentTabs,
                        splitGrid,
                        payButton,
                        status
                    }
                }
            }
        };
        Grid.SetRow(bonusBox, 1);
        Grid.SetRow(bonusCommentBox, 2);
        Grid.SetRow(promoGrid, 3);
        Grid.SetRow(right.Children[4], 4);
        Grid.SetRow(right.Children[5], 5);

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("318,315"),
            ColumnSpacing = 32,
            Children = { clientPanel, right }
        };
        Grid.SetColumn(right, 1);

        var shell = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 28,
            Children = { header, body }
        };
        Grid.SetRow(body, 1);

        dialog.Content = new Border
        {
            Background = Brush("#0d1217"),
            BorderBrush = Brush("#33FFFFFF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(32),
            Child = shell
        };

        RefreshPay();
        await dialog.ShowDialog<bool?>(owner);
    }

    private async Task ShowClientEditDialogAsync(ClientInfo client, Window owner)
    {
        string accessText;
        try
        {
            apiAccessInfo = await service.LoadApiAccessInfoAsync(config, CancellationToken.None);
            accessText = apiAccessInfo.CanManageClients
                ? $"Доступ подтверждён: {apiAccessInfo.DisplayName} · {apiAccessInfo.RoleText}"
                : $"Токен: {apiAccessInfo.DisplayName} · {apiAccessInfo.RoleText}. Нет права client_manage.";
        }
        catch (Exception ex)
        {
            apiAccessInfo = null;
            accessText = "Не удалось проверить права токена: " + ex.Message;
        }
        var canManageClients = apiAccessInfo?.CanManageClients == true;

        var dialog = new ModalWindow
        {
            Title = "Данные клиента",
            Width = 560,
            Height = 790,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#0d1217"),
            SystemDecorations = SystemDecorations.None
        };

        var depositBox = SaleInput("Текущий депозит");
        depositBox.Text = client.Deposit.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        var bonusBox = SaleInput("Текущие бонусы");
        bonusBox.Text = client.Bonus.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        var discountBox = SaleInput("Личная скидка, %");
        discountBox.Text = client.PersonalDiscount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        var status = new TextBlock
        {
            Foreground = Brush("#f5a524"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        var save = SalePrimaryButton("Сохранить");
        save.IsEnabled = canManageClients;
        var accessStatus = new TextBlock
        {
            Text = accessText,
            Foreground = canManageClients ? Brush("#38d996") : Brush("#f5a524"),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        var close = new Button
        {
            Content = "×",
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = Brush("#f5f7ff"),
            FontSize = 20
        };
        close.Click += (_, _) => dialog.Close(false);

        Border ProfileLine(string label, string value)
        {
            return new Border
            {
                Background = Brush("#161c23"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10),
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("150,*"),
                    Children =
                    {
                        new TextBlock { Text = label, Foreground = Brush("#8f9aad"), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock
                        {
                            Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
                            Foreground = Brush("#f5f7ff"),
                            FontWeight = FontWeight.SemiBold,
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            };
        }

        StackPanel LabeledInput(string label, TextBox input)
        {
            return new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = label,
                        Foreground = Brush("#8f9aad"),
                        FontSize = 12,
                        FontWeight = FontWeight.SemiBold
                    },
                    input
                }
            };
        }

        var profileLines = new[]
        {
            ProfileLine("Никнейм", client.Nickname),
            ProfileLine("Телефон", client.Phone),
            ProfileLine("Имя", string.Join(" ", new[] { client.LastName, client.FirstName, client.MiddleName }.Where(x => !string.IsNullOrWhiteSpace(x)))),
            ProfileLine("Дата рождения", client.BirthDateText)
        };
        foreach (var line in profileLines)
            Grid.SetColumn(((Grid)line.Child!).Children[1], 1);

        save.Click += async (_, _) =>
        {
            if (!canManageClients)
            {
                status.Text = "Для изменения данных нужен access_token менеджера или владельца с правом client_manage.";
                return;
            }
            if (!TryParseMoney(depositBox.Text, out var deposit) || deposit < 0 || deposit > 16000000m)
            {
                status.Text = "Депозит должен быть от 0 до 16 000 000.";
                return;
            }
            if (!TryParseMoney(bonusBox.Text, out var bonus) || bonus < 0 || bonus > 16000000m)
            {
                status.Text = "Бонусы должны быть от 0 до 16 000 000.";
                return;
            }
            if (!TryParseMoney(discountBox.Text, out var discount) || discount < 0 || discount > 100m)
            {
                status.Text = "Личная скидка должна быть от 0 до 100%.";
                return;
            }

            try
            {
                save.IsEnabled = false;
                status.Text = "Сохраняю данные в SmartShell...";
                if (deposit != client.Deposit)
                {
                    try
                    {
                        await service.SetClientDepositAsync(config, client.Uuid, deposit, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("Депозит: " + ex.Message, ex);
                    }
                    client.Deposit = deposit;
                }
                if (bonus != client.Bonus)
                {
                    try
                    {
                        await service.SetClientBonusAsync(config, client.Uuid, bonus, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("Бонусы: " + ex.Message, ex);
                    }
                    client.Bonus = bonus;
                }
                if (discount != client.PersonalDiscount)
                {
                    try
                    {
                        await service.SetClientDiscountAsync(config, client.Uuid, discount, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("Личная скидка: " + ex.Message, ex);
                    }
                    client.PersonalDiscount = discount;
                    client.DiscountText = discount > 0
                        ? discount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%"
                        : "Нет скидки";
                }

                ShowToast("Клиент", "Финансовые данные обновлены.");
                dialog.Close(true);
            }
            catch (Exception ex)
            {
                status.Text = "Не удалось сохранить: " + ex.Message;
                save.IsEnabled = true;
            }
        };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock
        {
            Text = "Данные клиента",
            Foreground = Brush("#f5f7ff"),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                header,
                new TextBlock
                {
                    Text = "Имя и телефон доступны только для чтения. Финансовые данные редактируются при наличии права управления клиентами.",
                    Foreground = Brush("#8f9aad"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 6)
                },
                accessStatus,
                profileLines[0],
                profileLines[1],
                profileLines[2],
                profileLines[3],
                new Border { Height = 1, Background = Brush("#303a48"), Margin = new Thickness(0, 6) },
                LabeledInput("Текущий депозит", depositBox),
                LabeledInput("Бонусный баланс", bonusBox),
                LabeledInput("Личная скидка, %", discountBox),
                status,
                save
            }
        };

        dialog.Content = new Border
        {
            Background = Brush("#0d1217"),
            BorderBrush = Brush("#33FFFFFF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(28),
            Child = content
        };

        await dialog.ShowDialog<bool?>(owner);
    }

    private Control BuildDepositClientPanel(ClientInfo client, Func<Task>? editClient = null, Func<Task>? topUpClient = null)
    {
        var name = !string.IsNullOrWhiteSpace(client.Nickname) ? client.Nickname : client.Phone;
        if (string.IsNullOrWhiteSpace(name)) name = "Клиент";
        var phone = string.IsNullOrWhiteSpace(client.Phone) ? client.Uuid : client.Phone;
        var balanceText = CopyableClientText(client.BalanceText, "#a9b2c5", 14, true);
        var bonusText = CopyableClientText(
            client.Bonus <= 0 ? "Нет бонусов" : client.BonusText,
            "#a9b2c5",
            14,
            true);
        var editButton = new Button
        {
            Content = "Редактировать данные",
            Height = 40,
            Background = Brush("#1A2C87FD"),
            BorderBrush = Brushes.Transparent,
            Foreground = Brush("#2f8cff"),
            FontWeight = FontWeight.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsVisible = editClient != null
        };
        if (editClient != null)
            editButton.Click += async (_, _) => await editClient();
        var topUpButton = SalePrimaryButton("▣  Пополнить депозит");
        topUpButton.IsVisible = topUpClient != null;
        if (topUpClient != null)
            topUpButton.Click += async (_, _) =>
            {
                await topUpClient();
                balanceText.Text = client.BalanceText;
                bonusText.Text = client.Bonus <= 0 ? "Нет бонусов" : client.BonusText;
            };

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                SalePanel(new StackPanel
                {
                    Spacing = 14,
                    Children =
                    {
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 12,
                            Children =
                            {
                                new Border
                                {
                                    Width = 42,
                                    Height = 42,
                                    CornerRadius = new CornerRadius(21),
                                    Background = Brush("#2a303a"),
                                    Child = new TextBlock
                                    {
                                        Text = "◉",
                                        Foreground = Brush("#8f9aad"),
                                        HorizontalAlignment = HorizontalAlignment.Center,
                                        VerticalAlignment = VerticalAlignment.Center,
                                        FontSize = 18
                                    }
                                },
                                new StackPanel
                                {
                                    VerticalAlignment = VerticalAlignment.Center,
                                    Children =
                                    {
                                        CopyableClientText(name, "#f5f7ff", 16, true),
                                        CopyableClientText(phone, "#8f9aad", 13, true)
                                    }
                                }
                            }
                        },
                        topUpButton,
                        new Border
                        {
                            Background = Brush("#102a4d"),
                            CornerRadius = new CornerRadius(10),
                            Padding = new Thickness(8, 4),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            Child = new TextBlock { Text = "Клиент", Foreground = Brush("#2f8cff"), FontSize = 12, FontWeight = FontWeight.SemiBold }
                        },
                        new Border { Height = 1, Background = Brush("#303a48") },
                        SaleMetricLine("₽", balanceText),
                        SaleMetricLine("★", bonusText),
                        SaleMetricLine("%", client.DiscountText),
                        editButton
                    }
                }, new Thickness(14)),
                SaleAccordionPanel("▣", "Подробнее о клиенте", ClientDetailsText(client)),
                SaleAccordionPanel("▤", "Комментарий о клиенте", ClientCommentText(client), client.LastCommentImportant),
                SaleAccordionPanel("◎", "Абонемент", client.SubscriptionText)
            }
        };
    }

    private Border SalePanel(Control child, Thickness padding)
    {
        return new Border
        {
            Background = PanelBrush("#161c23", 178),
            BorderBrush = Brush("#161c23"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = padding,
            Child = WithDragonWatermark(child, 132, 0.045)
        };
    }

    private TextBox SaleInput(string watermark)
    {
        return new TextBox
        {
            Watermark = watermark,
            Height = 48,
            Background = Brush("#161c23"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2f8cff"),
            SelectionBrush = Brush("#2f8cff")
        };
    }

    private Button SalePrimaryButton(string text)
    {
        return new Button
        {
            Content = text,
            Height = 48,
            Background = Brush("#2f8cff"),
            BorderBrush = Brush("#2f8cff"),
            Foreground = Brush("#f5f7ff"),
            FontWeight = FontWeight.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
    }

    private static Control PaymentIcon(string kind, bool enabled = true)
    {
        var stroke = enabled ? Brush("#f5f7ff") : Brush("#667085");
        var fill = enabled ? Brush("#14FFFFFF") : Brush("#0F667085");

        if (string.Equals(kind, "CASH", StringComparison.OrdinalIgnoreCase))
        {
            var grid = new Grid { Width = 24, Height = 18 };
            grid.Children.Add(new Border
            {
                Width = 22,
                Height = 14,
                CornerRadius = new CornerRadius(2),
                BorderBrush = stroke,
                BorderThickness = new Thickness(1.4),
                Background = fill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            grid.Children.Add(new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                BorderBrush = stroke,
                BorderThickness = new Thickness(1.2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            grid.Children.Add(new Border
            {
                Width = 2,
                Height = 7,
                CornerRadius = new CornerRadius(1),
                Background = stroke,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            });
            grid.Children.Add(new Border
            {
                Width = 2,
                Height = 7,
                CornerRadius = new CornerRadius(1),
                Background = stroke,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            });
            return grid;
        }

        if (string.Equals(kind, "CARD", StringComparison.OrdinalIgnoreCase))
        {
            var grid = new Grid { Width = 24, Height = 18 };
            grid.Children.Add(new Border
            {
                Width = 22,
                Height = 15,
                CornerRadius = new CornerRadius(3),
                BorderBrush = stroke,
                BorderThickness = new Thickness(1.4),
                Background = fill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            grid.Children.Add(new Border
            {
                Width = 18,
                Height = 3,
                CornerRadius = new CornerRadius(1),
                Background = stroke,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 0, 0)
            });
            grid.Children.Add(new Border
            {
                Width = 5,
                Height = 4,
                CornerRadius = new CornerRadius(1),
                BorderBrush = stroke,
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(5, 0, 0, 4)
            });
            return grid;
        }

        if (string.Equals(kind, "DEPOSIT", StringComparison.OrdinalIgnoreCase))
        {
            var grid = new Grid { Width = 24, Height = 18 };
            grid.Children.Add(new Border
            {
                Width = 22,
                Height = 15,
                CornerRadius = new CornerRadius(4),
                BorderBrush = stroke,
                BorderThickness = new Thickness(1.4),
                Background = fill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            grid.Children.Add(new Border
            {
                Width = 10,
                Height = 8,
                CornerRadius = new CornerRadius(3),
                BorderBrush = stroke,
                BorderThickness = new Thickness(1.2),
                Background = Brush("#0d1217"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 1, 0)
            });
            grid.Children.Add(new Border
            {
                Width = 2.5,
                Height = 2.5,
                CornerRadius = new CornerRadius(2),
                Background = stroke,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            });
            return grid;
        }

        if (string.Equals(kind, "BONUS", StringComparison.OrdinalIgnoreCase))
        {
            return new TextBlock
            {
                Text = "★",
                Width = 24,
                Height = 18,
                Foreground = stroke,
                FontSize = 16,
                FontWeight = FontWeight.SemiBold,
                TextAlignment = TextAlignment.Center
            };
        }

        return new TextBlock
        {
            Text = "÷",
            Width = 24,
            Height = 18,
            Foreground = stroke,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center
        };
    }

    private static Border SaleInfoLine(string titleText, TextBlock value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new TextBlock
        {
            Text = titleText,
            Foreground = Brush("#8f9aad"),
            FontWeight = FontWeight.SemiBold
        });
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        return new Border { Child = grid };
    }

    private static Control SaleMetricLine(string icon, string text)
    {
        return SaleMetricLine(icon, CopyableClientText(text, "#a9b2c5", 14, true));
    }

    private static Control SaleMetricLine(string icon, Control value)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = icon, Foreground = Brush("#2f8cff"), Width = 20, FontWeight = FontWeight.SemiBold },
                value
            }
        };
    }

    private static TextBox CopyableClientText(string text, string color, double fontSize = 14, bool bold = false, TextWrapping wrapping = TextWrapping.NoWrap)
    {
        return new TextBox
        {
            Text = text,
            IsReadOnly = true,
            IsTabStop = false,
            MinHeight = 0,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brush(color),
            FontSize = fontSize,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            TextWrapping = wrapping,
            SelectionBrush = Brush("#2f8cff"),
            SelectionForegroundBrush = Brush("#ffffff")
        };
    }

    private Control SaleAccordionPanel(string icon, string text, string content, bool important = false)
    {
        var arrow = new TextBlock
        {
            Text = "⌄",
            Foreground = Brush("#8f9aad"),
            VerticalAlignment = VerticalAlignment.Center
        };
        var button = new Button
        {
            Height = 40,
            Padding = new Thickness(12, 0),
            Background = Brush("#161c23"),
            BorderBrush = Brush("#161c23"),
            Foreground = Brush("#f5f7ff"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Children =
                {
                    new Border
                    {
                        Width = 22,
                        Height = 22,
                        CornerRadius = new CornerRadius(4),
                        Background = Brush("#29313d"),
                        Child = new TextBlock { Text = icon, Foreground = Brush("#8f9aad"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 }
                    },
                    new TextBlock { Text = text, Foreground = Brush("#f5f7ff"), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) },
                    arrow
                }
            }
        };
        var contentGrid = (Grid)button.Content!;
        Grid.SetColumn(contentGrid.Children[1], 1);
        Grid.SetColumn(contentGrid.Children[2], 2);

        var details = new Border
        {
            IsVisible = false,
            Background = Brush("#111820"),
            BorderBrush = important ? Brush("#f5a524") : Brush("#253143"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10),
            Child = CopyableClientText(
                string.IsNullOrWhiteSpace(content) ? "Нет данных" : content,
                important ? "#ffd58a" : "#a9b2c5",
                12,
                wrapping: TextWrapping.Wrap)
        };

        button.Click += (_, _) =>
        {
            details.IsVisible = !details.IsVisible;
            arrow.Text = details.IsVisible ? "⌃" : "⌄";
        };

        return new StackPanel
        {
            Spacing = 4,
            Children = { button, details }
        };
    }

    private static string ClientDetailsText(ClientInfo? client)
    {
        if (client == null)
            return "Дополнительные данные клиента не загружены.";

        var fullName = string.Join(" ", new[] { client.LastName, client.FirstName, client.MiddleName }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(fullName)) lines.Add("ФИО: " + fullName);
        if (!string.IsNullOrWhiteSpace(client.Nickname)) lines.Add("Логин: " + client.Nickname);
        if (!string.IsNullOrWhiteSpace(client.Phone)) lines.Add("Телефон: " + client.Phone);
        if (!string.IsNullOrWhiteSpace(client.GroupTitle)) lines.Add("Группа: " + client.GroupTitle);
        if (!string.IsNullOrWhiteSpace(client.RoleTitle)) lines.Add("Роль: " + client.RoleTitle);
        if (client.TotalHours > 0) lines.Add("Всего часов: " + client.TotalHours.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(client.LastActivityText)) lines.Add("Последняя активность: " + client.LastActivityText);
        if (!string.IsNullOrWhiteSpace(client.CreatedAtText)) lines.Add("Регистрация: " + client.CreatedAtText);
        if (!string.IsNullOrWhiteSpace(client.BirthDateText)) lines.Add("Дата рождения: " + client.BirthDateText);
        return lines.Count == 0 ? "Дополнительные данные отсутствуют." : string.Join(Environment.NewLine, lines);
    }

    private static string ClientCommentText(ClientInfo? client)
    {
        if (client == null || string.IsNullOrWhiteSpace(client.LastComment))
            return "Комментариев о клиенте нет.";
        return client.LastCommentImportant ? "Важный комментарий:\n" + client.LastComment : client.LastComment;
    }

    private static string FormatTariffDuration(int seconds)
    {
        if (seconds <= 0) return "Тариф";
        if (seconds % 3600 == 0) return seconds / 3600 + " ч.";
        if (seconds >= 3600) return seconds / 3600 + " ч. " + seconds % 3600 / 60 + " мин.";
        return Math.Max(1, seconds / 60) + " мин.";
    }

    private static void AddSaleHeaderCell(Grid grid, int column, string text)
    {
        AddSaleCell(grid, column, text + "  ⇅", Brush("#f5f7ff"), true);
    }

    private static void AddSaleCell(Grid grid, int column, string text, IBrush brush, bool semiBold)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = brush,
            FontWeight = semiBold ? FontWeight.SemiBold : FontWeight.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private static string ActiveSessionDetailedEndText(PcRecord record)
    {
        if (!record.SessionEndsAtUtc.HasValue)
            return "-";

        return record.SessionEndsAtUtc.Value.ToLocalTime().ToString("d MMMM, HH:mm:ss", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
    }

    private static string TariffFinishText(PcRecord record, TariffInfo tariff)
    {
        if (record.HasSession)
        {
            var active = ActiveSessionDetailedEndText(record);
            if (active != "-") return active;
        }

        var seconds = tariff.Duration > 0 ? tariff.Duration : 0;
        if (seconds <= 0) return "—";
        return DateTime.Now.AddSeconds(seconds).ToString("d MMMM, HH:mm:ss", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
    }

    private async Task CreateTariffPaymentAsync(PcRecord record, TariffSaleResult sale, IReadOnlyCollection<PcRecord>? targetHosts = null)
    {
        var items = sale.Items?.Where(x => x.Amount > 0 && x.UnitCost >= 0).Select(x => x.Copy()).ToList() ?? new List<CartItem>();
        if (items.Count == 0 && sale.Tariff != null)
        {
            items.Add(new CartItem
            {
                Type = "TARIFF",
                EntityId = sale.Tariff.Id,
                HostId = record.HostId,
                Title = sale.Tariff.Title,
                Amount = 1,
                UnitCost = sale.Tariff.Cost
            });
        }
        if (items.Count == 0) return;

        var saleHosts = (targetHosts ?? new[] { record })
            .Where(x => x.HostId > 0)
            .DistinctBy(x => x.HostId)
            .OrderBy(x => NaturalKey(x.Name))
            .ToList();
        if (saleHosts.Count > 1)
        {
            var tariffItems = items.Where(x => x.Type == "TARIFF").Select(x => x.Copy()).ToList();
            items.RemoveAll(x => x.Type == "TARIFF");
            foreach (var host in saleHosts)
            {
                foreach (var tariffItem in tariffItems)
                {
                    var copy = tariffItem.Copy();
                    copy.HostId = host.HostId;
                    items.Add(copy);
                }
            }
        }

        var total = items.Sum(x => x.Total);
        var titleText = items.Count == 1 ? items[0].Title : "позиций: " + items.Count;
        var hostNames = string.Join(", ", saleHosts.Select(x => x.Name));
        var confirmed = await ConfirmAsync(
            "Подтвердить оплату",
            "Создать продажу для " + hostNames + "?\n" + titleText + "\nСумма: " + MoneyText(total) + "\nОплата: " + PaymentMethodTitle(sale.Method),
            false);
        if (!confirmed) return;

        try
        {
            var clientUuid = sale.ClientUuid;
            if (string.IsNullOrWhiteSpace(clientUuid) && !string.IsNullOrWhiteSpace(sale.ClientQuery))
            {
                var client = await service.FindFirstClientAsync(config, sale.ClientQuery, CancellationToken.None);
                if (client == null || client.Id <= 0)
                    throw new InvalidOperationException("клиент не найден");
                clientUuid = client.Uuid;
            }

            var input = BuildPaymentInput(items, clientUuid, sale.Method, sale.CardSum);
            await service.ValidateAndCreatePaymentAsync(config, input, CancellationToken.None);
            ShowToast(record.Name, "Продажа создана: " + MoneyText(total));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowToast(record.Name, "Ошибка продажи: " + ex.Message);
            source.Text = record.Name + ": ошибка продажи - " + ex.Message;
        }
    }

    private async Task OpenPenaltyDialogAsync(PcRecord record)
    {
        if (!EnsureSmartShellHost(record)) return;
        var raw = await AskTextAsync("Штраф на " + record.Name, "Сколько минут штрафа добавить?");
        if (!TryParseMinutesToSeconds(raw, false, out var seconds))
        {
            ShowToast("Штраф", "Введите число минут больше нуля.");
            return;
        }
        if (!await ConfirmAsync("Подтвердить штраф", "Добавить штраф " + FormatPenaltyMinutes(seconds) + " на " + record.Name + "?", false))
            return;

        try
        {
            await service.AddPenaltyAsync(config, record.HostId, seconds, CancellationToken.None);
            ShowToast(record.Name, "Штраф отправлен.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowToast(record.Name, "Ошибка штрафа: " + ex.Message);
        }
    }

    private async Task AddPenaltyPresetAsync(PcRecord record, int minutes)
    {
        if (!EnsureSmartShellHost(record)) return;
        try
        {
            await service.AddPenaltyAsync(config, record.HostId, minutes * 60, CancellationToken.None);
            ShowToast(record.Name, "Штраф отправлен: " + minutes + " мин.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowToast(record.Name, "Ошибка штрафа: " + ex.Message);
        }
    }

    private List<PcRecord> SelectedHosts()
    {
        return records.Values
            .Where(x => selectedHostKeys.Contains(x.Name))
            .OrderBy(x => NaturalKey(x.Name))
            .ToList();
    }

    private async Task SendBulkHostCommandAsync(IReadOnlyCollection<PcRecord> hosts, string action)
    {
        var sent = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var host in hosts)
        {
            if (host.HostId <= 0
                || host.Locked
                || (action == "WOL" && host.Online)
                || (action != "WOL" && !host.Online))
            {
                skipped++;
                continue;
            }

            try
            {
                await service.SendHostCommandAsync(config, host.HostId, action, null, CancellationToken.None);
                sent++;
            }
            catch
            {
                failed++;
            }
        }

        ShowToast("Групповая команда", $"{action}: отправлено {sent}, пропущено {skipped}, ошибок {failed}.");
        await RefreshAsync();
    }

    private async Task SendBulkMessageAsync(IReadOnlyCollection<PcRecord> hosts, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            ShowToast("Уведомление", "Введите текст сообщения.");
            return;
        }

        var sent = 0;
        var failed = 0;
        foreach (var host in hosts.Where(x => x.HostId > 0))
        {
            try
            {
                await service.SendHostCommandAsync(config, host.HostId, "SHOW_MESSAGE", message.Trim(), CancellationToken.None);
                sent++;
            }
            catch
            {
                failed++;
            }
        }
        ShowToast("Групповое уведомление", $"Отправлено: {sent}, ошибок: {failed}.");
    }

    private async Task PromptBulkMessageAsync(IReadOnlyCollection<PcRecord> hosts)
    {
        var message = await AskTextAsync("Уведомление для выбранных ПК", "Введите сообщение:");
        if (!string.IsNullOrWhiteSpace(message))
            await SendBulkMessageAsync(hosts, message);
    }

    private async Task OpenGroupedControlPanelAsync(PcRecord? record)
    {
        if (record == null)
        {
            ShowToast("Управление", "Сначала выберите ПК.");
            return;
        }

        var dialog = new ModalWindow
        {
            Title = "Панель управления " + record.Name,
            Width = 920,
            Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#0d1217")
        };

        Button ActionButton(string text, Func<Task> action, bool enabled = true, bool danger = false)
        {
            var button = new Button
            {
                Content = text,
                Height = 40,
                Margin = new Thickness(0, 0, 8, 8),
                Background = enabled ? danger ? Brush("#35161d") : Brush("#182231") : Brush("#111820"),
                Foreground = enabled ? danger ? Brush("#ff4d4f") : Brush("#f5f7ff") : Brush("#667085"),
                BorderBrush = enabled ? danger ? Brush("#ff4d4f") : Brush("#2b3550") : Brush("#202938"),
                IsEnabled = enabled,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            button.Click += async (_, _) =>
            {
                try { await action(); }
                catch (Exception ex) { ShowToast(record.Name, ex.Message); }
            };
            return button;
        }

        Border Group(string titleText, Control content)
        {
            return new Border
            {
                Background = Brush("#151d28"),
                BorderBrush = Brush("#263244"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(14),
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = titleText, Foreground = Brush("#f5f7ff"), FontSize = 16, FontWeight = FontWeight.SemiBold },
                        content
                    }
                }
            };
        }

        WrapPanel Buttons(params Button[] buttons)
        {
            var panel = new WrapPanel();
            foreach (var button in buttons)
                panel.Children.Add(button);
            return panel;
        }

        var bookingGroup = Group("Бронирование", Buttons(
            ActionButton("Забронировать", async () => await OpenBookingDialogAsync(record)),
            ActionButton("Список бронирований", async () => await OpenBookingsListAsync(record)),
            ActionButton("Клиент пришёл", async () => await RedeemFirstBookingAsync(record), enabled: record.BookingCount > 0)
        ));

        var penaltyGroup = Group("Штраф", Buttons(
            ActionButton("5 мин", async () => await AddPenaltyPresetAsync(record, 5), record.HasSession),
            ActionButton("10 мин", async () => await AddPenaltyPresetAsync(record, 10), record.HasSession),
            ActionButton("15 мин", async () => await AddPenaltyPresetAsync(record, 15), record.HasSession),
            ActionButton("30 мин", async () => await AddPenaltyPresetAsync(record, 30), record.HasSession),
            ActionButton("60 мин", async () => await AddPenaltyPresetAsync(record, 60), record.HasSession),
            ActionButton("Другое время", async () => await OpenPenaltyDialogAsync(record), record.HasSession)
        ));

        var powerGroup = Group("Электропитание", Buttons(
            ActionButton("Включить", async () => await SendHostCommandAsync(record, "WOL", "включить")),
            ActionButton("Выключить", async () => await SendHostCommandAsync(record, "SHUTDOWN", "выключить"), danger: true),
            ActionButton("Перезагрузить", async () => await SendHostCommandAsync(record, "REBOOT", "перезагрузить"))
        ));

        var pcGroup = Group("Управление ПК", Buttons(
            ActionButton("Просмотр экрана", () => OpenSstvnAsync(record, true), record.Online && !record.Locked && !string.IsNullOrWhiteSpace(record.MacAddress)),
            ActionButton("Подключиться к ПК", () => OpenSstvnAsync(record, false), record.Online && !record.Locked && !string.IsNullOrWhiteSpace(record.MacAddress)),
            ActionButton("Выполнить", async () => await SendRunCommandAsync(record), record.Online && !record.Locked),
            ActionButton("Заметка о ПК", async () => await AddHostCommentAsync(record), !record.Locked)
        ));

        var shellGroup = Group("Shell", Buttons(
            ActionButton("Поделиться логами", async () => await SendHostCommandAsync(record, "SEND_LOGS", "запросить логи")),
            ActionButton("Высокий доступ", async () => await SendHighAccessAsync(record)),
            ActionButton("Снять Shell", async () => await SendProtectedHostCommandAsync(record, "DISABLE_SHELL", "выключить Shell"), danger: true),
            ActionButton("Обновить Shell", async () => await SendHostCommandAsync(record, "SHELL_UPDATE", "обновить Shell"))
        ));

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnSpacing = 12,
            RowSpacing = 12
        };
        grid.Children.Add(bookingGroup);
        Grid.SetColumn(penaltyGroup, 1);
        grid.Children.Add(penaltyGroup);
        Grid.SetRow(powerGroup, 1);
        grid.Children.Add(powerGroup);
        Grid.SetRow(pcGroup, 1);
        Grid.SetColumn(pcGroup, 1);
        grid.Children.Add(pcGroup);
        Grid.SetRow(shellGroup, 2);
        Grid.SetColumnSpan(shellGroup, 2);
        grid.Children.Add(shellGroup);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 16,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Children =
                    {
                        new StackPanel
                        {
                            Children =
                            {
                                new TextBlock { Text = "Панель управления " + record.Name, Foreground = Brush("#f5f7ff"), FontSize = 22, FontWeight = FontWeight.SemiBold },
                                new TextBlock { Text = record.StatusTitle + " · " + HostHoverPlaceText(record), Foreground = Brush("#8f9aad"), FontSize = 13 }
                            }
                        },
                        GhostButton("Закрыть", () => dialog.Close())
                    }
                },
                new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            }
        };
        Grid.SetColumn(((Grid)((StackPanel)dialog.Content).Children[0]).Children[1], 1);

        await dialog.ShowDialog(this);
    }

    private async Task OpenChangeHostDialogAsync(PcRecord record)
    {
        if (!EnsureSmartShellHost(record)) return;
        if (record.ClientSessionId <= 0)
        {
            ShowToast(record.Name, "Нет активной сессии для переноса.");
            return;
        }

        try
        {
            source.Text = record.Name + ": ищу свободные места...";
            var hosts = await service.LoadFreeHostsAsync(config, record.HostId, CancellationToken.None);
            if (hosts.Count == 0)
            {
                ShowToast(record.Name, "Свободные места не найдены.");
                return;
            }

            var target = await ShowFreeHostChoiceDialogAsync(record, hosts);
            if (target == null) return;

            await service.ChangeClientSessionHostAsync(config, record.ClientSessionId, target.Id, CancellationToken.None);
            ShowToast(record.Name, "Сеанс перенесён на " + target.Display + ".");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowToast(record.Name, "Ошибка переноса: " + ex.Message);
        }
    }

    private async Task<FreeHostInfo?> ShowFreeHostChoiceDialogAsync(PcRecord record, List<FreeHostInfo> hosts)
    {
        var dialog = new ModalWindow
        {
            Title = "Смена места",
            Width = 470,
            Height = 760,
            MinWidth = 430,
            MinHeight = 620,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#080d12"),
            SystemDecorations = SystemDecorations.None
        };

        Control SeatCard(string alias, string? groupTitle)
        {
            var priority = new Border
            {
                Background = Brush("#202a37"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(9, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = "Приоритет | 0",
                    Foreground = Brush("#9ba6b7"),
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold
                }
            };

            return new Border
            {
                Background = Brush("#151d28"),
                BorderBrush = Brush("#263244"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(13, 11),
                MinHeight = 94,
                Child = new StackPanel
                {
                    Spacing = 5,
                    Children =
                    {
                        new TextBlock { Text = alias, Foreground = Brush("#f5f7ff"), FontSize = 16, FontWeight = FontWeight.Bold },
                        new TextBlock { Text = string.IsNullOrWhiteSpace(groupTitle) ? "Без группы" : groupTitle, Foreground = Brush("#8f9aad"), FontSize = 13 },
                        priority
                    }
                }
            };
        }

        var close = new Button
        {
            Content = "×",
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = Brush("#f5f7ff"),
            FontSize = 20,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        close.Click += (_, _) => dialog.Close(null);

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("34,*,34") };
        var title = new TextBlock
        {
            Text = "Смена места",
            Foreground = Brush("#f5f7ff"),
            FontSize = 19,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 1);
        Grid.SetColumn(close, 2);
        header.Children.Add(title);
        header.Children.Add(close);

        var clientIcon = new Border
        {
            Width = 46,
            Height = 46,
            CornerRadius = new CornerRadius(23),
            Background = Brush("#26303d"),
            Child = DashboardIcon("user", "#8f9aad", 21)
        };
        var clientDetails = new StackPanel
        {
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(record.ClientName) ? "Клиент" : record.ClientName,
                    Foreground = Brush("#f5f7ff"),
                    FontSize = 15,
                    FontWeight = FontWeight.Bold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(record.ClientPhone) ? "Телефон не указан" : record.ClientPhone,
                    Foreground = Brush("#8f9aad"),
                    FontSize = 13
                }
            }
        };
        Grid.SetColumn(clientDetails, 1);
        var clientGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        clientGrid.Children.Add(clientIcon);
        clientGrid.Children.Add(clientDetails);
        var clientCard = new Border
        {
            Background = Brush("#151d28"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12),
            Child = clientGrid
        };

        var targetCard = new Border
        {
            Background = Brush("#101720"),
            BorderBrush = Brush("#263244"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            MinHeight = 94,
            Child = new TextBlock
            {
                Text = "Новое место\nне выбрано",
                Foreground = Brush("#778397"),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var transferGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,42,*"), ColumnSpacing = 8 };
        transferGrid.Children.Add(SeatCard(record.Name, record.GroupTitle));
        var arrow = new TextBlock
        {
            Text = "→",
            Foreground = Brush("#2f8cff"),
            FontSize = 25,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(arrow, 1);
        Grid.SetColumn(targetCard, 2);
        transferGrid.Children.Add(arrow);
        transferGrid.Children.Add(targetCard);

        var groupTitles = new List<string> { "Все группы" };
        groupTitles.AddRange(hosts
            .Select(x => x.GroupTitle)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase));
        var groupBox = new ComboBox
        {
            ItemsSource = groupTitles,
            SelectedIndex = 0,
            Height = 44,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brush("#151d28"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#263244")
        };

        FreeHostInfo? selected = null;
        var hostList = new StackPanel { Spacing = 8 };
        var moveButton = new Button
        {
            Content = "Пересадить",
            Height = 48,
            IsEnabled = false,
            Background = Brush("#232c37"),
            BorderBrush = Brushes.Transparent,
            Foreground = Brush("#727d8d"),
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        void RebuildHostList()
        {
            hostList.Children.Clear();
            var selectedGroup = groupBox.SelectedItem as string;
            var visibleHosts = hosts.Where(x => selectedGroup == "Все группы"
                || string.Equals(x.GroupTitle, selectedGroup, StringComparison.OrdinalIgnoreCase));

            foreach (var host in visibleHosts)
            {
                var details = new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new TextBlock { Text = host.Alias, Foreground = Brush("#f5f7ff"), FontSize = 15, FontWeight = FontWeight.Bold },
                        new TextBlock { Text = string.IsNullOrWhiteSpace(host.GroupTitle) ? "Без группы" : host.GroupTitle, Foreground = Brush("#8f9aad"), FontSize = 12 }
                    }
                };
                var priority = new Border
                {
                    Background = Brush("#202a37"),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(9, 4),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "Приоритет | 0", Foreground = Brush("#9ba6b7"), FontSize = 11, FontWeight = FontWeight.SemiBold }
                };
                Grid.SetColumn(priority, 1);
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
                row.Children.Add(details);
                row.Children.Add(priority);

                var isSelected = selected?.Id == host.Id;
                var hostButton = new Button
                {
                    Content = row,
                    MinHeight = 70,
                    Padding = new Thickness(13, 10),
                    Background = Brush("#151d28"),
                    BorderBrush = Brush(isSelected ? "#2f8cff" : "#263244"),
                    BorderThickness = new Thickness(isSelected ? 2 : 1),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                hostButton.Click += (_, _) =>
                {
                    selected = host;
                    targetCard.Child = SeatCard(host.Alias, host.GroupTitle);
                    targetCard.BorderBrush = Brush("#2f8cff");
                    moveButton.IsEnabled = true;
                    moveButton.Background = Brush("#2f8cff");
                    moveButton.Foreground = Brush("#ffffff");
                    RebuildHostList();
                };
                hostList.Children.Add(hostButton);
            }
        }

        groupBox.SelectionChanged += (_, _) => RebuildHostList();
        moveButton.Click += (_, _) =>
        {
            if (selected != null)
                dialog.Close(selected);
        };

        var equipmentHeader = new TextBlock
        {
            Text = "Доступное оборудование",
            Foreground = Brush("#f5f7ff"),
            FontSize = 15,
            FontWeight = FontWeight.Bold
        };
        var equipmentPanel = new StackPanel
        {
            Spacing = 9,
            Children = { equipmentHeader, groupBox }
        };
        var hostScroll = new ScrollViewer
        {
            Content = hostList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto"),
            RowSpacing = 14
        };
        Grid.SetRow(clientCard, 1);
        Grid.SetRow(transferGrid, 2);
        Grid.SetRow(equipmentPanel, 3);
        Grid.SetRow(hostScroll, 4);
        Grid.SetRow(moveButton, 5);
        content.Children.Add(header);
        content.Children.Add(clientCard);
        content.Children.Add(transferGrid);
        content.Children.Add(equipmentPanel);
        content.Children.Add(hostScroll);
        content.Children.Add(moveButton);

        dialog.Content = new Border
        {
            Margin = new Thickness(10),
            Padding = new Thickness(22, 18),
            Background = Brush("#0d1217"),
            BorderBrush = Brush("#334052"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = content
        };

        RebuildHostList();

        return await dialog.ShowDialog<FreeHostInfo?>(this);
    }

    private async Task OpenBookingDialogAsync(PcRecord record, IEnumerable<int>? initialHostIds = null, BookingInfo? existingBooking = null)
    {
        if (!EnsureSmartShellHost(record)) return;
        BookingDraft? booking;
        try
        {
            booking = await ShowBookingDialogAsync(record, initialHostIds, existingBooking);
        }
        catch (Exception ex)
        {
            ShowToast(record.Name, "Не удалось открыть окно брони: " + ex.Message);
            return;
        }
        if (booking == null) return;

        var clientQuery = booking.ClientQuery;
        var startLocal = booking.StartLocal;
        var durationHours = booking.DurationHours;
        var comment = booking.Comment;
        var hostIds = booking.HostIds.Where(x => x > 0).Distinct().ToList();
        if (hostIds.Count == 0)
            hostIds.Add(record.HostId);
        var durationSeconds = (int)Math.Round(durationHours * 3600m, MidpointRounding.AwayFromZero);
        var toLocal = startLocal.AddSeconds(durationSeconds);

        try
        {
            var clientUuid = booking.ClientUuid;
            if (string.IsNullOrWhiteSpace(clientUuid) && !string.IsNullOrWhiteSpace(clientQuery))
            {
                var client = await service.FindFirstClientAsync(config, clientQuery, CancellationToken.None);
                if (client == null || string.IsNullOrWhiteSpace(client.Uuid))
                    throw new InvalidOperationException("клиент для брони не найден");
                clientUuid = client.Uuid;
            }

            var sessionConflict = records.Values.FirstOrDefault(x =>
                hostIds.Contains(x.HostId)
                && x.HasSession
                && (!x.SessionEndsAtUtc.HasValue || startLocal < x.SessionEndsAtUtc.Value.ToLocalTime()));
            if (sessionConflict != null)
            {
                var until = sessionConflict.SessionEndsAtUtc.HasValue
                    ? sessionConflict.SessionEndsAtUtc.Value.ToLocalTime().ToString("dd.MM HH:mm")
                    : "неизвестного времени";
                throw new InvalidOperationException($"{sessionConflict.Name} занят до {until}.");
            }

            var existingBookings = await service.LoadBookingsAsync(
                config,
                hostIds[0],
                startLocal.ToUniversalTime(),
                toLocal.ToUniversalTime(),
                CancellationToken.None);
            foreach (var hostId in hostIds.Skip(1))
            {
                existingBookings.AddRange(await service.LoadBookingsAsync(
                    config,
                    hostId,
                    startLocal.ToUniversalTime(),
                    toLocal.ToUniversalTime(),
                    CancellationToken.None));
            }

            bool IsEditedBooking(BookingInfo candidate)
            {
                if (existingBooking == null) return false;
                if (candidate.Id > 0 && candidate.Id == existingBooking.Id) return true;

                var sameInterval = Math.Abs((candidate.FromLocal - existingBooking.FromLocal).TotalSeconds) < 1
                    && Math.Abs((candidate.ToLocal - existingBooking.ToLocal).TotalSeconds) < 1;
                var sameClient = string.Equals(candidate.ClientUuid, existingBooking.ClientUuid, StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(candidate.ClientUuid)
                    || string.IsNullOrWhiteSpace(existingBooking.ClientUuid);
                return sameInterval
                    && sameClient
                    && candidate.HostIds.Intersect(existingBooking.HostIds).Any();
            }

            var conflict = existingBookings.FirstOrDefault(x =>
                !IsEditedBooking(x)
                && !IsInactiveBookingStatus(x.Status)
                && x.FromLocal < toLocal
                && x.ToLocal > startLocal);
            if (conflict != null)
            {
                throw new InvalidOperationException(
                    $"На это время уже есть бронь: {conflict.FromLocal:dd.MM HH:mm}–{conflict.ToLocal:HH:mm}.");
            }

            if (existingBooking == null)
            {
                await service.CreateBookingAsync(config, hostIds, clientUuid, startLocal, toLocal, comment, CancellationToken.None);
                ShowToast(record.Name, hostIds.Count == 1 ? "Бронь создана." : "Бронь создана на ПК: " + hostIds.Count);
            }
            else
            {
                await service.UpdateBookingAsync(config, existingBooking.Id, hostIds, clientUuid, startLocal, toLocal, comment, CancellationToken.None);
                ShowToast(record.Name, "Бронь обновлена.");
            }
            upcomingBookingsRefreshAfterUtc = DateTime.MinValue;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowToast(record.Name, (existingBooking == null ? "Ошибка брони: " : "Ошибка обновления брони: ") + ex.Message + "\nПодробности записаны в booking-diagnostic.log.");
        }
    }

    private async Task OpenBookingsListAsync(PcRecord record)
    {
        if (!EnsureSmartShellHost(record)) return;

        try
        {
            var from = DateTime.Today.AddDays(-1).ToUniversalTime();
            var to = DateTime.Today.AddDays(14).ToUniversalTime();
            var bookings = await service.LoadBookingsAsync(config, record.HostId, from, to, CancellationToken.None);

            var dialog = new ModalWindow
            {
                Title = "Брони " + record.Name,
                Width = 760,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#080d12"),
                SystemDecorations = SystemDecorations.None
            };

            var rows = new StackPanel { Spacing = 8 };
            if (bookings.Count == 0)
            {
                rows.Children.Add(new TextBlock
                {
                    Text = "На ближайшие дни броней для " + record.Name + " нет.",
                    Foreground = Brush("#8f9aad"),
                    FontWeight = FontWeight.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 34)
                });
            }
            else
            {
                foreach (var booking in bookings)
                {
                    var grid = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("150,*,100"),
                        ColumnSpacing = 14
                    };
                    grid.Children.Add(new TextBlock
                    {
                        Text = booking.FromLocal == DateTime.MinValue ? "—" : booking.FromLocal.ToString("dd.MM HH:mm") + " - " + booking.ToLocal.ToString("HH:mm"),
                        Foreground = Brush("#f5f7ff"),
                        FontWeight = FontWeight.SemiBold
                    });
                    var client = new StackPanel
                    {
                        Spacing = 2,
                        Children =
                        {
                            new TextBlock { Text = booking.ClientDisplay, Foreground = Brush("#f5f7ff"), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                            new TextBlock { Text = string.IsNullOrWhiteSpace(booking.Comment) ? "Без комментария" : booking.Comment, Foreground = Brush("#8f9aad"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis }
                        }
                    };
                    Grid.SetColumn(client, 1);
                    grid.Children.Add(client);
                    var status = new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(booking.Status) ? "—" : booking.Status,
                        Foreground = Brush("#8f9aad"),
                        FontWeight = FontWeight.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    Grid.SetColumn(status, 2);
                    grid.Children.Add(status);

                    rows.Children.Add(new Border
                    {
                        Background = Brush("#151d28"),
                        BorderBrush = Brush("#263244"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(14, 12),
                        Child = grid
                    });
                }
            }

            var close = new Button
            {
                Content = "×",
                Width = 36,
                Height = 36,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Foreground = Brush("#f5f7ff"),
                FontSize = 20
            };
            close.Click += (_, _) => dialog.Close();
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            header.Children.Add(new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = "Список бронирований", Foreground = Brush("#f5f7ff"), FontSize = 18, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = record.Name + " · ближайшие 14 дней", Foreground = Brush("#8f9aad"), FontSize = 13 }
                }
            });
            Grid.SetColumn(close, 1);
            header.Children.Add(close);

            var body = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                RowSpacing = 14,
                Children =
                {
                    header,
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = rows
                    }
                }
            };
            Grid.SetRow(body.Children[1], 1);

            dialog.Content = new Border
            {
                Background = Brush("#080d12"),
                BorderBrush = Brush("#344052"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(28, 18, 28, 24),
                Child = body
            };

            await dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ShowToast(record.Name, "Не удалось загрузить брони: " + ex.Message);
        }
    }

    private static bool IsInactiveBookingStatus(string? status)
    {
        return string.Equals(status, "CANCELED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "REDEEMED", StringComparison.OrdinalIgnoreCase);
    }

    private async Task RedeemFirstBookingAsync(PcRecord record)
    {
        if (!EnsureSmartShellHost(record)) return;

        try
        {
            var from = DateTime.Now.AddHours(-6).ToUniversalTime();
            var to = DateTime.Now.AddDays(2).ToUniversalTime();
            var bookings = await service.LoadBookingsAsync(config, record.HostId, from, to, CancellationToken.None);
            var booking = bookings
                .Where(x => !string.Equals(x.Status, "REDEEMED", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(x.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(x.Status, "CANCELED", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => Math.Abs((x.FromLocal - DateTime.Now).TotalMinutes))
                .FirstOrDefault();

            if (booking == null)
            {
                ShowToast(record.Name, "Активная бронь для подтверждения не найдена.");
                return;
            }

            await service.RedeemBookingAsync(config, booking.Id, CancellationToken.None);
            ShowToast(record.Name, "Бронь отмечена: клиент пришёл.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowToast(record.Name, "Не удалось подтвердить бронь: " + ex.Message);
        }
    }

    private async Task<BookingDraft?> ShowBookingDialogAsync(PcRecord record, IEnumerable<int>? initialHostIds = null, BookingInfo? existingBooking = null)
    {
        var editing = existingBooking != null;
        var dialog = new ModalWindow
        {
            Title = editing
                ? "Редактирование брони " + record.Name
                : record.HostId > 0 ? "Бронирование " + record.Name : "Создание брони",
            Width = 1080,
            Height = 650,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#080d12"),
            SystemDecorations = SystemDecorations.None
        };

        var clientBox = SaleInput("Найти клиента");
        var now = DateTime.Now;
        var initialStart = editing && existingBooking!.FromLocal != DateTime.MinValue ? existingBooking.FromLocal : now;
        var selectedHostIds = (editing ? existingBooking!.HostIds : initialHostIds)?
            .Where(x => x > 0)
            .ToHashSet() ?? new HashSet<int>();
        if (record.HostId > 0 && selectedHostIds.Count == 0)
            selectedHostIds.Add(record.HostId);
        var hostChoices = records.Values
            .Where(x => x.HostId > 0)
            .OrderBy(x => NaturalKey(x.Name))
            .ToList();
        if (!hostChoices.Any(x => x.HostId == record.HostId) && record.HostId > 0)
            hostChoices.Insert(0, record);

        ClientInfo? bookingClient = editing && !string.IsNullOrWhiteSpace(existingBooking!.ClientUuid)
            ? new ClientInfo
            {
                Uuid = existingBooking.ClientUuid,
                Nickname = existingBooking.ClientName,
                Phone = existingBooking.ClientPhone
            }
            : null;
        if (bookingClient != null)
        {
            try
            {
                bookingClient = await service.LoadClientDetailsAsync(config, bookingClient.Uuid, CancellationToken.None) ?? bookingClient;
            }
            catch
            {
            }
            clientBox.Text = bookingClient.Display;
        }
        var clientSearchVersion = 0;
        var settingClientText = false;
        var clientResultsPanel = new StackPanel { Spacing = 8 };
        var clientHint = new TextBlock
        {
            Text = "Начните вводить никнейм или телефон\nавторизованного клиента.",
            Foreground = Brush("#8f9aad"),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeight.SemiBold
        };
        var status = new TextBlock { Foreground = Brush("#f5a524"), TextWrapping = TextWrapping.Wrap };
        var clientResultsScroll = new ScrollViewer
        {
            MaxHeight = 380,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = clientResultsPanel
        };
        var clientSearchArea = new StackPanel
        {
            Spacing = 12,
            Children = { clientBox, clientHint, clientResultsScroll }
        };
        var clientArea = new ContentControl { Content = clientSearchArea };
        var datePicker = new CalendarDatePicker
        {
            SelectedDate = initialStart.Date,
            DisplayDate = initialStart.Date,
            DisplayDateStart = now.Date,
            DisplayDateEnd = now.Date.AddYears(1),
            FirstDayOfWeek = DayOfWeek.Monday,
            IsTodayHighlighted = true,
            Height = 44,
            FontSize = 14,
            Background = Brush("#151d28"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2f8cff"),
            Watermark = "дд.мм.гггг",
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsHitTestVisible = false,
            Focusable = false
        };

        List<string> TimeOptions(DateTime date)
        {
            var firstMinute = 0;
            if (date.Date == DateTime.Today)
            {
                var current = DateTime.Now;
                firstMinute = (int)Math.Ceiling((current.Hour * 60 + current.Minute) / 30d) * 30;
            }

            var result = new List<string>();
            for (var minutes = firstMinute; minutes < 24 * 60; minutes += 30)
                result.Add(TimeSpan.FromMinutes(minutes).ToString(@"hh\:mm"));
            if (result.Count == 0)
                result.Add("23:30");
            return result;
        }

        var currentTimeOptions = TimeOptions(initialStart.Date);
        var initialTime = editing ? initialStart.ToString("HH:mm") : currentTimeOptions.FirstOrDefault() ?? now.ToString("HH:mm");
        var durationOptions = Enumerable.Range(1, 48)
            .Select(x => (x / 2m).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        var selectedTime = initialTime;
        var selectedDuration = editing && existingBooking!.ToLocal > initialStart
            ? (existingBooking.ToLocal - initialStart).TotalHours.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
            : "0";
        var timeBox = BookingChoiceInput(selectedTime, "00:00", 5);
        var durationBox = BookingChoiceInput(selectedDuration, "0", 5);
        Button? okButton = null;
        var choiceGrid = new UniformGrid { Columns = 4 };
        var choiceOverlay = new Border
        {
            IsVisible = false,
            Background = Brush("#151d28"),
            BorderBrush = Brush("#263244"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            VerticalAlignment = VerticalAlignment.Top,
            MaxHeight = 482,
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = choiceGrid
            }
        };
        var startValue = new TextBlock { Foreground = Brush("#f5f7ff"), FontWeight = FontWeight.SemiBold };
        var endValue = new TextBlock { Foreground = Brush("#9aa6ba"), FontWeight = FontWeight.SemiBold };
        var durationValue = new TextBlock { Foreground = Brush("#9aa6ba"), FontWeight = FontWeight.SemiBold };
        var hostTitle = new TextBlock { Foreground = Brush("#f5f7ff"), FontSize = 20, FontWeight = FontWeight.SemiBold };
        var hostSubtitle = new TextBlock { Foreground = Brush("#8f9aad"), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var hostListPanel = new StackPanel { Spacing = 8 };
        var selectedHostsPanel = new StackPanel { Spacing = 8 };
        var commentBox = new TextBox
        {
            Text = editing ? existingBooking!.Comment : "",
            Watermark = "Дополнительная информация",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 74,
            Background = Brush("#151d28"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#334052"),
            SelectionBrush = Brush("#2f8cff")
        };
        void OpenTimeChoices()
        {
            if (!TryGetBookingDate(out var date)) return;
            ShowChoices(TimeOptions(date), value =>
            {
                selectedTime = value;
                timeBox.Text = value;
            });
        }
        void OpenDurationChoices()
        {
            ShowChoices(durationOptions, value =>
            {
                selectedDuration = value;
                durationBox.Text = value;
            });
        }
        timeBox.TextChanged += (_, _) =>
        {
            selectedTime = (timeBox.Text ?? "").Trim();
            RefreshHostRows();
        };
        durationBox.TextChanged += (_, _) =>
        {
            selectedDuration = (durationBox.Text ?? "").Trim();
            RefreshHostRows();
        };
        timeBox.GotFocus += (_, _) => OpenTimeChoices();
        durationBox.GotFocus += (_, _) => OpenDurationChoices();

        void HideChoices()
        {
            choiceOverlay.IsVisible = false;
        }

        TextBox BookingChoiceInput(string text, string watermark, int maxLength)
        {
            return new TextBox
            {
                Text = text,
                Watermark = watermark,
                MaxLength = maxLength,
                Height = 24,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brush("#f5f7ff"),
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                VerticalContentAlignment = VerticalAlignment.Center,
                SelectionBrush = Brush("#2f8cff")
            };
        }

        Button ChoiceOption(string text, Action choose)
        {
            var button = new Button
            {
                Content = text,
                Height = 32,
                Margin = new Thickness(3),
                Padding = new Thickness(0),
                Background = Brush("#101820"),
                BorderBrush = Brush("#2d3a4d"),
                Foreground = Brush("#f5f7ff"),
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 76,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            button.Click += (_, _) => choose();
            return button;
        }

        void ShowChoices(IEnumerable<string> items, Action<string> choose)
        {
            choiceGrid.Children.Clear();
            foreach (var item in items)
            {
                var value = item;
                choiceGrid.Children.Add(ChoiceOption(value, () =>
                {
                    choose(value);
                    HideChoices();
                    RefreshHostRows();
                }));
            }
            choiceOverlay.IsVisible = true;
        }

        bool TryGetBookingDate(out DateTime date)
        {
            if (datePicker.SelectedDate.HasValue)
            {
                date = datePicker.SelectedDate.Value.Date;
                return true;
            }

            return DateTime.TryParse(
                datePicker.Text,
                System.Globalization.CultureInfo.GetCultureInfo("ru-RU"),
                System.Globalization.DateTimeStyles.AllowWhiteSpaces,
                out date);
        }

        bool TryGetBookingTime(out TimeSpan time)
        {
            return TimeSpan.TryParseExact(
                selectedTime.Trim(),
                new[] { @"h\:mm", @"hh\:mm" },
                System.Globalization.CultureInfo.InvariantCulture,
                out time)
                && time >= TimeSpan.Zero
                && time < TimeSpan.FromDays(1);
        }

        bool TryGetDuration(out decimal hours)
        {
            hours = 0m;
            if (!decimal.TryParse(
                    selectedDuration.Trim().Replace(",", "."),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out hours))
                return false;

            return hours >= 0.5m
                && hours <= 24m
                && decimal.Truncate(hours * 2m) == hours * 2m;
        }

        bool TryGetStart(out DateTime start)
        {
            start = default;
            if (!TryGetBookingDate(out var date) || !TryGetBookingTime(out var time))
                return false;
            start = date.Date.Add(time);
            return true;
        }

        void RefreshBookingSummary()
        {
            var hasDuration = TryGetDuration(out var duration);
            durationValue.Text = hasDuration
                ? duration.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " ч."
                : "—";
            if (!TryGetStart(out var start) || !hasDuration)
            {
                startValue.Text = "—";
                endValue.Text = "—";
                if (okButton != null)
                    okButton.IsEnabled = false;
                return;
            }

            startValue.Text = start.ToString("dd.MM.yyyy HH:mm");
            endValue.Text = start.AddMinutes((double)(duration * 60m)).ToString("dd.MM.yyyy HH:mm");
            var unavailableSelected = hostChoices
                .Where(x => selectedHostIds.Contains(x.HostId) && HostUnavailable(x))
                .ToList();
            if (okButton != null)
                okButton.IsEnabled = start > DateTime.Now
                    && selectedHostIds.Any(x => x > 0)
                    && unavailableSelected.Count == 0;
            status.Text = unavailableSelected.Count == 0
                ? ""
                : "Выбранный ПК занят до окончания текущего сеанса.";
        }

        string SelectedHostNames()
        {
            return string.Join(", ", hostChoices
                .Where(x => selectedHostIds.Contains(x.HostId))
                .OrderBy(x => NaturalKey(x.Name))
                .Select(x => x.Name));
        }

        string HostGroupName(PcRecord host)
        {
            if (host.Name.StartsWith("PS", StringComparison.OrdinalIgnoreCase)) return "PlayStation";
            var digits = new string(host.Name.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var number))
            {
                if (number <= 5) return "VIP";
                if (number <= 15) return "Premium";
                if (number <= 29) return "Standart";
                return "Dual";
            }
            return "Standart";
        }

        bool HostUnavailable(PcRecord host)
        {
            if (!host.HasSession) return false;
            if (!TryGetStart(out var start) || !host.SessionEndsAtUtc.HasValue) return true;
            return start < host.SessionEndsAtUtc.Value.ToLocalTime();
        }

        string HostBookingState(PcRecord host)
        {
            if (host.HasSession)
            {
                if (host.SessionEndsAtUtc.HasValue)
                {
                    var sessionEnd = host.SessionEndsAtUtc.Value.ToLocalTime();
                    if (TryGetStart(out var start) && start >= sessionEnd)
                        return "Доступен после " + sessionEnd.ToString("HH:mm");
                    return "Сессия до " + sessionEnd.ToString("HH:mm");
                }
                return "Занят";
            }
            return "Свободен";
        }

        void RefreshSelectedHosts()
        {
            var names = SelectedHostNames();
            hostTitle.Text = selectedHostIds.Count == 1
                ? names
                : "ПК: " + selectedHostIds.Count;
            hostSubtitle.Text = string.IsNullOrWhiteSpace(names) ? "Выберите хотя бы один ПК" : names;
            selectedHostsPanel.Children.Clear();

            foreach (var host in hostChoices.Where(x => selectedHostIds.Contains(x.HostId)).OrderBy(x => NaturalKey(x.Name)))
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                row.Children.Add(new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = host.Name, Foreground = Brush("#f5f7ff"), FontSize = 18, FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = HostGroupName(host), Foreground = Brush("#8f9aad"), FontWeight = FontWeight.SemiBold }
                    }
                });
                var remove = new Button
                {
                    Content = "×",
                    Width = 28,
                    Height = 28,
                    Padding = new Thickness(0),
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Transparent,
                    Foreground = Brush("#8f9aad"),
                    FontSize = 18,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                remove.Click += (_, _) =>
                {
                    if (selectedHostIds.Count <= 1)
                    {
                        status.Text = "Нельзя снять все ПК из брони.";
                        return;
                    }
                    selectedHostIds.Remove(host.HostId);
                    RefreshHostRows();
                };
                Grid.SetColumn(remove, 1);
                row.Children.Add(remove);
                selectedHostsPanel.Children.Add(row);
            }
        }

        Border FieldCard(string label, Control value)
        {
            return new Border
            {
                Background = Brush("#151d28"),
                BorderBrush = Brush("#222c3a"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10, 7),
                Child = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = label, Foreground = Brush("#8f9aad"), FontSize = 11, FontWeight = FontWeight.SemiBold },
                        value
                    }
                }
            };
        }

        Border InfoLine(string label, TextBlock value)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            grid.Children.Add(new TextBlock { Text = label, Foreground = Brush("#f5f7ff"), FontWeight = FontWeight.SemiBold });
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
            return new Border { Child = grid };
        }

        Border BookingHostRow(PcRecord host)
        {
            var selectedHost = selectedHostIds.Contains(host.HostId);
            var unavailable = HostUnavailable(host);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = host.Name, Foreground = unavailable ? Brush("#8f9aad") : Brush("#f5f7ff"), FontSize = 16, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = HostGroupName(host), Foreground = Brush("#8f9aad"), FontSize = 12, FontWeight = FontWeight.SemiBold }
                }
            });
            var state = new TextBlock
            {
                Text = HostBookingState(host),
                Foreground = unavailable ? Brush("#8f9aad") : Brush("#22c55e"),
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(state, 1);
            row.Children.Add(state);

            var border = new Border
            {
                Background = unavailable ? Brush("#334052") : Brush("#161c23"),
                BorderBrush = selectedHost ? Brush("#2f8cff") : unavailable ? Brush("#ffffff55") : Brush("#161c23"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12),
                MinHeight = 68,
                Child = row
            };
            border.PointerPressed += (_, _) =>
            {
                HideChoices();
                if (unavailable)
                    return;
                if (selectedHostIds.Contains(host.HostId))
                {
                    if (selectedHostIds.Count <= 1)
                    {
                        status.Text = "Нельзя снять все ПК из брони.";
                        return;
                    }
                    selectedHostIds.Remove(host.HostId);
                }
                else
                {
                    selectedHostIds.Add(host.HostId);
                }
                RefreshHostRows();
            };
            return border;
        }

        void RefreshHostRows()
        {
            hostListPanel.Children.Clear();
            foreach (var host in hostChoices)
                hostListPanel.Children.Add(BookingHostRow(host));
            RefreshSelectedHosts();
            RefreshBookingSummary();
        }

        var ok = SalePrimaryButton(editing ? "Обновить" : "Забронировать");
        okButton = ok;
        ok.IsEnabled = false;
        ok.Click += (_, _) =>
        {
            if (!TryGetStart(out var start))
            {
                status.Text = "Проверьте дату и время.";
                return;
            }
            if (start <= DateTime.Now)
            {
                status.Text = "Нельзя сохранить бронь на прошедшее время.";
                return;
            }
            if (!TryGetDuration(out var duration))
            {
                status.Text = "Длительность должна быть от 0.5 до 24 часов с шагом 0.5.";
                return;
            }
            var hosts = selectedHostIds.Where(x => x > 0).Distinct().ToList();
            if (hosts.Count == 0)
            {
                status.Text = "Выберите хотя бы один ПК.";
                return;
            }
            if (hostChoices.Any(x => selectedHostIds.Contains(x.HostId) && HostUnavailable(x)))
            {
                status.Text = "Бронь должна начинаться после окончания текущего сеанса выбранного ПК.";
                return;
            }
            dialog.Close(new BookingDraft(clientBox.Text ?? "", bookingClient?.Uuid, start, duration, commentBox.Text ?? "", hosts));
        };

        void RefreshTimeOptions()
        {
            if (!TryGetBookingDate(out var date)) return;
            var options = TimeOptions(date);
            if (!TryGetBookingTime(out _))
                selectedTime = options.FirstOrDefault() ?? "23:30";
            timeBox.Text = selectedTime;
            RefreshHostRows();
        }

        void ShowSelectedBookingClient()
        {
            if (bookingClient == null)
            {
                clientArea.Content = clientSearchArea;
                return;
            }

            var clear = new Button
            {
                Content = "×",
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 8, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Foreground = Brush("#2f8cff"),
                FontSize = 20,
                FontWeight = FontWeight.SemiBold
            };
            clear.Click += (_, _) =>
            {
                bookingClient = null;
                settingClientText = true;
                clientBox.Text = "";
                settingClientText = false;
                clientResultsPanel.Children.Clear();
                clientHint.Text = "Начните вводить никнейм или телефон\nавторизованного клиента.";
                clientArea.Content = clientSearchArea;
                status.Text = "";
            };

            clientArea.Content = new Grid
            {
                Children =
                {
                    BuildSaleActiveClientCard(record, bookingClient, dialog),
                    clear
                }
            };
        }

        Border BookingClientRow(ClientInfo client, bool active = false)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(new TextBlock
            {
                Text = client.Display,
                Foreground = Brush("#f5f7ff"),
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Баланс: " + client.BalanceText + " | Бонус: " + client.BonusText + " | Скидка: " + client.DiscountText,
                Foreground = Brush("#8f9aad"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            row.Children.Add(stack);
            var mark = new TextBlock
            {
                Text = active ? "✓" : "+",
                Foreground = active ? Brush("#22c55e") : Brush("#4ea1ff"),
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(mark, 1);
            row.Children.Add(mark);

            var border = new Border
            {
                Background = active ? Brush("#132944") : Brush("#151d28"),
                BorderBrush = active ? Brush("#3b82f6") : Brush("#263244"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(12, 10),
                Child = row
            };
            border.PointerPressed += async (_, _) =>
            {
                bookingClient = client;
                settingClientText = true;
                clientBox.Text = client.Display;
                settingClientText = false;
                clientResultsPanel.Children.Clear();
                ShowSelectedBookingClient();
                status.Text = "";

                if (string.IsNullOrWhiteSpace(client.Uuid)) return;
                try
                {
                    var details = await service.LoadClientDetailsAsync(config, client.Uuid, CancellationToken.None);
                    if (details != null && bookingClient?.Uuid == client.Uuid)
                    {
                        bookingClient = details;
                        ShowSelectedBookingClient();
                    }
                }
                catch
                {
                    // Базовая карточка уже доступна; подробности можно догрузить при следующем выборе.
                }
            };
            return border;
        }

        async Task SearchBookingClientsAsync(string query, int version)
        {
            if (string.IsNullOrWhiteSpace(config.AccessToken))
            {
                status.Text = "SmartShell API не подключён.";
                return;
            }

            clientHint.Text = "Ищу клиента...";
            try
            {
                var found = await service.SearchClientsAsync(config, query, CancellationToken.None);
                if (version != clientSearchVersion) return;
                clientResultsPanel.Children.Clear();
                foreach (var client in found.Take(8))
                    clientResultsPanel.Children.Add(BookingClientRow(client, bookingClient?.Uuid == client.Uuid));
                clientHint.Text = found.Count == 0
                    ? "Клиент не найден. Можно оставить поле пустым."
                    : "Найдено: " + found.Count;
            }
            catch (Exception ex)
            {
                if (version == clientSearchVersion)
                    status.Text = "Ошибка поиска клиента: " + ex.Message;
            }
        }

        var clientSearchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        clientSearchTimer.Tick += async (_, _) =>
        {
            clientSearchTimer.Stop();
            var query = (clientBox.Text ?? "").Trim();
            var version = clientSearchVersion;
            if (query.Length < 2)
            {
                clientResultsPanel.Children.Clear();
                clientHint.Text = "Начните вводить никнейм или телефон\nавторизованного клиента.";
                return;
            }
            await SearchBookingClientsAsync(query, version);
        };

        datePicker.SelectedDateChanged += (_, _) => RefreshTimeOptions();
        datePicker.LostFocus += (_, _) => RefreshTimeOptions();
        clientBox.TextChanged += (_, _) =>
        {
            if (settingClientText) return;
            bookingClient = null;
            clientSearchVersion++;
            clientSearchTimer.Stop();
            clientSearchTimer.Start();
        };
        clientBox.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            clientSearchTimer.Stop();
            var query = (clientBox.Text ?? "").Trim();
            if (query.Length >= 2)
                await SearchBookingClientsAsync(query, ++clientSearchVersion);
        };

        var close = new Button
        {
            Content = "×",
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = Brush("#f5f7ff"),
            FontSize = 20
        };
        close.Click += (_, _) => dialog.Close(null);

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock
        {
            Text = editing ? "Редактирование брони" : "Создание брони",
            Foreground = Brush("#f5f7ff"),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var left = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = clientArea
        };

        var dateCard = FieldCard("Дата брони", datePicker);
        var timeCard = FieldCard("Начало", timeBox);
        var durationCard = FieldCard("Время, ч.", durationBox);
        dateCard.PointerPressed += (_, e) =>
        {
            datePicker.IsDropDownOpen = true;
            e.Handled = true;
        };
        timeCard.PointerPressed += (_, e) =>
        {
            timeBox.Focus();
            OpenTimeChoices();
        };
        durationCard.PointerPressed += (_, e) =>
        {
            durationBox.Focus();
            OpenDurationChoices();
        };

        var centerTop = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("150,104,104"),
            ColumnSpacing = 8,
            Children =
            {
                dateCard,
                timeCard,
                durationCard
            }
        };
        Grid.SetColumn(centerTop.Children[1], 1);
        Grid.SetColumn(centerTop.Children[2], 2);

        var hostScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = hostListPanel
        };
        var hostHeader = new TextBlock
        {
            Text = "Компьютеры",
            Foreground = Brush("#f5f7ff"),
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(4, 2, 4, 6)
        };
        var hostSection = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children = { hostHeader, hostScroll }
        };
        Grid.SetRow(hostScroll, 1);
        var hostArea = new Grid
        {
            Children = { hostSection, choiceOverlay }
        };

        var center = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 10,
            Children =
            {
                centerTop,
                hostArea
            }
        };
        Grid.SetRow(center.Children[1], 1);

        var rightContent = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children =
            {
                new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                            Children =
                            {
                                hostTitle,
                                new TextBlock { Text = "▱", Foreground = Brush("#8f9aad"), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Right }
                            }
                        },
                        hostSubtitle,
                        selectedHostsPanel
                    }
                },
                new Border(),
                new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new Border { Height = 1, Background = Brush("#303a48") },
                        InfoLine("Начало брони", startValue),
                        InfoLine("Конец брони", endValue),
                        InfoLine("Длительность", durationValue),
                        commentBox,
                        ok,
                        status
                    }
                }
            }
        };
        Grid.SetRow(rightContent.Children[2], 2);
        var right = SalePanel(rightContent, new Thickness(14));

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("292,390,280"),
            ColumnSpacing = 14,
            Children = { left, center, right }
        };
        Grid.SetColumn(center, 1);
        Grid.SetColumn(right, 2);

        var shell = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 18,
            Children = { header, body }
        };
        Grid.SetRow(body, 1);

        dialog.Content = new Border
        {
            Background = Brush("#080d12"),
            BorderBrush = Brush("#344052"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(30, 18, 30, 26),
            Child = shell
        };

        RefreshHostRows();
        if (bookingClient != null)
            ShowSelectedBookingClient();
        RefreshBookingSummary();
        var result = await dialog.ShowDialog<BookingDraft?>(this);
        clientSearchTimer.Stop();
        return result;
    }

    private bool TryParseMinutesToSeconds(string? raw, bool allowZero, out int seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (!decimal.TryParse(raw.Trim().Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var minutes))
            return false;
        if (minutes < 0 || (!allowZero && minutes <= 0)) return false;
        seconds = (int)Math.Round(minutes * 60m, MidpointRounding.AwayFromZero);
        if (!allowZero && seconds <= 0) seconds = 1;
        return true;
    }

    private string FormatPenaltyMinutes(int seconds)
    {
        var minutes = seconds / 60.0;
        return minutes.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " мин";
    }

    private async Task SendHostCommandAsync(PcRecord record, string action, string verb)
    {
        if (!EnsureSmartShellHost(record)) return;
        await service.SendHostCommandAsync(config, record.HostId, action, null, CancellationToken.None);
        ShowToast(record.Name, "Команда отправлена: " + action);
        await RefreshAsync();
    }

    private async Task SendRunCommandAsync(PcRecord record)
    {
        if (!EnsureSmartShellHost(record)) return;
        if (!record.Online || record.Locked)
        {
            ShowToast(record.Name, "Выполнение команды доступно только для включённого и не заблокированного ПК.");
            return;
        }

        var command = await AskTextAsync("Выполнить на " + record.Name, "Введите команду:");
        if (string.IsNullOrWhiteSpace(command)) return;

        await service.SendHostCommandAsync(config, record.HostId, "RUN", command.Trim(), CancellationToken.None);
        ShowToast(record.Name, "Команда отправлена: RUN");
        await RefreshAsync();
    }

    private async Task AddHostCommentAsync(PcRecord record)
    {
        if (!EnsureSmartShellHost(record)) return;
        var note = await AskTextAsync("Заметка о " + record.Name, "Текст заметки:");
        if (string.IsNullOrWhiteSpace(note)) return;

        try
        {
            await service.CreateHostCommentAsync(config, record.HostId, note.Trim(), false, CancellationToken.None);
            ShowToast(record.Name, "Заметка сохранена в SmartShell.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowToast(record.Name, "Не удалось сохранить заметку: " + ex.Message);
        }
    }

    private async Task SendProtectedHostCommandAsync(PcRecord record, string action, string verb)
    {
        if (!EnsureSmartShellHost(record)) return;
        var password = await AskPasswordAsync(verb, "Введите пароль:");
        if (password != "Osen1983!")
        {
            ShowToast(verb, "Неверный пароль.");
            return;
        }

        await service.SendHostCommandAsync(config, record.HostId, action, null, CancellationToken.None);
        ShowToast(record.Name, "Команда отправлена: " + action);
        await RefreshAsync();
    }

    private async Task SendHighAccessAsync(PcRecord record)
    {
        if (!EnsureSmartShellHost(record)) return;
        var password = await AskPasswordAsync("High Access", "Введите пароль для высокого доступа:");
        if (password != "Osen1983!")
        {
            ShowToast("High Access", "Неверный пароль.");
            return;
        }

        await service.SendHostCommandAsync(config, record.HostId, "ALLOW_ADMIN_MODE", null, CancellationToken.None);
        ShowToast(record.Name, "High Access отправлен.");
        await RefreshAsync();
    }

    private async Task StopSessionAsync(PcRecord record)
    {
        if (record.ClientSessionId <= 0)
        {
            ShowToast(record.Name, "Активная сессия не найдена.");
            return;
        }

        await service.StopClientSessionAsync(config, record.ClientSessionId, CancellationToken.None);
        ShowToast(record.Name, "Сеанс завершён.");
        await RefreshAsync();
    }

    private async Task SetServiceModeAsync(PcRecord record, bool enabled)
    {
        if (!EnsureSmartShellHost(record)) return;
        await service.SetServiceModeAsync(config, record.HostId, enabled, CancellationToken.None);
        ShowToast(record.Name, "Сервисный режим обновлён.");
        await RefreshAsync();
    }

    private async Task SendMessageToHostAsync(PcRecord record)
    {
        if (!EnsureSmartShellHost(record)) return;
        var message = await AskTextAsync("Уведомление на " + record.Name, "Текст сообщения:");
        if (string.IsNullOrWhiteSpace(message)) return;

        await service.SendHostCommandAsync(config, record.HostId, "SHOW_MESSAGE", message.Trim(), CancellationToken.None);
        ShowToast(record.Name, "Уведомление отправлено.");
    }

    private bool EnsureSmartShellHost(PcRecord record)
    {
        if (!config.SmartShellEnabled || string.IsNullOrWhiteSpace(config.AccessToken))
        {
            ShowToast("SmartShell", "В конфиге нет активного SmartShell-токена.");
            return false;
        }
        if (record.HostId <= 0)
        {
            ShowToast(record.Name, "Нет SmartShell host_id. Обновите карту.");
            return false;
        }
        return true;
    }

    private bool IsAutoShutdownExcluded(PcRecord record)
    {
        return config.NoAutoShutdownNames.Any(x => string.Equals(NormalizeName(x), NormalizeName(record.Name), StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> ConfirmAsync(string dialogTitle, string message, bool danger)
    {
        var dialog = new ModalWindow
        {
            Title = dialogTitle,
            Width = 420,
            Height = 210,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = Brush("#0b1118")
        };

        var no = GhostButton("Отмена", () => dialog.Close(false));
        no.Width = 120;
        no.Margin = new Thickness(0, 0, 10, 0);
        var yes = new Button
        {
            Content = "Подтвердить",
            Width = 140,
            Height = 40,
            Background = danger ? Brush("#dc2626") : Brush("#2563eb"),
            Foreground = Brushes.White,
            BorderBrush = danger ? Brush("#ef4444") : Brush("#3b82f6")
        };
        yes.Click += (_, _) => dialog.Close(true);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = dialogTitle, Foreground = Brush("#f5f7ff"), FontSize = 20, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = message, Foreground = Brush("#a9b2c5"), TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { no, yes }
                }
            }
        };

        return await dialog.ShowDialog<bool>(this);
    }

    private async Task<string?> AskTextAsync(string dialogTitle, string label, string initialText = "")
    {
        var dialog = new ModalWindow
        {
            Title = dialogTitle,
            Width = 460,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = Brush("#0b1118")
        };
        var input = new TextBox
        {
            Text = initialText,
            Height = 42,
            Watermark = label,
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550")
        };
        var cancel = GhostButton("Отмена", () => dialog.Close(null));
        cancel.Width = 110;
        cancel.Margin = new Thickness(0, 0, 10, 0);
        var ok = AccentButton("Отправить", () => dialog.Close(input.Text));
        ok.Width = 130;

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = dialogTitle, Foreground = Brush("#f5f7ff"), FontSize = 20, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = label, Foreground = Brush("#8f9aad") },
                input,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } }
            }
        };

        return await dialog.ShowDialog<string?>(this);
    }

    private async Task<string?> AskPasswordAsync(string dialogTitle, string label)
    {
        var dialog = new ModalWindow
        {
            Title = dialogTitle,
            Width = 460,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = Brush("#0b1118")
        };
        var input = new TextBox
        {
            Height = 42,
            Watermark = label,
            PasswordChar = '*',
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550")
        };
        var cancel = GhostButton("Отмена", () => dialog.Close(null));
        cancel.Width = 110;
        cancel.Margin = new Thickness(0, 0, 10, 0);
        var ok = AccentButton("Открыть", () => dialog.Close(input.Text));
        ok.Width = 130;

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = dialogTitle, Foreground = Brush("#f5f7ff"), FontSize = 20, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = label, Foreground = Brush("#8f9aad") },
                input,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } }
            }
        };

        return await dialog.ShowDialog<string?>(this);
    }

    private async Task EnsureApiConfiguredAsync()
    {
        if (!config.SmartShellEnabled)
            return;
        if (!string.IsNullOrWhiteSpace(config.AccessToken))
            return;

        await ShowApiSettingsDialogAsync(true);
    }

    private void StartBrowserBridge()
    {
        try
        {
            browserBridgeListener = new TcpListener(IPAddress.Loopback, 17843);
            browserBridgeListener.Start();
            _ = Task.Run(() => BrowserBridgeLoopAsync(browserBridgeCts.Token));
        }
        catch
        {
            browserBridgeListener = null;
        }
    }

    private async Task BrowserBridgeLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && browserBridgeListener != null)
        {
            try
            {
                var client = await browserBridgeListener.AcceptTcpClientAsync(token);
                _ = Task.Run(() => HandleBrowserBridgeClientAsync(client, token), token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                if (!token.IsCancellationRequested)
                    await Task.Delay(500, token);
            }
        }
    }

    private async Task HandleBrowserBridgeClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        await using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true))
        {
            try
            {
                var requestLine = await reader.ReadLineAsync(token);
                if (string.IsNullOrWhiteSpace(requestLine)) return;
                var request = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (request.Length < 2) return;

                var contentLength = 0;
                var origin = "";
                while (true)
                {
                    var header = await reader.ReadLineAsync(token);
                    if (string.IsNullOrEmpty(header)) break;
                    if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(header[15..].Trim(), out contentLength);
                    if (header.StartsWith("Origin:", StringComparison.OrdinalIgnoreCase))
                        origin = header[7..].Trim();
                }

                if (origin.Length > 0 && !origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteBrowserBridgeResponseAsync(stream, "403 Forbidden", "", token);
                    return;
                }

                if (request[0].Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteBrowserBridgeResponseAsync(stream, "204 No Content", "", token);
                    return;
                }

                if (!request[0].Equals("POST", StringComparison.OrdinalIgnoreCase)
                    || !request[1].Equals("/smartshell/token", StringComparison.OrdinalIgnoreCase)
                    || contentLength <= 0 || contentLength > 131072)
                {
                    await WriteBrowserBridgeResponseAsync(stream, "404 Not Found", "", token);
                    return;
                }

                var chars = new char[contentLength];
                var read = 0;
                while (read < chars.Length)
                {
                    var count = await reader.ReadAsync(chars.AsMemory(read, chars.Length - read), token);
                    if (count == 0) break;
                    read += count;
                }

                using var body = JsonDocument.Parse(new string(chars, 0, read));
                var accessToken = body.RootElement.TryGetProperty("accessToken", out var access) ? access.GetString() ?? "" : "";
                var refreshToken = body.RootElement.TryGetProperty("refreshToken", out var refresh) ? refresh.GetString() ?? "" : "";
                if (accessToken.Length < 20)
                {
                    await WriteBrowserBridgeResponseAsync(stream, "400 Bad Request", "", token);
                    return;
                }

                Dispatcher.UIThread.Post(() => _ = ApplyBrowserBridgeTokensAsync(accessToken, refreshToken));
                await WriteBrowserBridgeResponseAsync(stream, "202 Accepted", "", token);
            }
            catch
            {
                try { await WriteBrowserBridgeResponseAsync(stream, "400 Bad Request", "", token); }
                catch { }
            }
        }
    }

    private static async Task WriteBrowserBridgeResponseAsync(NetworkStream stream, string status, string body, CancellationToken token)
    {
        var payload = Encoding.UTF8.GetBytes(body);
        var headers = Encoding.ASCII.GetBytes(
            "HTTP/1.1 " + status + "\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
            "Access-Control-Allow-Methods: POST, OPTIONS\r\n" +
            "Access-Control-Allow-Headers: Content-Type\r\n" +
            "Access-Control-Allow-Private-Network: true\r\n" +
            "Content-Type: text/plain; charset=utf-8\r\n" +
            "Content-Length: " + payload.Length + "\r\n" +
            "Connection: close\r\n\r\n");
        await stream.WriteAsync(headers, token);
        if (payload.Length > 0)
            await stream.WriteAsync(payload, token);
    }

    private async Task ApplyBrowserBridgeTokensAsync(string accessToken, string refreshToken)
    {
        accessToken = CleanTokenValue(accessToken);
        refreshToken = CleanTokenValue(refreshToken);
        if (bridgeTokenApplyInProgress || accessToken.Length < 20) return;
        if (string.Equals(config.AccessToken, accessToken, StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(refreshToken))
                config.RefreshToken = refreshToken;
            return;
        }

        bridgeTokenApplyInProgress = true;
        var previousAccess = config.AccessToken;
        var previousRefresh = config.RefreshToken;
        var previousInfo = apiAccessInfo;
        var previousPhone = NormalizeOperatorPhone(previousInfo?.Phone);
        try
        {
            config.SmartShellEnabled = true;
            config.SmartShellEndpoint ??= "https://billing.smartshell.gg/api/graphql";
            config.AccessToken = accessToken;
            config.RefreshToken = refreshToken;
            var nextInfo = await service.LoadApiAccessInfoAsync(config, CancellationToken.None);
            apiAccessInfo = nextInfo;
            service.SaveConfig(config);
            ApplyOperatorPalette(nextInfo);
            UpdateCleaningAuditAccess();

            timer.Interval = TimeSpan.FromSeconds(Math.Max(5, config.PollSeconds));
            if (!timer.IsEnabled) timer.Start();
            source.Text = "SmartShell Bridge · " + nextInfo.DisplayName;

            var nextPhone = NormalizeOperatorPhone(nextInfo.Phone);
            if (previousPhone.Length > 0 && !string.Equals(previousPhone, nextPhone, StringComparison.Ordinal))
            {
                ResetCleanedHostsForOperatorChange();
                var message = "Новый оператор: " + nextInfo.DisplayName;
                ShowToast("Смена оператора", message);
                ShowWindowsNotification("Смена оператора", message);
            }

            await RefreshAsync();
        }
        catch
        {
            config.AccessToken = previousAccess;
            config.RefreshToken = previousRefresh;
            apiAccessInfo = previousInfo;
            UpdateCleaningAuditAccess();
            if (string.IsNullOrWhiteSpace(previousAccess))
            {
                source.Text = "ожидаю авторизацию SmartShell в Chrome...";
                summary.Text = "SmartShell не подключён";
            }
        }
        finally
        {
            bridgeTokenApplyInProgress = false;
        }
    }

    private void ResetCleanedHostsForOperatorChange()
    {
        foreach (var record in records.Values.Where(x => x.CleaningStatus == "Убрано"))
        {
            record.CleaningStatus = "Нет задачи";
            record.FreedAtUtc = null;
            cleaningReminderMinutes.Remove(record.Name);
        }
    }

    private async Task<bool> ShowApiSettingsDialogAsync(bool firstRun)
    {
        var dialog = new ModalWindow
        {
            Title = firstRun ? "Настройка SmartShell API" : "SmartShell API",
            Width = 720,
            Height = 620,
            MinWidth = 560,
            MinHeight = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#0b1118")
        };

        var endpointBox = TokenTextBox("GraphQL endpoint");
        endpointBox.Text = string.IsNullOrWhiteSpace(config.SmartShellEndpoint)
            ? "https://billing.smartshell.gg/api/graphql"
            : config.SmartShellEndpoint;

        var accessBox = TokenTextBox("access_token");
        accessBox.Text = config.AccessToken ?? "";
        accessBox.PasswordChar = '*';

        var refreshBox = TokenTextBox("refresh_token");
        refreshBox.Text = config.RefreshToken ?? "";
        refreshBox.PasswordChar = '*';

        var status = new TextBlock
        {
            Foreground = Brush("#f5a524"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        };

        var save = new Button
        {
            Content = "Сохранить",
            Width = 130,
            Height = 40,
            Background = Brush("#2563eb"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3b82f6")
        };
        var openBrowser = GhostButton("Резервный браузер", () => OpenSmartShellAuthBrowser(status));
        openBrowser.Width = 170;
        openBrowser.Height = 40;

        var importFromBrowser = new Button
        {
            Content = "Импорт через порт 9222",
            Width = 210,
            Height = 40,
            Background = Brush("#2563eb"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3b82f6")
        };
        var later = GhostButton(firstRun ? "Позже" : "Закрыть", () => dialog.Close(false));
        later.Width = 110;
        later.Margin = new Thickness(0, 0, 10, 0);

        async Task ConnectWithTokensAsync(string access, string refresh, string endpoint)
        {
            if (string.IsNullOrWhiteSpace(access))
            {
                status.Text = "Нужен access_token. Способ получения уточните у администратора клуба.";
                return;
            }

            config.SmartShellEnabled = true;
            config.SmartShellEndpoint = string.IsNullOrWhiteSpace(endpoint) ? "https://billing.smartshell.gg/api/graphql" : endpoint;
            config.AccessToken = access;
            config.RefreshToken = refresh;
            save.IsEnabled = false;
            importFromBrowser.IsEnabled = false;
            status.Text = "Проверяю токен и права...";

            try
            {
                apiAccessInfo = await service.LoadApiAccessInfoAsync(config, CancellationToken.None);
                UpdateCleaningAuditAccess();
                service.SaveConfig(config);
                timer.Interval = TimeSpan.FromSeconds(Math.Max(5, config.PollSeconds));
                if (!timer.IsEnabled) timer.Start();
                await RefreshAsync();
                var permissionText = apiAccessInfo.CanManageClients
                    ? $"Подключено: {apiAccessInfo.DisplayName} · {apiAccessInfo.RoleText}. Управление клиентами доступно."
                    : $"Подключено: {apiAccessInfo.DisplayName} · {apiAccessInfo.RoleText}. Нет права client_manage; нужен токен менеджера или владельца.";
                ShowToast("SmartShell API", permissionText);
                dialog.Close(true);
            }
            catch (Exception ex)
            {
                apiAccessInfo = null;
                UpdateCleaningAuditAccess();
                status.Text = "Токен не прошёл проверку: " + ex.Message;
                save.IsEnabled = true;
                importFromBrowser.IsEnabled = true;
            }
        }

        importFromBrowser.Click += async (_, _) =>
        {
            importFromBrowser.IsEnabled = false;
            status.Text = "Ищу открытую вкладку SmartShell в браузере...";
            try
            {
                var imported = await ImportSmartShellTokensFromBrowserAsync(CancellationToken.None);
                accessBox.Text = imported.AccessToken;
                refreshBox.Text = imported.RefreshToken ?? "";
                status.Text = "Токены найдены, проверяю доступ...";
                await ConnectWithTokensAsync(imported.AccessToken, imported.RefreshToken ?? "", (endpointBox.Text ?? "").Trim());
            }
            catch (Exception ex)
            {
                status.Text = "Не удалось подтянуть из браузера: " + ex.Message;
                importFromBrowser.IsEnabled = true;
            }
        };

        save.Click += async (_, _) =>
        {
            await ConnectWithTokensAsync(
                (accessBox.Text ?? "").Trim(),
                (refreshBox.Text ?? "").Trim(),
                (endpointBox.Text ?? "").Trim());
        };

        dialog.Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = firstRun ? "Подключение к SmartShell" : "Настройка SmartShell API",
                        Foreground = Brush("#f5f7ff"),
                        FontSize = 24,
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "SmartShell Bridge автоматически получает авторизацию из рабочего Chrome. Токены сохраняются только на время работы программы и удаляются при полном закрытии.",
                        Foreground = Brush("#a9b2c5"),
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 14
                    },
                    HintCard("SmartShell Bridge", "Оставьте вкладку admin.smartshell.gg открытой. Расширение проверяет оператора каждые 30 секунд и передаёт приложению новую авторизацию без перезапуска Chrome."),
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        Children = { openBrowser, importFromBrowser }
                    },
                    HintCard("Резервный вариант", "Отдельный браузер с портом 9222 и ручной ввод токенов оставлены только на случай изменения SmartShell."),
                    Label("Endpoint"),
                    endpointBox,
                    Label("Access token"),
                    accessBox,
                    Label("Refresh token"),
                    refreshBox,
                    status,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { later, save }
                    }
                }
            }
        };

        return await dialog.ShowDialog<bool>(this);
    }

    private void OpenSmartShellAuthBrowser(TextBlock status)
    {
        var browser = FindBrowserExecutable();
        if (browser == null)
        {
            status.Text = "Не нашёл Yandex или Chrome. Откройте браузер вручную с remote-debugging-port=9222.";
            return;
        }

        Directory.CreateDirectory(authBrowserProfileDir);
        var info = new ProcessStartInfo
        {
            FileName = browser,
            UseShellExecute = false
        };
        info.ArgumentList.Add("--remote-debugging-port=9222");
        info.ArgumentList.Add("--user-data-dir=" + authBrowserProfileDir);
        info.ArgumentList.Add("--new-window");
        info.ArgumentList.Add("https://admin.smartshell.gg/map");
        Process.Start(info);
        status.Text = "Браузер открыт. Войдите в SmartShell и нажмите «Подтянуть из браузера».";
    }

    private static string? FindBrowserExecutable()
    {
        var paths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Yandex", "YandexBrowser", "Application", "browser.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Yandex", "YandexBrowser", "Application", "browser.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Yandex", "YandexBrowser", "Application", "browser.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe")
        };
        return paths.FirstOrDefault(File.Exists);
    }

    private async Task<BrowserTokenImport> ImportSmartShellTokensFromBrowserAsync(CancellationToken token)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var pages = await http.GetAsync("http://127.0.0.1:9222/json/list", token);
        pages.EnsureSuccessStatusCode();
        await using var stream = await pages.Content.ReadAsStreamAsync(token);
        using var pageList = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var page = pageList.RootElement.EnumerateArray()
            .FirstOrDefault(x =>
                x.TryGetProperty("url", out var url) &&
                (url.GetString() ?? "").Contains("admin.smartshell.gg", StringComparison.OrdinalIgnoreCase) &&
                x.TryGetProperty("webSocketDebuggerUrl", out _));

        if (page.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException("Откройте вкладку admin.smartshell.gg через кнопку «Открыть браузер».");

        var webSocketUrl = page.GetProperty("webSocketDebuggerUrl").GetString();
        if (string.IsNullOrWhiteSpace(webSocketUrl))
            throw new InvalidOperationException("Вкладка SmartShell не дала DevTools-ссылку.");

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(webSocketUrl), token);

        const string dumpScript = """
            (async () => {
              const read = (storage) => {
                const data = {};
                for (let i = 0; i < storage.length; i++) {
                  const key = storage.key(i);
                  data[key] = storage.getItem(key);
                }
                return data;
              };
              const readIndexedDb = async () => {
                const result = {};
                if (!('indexedDB' in window) || !indexedDB.databases) return result;
                const databases = await indexedDB.databases();
                for (const dbInfo of databases) {
                  if (!dbInfo.name) continue;
                  result[dbInfo.name] = {};
                  const db = await new Promise((resolve, reject) => {
                    const request = indexedDB.open(dbInfo.name);
                    request.onsuccess = () => resolve(request.result);
                    request.onerror = () => reject(request.error);
                  });
                  for (const storeName of Array.from(db.objectStoreNames)) {
                    result[dbInfo.name][storeName] = await new Promise((resolve) => {
                      const transaction = db.transaction(storeName, 'readonly');
                      const store = transaction.objectStore(storeName);
                      const request = store.getAll();
                      request.onsuccess = () => resolve(request.result);
                      request.onerror = () => resolve([]);
                    });
                  }
                  db.close();
                }
                return result;
              };
              return JSON.stringify({
                href: location.href,
                localStorage: read(localStorage),
                sessionStorage: read(sessionStorage),
                indexedDB: await readIndexedDb(),
                cookie: document.cookie
              });
            })()
            """;

        using var storage = await CdpCallAsync(socket, 1, "Runtime.evaluate", new Dictionary<string, object?>
        {
            ["expression"] = dumpScript,
            ["returnByValue"] = true,
            ["awaitPromise"] = true
        }, token);
        using var cookies = await CdpCallAsync(socket, 2, "Network.getAllCookies", null, token);

        var storageText = storage.RootElement
            .GetProperty("result")
            .GetProperty("result")
            .GetProperty("value")
            .GetString() ?? "";
        var raw = storageText + Environment.NewLine + cookies.RootElement.GetRawText();
        if (!TryExtractTokenPair(raw, out var access, out var refresh))
            throw new InvalidOperationException("Не нашёл токен в localStorage, sessionStorage, IndexedDB и cookies. Проверьте, что вход в SmartShell завершён именно в окне, открытом этой программой.");

        return new BrowserTokenImport(access, refresh, "browser");
    }

    private static async Task<JsonDocument> CdpCallAsync(
        ClientWebSocket socket,
        int id,
        string method,
        Dictionary<string, object?>? parameters,
        CancellationToken token)
    {
        var payload = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["method"] = method
        };
        if (parameters != null)
            payload["params"] = parameters;

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, token);

        var buffer = new byte[8192];
        while (true)
        {
            using var memory = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, token);
                memory.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            var text = Encoding.UTF8.GetString(memory.ToArray());
            var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("id", out var responseId) && responseId.GetInt32() == id)
                return doc;

            doc.Dispose();
        }
    }

    private static bool TryExtractTokenPair(string raw, out string accessToken, out string? refreshToken)
    {
        var candidates = new List<(string Key, string Value)>();
        CollectTokenCandidates(raw, "raw", candidates, 0);
        accessToken = candidates.FirstOrDefault(x => IsAccessTokenKey(x.Key)).Value;
        refreshToken = candidates.FirstOrDefault(x => IsRefreshTokenKey(x.Key)).Value;

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            accessToken = candidates
                .Where(x => !IsRefreshTokenKey(x.Key))
                .Select(x => x.Value)
                .FirstOrDefault(LooksLikeJwt) ?? "";
        }

        return !string.IsNullOrWhiteSpace(accessToken);
    }

    private static void CollectTokenCandidates(string? text, string key, List<(string Key, string Value)> candidates, int depth)
    {
        if (string.IsNullOrWhiteSpace(text) || depth > 5)
            return;

        if ((key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
             key.Contains("jwt", StringComparison.OrdinalIgnoreCase) ||
             key.Contains("authorization", StringComparison.OrdinalIgnoreCase)) &&
            text.Length > 20)
            candidates.Add((key, CleanTokenValue(text)));

        var trimmed = text.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal) && !trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            ExtractRegexToken(raw: text, candidates);
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            CollectTokenCandidates(doc.RootElement, key, candidates, depth + 1);
        }
        catch
        {
            ExtractRegexToken(raw: text, candidates);
        }
    }

    private static void CollectTokenCandidates(JsonElement element, string key, List<(string Key, string Value)> candidates, int depth)
    {
        if (depth > 8)
            return;

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("name", out var name) &&
                element.TryGetProperty("value", out var value) &&
                name.ValueKind == JsonValueKind.String &&
                value.ValueKind == JsonValueKind.String)
            {
                var cookieName = name.GetString() ?? "";
                if (cookieName.Contains("token", StringComparison.OrdinalIgnoreCase))
                    candidates.Add((cookieName, CleanTokenValue(value.GetString() ?? "")));
            }

            foreach (var property in element.EnumerateObject())
                CollectTokenCandidates(property.Value, key + "." + property.Name, candidates, depth + 1);
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
                CollectTokenCandidates(item, key + "[" + index++ + "]", candidates, depth + 1);
            return;
        }

        if (element.ValueKind == JsonValueKind.String)
            CollectTokenCandidates(element.GetString(), key, candidates, depth + 1);
    }

    private static void ExtractRegexToken(string raw, List<(string Key, string Value)> candidates)
    {
        foreach (Match match in Regex.Matches(raw, """\\?["']?(?<key>access[_-]?token|accessToken|refresh[_-]?token|refreshToken|jwt|token|Authorization)\\?["']?\s*[:=]\s*(?:\\?["'](?<quoted>[^"'\\]{20,})\\?["']|(?<plain>[^;"'\s,}{\\]{20,}))""", RegexOptions.IgnoreCase))
        {
            var value = match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["plain"].Value;
            candidates.Add((match.Groups["key"].Value, CleanTokenValue(value)));
        }
    }

    private static string CleanTokenValue(string value)
    {
        var token = value.Trim();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = token[7..].Trim();
        return token.Trim('"', '\'', '\\');
    }

    private static bool IsAccessTokenKey(string key)
    {
        return key.Contains("access_token", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("accessToken", StringComparison.OrdinalIgnoreCase) ||
               (key.Contains("access", StringComparison.OrdinalIgnoreCase) && key.Contains("token", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRefreshTokenKey(string key)
    {
        return key.Contains("refresh_token", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("refreshToken", StringComparison.OrdinalIgnoreCase) ||
               (key.Contains("refresh", StringComparison.OrdinalIgnoreCase) && key.Contains("token", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeJwt(string value)
    {
        return value.Count(x => x == '.') >= 2 && value.Length > 80;
    }

    private void TryDeleteAuthBrowserProfile()
    {
        try
        {
            if (Directory.Exists(authBrowserProfileDir))
                Directory.Delete(authBrowserProfileDir, true);
        }
        catch
        {
        }
    }

    private sealed record BrowserTokenImport(string AccessToken, string? RefreshToken, string Source);

    private TextBox TokenTextBox(string watermark)
    {
        return new TextBox
        {
            Height = 42,
            Watermark = watermark,
            Background = Brush("#182231"),
            Foreground = Brush("#f5f7ff"),
            BorderBrush = Brush("#2b3550")
        };
    }

    private TextBlock Label(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = Brush("#8f9aad"),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold
        };
    }

    private Border HintCard(string heading, string body)
    {
        return new Border
        {
            Background = Brush("#101a27"),
            BorderBrush = Brush("#263244"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = heading, Foreground = Brush("#f5f7ff"), FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = body, Foreground = Brush("#a9b2c5"), TextWrapping = TextWrapping.Wrap, FontSize = 13 }
                }
            }
        };
    }

    private async Task RefreshUpcomingBookingsAsync()
    {
        if (DateTime.UtcNow < upcomingBookingsRefreshAfterUtc) return;
        upcomingBookingsRefreshAfterUtc = DateTime.UtcNow.AddSeconds(30);

        try
        {
            var now = DateTime.Now;
            var bookings = await service.LoadBookingsAsync(config, now.AddMinutes(-1), now.AddDays(30), CancellationToken.None);
            upcomingBookingsByHost.Clear();
            foreach (var booking in bookings
                .Where(x => !IsInactiveBookingStatus(x.Status) && x.ToLocal > now)
                .OrderBy(x => x.FromLocal))
            {
                foreach (var hostId in booking.HostIds.Where(x => x > 0))
                    upcomingBookingsByHost.TryAdd(hostId, booking);
            }
        }
        catch
        {
            upcomingBookingsRefreshAfterUtc = DateTime.UtcNow.AddSeconds(10);
        }
    }

    private static bool HasClientAtHost(PcRecord record) =>
        record.HasSession
        || !string.IsNullOrWhiteSpace(record.ClientUuid)
        || !string.IsNullOrWhiteSpace(record.ClientName)
        || !string.IsNullOrWhiteSpace(record.ClientPhone);

    private bool CompletePendingCleaningForNewClient(PcRecord record)
    {
        if (!TryGetPendingCleaning(record.Name, out var pending)) return false;

        var cleanedAtUtc = DateTime.UtcNow;
        var entry = FindCleaningAuditEntry(record.Name, pending.ClientLeftAtUtc);
        if (entry == null)
        {
            entry = new CleaningAuditEntry
            {
                Operator = pending.Operator,
                HostName = record.Name,
                ClientLeftAtUtc = pending.ClientLeftAtUtc
            };
            cleaningAuditEntries.Add(entry);
        }

        entry.LastReminderAtUtc ??= pending.LastReminderAtUtc;
        entry.CleanedAtUtc = cleanedAtUtc;
        entry.SuspiciousReason = CleaningSuspicion(entry.ClientLeftAtUtc, cleanedAtUtc, entry.LastReminderAtUtc);
        RemovePendingCleaning(record.Name);
        cleaningReminderMinutes.Remove(record.Name);
        record.CleaningStatus = "Нет задачи";
        record.FreedAtUtc = null;
        return true;
    }

    private async Task RefreshAsync()
    {
        if (refreshInProgress) return;
        refreshInProgress = true;
        try
        {
            source.Text = "обновляю статусы...";
            var incoming = await service.LoadHostsAsync(config, CancellationToken.None);
            if (incoming.Count == 0)
                incoming = SmartShellService.BuildFallbackHosts();

            var cleaningStateChanged = false;
            foreach (var record in incoming)
            {
                if (records.TryGetValue(record.Name, out var existing))
                {
                    var previousHadClient = HasClientAtHost(existing);
                    var previousCleaning = existing.CleaningStatus;
                    existing.CopyFrom(record);
                    var hasClient = HasClientAtHost(existing);
                    if (!hostsLoadedOnce)
                    {
                        if (hasClient)
                        {
                            existing.CleaningStatus = "Нет задачи";
                            existing.FreedAtUtc = null;
                            cleaningStateChanged |= CompletePendingCleaningForNewClient(existing);
                        }
                        else if (TryGetPendingCleaning(existing.Name, out var pending))
                        {
                            existing.CleaningStatus = "Нужно убрать";
                            existing.FreedAtUtc = pending.ClientLeftAtUtc;
                        }
                        else
                        {
                            existing.CleaningStatus = "Нет задачи";
                            existing.FreedAtUtc = null;
                        }
                        cleaningReminderMinutes.Remove(existing.Name);
                    }
                    else if (previousHadClient && !hasClient)
                    {
                        existing.CleaningStatus = "Нужно убрать";
                        existing.FreedAtUtc = DateTime.UtcNow;
                        SetPendingCleaning(existing.Name, existing.FreedAtUtc.Value);
                        cleaningStateChanged = true;
                        cleaningReminderMinutes.Remove(existing.Name);
                    }
                    else if (hasClient)
                    {
                        existing.CleaningStatus = "Нет задачи";
                        existing.FreedAtUtc = null;
                        cleaningStateChanged |= CompletePendingCleaningForNewClient(existing);
                        cleaningReminderMinutes.Remove(existing.Name);
                    }
                    else if (previousCleaning == "Нужно убрать" && record.SourceStatus == "free")
                    {
                        existing.CleaningStatus = previousCleaning;
                    }
                }
                else
                {
                    if (HasClientAtHost(record))
                    {
                        cleaningStateChanged |= CompletePendingCleaningForNewClient(record);
                    }
                    else if (TryGetPendingCleaning(record.Name, out var pending))
                    {
                        record.CleaningStatus = "Нужно убрать";
                        record.FreedAtUtc = pending.ClientLeftAtUtc;
                    }
                    records[record.Name] = record;
                }
            }

            if (cleaningStateChanged) SaveCleaningAuditState();
            hostsLoadedOnce = true;
            await RefreshUpcomingBookingsAsync();
            await RefreshAdminCallsAsync(true);
            await CheckCleaningRemindersAsync();
            RefreshMap();
            if (screen == "dashboard") await RefreshDashboardAsync();
            if (screen == "details") RenderComputerGroups();
            UpdateSelectedPanel();
            UpdateSummary("обновлено " + DateTime.Now.ToString("HH:mm:ss"));
        }
        catch (Exception ex)
        {
            source.Text = "ошибка обновления: " + ex.Message;
        }
        finally
        {
            refreshInProgress = false;
        }
    }

    private void UpdateSelectedPanel()
    {
        selectedHostKeys.RemoveWhere(name => !records.ContainsKey(name));
        var selectedHosts = records.Values
            .Where(x => selectedHostKeys.Contains(x.Name))
            .OrderBy(x => NaturalKey(x.Name))
            .ToList();
        SetInspectorVisibility(screen == "map" && selectedHosts.Count > 0);
        selected = selectedHosts.FirstOrDefault();
        var bulk = selectedHosts.Count > 1;
        if (singleSelectionActions != null) singleSelectionActions.IsVisible = selectedHosts.Count == 1;
        if (bulkSelectionActions != null) bulkSelectionActions.IsVisible = bulk;

        if (bulk)
        {
            PopulateBulkSelectionActions(selectedHosts);
            selectedTitle.Text = "Выбрано: " + selectedHosts.Count;
            selectedBody.Text = string.Join(", ", selectedHosts.Select(x => x.Name)) + "\n\n" +
                "Включено: " + selectedHosts.Count(x => x.Online) +
                " | Сеансов: " + selectedHosts.Count(x => x.HasSession) +
                " | Выключено: " + selectedHosts.Count(x => !x.Online);
            return;
        }

        if (selected == null)
        {
            selectedTitle.Text = "ПК не выбран";
            selectedBody.Text = "";
            return;
        }

        if (cleanedButton != null)
        {
            cleanedButton.IsEnabled = NeedsCleaning(selected);
            cleanedButton.Opacity = cleanedButton.IsEnabled ? 1 : 0.45;
        }

        selectedTitle.Text = selected.Name;
        selectedBody.Text =
            "Статус: " + selected.StatusTitle + "\n" +
            "Уборка: " + selected.CleaningStatus + "\n" +
            "Прошло после ухода: " + CleaningElapsedText(selected) + "\n" +
            "Сидит до: " + ActiveSessionUntilText(selected) + "\n" +
            "Клиент: " + (string.IsNullOrWhiteSpace(selected.ClientName) ? "-" : selected.ClientName) + "\n" +
            "Телефон: " + (string.IsNullOrWhiteSpace(selected.ClientPhone) ? "-" : selected.ClientPhone) + "\n" +
            "IP: " + (string.IsNullOrWhiteSpace(selected.IpAddress) ? "-" : selected.IpAddress) + "\n" +
            "MAC: " + (string.IsNullOrWhiteSpace(selected.MacAddress) ? "-" : selected.MacAddress) + "\n" +
            "Shell: " + (string.IsNullOrWhiteSpace(selected.ShellVersion) ? "-" : selected.ShellVersion) + "\n" +
            "Сеансов: " + selected.ActiveSessionCount + " | Броней: " + selected.BookingCount;
    }

    private void SetInspectorVisibility(bool visible)
    {
        sidePanel.IsVisible = visible;
        if (inspectorColumn != null)
            inspectorColumn.Width = visible ? new GridLength(currentInspectorWidth) : new GridLength(0);
    }

    private bool NeedsCleaning(PcRecord record) =>
        record.CleaningStatus == "Нужно убрать"
        && (record.FreedAtUtc.HasValue || TryGetPendingCleaning(record.Name, out _));

    private async Task CheckCleaningRemindersAsync()
    {
        var auditChanged = false;
        string? currentOperator = null;
        foreach (var record in records.Values)
        {
            if (record.CleaningStatus != "Нужно убрать" || record.FreedAtUtc == null)
                continue;

            var elapsedMinutes = (DateTime.UtcNow - record.FreedAtUtc.Value).TotalMinutes;
            TryGetPendingCleaning(record.Name, out var pending);
            var auditEntry = FindCleaningAuditEntry(record.Name, record.FreedAtUtc.Value);
            if (auditEntry == null)
            {
                var operatorName = pending?.Operator ?? "";
                if (string.IsNullOrWhiteSpace(operatorName))
                {
                    currentOperator ??= await LoadCurrentCleaningOperatorAsync();
                    operatorName = currentOperator;
                }

                auditEntry = new CleaningAuditEntry
                {
                    Operator = operatorName,
                    HostName = record.Name,
                    ClientLeftAtUtc = record.FreedAtUtc.Value
                };
                cleaningAuditEntries.Add(auditEntry);
                auditChanged = true;
            }

            foreach (var minute in new[] { 10, 20 })
            {
                if (elapsedMinutes < minute)
                    continue;

                if (!cleaningReminderMinutes.TryGetValue(record.Name, out var sent))
                {
                    sent = new HashSet<int>();
                    cleaningReminderMinutes[record.Name] = sent;
                }

                if (sent.Add(minute))
                {
                    var reminderAtUtc = DateTime.UtcNow;
                    auditEntry.LastReminderAtUtc = reminderAtUtc;
                    if (pending != null) pending.LastReminderAtUtc = reminderAtUtc;
                    auditChanged = true;
                    var titleText = record.Name + " свободен " + minute + " мин";
                    var message = "Проверьте, убрано ли место.";
                    ShowToast(titleText, message);
                    ShowWindowsNotification(titleText, message);
                }
            }
        }

        if (!auditChanged) return;
        SaveCleaningAuditState();
        if (screen == "cleaning-log")
            contentHost.Content = BuildCleaningAuditView();
    }

    private static string CleaningElapsedText(PcRecord record)
    {
        if (record.FreedAtUtc == null)
            return "-";

        var elapsed = DateTime.UtcNow - record.FreedAtUtc.Value;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        if (elapsed.TotalHours >= 1)
            return ((int)elapsed.TotalHours) + "ч " + elapsed.Minutes.ToString("00") + "м";
        if (elapsed.TotalMinutes >= 1)
            return ((int)elapsed.TotalMinutes) + "м " + elapsed.Seconds.ToString("00") + "с";
        return elapsed.Seconds.ToString("00") + "с";
    }

    private static string ActiveSessionUntilText(PcRecord record)
    {
        if (!record.HasSession) return "-";
        if (record.SessionEndsAtUtc.HasValue)
            return ActiveSessionDetailedEndText(record);
        var value = SessionDisplayText(record);
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private async Task MarkSelectedCleanedAsync()
    {
        var targets = SelectedHosts().Where(NeedsCleaning).ToList();
        if (targets.Count == 0 && selected != null)
        {
            if (NeedsCleaning(selected))
                targets.Add(selected);
        }
        if (targets.Count == 0)
        {
            ShowToast("Уборка", "Для выбранных ПК уборка не требуется.");
            return;
        }

        var cleanedAtUtc = DateTime.UtcNow;
        var audits = new List<(PcRecord Target, DateTime ClientLeftAtUtc, PendingCleaningAudit? Pending, CleaningAuditEntry? Entry)>();
        foreach (var target in targets)
        {
            PendingCleaningAudit? pendingAudit = null;
            if (TryGetPendingCleaning(target.Name, out var persistedPending))
                pendingAudit = persistedPending;
            DateTime? clientLeftAtUtc = target.FreedAtUtc;
            if (!clientLeftAtUtc.HasValue)
                clientLeftAtUtc = pendingAudit?.ClientLeftAtUtc;
            if (clientLeftAtUtc.HasValue)
                audits.Add((target, clientLeftAtUtc.Value, pendingAudit,
                    FindCleaningAuditEntry(target.Name, clientLeftAtUtc.Value)));
        }

        static bool OperatorMissing(string? value) => string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "Оператор не определён", StringComparison.OrdinalIgnoreCase);
        var operatorName = audits.Any(x => OperatorMissing(x.Entry?.Operator) && OperatorMissing(x.Pending?.Operator))
            ? await LoadCurrentCleaningOperatorAsync()
            : "";
        foreach (var item in audits)
        {
            var entry = item.Entry;
            if (entry == null)
            {
                entry = new CleaningAuditEntry
                {
                    HostName = item.Target.Name,
                    ClientLeftAtUtc = item.ClientLeftAtUtc
                };
                cleaningAuditEntries.Add(entry);
            }

            if (OperatorMissing(entry.Operator))
                entry.Operator = OperatorMissing(item.Pending?.Operator) ? operatorName : item.Pending!.Operator;
            entry.LastReminderAtUtc ??= item.Pending?.LastReminderAtUtc;
            entry.CleanedAtUtc = cleanedAtUtc;
            entry.SuspiciousReason = CleaningSuspicion(item.ClientLeftAtUtc, cleanedAtUtc, entry.LastReminderAtUtc);
        }

        foreach (var target in targets)
        {
            target.CleaningStatus = "Убрано";
            target.FreedAtUtc = null;
            RemovePendingCleaning(target.Name);
            cleaningReminderMinutes.Remove(target.Name);
        }
        SaveCleaningAuditState();
        UpdateSelectedPanel();
        RefreshMap();
        UpdateSummary("уборка отмечена " + DateTime.Now.ToString("HH:mm:ss"));
        ShowToast("Уборка", targets.Count == 1
            ? targets[0].Name + " отмечен как убранный."
            : "Убранными отмечено мест: " + targets.Count + ".");

        if (!config.AutoShutdownAfterCleaned)
            return;

        var shutdownTargets = targets.Where(x => !IsAutoShutdownExcluded(x)).ToList();
        if (shutdownTargets.Count == 0)
            return;

        if (shutdownTargets.Count > 1)
        {
            await SendBulkHostCommandAsync(shutdownTargets, "SHUTDOWN");
            return;
        }

        var host = shutdownTargets[0];
        if (host.HostId <= 0)
            return;

        await service.SendHostCommandAsync(config, host.HostId, "SHUTDOWN", null, CancellationToken.None);
        ShowToast(host.Name, "Команда выключения отправлена.");
    }

    private void UpdateSummary(string note)
    {
        var needClean = records.Values.Count(x => x.CleaningStatus == "Нужно убрать");
        var busy = records.Values.Count(x => x.SourceStatus == "busy");
        var free = records.Values.Count(x => x.SourceStatus == "free");
        summary.Text = "занято: " + busy + " | свободно: " + free + " | уборка: " + needClean;
        source.Text = note + " | ПК: " + records.Count;
    }

    private void ConfigureWindowsNotifications()
    {
        try
        {
            windowsNotifier.Text = "SmartShell уборка";
            windowsNotifier.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
                ?? System.Drawing.SystemIcons.Application;
            var menu = new System.Windows.Forms.ContextMenuStrip();
            var showItem = new System.Windows.Forms.ToolStripMenuItem("Открыть SmartShell Panel");
            showItem.Click += (_, _) => Dispatcher.UIThread.Post(RestoreFromTray);
            var exitItem = new System.Windows.Forms.ToolStripMenuItem("Полностью закрыть");
            exitItem.Click += (_, _) => Dispatcher.UIThread.Post(ExitApplication);
            menu.Items.Add(showItem);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(exitItem);
            windowsNotifier.ContextMenuStrip = menu;
            windowsNotifier.DoubleClick += (_, _) => Dispatcher.UIThread.Post(RestoreFromTray);
            windowsNotifier.Visible = true;
        }
        catch
        {
        }
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
        if (trayHintShown) return;
        trayHintShown = true;
        ShowWindowsNotification("SmartShell Panel", "Приложение продолжает работать в трее.");
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        allowApplicationExit = true;
        Close();
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void ShowWindowsNotification(string notificationTitle, string message, bool playSound = false)
    {
        FlashTaskbar();
        try
        {
            if (playSound)
                System.Media.SystemSounds.Exclamation.Play();

            windowsNotifier.BalloonTipTitle = notificationTitle;
            windowsNotifier.BalloonTipText = message;
            windowsNotifier.BalloonTipIcon = playSound
                ? System.Windows.Forms.ToolTipIcon.Warning
                : System.Windows.Forms.ToolTipIcon.Info;
            windowsNotifier.ShowBalloonTip(8000);
        }
        catch
        {
            if (playSound)
            {
                try { Console.Beep(900, 450); }
                catch { }
            }
        }
    }

    private void ShowToast(string toastTitle, string message)
    {
        FlashTaskbar();
        var card = new Border
        {
            Width = 320,
            Background = Brush("#121a24"),
            BorderBrush = Brush("#334155"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            BoxShadow = new BoxShadows(new BoxShadow { Color = Color.Parse("#66000000"), Blur = 18, OffsetY = 6 })
        };

        var close = new Button
        {
            Content = "×",
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = Brush("#8f9aad"),
            BorderBrush = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var textStack = new StackPanel { Spacing = 4 };
        textStack.Children.Add(new TextBlock
        {
            Text = toastTitle,
            Foreground = Brush("#f5f7ff"),
            FontWeight = FontWeight.SemiBold,
            FontSize = 14
        });
        textStack.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = Brush("#a9b2c5"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        });
        grid.Children.Add(textStack);
        Grid.SetColumn(close, 1);
        grid.Children.Add(close);
        card.Child = grid;

        void RemoveToast()
        {
            toastHost.Children.Remove(card);
        }

        close.Click += (_, _) => RemoveToast();
        toastHost.Children.Insert(0, card);

        while (toastHost.Children.Count > 4)
            toastHost.Children.RemoveAt(toastHost.Children.Count - 1);

        var removeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        removeTimer.Tick += (_, _) =>
        {
            removeTimer.Stop();
            RemoveToast();
        };
        removeTimer.Start();
    }

    private void FlashTaskbar()
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;
        var info = new FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
            Handle = handle,
            Flags = FlashTray | FlashUntilForeground,
            Count = uint.MaxValue,
            Timeout = 0
        };
        FlashWindowEx(ref info);
    }

    private static string ShortName(string name)
    {
        return name.StartsWith("PC", StringComparison.OrdinalIgnoreCase) ? name[2..].TrimStart('0') : name;
    }

    private static string HostHoverIcon(PcRecord record)
    {
        if (IsConnectionLost(record)) return "⚠";
        if (record.HasSession) return "◷";
        if (IsShellDisabled(record)) return "×";
        if (IsServiceMode(record)) return "▲";
        if (!record.Online && record.SourceStatus == "offline") return "⏻";
        if (record.CleaningStatus == "Убрано") return "✓";
        return "⏻";
    }

    private static string HostHoverStatusText(PcRecord record)
    {
        if (IsConnectionLost(record)) return "Потеряна связь";
        if (record.HasSession)
            return "Активный сеанс";
        if (IsShellDisabled(record)) return "Shell снят";
        if (IsServiceMode(record)) return "Обслуживание";
        if (!record.Online && record.SourceStatus == "offline") return "Выключен";
        if (record.CleaningStatus == "Убрано") return "Убран";
        if (record.Online || record.SourceStatus == "free") return "Включен";
        return record.StatusTitle;
    }

    private static string HostHoverPlaceText(PcRecord record)
    {
        var place = ShortName(record.Name);
        if (record.Name.StartsWith("PS", StringComparison.OrdinalIgnoreCase))
            return "PlayStation • " + place;

        var group = "Premium";
        var digits = new string(record.Name.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, out var number) && number >= 16 && number <= 29)
            group = "Standart";

        return group + " • " + place;
    }

    private static string NormalizeName(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Equals("PS1", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("PS5-1", StringComparison.OrdinalIgnoreCase)) return "PS1";
        if (trimmed.Equals("PS2", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("PS5-2", StringComparison.OrdinalIgnoreCase)) return "PS2";
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? "PC" + n.ToString("00") : trimmed;
    }

    private static string NaturalKey(string name)
    {
        if (name.StartsWith("PS", StringComparison.OrdinalIgnoreCase)) return "ZZ" + name;
        var digits = new string(name.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n.ToString("000") : name;
    }

    private static IBrush StatusBrush(PcRecord record)
    {
        if (IsConnectionLost(record)) return Brush(UiLabAppearance.PcConnectionLost);
        if (record.HasSession) return Brush(UiLabAppearance.PcSession);
        if (IsShellDisabled(record)) return Brush(UiLabAppearance.PcShellOff);
        if (IsServiceMode(record)) return Brush(UiLabAppearance.PcService);
        if (HasLoggedInClient(record)) return Brush(UiLabAppearance.PcLoggedIn);
        if (record.CleaningStatus == "Убрано") return Brush(UiLabAppearance.PcCleaned);
        if (record.Online || record.SourceStatus == "free") return Brush(UiLabAppearance.PcFree);

        return record.SourceStatus switch
        {
            "service" => Brush(UiLabAppearance.PcService),
            "locked" => Brush(UiLabAppearance.PcShellOff),
            _ => Brush(UiLabAppearance.PcOffline)
        };
    }

    private static IBrush StatusFill(PcRecord record)
    {
        if (IsConnectionLost(record)) return StatusTint(UiLabAppearance.PcConnectionLost);
        if (record.HasSession) return StatusTint(UiLabAppearance.PcSession);
        if (IsShellDisabled(record)) return StatusTint(UiLabAppearance.PcShellOff);
        if (IsServiceMode(record)) return StatusTint(UiLabAppearance.PcService);
        if (HasLoggedInClient(record)) return StatusTint(UiLabAppearance.PcLoggedIn);
        if (record.CleaningStatus == "Убрано") return StatusTint(UiLabAppearance.PcCleaned);
        if (record.Online || record.SourceStatus == "free") return StatusTint(UiLabAppearance.PcFree);

        return record.SourceStatus switch
        {
            "service" => StatusTint(UiLabAppearance.PcService),
            "locked" => StatusTint(UiLabAppearance.PcShellOff),
            _ => StatusTint(UiLabAppearance.PcOffline)
        };
    }

    private static IBrush StatusTint(string hex)
    {
        var color = Color.Parse(hex);
        var alpha = (byte)Math.Round(255 * UiLabAppearance.PcCardOpacity);
        if (UiLabAppearance.HasDragonMotif)
        {
            var surface = Color.Parse(UiLabAppearance.Resolve("#111820"));
            return new SolidColorBrush(Color.FromArgb(
                alpha,
                (byte)(surface.R * 0.78 + color.R * 0.22),
                (byte)(surface.G * 0.78 + color.G * 0.22),
                (byte)(surface.B * 0.78 + color.B * 0.22)));
        }
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    private static bool IsConnectionLost(PcRecord record)
    {
        return !record.Online && record.HasSession;
    }

    private static bool HasLoggedInClient(PcRecord record)
    {
        return !record.HasSession
            && (record.Online || record.SourceStatus == "free")
            && (!string.IsNullOrWhiteSpace(record.ClientUuid)
                || !string.IsNullOrWhiteSpace(record.ClientName)
                || !string.IsNullOrWhiteSpace(record.ClientPhone));
    }

    private static bool IsServiceMode(PcRecord record)
    {
        var mode = (record.ShellMode ?? "").Trim().ToLowerInvariant();
        return record.SourceStatus == "service"
            || (record.Online && record.InService)
            || mode.Contains("admin")
            || mode.Contains("high")
            || mode.Contains("access");
    }

    private static bool IsHighAccessMode(PcRecord record)
    {
        var mode = (record.ShellMode ?? "").Trim().ToLowerInvariant();
        return mode.Contains("admin") || mode.Contains("high") || mode.Contains("access");
    }

    private static bool IsShellDisabled(PcRecord record)
    {
        if (record.Locked) return true;
        var mode = (record.ShellMode ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(mode)) return false;
        return mode.Contains("disable")
            || mode.Contains("disabled")
            || mode.Contains("off")
            || mode.Contains("without")
            || mode.Contains("none")
            || mode.Contains("no_shell")
            || mode.Contains("noshell")
            || mode.Contains("desktop");
    }

    private static SolidColorBrush Brush(string hex)
    {
        var key = hex.ToLowerInvariant();
        var accent = key switch
        {
            "#2f8cff" => PrimaryAccent,
            "#4ea1ff" => BrightAccent,
            "#102a4d" => DeepAccent,
            "#16243a" => SoftAccent,
            "#24466f" => AccentBorder,
            _ => null
        };
        if (accent != null) return accent;

        if (!ThemeBrushes.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(ResolvedThemeColor(key));
            ThemeBrushes[key] = brush;
        }
        return brush;
    }

    private static Color ResolvedThemeColor(string key)
    {
        return Color.Parse(UiLabAppearance.Resolve(key));
    }

    private static SolidColorBrush PanelBrush(string hex, byte _)
    {
        var color = Color.Parse(UiLabAppearance.Resolve(hex));
        var alpha = (byte)Math.Round(255 * UiLabAppearance.PanelOpacity);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        ThemePanelBrushes.Add((hex, brush));
        return brush;
    }

    private static IBrush PageBackground() =>
        UiLabAppearance.HasBackdrop ? Brushes.Transparent : Brush("#080d12");

    private static Dictionary<string, Point> BuildLayout()
    {
        var m = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        void Set(int n, double x, double y) => m["PC" + n.ToString("00")] = new Point(x, y);
        Set(1, 820, 370); Set(2, 820, 298); Set(3, 820, 226); Set(4, 820, 154); Set(5, 820, 82);
        Set(6, 672, 370); Set(7, 672, 298); Set(8, 672, 226); Set(9, 672, 154); Set(10, 672, 82);
        Set(11, 526, 82); Set(12, 526, 154); Set(13, 526, 226); Set(14, 526, 298); Set(15, 526, 370);
        Set(16, 382, 586); Set(17, 382, 514); Set(18, 382, 442); Set(19, 382, 370); Set(20, 382, 298); Set(21, 382, 226); Set(22, 382, 154); Set(23, 382, 82);
        Set(24, 240, 82); Set(25, 168, 82); Set(26, 96, 82); Set(27, 96, 226); Set(28, 168, 226); Set(29, 240, 226);
        Set(30, 26, 442); Set(31, 26, 370);
        m["PS2"] = new Point(168, 598);
        m["PS1"] = new Point(240, 598);
        return m;
    }
}
