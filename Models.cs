using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartShellCleanerAvalonia;

internal sealed class AppConfig
{
    public string? StatusFile { get; set; }
    public int PollSeconds { get; set; } = 15;
    public bool SmartShellEnabled { get; set; } = true;
    public string? SmartShellEndpoint { get; set; } = "https://billing.smartshell.gg/api/graphql";
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public bool AutoShutdownAfterCleaned { get; set; } = true;
    public List<string> NoAutoShutdownNames { get; set; } = new() { "PC30", "PC31" };
}

internal sealed class PcRecord : INotifyPropertyChanged
{
    private string sourceStatus = "unknown";
    private string cleaningStatus = "Нет задачи";
    private string? clientName;
    private string? clientPhone;
    private string? clientUuid;
    private string? activeTariffTitle;
    private string? activeWindow;
    private string? ipAddress;
    private string? macAddress;
    private string? shellVersion;
    private string? shellMode;
    private string? sessionTimeLeft;
    private int sessionTimeLeftSeconds;
    private DateTime? sessionEndsAtUtc;
    private int activeSessionCount;
    private int bookingCount;
    private int groupId;
    private int hostId;
    private int clientSessionId;
    private string? groupTitle;
    private DateTime? sessionStartedAtUtc;
    private bool online;
    private bool inService;
    private bool locked;
    private DateTime? freedAtUtc;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; set; } = "";
    public int HostId { get => hostId; set => Set(ref hostId, value); }
    public int GroupId { get => groupId; set => Set(ref groupId, value); }
    public string? GroupTitle { get => groupTitle; set => Set(ref groupTitle, value); }
    public int ClientSessionId { get => clientSessionId; set => Set(ref clientSessionId, value); }
    public string SourceStatus { get => sourceStatus; set => Set(ref sourceStatus, value); }
    public string CleaningStatus { get => cleaningStatus; set => Set(ref cleaningStatus, value); }
    public string? ClientName { get => clientName; set => Set(ref clientName, value); }
    public string? ClientPhone { get => clientPhone; set => Set(ref clientPhone, value); }
    public string? ClientUuid { get => clientUuid; set => Set(ref clientUuid, value); }
    public string? ActiveTariffTitle { get => activeTariffTitle; set => Set(ref activeTariffTitle, value); }
    public string? ActiveWindow { get => activeWindow; set => Set(ref activeWindow, value); }
    public string? IpAddress { get => ipAddress; set => Set(ref ipAddress, value); }
    public string? MacAddress { get => macAddress; set => Set(ref macAddress, value); }
    public string? ShellVersion { get => shellVersion; set => Set(ref shellVersion, value); }
    public string? ShellMode { get => shellMode; set => Set(ref shellMode, value); }
    public string? SessionTimeLeft { get => sessionTimeLeft; set => Set(ref sessionTimeLeft, value); }
    public int SessionTimeLeftSeconds { get => sessionTimeLeftSeconds; set => Set(ref sessionTimeLeftSeconds, value); }
    public DateTime? SessionStartedAtUtc { get => sessionStartedAtUtc; set => Set(ref sessionStartedAtUtc, value); }
    public DateTime? SessionEndsAtUtc { get => sessionEndsAtUtc; set => Set(ref sessionEndsAtUtc, value); }
    public int ActiveSessionCount { get => activeSessionCount; set => Set(ref activeSessionCount, value); }
    public int BookingCount { get => bookingCount; set => Set(ref bookingCount, value); }
    public bool Online { get => online; set => Set(ref online, value); }
    public bool InService { get => inService; set => Set(ref inService, value); }
    public bool Locked { get => locked; set => Set(ref locked, value); }
    public DateTime? FreedAtUtc { get => freedAtUtc; set => Set(ref freedAtUtc, value); }

    public string StatusTitle => SourceStatus switch
    {
        "busy" => "Занят",
        "free" => "Свободен",
        "offline" => "Оффлайн",
        "service" => "Сервис",
        "locked" => "Заблокирован",
        _ => "Неизвестно"
    };

    public bool HasSession => ClientSessionId > 0 || ActiveSessionCount > 0;

    public void CopyFrom(PcRecord other)
    {
        HostId = other.HostId;
        GroupId = other.GroupId;
        GroupTitle = other.GroupTitle;
        ClientSessionId = other.ClientSessionId;
        SourceStatus = other.SourceStatus;
        ClientName = other.ClientName;
        ClientPhone = other.ClientPhone;
        ClientUuid = other.ClientUuid;
        ActiveTariffTitle = other.ActiveTariffTitle;
        ActiveWindow = other.ActiveWindow;
        IpAddress = other.IpAddress;
        MacAddress = other.MacAddress;
        ShellVersion = other.ShellVersion;
        ShellMode = other.ShellMode;
        SessionTimeLeft = other.SessionTimeLeft;
        SessionTimeLeftSeconds = other.SessionTimeLeftSeconds;
        SessionStartedAtUtc = other.SessionStartedAtUtc;
        SessionEndsAtUtc = other.SessionEndsAtUtc;
        ActiveSessionCount = other.ActiveSessionCount;
        BookingCount = other.BookingCount;
        Online = other.Online;
        InService = other.InService;
        Locked = other.Locked;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}

internal sealed class PsTimerSession : INotifyPropertyChanged
{
    private bool completed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; init; } = Guid.NewGuid();
    public string Device { get; init; } = "PS1";
    public string Client { get; init; } = "";
    public string Source { get; init; } = "Ручной запуск";
    public DateTime StartedAt { get; init; } = DateTime.Now;
    public DateTime EndsAt { get; init; } = DateTime.Now.AddHours(1);
    public bool Completed { get => completed; set => Set(ref completed, value); }

    public TimeSpan Remaining => EndsAt - DateTime.Now;
    public string RemainingText
    {
        get
        {
            var left = Remaining;
            if (left <= TimeSpan.Zero) return "завершен";
            if (left.TotalHours >= 1) return ((int)left.TotalHours).ToString("0") + "ч " + left.Minutes.ToString("00") + "м";
            return left.Minutes.ToString("00") + "м " + left.Seconds.ToString("00") + "с";
        }
    }

    public string TimeRange => StartedAt.ToString("HH:mm") + " - " + EndsAt.ToString("HH:mm");

    public void Tick()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RemainingText)));
        if (!Completed && Remaining <= TimeSpan.Zero)
            Completed = true;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}

internal sealed class ShopItem
{
    public string Type { get; set; } = "GOOD";
    public int EntityId { get; set; }
    public string Title { get; set; } = "";
    public string GroupTitle { get; set; } = "";
    public decimal Cost { get; set; }
    public int? Stock { get; set; }
    public bool AvailableToBuy { get; set; } = true;
    public string CostText => Cost.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " ₽";
    public string StockText => Stock.HasValue ? Stock.Value + " шт." : "-";
}

internal sealed class ClientInfo
{
    public int Id { get; set; }
    public string Uuid { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Phone { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string MiddleName { get; set; } = "";
    public string GroupTitle { get; set; } = "";
    public string RoleTitle { get; set; } = "";
    public string LastComment { get; set; } = "";
    public bool LastCommentImportant { get; set; }
    public string BirthDateText { get; set; } = "";
    public string LastActivityText { get; set; } = "";
    public DateTime? LastActivityAtUtc { get; set; }
    public string CreatedAtText { get; set; } = "";
    public DateTime? CreatedAtUtc { get; set; }
    public string StatusText { get; set; } = "Активен";
    public decimal TotalHours { get; set; }
    public string SubscriptionText { get; set; } = "Нет активного абонемента";
    public decimal Deposit { get; set; }
    public decimal Bonus { get; set; }
    public decimal PersonalDiscount { get; set; }
    public string DiscountText { get; set; } = "Нет скидки";

    public string Display
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Nickname) && !string.IsNullOrWhiteSpace(Phone)) return Nickname + " / " + Phone;
            if (!string.IsNullOrWhiteSpace(Nickname)) return Nickname;
            return string.IsNullOrWhiteSpace(Phone) ? Uuid : Phone;
        }
    }

    public string BalanceText => Deposit.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " ₽";
    public string BonusText => Bonus.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " ₽";
}

internal sealed class ClientListPage
{
    public List<ClientInfo> Entries { get; set; } = new();
    public int CurrentPage { get; set; } = 1;
    public int LastPage { get; set; } = 1;
    public int Total { get; set; }
}

internal sealed class ClientHistoryEntry
{
    public string DateText { get; set; } = "";
    public string Operator { get; set; } = "—";
    public string Category { get; set; } = "—";
    public string Payment { get; set; } = "—";
    public string Total { get; set; } = "—";
    public string BonusText { get; set; } = "";
    public bool IsRefunded { get; set; }
}

internal sealed class ClientHistoryPage
{
    public List<ClientHistoryEntry> Entries { get; set; } = new();
    public int CurrentPage { get; set; } = 1;
    public int LastPage { get; set; } = 1;
}

internal sealed class WorkShiftPayment
{
    public int Id { get; set; }
    public DateTime CreatedAtLocal { get; set; }
    public string Category { get; set; } = "Платёж";
    public string ClientUuid { get; set; } = "";
    public string ClientDisplay { get; set; } = "Гость";
    public string Method { get; set; } = "—";
    public string MethodCode { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal Total { get; set; }
    public decimal Bonus { get; set; }
    public decimal CashSum { get; set; }
    public decimal CardSum { get; set; }
    public bool IsRefunded { get; set; }
    public bool NotRefundable { get; set; }
    public List<WorkShiftPaymentLine> Lines { get; set; } = new();

    public string DateText => CreatedAtLocal.ToString("dd.MM.yyyy HH:mm");
    public string TotalText => Total.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " ₽";
    public bool IsPostPayment => MethodCode.Contains("POST", StringComparison.OrdinalIgnoreCase)
        || Status.Contains("POST", StringComparison.OrdinalIgnoreCase)
        || Status.Equals("UNPAID", StringComparison.OrdinalIgnoreCase);
}

internal sealed class WorkShiftPaymentLine
{
    public string Type { get; set; } = "";
    public string Title { get; set; } = "Позиция";
    public int Amount { get; set; } = 1;
    public decimal Sum { get; set; }
}

internal sealed class EventLogEntry
{
    public DateTime CreatedAtLocal { get; set; }
    public string Type { get; set; } = "";
    public string Category { get; set; } = "Событие";
    public string Host { get; set; } = "";
    public string Subject { get; set; } = "";
    public string SubjectClientUuid { get; set; } = "";
    public string Description { get; set; } = "";
    public string Object { get; set; } = "";
    public string ObjectClientUuid { get; set; } = "";
}

internal sealed class EventLogPage
{
    public List<EventLogEntry> Entries { get; set; } = new();
    public int WorkShiftId { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int LastPage { get; set; } = 1;
    public int Count { get; set; }
}

internal sealed class DashboardSnapshot
{
    public int WorkShiftId { get; set; }
    public string WorkerName { get; set; } = "Оператор";
    public string WorkerPhone { get; set; } = "";
    public DateTime? StartedAtLocal { get; set; }
    public DateTime? FinishedAtLocal { get; set; }
    public decimal Total { get; set; }
    public decimal Cash { get; set; }
    public decimal Card { get; set; }
    public decimal Deposit { get; set; }
    public decimal OnlineDeposit { get; set; }
    public decimal Bonus { get; set; }
    public decimal CashOnStart { get; set; }
    public decimal Goods { get; set; }
    public decimal Services { get; set; }
    public decimal Tariffs { get; set; }
    public decimal DepositTopUps { get; set; }
    public decimal Tips { get; set; }
    public decimal CashOrdersDelta { get; set; }
    public List<DashboardTaskInfo> Tasks { get; set; } = new();
    public List<DashboardActiveClient> ActiveClients { get; set; } = new();

    public decimal CashInBox => CashOnStart + Cash + CashOrdersDelta;
}

internal sealed class DashboardTaskInfo
{
    public int Id { get; set; }
    public string Title { get; set; } = "Задача";
    public string Worker { get; set; } = "—";
    public DateTime? DeadlineLocal { get; set; }
    public DateTime? FinishedAtLocal { get; set; }
}

internal sealed class DashboardActiveClient
{
    public string Nickname { get; set; } = "Клиент";
    public decimal Payments { get; set; }
    public int TotalSeconds { get; set; }
    public DateTime? LastActivityLocal { get; set; }
}

internal sealed class RefundPolicy
{
    public bool Enabled { get; set; } = true;
    public int HeadroomMinutes { get; set; } = 5;
    public bool CashboxEnabled { get; set; }
}

internal sealed class ApiAccessInfo
{
    public string DisplayName { get; set; } = "";
    public string Phone { get; set; } = "";
    public bool IsOwner { get; set; }
    public List<string> Roles { get; set; } = new();
    public HashSet<string> Permissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool CanManageClients => IsOwner || Permissions.Contains("client_manage");
    public string RoleText => IsOwner
        ? "Владелец"
        : Roles.Count > 0 ? string.Join(", ", Roles.Select(RoleTitle)) : "роль не определена";

    private static string RoleTitle(string role)
    {
        if (role.Equals("manager", StringComparison.OrdinalIgnoreCase)) return "Менеджер";
        if (role.Equals("operator", StringComparison.OrdinalIgnoreCase)) return "Оператор";
        if (role.Equals("active_operator", StringComparison.OrdinalIgnoreCase)) return "Оператор на смене";
        return role;
    }
}

internal sealed class AdminCallInfo
{
    public int HostId { get; set; }
    public string HostAlias { get; set; } = "ПК";
    public string GroupTitle { get; set; } = "";
    public DateTime CalledAtLocal { get; set; }
}

internal sealed class CartItem
{
    public string Type { get; set; } = "GOOD";
    public int EntityId { get; set; }
    public int HostId { get; set; }
    public int DurationSeconds { get; set; }
    public string Title { get; set; } = "";
    public string ClientUuid { get; set; } = "";
    public string ClientDisplay { get; set; } = "";
    public decimal BonusAmount { get; set; }
    public string Comment { get; set; } = "";
    public int Amount { get; set; } = 1;
    public decimal UnitCost { get; set; }
    public decimal Total => UnitCost * Amount;
    public decimal BonusTotal => BonusAmount * Amount;
    public string TotalText => Total.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " ₽";

    public CartItem Copy()
    {
        return new CartItem
        {
            Type = Type,
            EntityId = EntityId,
            HostId = HostId,
            DurationSeconds = DurationSeconds,
            Title = Title,
            ClientUuid = ClientUuid,
            ClientDisplay = ClientDisplay,
            BonusAmount = BonusAmount,
            Comment = Comment,
            Amount = Amount,
            UnitCost = UnitCost
        };
    }
}

internal sealed class PaymentDraft
{
    public List<CartItem> Items { get; set; } = new();
    public string ClientUuid { get; set; } = "";
    public string Method { get; set; } = "CASH";
    public decimal CardSum { get; set; }
    public string Comment { get; set; } = "";
}

internal sealed class FreeHostInfo
{
    public int Id { get; set; }
    public string Alias { get; set; } = "";
    public string GroupTitle { get; set; } = "";
    public string Display => string.IsNullOrWhiteSpace(GroupTitle) ? Alias : Alias + " / " + GroupTitle;
}

internal sealed class BookingInfo
{
    public int Id { get; set; }
    public List<int> HostIds { get; set; } = new();
    public string ClientUuid { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ClientPhone { get; set; } = "";
    public DateTime FromLocal { get; set; }
    public DateTime ToLocal { get; set; }
    public string Status { get; set; } = "";
    public string Comment { get; set; } = "";

    public string ClientDisplay => !string.IsNullOrWhiteSpace(ClientName) ? ClientName : !string.IsNullOrWhiteSpace(ClientPhone) ? ClientPhone : "-";
}

internal sealed class BookingOverview
{
    public List<BookingHostGroupInfo> Groups { get; set; } = new();
    public List<BookingHostInfo> Hosts { get; set; } = new();
}

internal sealed class BookingHostGroupInfo
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public override string ToString() => Title;
}

internal sealed class BookingHostInfo
{
    public int Id { get; set; }
    public string Alias { get; set; } = "";
    public int Position { get; set; }
    public int GroupId { get; set; }
    public string ClientName { get; set; } = "";
    public string ClientPhone { get; set; } = "";
    public List<BookingSessionInfo> Sessions { get; set; } = new();
}

internal sealed class BookingSessionInfo
{
    public int Id { get; set; }
    public DateTime StartedLocal { get; set; }
    public DateTime FinishedLocal { get; set; }
}

internal sealed class TariffInfo
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int Duration { get; set; }
    public decimal Cost { get; set; }
    public string TimeFrom { get; set; } = "";
    public string TimeTo { get; set; } = "";
    public bool Booked { get; set; }

    public string CostText => Cost.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " ₽";
    public string DurationText
    {
        get
        {
            if (Duration <= 0) return "-";
            if (Duration % 3600 == 0) return (Duration / 3600).ToString() + " ч.";
            if (Duration >= 3600) return (Duration / 3600) + " ч. " + ((Duration % 3600) / 60) + " мин.";
            return (Duration / 60) + " мин.";
        }
    }

    public override string ToString()
    {
        return Title + " | " + CostText + " | " + DurationText;
    }
}

internal sealed class TariffSaleResult
{
    public TariffInfo? Tariff { get; set; }
    public List<CartItem> Items { get; set; } = new();
    public string ClientQuery { get; set; } = "";
    public string ClientUuid { get; set; } = "";
    public string Method { get; set; } = "CASH";
    public decimal CardSum { get; set; }
}
