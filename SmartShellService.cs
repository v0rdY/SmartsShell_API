using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SmartShellCleanerAvalonia;

internal sealed class SmartShellService
{
    private readonly HttpClient http = new();
    private readonly string appDir;
    private static readonly string[] DefaultEventTypes =
    {
        "CLUB_CREATED", "TELEGRAM_LINKED", "TELEGRAM_UNLINKED", "PAYMENT_CREATED", "BONUS_PAYMENT_CREATED",
        "BONUS_UPDATED", "PAYMENT_REFUND", "CASH_ORDER_CREATED", "DEPOSIT_ADDED_ONLINE", "DEPOSIT_UPDATED",
        "CLIENT_SESSION_STARTED", "CLIENT_SESSION_PROLONGED", "CLIENT_SESSION_FINISHED", "CLIENT_SESSION_HOST_CHANGED",
        "CLIENT_SESSION_PAUSED", "CLIENT_SESSION_UNPAUSED", "CLIENT_SESSION_UNPAUSED_AUTO", "HOST_PUNISHED",
        "SEND_RESET_CODE", "SEND_VERIFY_CODE", "USER_RESET_PASSWORD", "USER_REGISTERED", "USER_BANNED",
        "USER_UNBANNED", "USER_DISCOUNT_UPDATED", "CLIENT_AUTHORIZED_FROM_HOST", "WORKER_AUTHORIZED_FROM_BILLING",
        "WORKER_AUTHORIZED_FROM_HOST", "WORK_SHIFT_STARTED", "WORK_SHIFT_FINISHED", "HOST_GROUP_CREATED",
        "HOST_GROUP_DELETED", "HOST_CREATED", "HOST_SWITCHED_GROUP", "WORKER_CREATED", "WORKER_UPDATED",
        "WORKER_DELETED", "WORKER_ENABLED", "TARIFF_CREATED", "TARIFF_UPDATED", "TARIFF_DELETED",
        "SERVICE_CREATED", "SERVICE_UPDATED", "SERVICE_DELETED", "GOOD_CREATED", "GOOD_UPDATED", "GOOD_DELETED",
        "GOOD_LOW_STOCK_THRESHOLD_REACHED", "DISCOUNT_CREATED", "DISCOUNT_UPDATED", "DISCOUNT_DELETED",
        "PROMO_CODE_CREATED", "PROMO_CODE_UPDATED", "PROMO_CODE_DELETED", "PROMO_CODE_USED", "PROMO_CODE_EXPIRED",
        "PROMO_CAMPAIGN_CREATED", "PROMO_CAMPAIGN_UPDATED", "PROMO_CAMPAIGN_DELETED", "PROMO_CAMPAIGN_CODES_GENERATED",
        "PROMO_CAMPAIGN_EXPIRED", "TASK_ADDED", "WAREHOUSE_GOODS_ADDED", "WAREHOUSE_GOODS_DISPOSED",
        "HOST_HARDWARE_CHANGED", "HOST_HARDWARE_APPLIED", "SHELL_DISABLED", "SHELL_ENABLED",
        "SHELL_HIGH_ACCESS_DISABLED", "SHELL_HIGH_ACCESS_ENABLED", "HOST_ADMIN_CALLED", "HOST_ADMIN_CALL_CHECKED",
        "HOST_ADMIN_CALL_FAILED", "CLUB_COMMENT_CREATED", "ACHIEVEMENT_CREATED", "ACHIEVEMENT_DELETED",
        "ACHIEVEMENT_ASSIGNED", "BOOKING_CREATED", "BOOKING_CANCELED", "BOOKING_FAILED_TO_START", "BOOKING_UPDATED",
        "BOOKING_CONFIRMED", "COMBO_CREATED", "COMBO_UPDATED", "COMBO_DELETED", "POST_PAYMENT_CANCELED",
        "DEBT_ACTIVATED", "SETTINGS_UPDATED", "USER_GROUP_UPDATED", "USER_GROUP_CREATED", "USER_GROUP_DELETED",
        "USER_CHANGED_GROUP", "GOOD_CATEGORY_CREATED", "GOOD_CATEGORY_UPDATED", "GOOD_CATEGORY_DELETED", "USER_VERIFIED",
        "USER_DEPOSIT_TRANSFERRED", "NEWS_CREATED", "NEWS_DELETED", "NEWS_UPDATED", "HOST_QR_GENERATE",
        "SHORTCUT_CREATED", "SHORTCUT_UPDATED", "SHORTCUT_DELETED", "SETTINGS_COPIED", "ADVERTISEMENT_APPROVED",
        "ADVERTISEMENT_MODERATION", "ADVERTISEMENT_REJECTED", "ADVERTISEMENT_MANUAL_COMPLETED", "OWNER_CHANGED"
    };

    public SmartShellService()
    {
        appDir = AppContext.BaseDirectory;
    }

    public AppConfig LoadConfig()
    {
        var config = new AppConfig();
        var path = FindConfigPath();
        if (path == null) return config;

        try
        {
            var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path, Encoding.UTF8), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (loaded == null) return config;
            if (!string.IsNullOrWhiteSpace(loaded.StatusFile)) config.StatusFile = MakeAbsolute(Path.GetDirectoryName(path)!, loaded.StatusFile);
            if (loaded.PollSeconds > 0) config.PollSeconds = loaded.PollSeconds;
            if (!string.IsNullOrWhiteSpace(loaded.SmartShellEndpoint)) config.SmartShellEndpoint = loaded.SmartShellEndpoint;
            if (!string.IsNullOrWhiteSpace(loaded.AccessToken)) config.AccessToken = loaded.AccessToken;
            if (!string.IsNullOrWhiteSpace(loaded.RefreshToken)) config.RefreshToken = loaded.RefreshToken;
            config.SmartShellEnabled = loaded.SmartShellEnabled;
            config.AutoShutdownAfterCleaned = loaded.AutoShutdownAfterCleaned;
            if (loaded.NoAutoShutdownNames != null && loaded.NoAutoShutdownNames.Count > 0) config.NoAutoShutdownNames = loaded.NoAutoShutdownNames;
        }
        catch
        {
        }

        return config;
    }

    public string GetWritableConfigPath()
    {
        return Path.Combine(appDir, "SmartShellCleaner.config.json");
    }

    public void DeleteConfig()
    {
        var path = GetWritableConfigPath();
        if (File.Exists(path))
            File.Delete(path);
    }

    public void SaveConfig(AppConfig config)
    {
        Directory.CreateDirectory(appDir);
        if (string.IsNullOrWhiteSpace(config.SmartShellEndpoint))
            config.SmartShellEndpoint = "https://billing.smartshell.gg/api/graphql";
        if (config.PollSeconds <= 0)
            config.PollSeconds = 15;
        if (string.IsNullOrWhiteSpace(config.StatusFile))
            config.StatusFile = "SmartShellCleaner.status.json";

        File.WriteAllText(
            GetWritableConfigPath(),
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);
    }

    public async Task<List<PcRecord>> LoadHostsAsync(AppConfig config, CancellationToken token)
    {
        if (config.SmartShellEnabled && !string.IsNullOrWhiteSpace(config.AccessToken))
        {
            try
            {
                return await LoadFromSmartShellAsync(config, token);
            }
            catch
            {
                return LoadFromStatusFile(config);
            }
        }

        return LoadFromStatusFile(config);
    }

    public async Task<List<ShopItem>> LoadShopItemsAsync(AppConfig config, CancellationToken token)
    {
        var result = new List<ShopItem>();

        using (var goods = await ExecuteGraphQlAsync(
            config,
            "goods",
            "query goods($input: GoodsInput) { goods(input: $input) { id title cost amount available_to_buy category { title } } }",
            new Dictionary<string, object?> { ["input"] = new Dictionary<string, object?>() },
            token))
        {
            var items = goods.RootElement.GetProperty("data").GetProperty("goods");
            foreach (var item in items.EnumerateArray())
            {
                var category = TryGet(item, "category");
                result.Add(new ShopItem
                {
                    Type = "GOOD",
                    EntityId = GetInt(item, "id"),
                    Title = GetString(item, "title"),
                    GroupTitle = category.HasValue ? GetString(category.Value, "title") : "",
                    Cost = GetDecimal(item, "cost"),
                    Stock = GetInt(item, "amount"),
                    AvailableToBuy = GetBool(item, "available_to_buy")
                });
            }
        }

        using (var services = await ExecuteGraphQlAsync(
            config,
            "services",
            "query services($input: ServicesInput!) { services(input: $input) { id title cost } }",
            new Dictionary<string, object?> { ["input"] = new Dictionary<string, object?>() },
            token))
        {
            var items = services.RootElement.GetProperty("data").GetProperty("services");
            foreach (var item in items.EnumerateArray())
            {
                result.Add(new ShopItem
                {
                    Type = "SERVICE",
                    EntityId = GetInt(item, "id"),
                    Title = GetString(item, "title"),
                    GroupTitle = "Услуги",
                    Cost = GetDecimal(item, "cost")
                });
            }
        }

        var page = 1;
        var lastPage = 1;
        do
        {
            using var combos = await ExecuteGraphQlAsync(
                config,
                "combos",
                "query combos($input: SearchComboInput, $page: Int) { combos(input: $input, page: $page) { paginatorInfo { currentPage lastPage } data { id title quantity base_price price host_group { id title } } } }",
                new Dictionary<string, object?>
                {
                    ["input"] = new Dictionary<string, object?>(),
                    ["page"] = page
                },
                token);

            var root = combos.RootElement.GetProperty("data").GetProperty("combos");
            var paginator = root.GetProperty("paginatorInfo");
            lastPage = Math.Max(1, GetInt(paginator, "lastPage"));

            foreach (var item in root.GetProperty("data").EnumerateArray())
            {
                var group = TryGet(item, "host_group");
                result.Add(new ShopItem
                {
                    Type = "COMBO",
                    EntityId = GetInt(item, "id"),
                    Title = GetString(item, "title"),
                    GroupTitle = group.HasValue ? GetString(group.Value, "title") : "Комбо",
                    Cost = GetDecimal(item, "price"),
                    Stock = GetInt(item, "quantity")
                });
            }

            page++;
        } while (page <= lastPage && page <= 100);

        return result
            .Where(x => !string.IsNullOrWhiteSpace(x.Title))
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Title)
            .ToList();
    }

    public async Task SendHostCommandAsync(AppConfig config, int hostId, string action, string? param, CancellationToken token)
    {
        if (hostId <= 0) throw new InvalidOperationException("У выбранного ПК нет SmartShell host_id.");
        if (action == "WOL")
        {
            await ExecuteGraphQlAsync(
                config,
                "sendWakePacket",
                "mutation sendWakePacket($input: SendWakePacketInput!) { sendWakePacket(input: $input) }",
                new Dictionary<string, object?>
                {
                    ["input"] = new Dictionary<string, object?> { ["host_id"] = hostId }
                },
                token);
            return;
        }

        var input = new Dictionary<string, object?>
        {
            ["host_id"] = hostId,
            ["action"] = action
        };
        if (!string.IsNullOrWhiteSpace(param)) input["param"] = param;

        await ExecuteGraphQlAsync(
            config,
            "createCommand",
            "mutation createCommand($input: CreateCommandInput) { createCommand(input: $input) { id action param } }",
            new Dictionary<string, object?> { ["input"] = input },
            token);
    }

    public async Task StopClientSessionAsync(AppConfig config, int sessionId, CancellationToken token)
    {
        if (sessionId <= 0) throw new InvalidOperationException("Активная сессия не найдена.");
        await ExecuteGraphQlAsync(
            config,
            "stopClientSession",
            "mutation stopClientSession($id: Int!, $ignorePause: Boolean) { stopClientSession(id: $id, ignorePause: $ignorePause) { id status finished_at } }",
            new Dictionary<string, object?> { ["id"] = sessionId, ["ignorePause"] = false },
            token);
    }

    public async Task SetServiceModeAsync(AppConfig config, int hostId, bool enabled, CancellationToken token)
    {
        if (hostId <= 0) throw new InvalidOperationException("У выбранного ПК нет SmartShell host_id.");
        await ExecuteGraphQlAsync(
            config,
            "setServiceMode",
            "mutation setServiceMode($id: Int!, $flag: FlagInput!) { setHostServiceMode(id: $id, input: $flag) { id in_service alias } }",
            new Dictionary<string, object?>
            {
                ["id"] = hostId,
                ["flag"] = new Dictionary<string, object?> { ["value"] = enabled }
            },
            token);
    }

    public async Task<List<ClientInfo>> SearchClientsAsync(AppConfig config, string queryText, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(queryText))
            return new List<ClientInfo>();

        return (await LoadClientsAsync(config, queryText, 1, 20, token)).Entries;
    }

    public async Task<ClientListPage> LoadClientsAsync(
        AppConfig config,
        string queryText,
        int page,
        int first,
        CancellationToken token)
    {
        using var doc = await ExecuteGraphQlAsync(
            config,
            "clients",
            "query clients($input: ClientsInput, $first: Int, $page: Int) { clients(input: $input, first: $first, page: $page) { paginatorInfo { total currentPage lastPage } data { id uuid phone nickname deposit bonus last_client_activity created_at total_hours user_discount banned_at unverified discounts { type value } group { title discount } roles { alias title } } } }",
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?> { ["q"] = queryText.Trim() },
                ["first"] = Math.Clamp(first, 1, 100),
                ["page"] = Math.Max(1, page)
            },
            token);

        var result = new ClientListPage();
        if (!doc.RootElement.TryGetProperty("data", out var data)) return result;
        if (!data.TryGetProperty("clients", out var clients)) return result;
        if (clients.TryGetProperty("paginatorInfo", out var paginator))
        {
            result.Total = GetInt(paginator, "total");
            result.CurrentPage = Math.Max(1, GetInt(paginator, "currentPage"));
            result.LastPage = Math.Max(1, GetInt(paginator, "lastPage"));
        }
        if (!clients.TryGetProperty("data", out var items) || items.ValueKind != JsonValueKind.Array) return result;

        foreach (var item in items.EnumerateArray())
        {
            var group = TryGet(item, "group");
            var role = GetFirstArrayItem(item, "roles");
            var lastActivityAtUtc = TryGetDateTimeUtc(item, "last_client_activity", out var parsedLastActivity)
                ? parsedLastActivity
                : (DateTime?)null;
            var createdAtUtc = TryGetDateTimeUtc(item, "created_at", out var parsedCreatedAt)
                ? parsedCreatedAt
                : (DateTime?)null;
            result.Entries.Add(new ClientInfo
            {
                Id = GetInt(item, "id"),
                Uuid = GetString(item, "uuid"),
                Nickname = GetString(item, "nickname"),
                Phone = GetString(item, "phone"),
                GroupTitle = group.HasValue ? GetString(group.Value, "title") : "",
                RoleTitle = role.HasValue
                    ? FirstNotEmpty(GetString(role.Value, "title"), GetString(role.Value, "alias"))
                    : "",
                LastActivityText = FormatClientDate(GetString(item, "last_client_activity")),
                LastActivityAtUtc = lastActivityAtUtc,
                CreatedAtText = FormatClientDate(GetString(item, "created_at")),
                CreatedAtUtc = createdAtUtc,
                StatusText = !string.IsNullOrWhiteSpace(GetString(item, "banned_at"))
                    ? "Заблокирован"
                    : GetBool(item, "unverified") ? "Не подтверждён" : "Активен",
                TotalHours = GetDecimal(item, "total_hours"),
                Deposit = GetDecimal(item, "deposit"),
                Bonus = GetDecimal(item, "bonus"),
                PersonalDiscount = GetPersonalDiscount(item),
                DiscountText = GetClientDiscountText(item)
            });
        }

        result.Entries = result.Entries.Where(x => x.Id > 0).ToList();
        if (result.Total <= 0) result.Total = result.Entries.Count;
        return result;
    }

    public async Task<ClientListPage> LoadAllClientsAsync(
        AppConfig config,
        string queryText,
        CancellationToken token)
    {
        const int batchSize = 100;
        var firstPage = await LoadClientsAsync(config, queryText, 1, batchSize, token);
        if (firstPage.LastPage <= 1) return firstPage;

        var remainingPages = await Task.WhenAll(
            Enumerable.Range(2, firstPage.LastPage - 1)
                .Select(page => LoadClientsAsync(config, queryText, page, batchSize, token)));
        var entries = firstPage.Entries
            .Concat(remainingPages.OrderBy(x => x.CurrentPage).SelectMany(x => x.Entries))
            .DistinctBy(x => x.Id)
            .ToList();
        return new ClientListPage
        {
            Entries = entries,
            CurrentPage = 1,
            LastPage = 1,
            Total = entries.Count
        };
    }

    private static string GetClientDiscountText(JsonElement item)
    {
        var discounts = TryGet(item, "discounts");
        if (discounts.HasValue && discounts.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var discount in discounts.Value.EnumerateArray())
            {
                var value = GetDecimal(discount, "value");
                if (value <= 0) continue;
                var type = GetString(discount, "type");
                return FormatDiscount(value, type);
            }
        }

        var userDiscount = GetDecimal(item, "user_discount");
        if (userDiscount > 0)
            return FormatDiscount(userDiscount, "PERCENT");

        var group = TryGet(item, "group");
        if (group.HasValue)
        {
            var groupDiscount = GetDecimal(group.Value, "discount");
            if (groupDiscount > 0)
                return FormatDiscount(groupDiscount, "PERCENT");
        }

        return "Нет скидки";
    }

    private static decimal GetPersonalDiscount(JsonElement item)
    {
        var discounts = TryGet(item, "discounts");
        if (discounts.HasValue && discounts.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var discount in discounts.Value.EnumerateArray())
            {
                if (GetString(discount, "type").Equals("PERSONAL", StringComparison.OrdinalIgnoreCase))
                    return GetDecimal(discount, "value");
            }
        }

        return GetDecimal(item, "user_discount");
    }

    private static string FormatDiscount(decimal value, string type)
    {
        var suffix = type.Contains("rub", StringComparison.OrdinalIgnoreCase)
            || type.Contains("sum", StringComparison.OrdinalIgnoreCase)
            || type.Contains("fixed", StringComparison.OrdinalIgnoreCase)
            ? " ₽"
            : "%";
        return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + suffix;
    }

    public async Task<ClientInfo?> FindFirstClientAsync(AppConfig config, string queryText, CancellationToken token)
    {
        var client = (await SearchClientsAsync(config, queryText, token)).FirstOrDefault();
        if (client == null || string.IsNullOrWhiteSpace(client.Uuid))
            return client;

        try
        {
            return await LoadClientDetailsAsync(config, client.Uuid, token) ?? client;
        }
        catch
        {
            return client;
        }
    }

    public async Task<ClientInfo?> LoadClientDetailsAsync(AppConfig config, string uuid, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(uuid)) return null;

        using var doc = await ExecuteGraphQlAsync(
            config,
            "user",
            "query user($uuid: String!) { user(uuid: $uuid) { id uuid phone nickname deposit bonus first_name last_name middle_name dob last_client_activity created_at total_hours user_discount discounts { type value } group { title discount } roles { alias title } last_comment { text is_important } pausable_info { duration elapsed expires_at tariff { title } } } }",
            new Dictionary<string, object?> { ["uuid"] = uuid.Trim() },
            token);

        if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
        if (!data.TryGetProperty("user", out var item) || item.ValueKind == JsonValueKind.Null) return null;

        var group = TryGet(item, "group");
        var role = GetFirstArrayItem(item, "roles");
        var comment = TryGet(item, "last_comment");
        var subscription = TryGet(item, "pausable_info");

        return new ClientInfo
        {
            Id = GetInt(item, "id"),
            Uuid = GetString(item, "uuid"),
            Nickname = GetString(item, "nickname"),
            Phone = GetString(item, "phone"),
            FirstName = GetString(item, "first_name"),
            LastName = GetString(item, "last_name"),
            MiddleName = GetString(item, "middle_name"),
            GroupTitle = group.HasValue ? GetString(group.Value, "title") : "",
            RoleTitle = role.HasValue
                ? FirstNotEmpty(GetString(role.Value, "title"), GetString(role.Value, "alias"))
                : "",
            LastComment = comment.HasValue ? GetString(comment.Value, "text") : "",
            LastCommentImportant = comment.HasValue && GetBool(comment.Value, "is_important"),
            BirthDateText = FormatClientDate(GetString(item, "dob"), true),
            LastActivityText = FormatClientDate(GetString(item, "last_client_activity")),
            CreatedAtText = FormatClientDate(GetString(item, "created_at")),
            TotalHours = GetDecimal(item, "total_hours"),
            SubscriptionText = FormatSubscription(subscription),
            Deposit = GetDecimal(item, "deposit"),
            Bonus = GetDecimal(item, "bonus"),
            PersonalDiscount = GetPersonalDiscount(item),
            DiscountText = GetClientDiscountText(item)
        };
    }

    public async Task<ClientHistoryPage> LoadClientPaymentHistoryAsync(AppConfig config, string uuid, int page, CancellationToken token)
    {
        using var doc = await ExecuteGraphQlAsync(
            config,
            "getPaymentsByClientId",
            "query getPaymentsByClientId($uuid: String!, $page: Int, $first: Int) { getPaymentsByClientId(uuid: $uuid, page: $page, first: $first) { paginatorInfo { currentPage lastPage } data { created_at title amount sum card_sum cash_sum bonus paymentMethod is_refunded worker { last_name first_name } } } }",
            new Dictionary<string, object?> { ["uuid"] = uuid, ["first"] = 25, ["page"] = Math.Max(1, page) },
            token);

        var result = ReadHistoryPage(doc, "getPaymentsByClientId", out var items);
        if (items.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in items.EnumerateArray())
        {
            var worker = TryGet(item, "worker");
            var total = GetDecimal(item, "sum");
            if (total == 0) total = GetDecimal(item, "amount");
            var bonus = GetDecimal(item, "bonus");
            result.Entries.Add(new ClientHistoryEntry
            {
                DateText = FormatClientDate(GetString(item, "created_at")),
                Operator = worker.HasValue
                    ? FirstNotEmpty(string.Join(" ", new[] { GetString(worker.Value, "last_name"), GetString(worker.Value, "first_name") }.Where(x => !string.IsNullOrWhiteSpace(x))), "—")
                    : "—",
                Category = FirstNotEmpty(GetString(item, "title"), "Платёж"),
                Payment = FormatPaymentMethod(GetString(item, "paymentMethod"), GetDecimal(item, "cash_sum"), GetDecimal(item, "card_sum")),
                Total = total.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " ₽",
                BonusText = bonus > 0 ? "★ " + bonus.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "",
                IsRefunded = GetBool(item, "is_refunded")
            });
        }
        return result;
    }

    public async Task<List<WorkShiftPayment>> LoadActiveWorkShiftPaymentsAsync(AppConfig config, CancellationToken token)
    {
        using var doc = await ExecuteGraphQlAsync(
            config,
            "activeWorkShift",
            "query activeWorkShift { activeWorkShift { payments { id sum sums { deposit bonus promo_code cashback } bonus method is_refunded not_refundable created_at client { uuid nickname phone } card_sum cash_sum items { id amount type sum entity { ... on Service { id title } ... on Good { id title } ... on Tariff { id title } } } status } } }",
            new Dictionary<string, object?>(),
            token);

        var result = new List<WorkShiftPayment>();
        if (!doc.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("activeWorkShift", out var shift)
            || shift.ValueKind == JsonValueKind.Null
            || !shift.TryGetProperty("payments", out var payments)
            || payments.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in payments.EnumerateArray())
        {
            var client = TryGet(item, "client");
            var methodCode = GetString(item, "method");
            var cash = GetDecimal(item, "cash_sum");
            var card = GetDecimal(item, "card_sum");
            var payment = new WorkShiftPayment
            {
                Id = GetInt(item, "id"),
                ClientUuid = client.HasValue ? GetString(client.Value, "uuid") : "",
                ClientDisplay = client.HasValue
                    ? FirstNotEmpty(GetString(client.Value, "nickname"), GetString(client.Value, "phone"), "Гость")
                    : "Гость",
                MethodCode = methodCode,
                Method = FormatPaymentMethod(methodCode, cash, card),
                Status = GetString(item, "status"),
                Total = GetDecimal(item, "sum"),
                Bonus = GetDecimal(item, "bonus"),
                CashSum = cash,
                CardSum = card,
                IsRefunded = GetBool(item, "is_refunded"),
                NotRefundable = GetBool(item, "not_refundable")
            };

            if (TryGetDateTimeUtc(item, "created_at", out var createdUtc))
                payment.CreatedAtLocal = createdUtc.ToLocalTime();

            var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lines = TryGet(item, "items");
            if (lines.HasValue && lines.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var line in lines.Value.EnumerateArray())
                {
                    var type = GetString(line, "type");
                    if (!string.IsNullOrWhiteSpace(type)) types.Add(type);
                    var entity = TryGet(line, "entity");
                    payment.Lines.Add(new WorkShiftPaymentLine
                    {
                        Type = type.ToUpperInvariant(),
                        Title = PaymentLineTitle(type, entity),
                        Amount = Math.Max(1, GetInt(line, "amount")),
                        Sum = GetDecimal(line, "sum")
                    });
                }
            }

            payment.Category = PaymentCategory(types);
            result.Add(payment);
        }

        return result.OrderByDescending(x => x.CreatedAtLocal).ThenByDescending(x => x.Id).ToList();
    }

    public async Task<EventLogPage> LoadEventLogsAsync(
        AppConfig config,
        int page,
        int first,
        string search,
        DateTime startLocal,
        DateTime finishLocal,
        IReadOnlyCollection<string>? types,
        CancellationToken token)
    {
        using var shiftDoc = await ExecuteGraphQlAsync(
            config,
            "getWorkShiftPaymentOverviewData",
            "query getWorkShiftPaymentOverviewData { getWorkShiftPaymentOverviewData { id } }",
            new Dictionary<string, object?>(),
            token);
        var workShiftId = 0;
        if (shiftDoc.RootElement.TryGetProperty("data", out var shiftData))
        {
            var shift = TryGet(shiftData, "getWorkShiftPaymentOverviewData");
            if (shift.HasValue) workShiftId = GetInt(shift.Value, "id");
        }

        var input = new Dictionary<string, object?>
        {
            ["start"] = startLocal.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            ["finish"] = finishLocal.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            ["types"] = types is { Count: > 0 } ? types : DefaultEventTypes
        };
        if (!string.IsNullOrWhiteSpace(search)) input["q"] = search.Trim();
        if (workShiftId > 0) input["work_shift_id"] = workShiftId;
        using var doc = await ExecuteGraphQlAsync(
            config,
            "EventList",
            "query EventList($input: EventsInput, $page: Int, $first: Int) { eventList(input: $input, page: $page, first: $first) { data { timestamp type description host { title } client { uuid phone nickname first_name last_name } worker { uuid phone nickname first_name last_name } manager { uuid phone nickname first_name last_name } } paginatorInfo { count currentPage lastPage } } }",
            new Dictionary<string, object?>
            {
                ["input"] = input,
                ["page"] = Math.Max(1, page),
                ["first"] = Math.Clamp(first, 1, 100)
            },
            token);

        var result = new EventLogPage { WorkShiftId = workShiftId };
        if (!doc.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("eventList", out var list)
            || list.ValueKind == JsonValueKind.Null)
            return result;

        var paginator = TryGet(list, "paginatorInfo");
        if (paginator.HasValue)
        {
            result.Count = GetInt(paginator.Value, "count");
            result.CurrentPage = Math.Max(1, GetInt(paginator.Value, "currentPage"));
            result.LastPage = Math.Max(1, GetInt(paginator.Value, "lastPage"));
        }

        var entries = TryGet(list, "data");
        if (!entries.HasValue || entries.Value.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in entries.Value.EnumerateArray())
        {
            var client = TryGet(item, "client");
            var worker = TryGet(item, "worker");
            var manager = TryGet(item, "manager");
            var host = TryGet(item, "host");
            var clientName = client.HasValue ? EventPersonName(client.Value) : "";
            var workerName = worker.HasValue ? EventPersonName(worker.Value) : "";
            var managerName = manager.HasValue ? EventPersonName(manager.Value) : "";
            var entry = new EventLogEntry
            {
                Type = GetString(item, "type"),
                Description = FormatEventDescription(FirstNotEmpty(GetString(item, "description"), "Событие SmartShell")),
                Host = host.HasValue ? GetString(host.Value, "title") : ""
            };
            entry.Category = FormatEventType(entry.Type);

            if (!string.IsNullOrWhiteSpace(managerName))
            {
                entry.Subject = managerName;
                if (!string.IsNullOrWhiteSpace(clientName))
                {
                    entry.Object = clientName;
                    entry.ObjectClientUuid = GetString(client!.Value, "uuid");
                }
                else
                {
                    entry.Object = workerName;
                }
            }
            else if (!string.IsNullOrWhiteSpace(workerName))
            {
                entry.Subject = workerName;
                if (!string.IsNullOrWhiteSpace(clientName))
                {
                    entry.Object = clientName;
                    entry.ObjectClientUuid = GetString(client!.Value, "uuid");
                }
            }
            else
            {
                entry.Subject = clientName;
                entry.SubjectClientUuid = client.HasValue ? GetString(client.Value, "uuid") : "";
            }

            if (TryGetDateTimeUtc(item, "timestamp", out var createdUtc))
                entry.CreatedAtLocal = createdUtc.ToLocalTime();
            result.Entries.Add(entry);
        }

        return result;
    }

    private static string EventPersonName(JsonElement person)
    {
        return FirstNotEmpty(
            GetString(person, "nickname"),
            string.Join(" ", new[] { GetString(person, "last_name"), GetString(person, "first_name") }.Where(x => !string.IsNullOrWhiteSpace(x))),
            GetString(person, "phone"));
    }

    private static string FormatEventDescription(string value)
    {
        if (!value.Contains('<') && !value.Contains('&')) return value.Trim();

        value = System.Text.RegularExpressions.Regex.Replace(value, @"<\s*br\s*/?\s*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        value = System.Text.RegularExpressions.Regex.Replace(value, @"<\s*li\b[^>]*>", "• ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        value = System.Text.RegularExpressions.Regex.Replace(value, @"<\s*/\s*(p|div|li|ul|ol)\s*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        value = System.Text.RegularExpressions.Regex.Replace(value, @"<[^>]+>", "");
        value = System.Net.WebUtility.HtmlDecode(value);

        return string.Join("\n", value.Replace("\r", "").Split('\n')
            .Select(line => System.Text.RegularExpressions.Regex.Replace(line, @"[ \t]+", " ").Trim())
            .Where(line => line.Length > 0));
    }

    private static string FormatEventType(string type)
    {
        return type.ToUpperInvariant() switch
        {
            "CLIENT_AUTHORIZED_FROM_HOST" or "WORKER_AUTHORIZED_FROM_HOST" or "WORKER_AUTHORIZED_FROM_BILLING" => "Авторизация",
            "CLIENT_SESSION_STARTED" => "Старт сеанса",
            "CLIENT_SESSION_PROLONGED" => "Продление сеанса",
            "CLIENT_SESSION_FINISHED" => "Завершение сеанса",
            "CLIENT_SESSION_PAUSED" => "Пауза сеанса",
            "CLIENT_SESSION_UNPAUSED" or "CLIENT_SESSION_UNPAUSED_AUTO" => "Возобновление сеанса",
            "PAYMENT_CREATED" => "Продажа",
            "PAYMENT_REFUND" => "Отмена продажи",
            "BONUS_PAYMENT_CREATED" => "Бонусное пополнение",
            "DEPOSIT_ADDED_ONLINE" => "Онлайн-пополнение",
            "BOOKING_CREATED" => "Создание бронирования",
            "BOOKING_UPDATED" => "Изменение бронирования",
            "BOOKING_CANCELED" => "Отмена бронирования",
            "BOOKING_CONFIRMED" => "Подтверждение бронирования",
            "BOOKING_FAILED_TO_START" => "Ошибка запуска брони",
            "WORK_SHIFT_STARTED" => "Открытие смены",
            "WORK_SHIFT_FINISHED" => "Закрытие смены",
            "HOST_ADMIN_CALLED" => "Вызов администратора",
            "HOST_ADMIN_CALL_CHECKED" => "Вызов принят",
            "HOST_ADMIN_CALL_FAILED" => "Пропущенный вызов",
            "SHELL_HIGH_ACCESS_ENABLED" => "Высокий доступ включён",
            "SHELL_HIGH_ACCESS_DISABLED" => "Высокий доступ выключен",
            "SHELL_DISABLED" => "Shell снят",
            "SHELL_ENABLED" => "Shell включён",
            _ when type.StartsWith("USER_", StringComparison.OrdinalIgnoreCase) => "Клиенты",
            _ when type.StartsWith("WORKER_", StringComparison.OrdinalIgnoreCase) => "Сотрудники",
            _ when type.StartsWith("HOST_", StringComparison.OrdinalIgnoreCase) => "Оборудование",
            _ when type.StartsWith("TARIFF_", StringComparison.OrdinalIgnoreCase) => "Тарифы",
            _ when type.StartsWith("GOOD_", StringComparison.OrdinalIgnoreCase) || type.StartsWith("WAREHOUSE_", StringComparison.OrdinalIgnoreCase) => "Товары",
            _ when type.StartsWith("SERVICE_", StringComparison.OrdinalIgnoreCase) => "Услуги",
            _ when type.StartsWith("DISCOUNT_", StringComparison.OrdinalIgnoreCase) => "Скидки",
            _ when type.StartsWith("PROMO_", StringComparison.OrdinalIgnoreCase) => "Промокоды",
            _ when type.StartsWith("COMBO_", StringComparison.OrdinalIgnoreCase) => "Комбо-наборы",
            _ when type.StartsWith("ADVERTISEMENT_", StringComparison.OrdinalIgnoreCase) => "Объявления",
            _ when type.StartsWith("NEWS_", StringComparison.OrdinalIgnoreCase) => "Новости",
            _ => "Событие"
        };
    }

    public async Task<DashboardSnapshot> LoadDashboardAsync(AppConfig config, CancellationToken token)
    {
        using var doc = await ExecuteGraphQlAsync(
            config,
            "getWorkShiftPaymentOverviewData",
            "query getWorkShiftPaymentOverviewData { getWorkShiftPaymentOverviewData { id total cash card deposit online_deposit bonus cash_on_start created_at finished_at sum { good service tariff deposit tips } cash_orders { type sum } worker { nickname first_name middle_name last_name phone } } }",
            new Dictionary<string, object?>(),
            token);

        var result = new DashboardSnapshot();
        if (!doc.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("getWorkShiftPaymentOverviewData", out var shift)
            || shift.ValueKind == JsonValueKind.Null)
            return result;

        result.WorkShiftId = GetInt(shift, "id");
        result.Total = GetDecimal(shift, "total");
        result.Cash = GetDecimal(shift, "cash");
        result.Card = GetDecimal(shift, "card");
        result.Deposit = GetDecimal(shift, "deposit");
        result.OnlineDeposit = GetDecimal(shift, "online_deposit");
        result.Bonus = GetDecimal(shift, "bonus");
        result.CashOnStart = GetDecimal(shift, "cash_on_start");
        if (TryGetDateTimeUtc(shift, "created_at", out var startedUtc))
            result.StartedAtLocal = startedUtc.ToLocalTime();
        if (TryGetDateTimeUtc(shift, "finished_at", out var finishedUtc))
            result.FinishedAtLocal = finishedUtc.ToLocalTime();

        var sums = TryGet(shift, "sum");
        if (sums.HasValue)
        {
            result.Goods = GetDecimal(sums.Value, "good");
            result.Services = GetDecimal(sums.Value, "service");
            result.Tariffs = GetDecimal(sums.Value, "tariff");
            result.DepositTopUps = GetDecimal(sums.Value, "deposit");
            result.Tips = GetDecimal(sums.Value, "tips");
        }

        var worker = TryGet(shift, "worker");
        if (worker.HasValue)
        {
            result.WorkerName = FirstNotEmpty(
                string.Join(" ", new[]
                {
                    GetString(worker.Value, "last_name"),
                    GetString(worker.Value, "first_name"),
                    GetString(worker.Value, "middle_name")
                }.Where(x => !string.IsNullOrWhiteSpace(x))),
                GetString(worker.Value, "nickname"),
                "Оператор");
            result.WorkerPhone = GetString(worker.Value, "phone");
        }

        var cashOrders = TryGet(shift, "cash_orders");
        if (cashOrders.HasValue && cashOrders.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var order in cashOrders.Value.EnumerateArray())
            {
                var type = GetString(order, "type").ToUpperInvariant();
                var amount = GetDecimal(order, "sum");
                result.CashOrdersDelta += type switch
                {
                    "PKO" => amount,
                    "RKO" => -amount,
                    _ => 0m
                };
            }
        }

        try
        {
            result.Tasks = await LoadDashboardTasksAsync(config, token);
        }
        catch
        {
            result.Tasks = new List<DashboardTaskInfo>();
        }

        if (result.StartedAtLocal.HasValue)
        {
            try
            {
                result.ActiveClients = await LoadDashboardActiveClientsAsync(config, result.StartedAtLocal.Value, token);
            }
            catch
            {
                result.ActiveClients = new List<DashboardActiveClient>();
            }
        }

        return result;
    }

    private async Task<List<DashboardTaskInfo>> LoadDashboardTasksAsync(AppConfig config, CancellationToken token)
    {
        using var doc = await ExecuteGraphQlAsync(
            config,
            "tasks",
            "query tasks($first: Int, $page: Int) { tasks(first: $first, page: $page) { data { id title assigned_to { id login first_name last_name } deadline_at finished_at } } }",
            new Dictionary<string, object?> { ["first"] = 100, ["page"] = 1 },
            token);

        var result = new List<DashboardTaskInfo>();
        if (!doc.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("tasks", out var tasks)
            || !tasks.TryGetProperty("data", out var items)
            || items.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in items.EnumerateArray())
        {
            var worker = TryGet(item, "assigned_to");
            var task = new DashboardTaskInfo
            {
                Id = GetInt(item, "id"),
                Title = FirstNotEmpty(GetString(item, "title"), "Задача"),
                Worker = worker.HasValue
                    ? FirstNotEmpty(
                        string.Join(" ", new[] { GetString(worker.Value, "last_name"), GetString(worker.Value, "first_name") }.Where(x => !string.IsNullOrWhiteSpace(x))),
                        GetString(worker.Value, "login"),
                        "—")
                    : "—"
            };
            if (TryGetDateTimeUtc(item, "deadline_at", out var deadlineUtc))
                task.DeadlineLocal = deadlineUtc.ToLocalTime();
            if (TryGetDateTimeUtc(item, "finished_at", out var finishedUtc))
                task.FinishedAtLocal = finishedUtc.ToLocalTime();
            result.Add(task);
        }
        return result;
    }

    private async Task<List<DashboardActiveClient>> LoadDashboardActiveClientsAsync(
        AppConfig config,
        DateTime shiftStartedLocal,
        CancellationToken token)
    {
        using var doc = await ExecuteGraphQlAsync(
            config,
            "getActiveClientList",
            "query getActiveClientList($input: GetActiveClientListInput!, $first: Int) { getActiveClientList(input: $input, first: $first) { nickname payments total_seconds last_client_activity } }",
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?>
                {
                    ["sort"] = "ByTime",
                    ["from"] = shiftStartedLocal.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                    ["to"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
                },
                ["first"] = 5
            },
            token);

        var result = new List<DashboardActiveClient>();
        if (!doc.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("getActiveClientList", out var items)
            || items.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in items.EnumerateArray())
        {
            var client = new DashboardActiveClient
            {
                Nickname = FirstNotEmpty(GetString(item, "nickname"), "Клиент"),
                Payments = GetDecimal(item, "payments"),
                TotalSeconds = GetInt(item, "total_seconds")
            };
            if (TryGetDateTimeUtc(item, "last_client_activity", out var activityUtc))
                client.LastActivityLocal = activityUtc.ToLocalTime();
            result.Add(client);
        }
        return result;
    }

    public async Task RefundPaymentAsync(AppConfig config, int paymentId, CancellationToken token)
    {
        if (paymentId <= 0) throw new InvalidOperationException("Платёж не найден.");

        var variables = new Dictionary<string, object?>
        {
            ["input"] = new Dictionary<string, object?> { ["id"] = paymentId }
        };
        using var validation = await ExecuteGraphQlAsync(
            config,
            "validateRefund",
            "query validateRefund($input: RefundPaymentInput!) { validateRefund(input: $input) }",
            variables,
            token);
        using var refund = await ExecuteGraphQlAsync(
            config,
            "refundPayment",
            "mutation refundPayment($input: RefundPaymentInput!) { refundPayment(input: $input) { id sum is_refunded } }",
            variables,
            token);
    }

    public async Task<RefundPolicy> LoadRefundPolicyAsync(AppConfig config, CancellationToken token)
    {
        using var doc = await ExecuteGraphQlAsync(
            config,
            "settingValues",
            "query settingValues { settingValues { alias value } }",
            new Dictionary<string, object?>(),
            token);

        var result = new RefundPolicy();
        if (!doc.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("settingValues", out var settings)
            || settings.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var setting in settings.EnumerateArray())
        {
            var alias = GetString(setting, "alias");
            var value = GetString(setting, "value");
            if (alias.Equals("backend.refunds.enabled", StringComparison.OrdinalIgnoreCase))
                result.Enabled = ParseSettingBool(value, true);
            else if (alias.Equals("backend.refunds.headroom", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value.Trim('"'), out var minutes))
                result.HeadroomMinutes = Math.Max(1, minutes);
            else if (alias.Equals("cashbox.enabled", StringComparison.OrdinalIgnoreCase))
                result.CashboxEnabled = ParseSettingBool(value, false);
        }

        return result;
    }

    public async Task<(bool Enabled, bool CcBoot, string Password)> LoadRemoteControlSettingsAsync(
        AppConfig config,
        CancellationToken token)
    {
        using var doc = await ExecuteGraphQlAsync(
            config,
            "settingValues",
            "query settingValues { settingValues { alias value } }",
            new Dictionary<string, object?>(),
            token);

        var enabled = false;
        var ccBoot = false;
        var password = "";
        if (!doc.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("settingValues", out var settings)
            || settings.ValueKind != JsonValueKind.Array)
            return (enabled, ccBoot, password);

        foreach (var setting in settings.EnumerateArray())
        {
            var alias = GetString(setting, "alias");
            var value = GetString(setting, "value");
            if (alias.Equals("shell.remote_control.enabled", StringComparison.OrdinalIgnoreCase))
                enabled = ParseSettingBool(value, false);
            else if (alias.Equals("shell.remote_control_ccboot.enabled", StringComparison.OrdinalIgnoreCase))
                ccBoot = ParseSettingBool(value, false);
            else if (alias.Equals("shell.remote_control.password", StringComparison.OrdinalIgnoreCase))
                password = value.Trim().Trim('"');
        }

        return (enabled, ccBoot, password);
    }

    private static bool ParseSettingBool(string value, bool fallback)
    {
        var normalized = value.Trim().Trim('"');
        if (normalized == "1") return true;
        if (normalized == "0") return false;
        return bool.TryParse(normalized, out var parsed) ? parsed : fallback;
    }

    private static string PaymentCategory(HashSet<string> types)
    {
        if (types.Count != 1) return "Покупка";
        return types.First().ToUpperInvariant() switch
        {
            "SERVICE" => "Покупка услуг",
            "TARIFF" => "Покупка тарифов",
            "GOOD" => "Покупка товаров",
            "DEPOSIT" => "Пополнение депозита",
            "BONUS" => "Пополнение бонусов",
            _ => "Платёж"
        };
    }

    private static string PaymentLineTitle(string type, JsonElement? entity)
    {
        if (entity.HasValue)
        {
            var title = GetString(entity.Value, "title");
            if (!string.IsNullOrWhiteSpace(title)) return title;
        }

        return type.ToUpperInvariant() switch
        {
            "DEPOSIT" => "Пополнение депозита",
            "BONUS" => "Пополнение бонусов",
            "GOOD" => "Товар",
            "SERVICE" => "Услуга",
            "TARIFF" => "Тариф",
            _ => "Позиция"
        };
    }

    public async Task<ClientHistoryPage> LoadClientBonusHistoryAsync(AppConfig config, string uuid, int page, CancellationToken token)
    {
        using var doc = await ExecuteGraphQlAsync(
            config,
            "GetBonusHistory",
            "query GetBonusHistory($uuid: String!, $page: Int, $first: Int) { getBonusHistory(uuid: $uuid, page: $page, first: $first) { paginatorInfo { currentPage lastPage } data { type worker_first_name worker_last_name created_at add new_value old_value delta } } }",
            new Dictionary<string, object?> { ["uuid"] = uuid, ["first"] = 25, ["page"] = Math.Max(1, page) },
            token);

        var result = ReadHistoryPage(doc, "getBonusHistory", out var items);
        if (items.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in items.EnumerateArray())
        {
            var delta = GetDecimal(item, "delta");
            if (delta == 0) delta = GetDecimal(item, "add");
            result.Entries.Add(new ClientHistoryEntry
            {
                DateText = FormatClientDate(GetString(item, "created_at")),
                Operator = FirstNotEmpty(string.Join(" ", new[] { GetString(item, "worker_last_name"), GetString(item, "worker_first_name") }.Where(x => !string.IsNullOrWhiteSpace(x))), "—"),
                Category = FormatBonusOperation(GetString(item, "type"), delta),
                Payment = (delta > 0 ? "+" : "") + delta.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " ★",
                Total = GetDecimal(item, "new_value").ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " ★"
            });
        }
        return result;
    }

    public async Task<ClientHistoryPage> LoadClientTransferHistoryAsync(AppConfig config, string uuid, int page, CancellationToken token)
    {
        using var doc = await ExecuteGraphQlAsync(
            config,
            "GetClientDepositTransfers",
            "query GetClientDepositTransfers($uuid: String!, $page: Int, $first: Int) { getClientDepositTransfers(uuid: $uuid, page: $page, first: $first) { paginatorInfo { currentPage lastPage } data { club { name address } type sum created_at } } }",
            new Dictionary<string, object?> { ["uuid"] = uuid, ["first"] = 25, ["page"] = Math.Max(1, page) },
            token);

        var result = ReadHistoryPage(doc, "getClientDepositTransfers", out var items);
        if (items.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in items.EnumerateArray())
        {
            var club = TryGet(item, "club");
            result.Entries.Add(new ClientHistoryEntry
            {
                DateText = FormatClientDate(GetString(item, "created_at")),
                Operator = club.HasValue ? FirstNotEmpty(GetString(club.Value, "name"), GetString(club.Value, "address"), "—") : "—",
                Category = FirstNotEmpty(GetString(item, "type"), "Перевод"),
                Payment = "Депозит",
                Total = GetDecimal(item, "sum").ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " ₽"
            });
        }
        return result;
    }

    private static ClientHistoryPage ReadHistoryPage(JsonDocument doc, string name, out JsonElement items)
    {
        items = default;
        var result = new ClientHistoryPage();
        if (!doc.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty(name, out var root)
            || root.ValueKind == JsonValueKind.Null)
            return result;

        var paginator = TryGet(root, "paginatorInfo");
        if (paginator.HasValue)
        {
            result.CurrentPage = Math.Max(1, GetInt(paginator.Value, "currentPage"));
            result.LastPage = Math.Max(1, GetInt(paginator.Value, "lastPage"));
        }
        if (!root.TryGetProperty("data", out items) || items.ValueKind != JsonValueKind.Array)
            items = default;
        return result;
    }

    private static string FormatPaymentMethod(string method, decimal cash, decimal card)
    {
        if (cash > 0 && card > 0) return "Раздельно";
        return method.ToUpperInvariant() switch
        {
            "CASH" => "Наличные",
            "CARD" => "Карта",
            "DEPOSIT" => "Депозит",
            "BONUS" => "СмартБонусы",
            "COMPOSITE" => "Раздельно",
            _ => string.IsNullOrWhiteSpace(method) ? "—" : method
        };
    }

    private static string FormatBonusOperation(string type, decimal delta)
    {
        if (!string.IsNullOrWhiteSpace(type)) return type;
        return delta < 0 ? "Списание бонусов" : "Пополнение бонусов";
    }

    public async Task<List<AdminCallInfo>> LoadAdminCallsAsync(AppConfig config, CancellationToken token)
    {
        using var doc = await ExecuteGraphQlAsync(
            config,
            "adminCalls",
            "query adminCalls { hostGroupsOverview { id title } hostsOverview { id group_id admin_called_at alias } }",
            new Dictionary<string, object?>(),
            token);

        var result = new List<AdminCallInfo>();
        if (!doc.RootElement.TryGetProperty("data", out var data)) return result;

        var groups = new Dictionary<int, string>();
        if (data.TryGetProperty("hostGroupsOverview", out var groupItems)
            && groupItems.ValueKind == JsonValueKind.Array)
        {
            foreach (var group in groupItems.EnumerateArray())
                groups[GetInt(group, "id")] = GetString(group, "title");
        }

        if (!data.TryGetProperty("hostsOverview", out var hosts)
            || hosts.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var host in hosts.EnumerateArray())
        {
            var calledAt = GetString(host, "admin_called_at");
            if (string.IsNullOrWhiteSpace(calledAt)) continue;
            DateTimeOffset.TryParse(
                calledAt,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var parsed);
            var groupId = GetInt(host, "group_id");
            result.Add(new AdminCallInfo
            {
                HostId = GetInt(host, "id"),
                HostAlias = FirstNotEmpty(GetString(host, "alias"), "ПК"),
                GroupTitle = groups.GetValueOrDefault(groupId, ""),
                CalledAtLocal = parsed == default ? DateTime.Now : parsed.LocalDateTime
            });
        }

        return result.OrderBy(x => x.CalledAtLocal).ToList();
    }

    public async Task CompleteAdminCallAsync(AppConfig config, int hostId, CancellationToken token)
    {
        await ExecuteGraphQlAsync(
            config,
            "adminCallDone",
            "mutation adminCallDone($id: Int!) { adminCallDone(id: $id) }",
            new Dictionary<string, object?> { ["id"] = hostId },
            token);
    }

    public async Task CreateCashOrderAsync(
        AppConfig config,
        string type,
        decimal sum,
        string comment,
        CancellationToken token)
    {
        await ExecuteGraphQlAsync(
            config,
            "createCashOrder",
            "mutation createCashOrder($input: CashOrderInput!) { createCashOrder(input: $input) { id } }",
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?>
                {
                    ["type"] = type,
                    ["sum"] = sum,
                    ["comment"] = comment
                }
            },
            token);
    }

    public async Task AddGoodsToWarehouseAsync(
        AppConfig config,
        IReadOnlyCollection<Dictionary<string, object?>> items,
        string comment,
        CancellationToken token)
    {
        await ExecuteGraphQlAsync(
            config,
            "changeGoodsQuantity",
            "mutation changeGoodsQuantity($input: ChangeGoodsQuantityInput!) { changeGoodsQuantity(input: $input) }",
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?>
                {
                    ["items"] = items,
                    ["operation"] = "ADD",
                    ["comment"] = comment
                }
            },
            token);
    }

    public async Task<ApiAccessInfo> LoadApiAccessInfoAsync(AppConfig config, CancellationToken token)
    {
        using var doc = await ExecuteGraphQlAsync(
            config,
            "me",
            "query me { me { login first_name middle_name last_name phone is_owner permissions { alias } roles { alias permissions { alias } } } }",
            new Dictionary<string, object?>(),
            token);

        if (!doc.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("me", out var me)
            || me.ValueKind == JsonValueKind.Null)
            throw new InvalidOperationException("SmartShell не вернул данные владельца токена.");

        var info = new ApiAccessInfo
        {
            DisplayName = FirstNotEmpty(
                string.Join(" ", new[]
                {
                    GetString(me, "last_name"),
                    GetString(me, "first_name"),
                    GetString(me, "middle_name")
                }.Where(x => !string.IsNullOrWhiteSpace(x))),
                GetString(me, "login"),
                GetString(me, "phone")),
            Phone = GetString(me, "phone"),
            IsOwner = GetBool(me, "is_owner")
        };

        AddPermissions(me, "permissions", info.Permissions);
        var roles = TryGet(me, "roles");
        if (roles.HasValue && roles.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var role in roles.Value.EnumerateArray())
            {
                var alias = GetString(role, "alias");
                if (!string.IsNullOrWhiteSpace(alias))
                    info.Roles.Add(alias);
                AddPermissions(role, "permissions", info.Permissions);
            }
        }

        return info;
    }

    private static void AddPermissions(JsonElement source, string property, HashSet<string> target)
    {
        var values = TryGet(source, property);
        if (!values.HasValue || values.Value.ValueKind != JsonValueKind.Array)
            return;

        foreach (var permission in values.Value.EnumerateArray())
        {
            var alias = GetString(permission, "alias");
            if (!string.IsNullOrWhiteSpace(alias))
                target.Add(alias);
        }
    }

    private static string FirstNotEmpty(params string[] values)
    {
        return values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
    }

    private static string FormatClientDate(string value, bool dateOnly = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (!DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
            return value;

        var local = parsed.ToLocalTime();
        return dateOnly
            ? local.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))
            : local.ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
    }

    private static string FormatSubscription(JsonElement? subscription)
    {
        if (!subscription.HasValue)
            return "Нет активного абонемента";

        var tariff = TryGet(subscription.Value, "tariff");
        var title = tariff.HasValue ? GetString(tariff.Value, "title") : "";
        var duration = GetInt(subscription.Value, "duration");
        var elapsed = GetInt(subscription.Value, "elapsed");
        var remaining = Math.Max(0, duration - elapsed);
        var expires = FormatClientDate(GetString(subscription.Value, "expires_at"));
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(title)) parts.Add(title);
        if (remaining > 0)
        {
            var hours = remaining / 3600;
            var minutes = remaining % 3600 / 60;
            parts.Add(hours > 0 ? $"осталось {hours} ч. {minutes} мин." : $"осталось {minutes} мин.");
        }
        if (!string.IsNullOrWhiteSpace(expires)) parts.Add("до " + expires);

        return parts.Count == 0 ? "Нет активного абонемента" : string.Join(" · ", parts);
    }

    public async Task ValidateAndCreatePaymentAsync(AppConfig config, Dictionary<string, object?> input, CancellationToken token)
    {
        using var validation = await ExecuteGraphQlAsync(
            config,
            "validatePayment",
            "query validatePayment($input: PaymentInput!) { validatePayment(input: $input) }",
            new Dictionary<string, object?> { ["input"] = input },
            token);

        using var payment = await ExecuteGraphQlAsync(
            config,
            "createPayment",
            "mutation createPayment($input: PaymentInput!) { createPayment(input: $input) { id } }",
            new Dictionary<string, object?> { ["input"] = input },
            token);
    }

    public async Task SetClientDepositAsync(AppConfig config, string clientUuid, decimal value, CancellationToken token)
    {
        using var result = await ExecuteGraphQlAsync(
            config,
            "setDeposit",
            "mutation setDeposit($input: SetDepositInput!) { setDeposit(input: $input) { uuid deposit } }",
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?>
                {
                    ["client_uuid"] = clientUuid,
                    ["value"] = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture)
                }
            },
            token);
    }

    public async Task SetClientBonusAsync(AppConfig config, string clientUuid, decimal value, CancellationToken token)
    {
        using var result = await ExecuteGraphQlAsync(
            config,
            "SetBonus",
            "mutation SetBonus($input: SetBonusInput!) { setBonus(input: $input) { uuid bonus } }",
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?>
                {
                    ["client_uuid"] = clientUuid,
                    ["value"] = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture)
                }
            },
            token);
    }

    public async Task SetClientDiscountAsync(AppConfig config, string clientUuid, decimal value, CancellationToken token)
    {
        using var result = await ExecuteGraphQlAsync(
            config,
            "setUserDiscount",
            "mutation setUserDiscount($input: SetUserDiscountInput!) { setUserDiscount(input: $input) { uuid user_discount discounts { type value } } }",
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?>
                {
                    ["client_uuid"] = clientUuid,
                    ["value"] = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture)
                }
            },
            token);
    }

    public async Task<List<TariffInfo>> LoadTariffsAsync(AppConfig config, int hostId, string? clientUuid, int? shiftSeconds, CancellationToken token)
    {
        if (hostId <= 0) throw new InvalidOperationException("У выбранного ПК нет SmartShell host_id.");
        var input = new Dictionary<string, object?> { ["host_id"] = hostId };
        if (!string.IsNullOrWhiteSpace(clientUuid)) input["client_uuid"] = clientUuid;
        if (shiftSeconds.HasValue && shiftSeconds.Value > 0) input["shift"] = shiftSeconds.Value;

        using var doc = await ExecuteGraphQlAsync(
            config,
            "tariffGrid",
            "query tariffGrid($input: TariffGridInput!) { tariffGrid(input: $input) { id title duration cost time_from time_to use_global_discounts has_fixed_finish_time per_minute pausable booked disable_sale_receipt_printing } }",
            new Dictionary<string, object?> { ["input"] = input },
            token);

        var result = new List<TariffInfo>();
        if (!doc.RootElement.TryGetProperty("data", out var data)) return result;
        if (!data.TryGetProperty("tariffGrid", out var items) || items.ValueKind != JsonValueKind.Array) return result;

        foreach (var item in items.EnumerateArray())
        {
            if (GetBool(item, "per_minute")) continue;
            result.Add(new TariffInfo
            {
                Id = GetInt(item, "id"),
                Title = GetString(item, "title"),
                Duration = GetInt(item, "duration"),
                Cost = GetDecimal(item, "cost"),
                TimeFrom = GetString(item, "time_from"),
                TimeTo = GetString(item, "time_to"),
                Booked = GetBool(item, "booked")
            });
        }

        return result.OrderBy(x => x.Cost).ThenBy(x => x.Duration).ToList();
    }

    public async Task StartClientSessionAsync(AppConfig config, int hostId, int userId, CancellationToken token)
    {
        if (hostId <= 0) throw new InvalidOperationException("У выбранного ПК нет SmartShell host_id.");
        if (userId <= 0) throw new InvalidOperationException("Клиент не найден.");

        await ExecuteGraphQlAsync(
            config,
            "startClientSession",
            "mutation startClientSession($input: StartClientSessionInput!) { startClientSession(input: $input) { status hosts { host { alias } } } }",
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?>
                {
                    ["host_id"] = hostId,
                    ["user_id"] = userId,
                    ["post_paid"] = false
                }
            },
            token);
    }

    public async Task AddPenaltyAsync(AppConfig config, int hostId, int seconds, CancellationToken token)
    {
        if (hostId <= 0) throw new InvalidOperationException("У выбранного ПК нет SmartShell host_id.");
        if (seconds <= 0) throw new InvalidOperationException("Введите время штрафа больше нуля.");

        await ExecuteGraphQlAsync(
            config,
            "penaltyHost",
            "mutation penaltyHost($input: PenaltyHostInput!) { penaltyHost(input: $input) { id group_id } }",
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?> { ["host_id"] = hostId, ["seconds"] = seconds }
            },
            token);
    }

    public async Task<List<FreeHostInfo>> LoadFreeHostsAsync(AppConfig config, int hostId, CancellationToken token)
    {
        if (hostId <= 0) throw new InvalidOperationException("У выбранного ПК нет SmartShell host_id.");

        using var doc = await ExecuteGraphQlAsync(
            config,
            "freeHosts",
            "query freeHosts($host_id: Int!) { freeHosts(host_id: $host_id) { alias id group { id title priority } position } }",
            new Dictionary<string, object?> { ["host_id"] = hostId },
            token);

        var result = new List<FreeHostInfo>();
        if (!doc.RootElement.TryGetProperty("data", out var data)) return result;
        if (!data.TryGetProperty("freeHosts", out var items) || items.ValueKind != JsonValueKind.Array) return result;

        foreach (var item in items.EnumerateArray())
        {
            var group = TryGet(item, "group");
            var info = new FreeHostInfo
            {
                Id = GetInt(item, "id"),
                Alias = NormalizeName(GetString(item, "alias")),
                GroupTitle = group.HasValue ? GetString(group.Value, "title") : ""
            };
            if (info.Id > 0) result.Add(info);
        }

        return result.OrderBy(x => NaturalKey(x.Alias)).ToList();
    }

    public async Task ChangeClientSessionHostAsync(AppConfig config, int sessionId, int targetHostId, CancellationToken token)
    {
        if (sessionId <= 0) throw new InvalidOperationException("Активная сессия не найдена.");
        if (targetHostId <= 0) throw new InvalidOperationException("Не выбрано новое место.");

        await ExecuteGraphQlAsync(
            config,
            "changeClientSessionHost",
            "mutation changeClientSessionHost($id: Int!, $input: ChangeClientSessionHostInput!) { changeClientSessionHost(id: $id, input: $input) { id hosts { host { id } } } }",
            new Dictionary<string, object?>
            {
                ["id"] = sessionId,
                ["input"] = new Dictionary<string, object?> { ["host_id"] = targetHostId }
            },
            token);
    }

    public async Task CreateBookingAsync(AppConfig config, int hostId, string? clientUuid, DateTime fromLocal, DateTime toLocal, string comment, CancellationToken token)
    {
        await CreateBookingAsync(config, new[] { hostId }, clientUuid, fromLocal, toLocal, comment, token);
    }

    public async Task CreateBookingAsync(AppConfig config, IEnumerable<int> hostIds, string? clientUuid, DateTime fromLocal, DateTime toLocal, string comment, CancellationToken token)
    {
        var ids = hostIds.Where(x => x > 0).Distinct().ToList();
        if (ids.Count == 0) throw new InvalidOperationException("У выбранного ПК нет SmartShell host_id.");
        if (toLocal <= fromLocal) throw new InvalidOperationException("Проверьте длительность брони.");

        var input = new Dictionary<string, object?>
        {
            ["hosts"] = ids,
            ["from"] = fromLocal.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            ["to"] = toLocal.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(clientUuid)) input["client"] = clientUuid.Trim();
        if (!string.IsNullOrWhiteSpace(comment)) input["comment"] = comment.Trim();

        var diagnosticPath = Path.Combine(appDir, "booking-diagnostic.log");
        try
        {
            using var result = await ExecuteGraphQlAsync(
                config,
                "createBookingV2",
                "mutation createBookingV2($input: BookingInput!) { createBooking(input: $input) { id __typename } }",
                new Dictionary<string, object?> { ["input"] = input },
                token,
                GetBookingEndpoint(config),
                diagnosticPath);
        }
        catch (Exception ex)
        {
            AppendDiagnostic(diagnosticPath, "ERROR " + ex);
            if (ex.Message.Contains("Object reference not set", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "SmartShell отклонил бронь. Проверьте, не занято ли выбранное время другой бронью или сеансом.",
                    ex);
            }
            throw;
        }
    }

    public async Task UpdateBookingAsync(AppConfig config, int bookingId, IEnumerable<int> hostIds, string? clientUuid, DateTime fromLocal, DateTime toLocal, string comment, CancellationToken token)
    {
        if (bookingId <= 0) throw new InvalidOperationException("Бронь не найдена.");
        var ids = hostIds.Where(x => x > 0).Distinct().ToList();
        if (ids.Count == 0) throw new InvalidOperationException("Выберите хотя бы один ПК.");
        if (toLocal <= fromLocal) throw new InvalidOperationException("Проверьте длительность брони.");

        var input = new Dictionary<string, object?>
        {
            ["hosts"] = ids,
            ["from"] = fromLocal.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            ["to"] = toLocal.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            ["comment"] = comment.Trim()
        };
        if (!string.IsNullOrWhiteSpace(clientUuid)) input["client"] = clientUuid.Trim();

        var diagnosticPath = Path.Combine(appDir, "booking-diagnostic.log");
        try
        {
            using var result = await ExecuteGraphQlAsync(
                config,
                "updateBookingV2",
                "mutation updateBookingV2($id: Int!, $input: BookingInput!) { updateBooking(id: $id, input: $input) { status } }",
                new Dictionary<string, object?> { ["id"] = bookingId, ["input"] = input },
                token,
                GetBookingEndpoint(config),
                diagnosticPath);
        }
        catch (Exception ex)
        {
            AppendDiagnostic(diagnosticPath, "ERROR " + ex);
            throw;
        }
    }

    public async Task<BookingOverview> LoadBookingOverviewAsync(AppConfig config, CancellationToken token)
    {
        using var doc = await ExecuteGraphQlAsync(
            config,
            "BookingsHostGroups",
            "query BookingsHostGroups { hostGroupsOverview { id title } hostsOverview { id alias position group_id client_sessions { finished_at started_at time_left id } user { nickname phone uuid } } }",
            new Dictionary<string, object?>(),
            token);

        var result = new BookingOverview();
        if (!doc.RootElement.TryGetProperty("data", out var data)) return result;

        if (data.TryGetProperty("hostGroupsOverview", out var groups) && groups.ValueKind == JsonValueKind.Array)
        {
            foreach (var group in groups.EnumerateArray())
            {
                result.Groups.Add(new BookingHostGroupInfo
                {
                    Id = GetInt(group, "id"),
                    Title = GetString(group, "title")
                });
            }
        }

        if (!data.TryGetProperty("hostsOverview", out var hosts) || hosts.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var host in hosts.EnumerateArray())
        {
            var user = TryGet(host, "user");
            var item = new BookingHostInfo
            {
                Id = GetInt(host, "id"),
                Alias = GetString(host, "alias"),
                Position = GetInt(host, "position"),
                GroupId = GetInt(host, "group_id"),
                ClientName = user.HasValue ? GetString(user.Value, "nickname") : "",
                ClientPhone = user.HasValue ? GetString(user.Value, "phone") : ""
            };

            var sessions = TryGet(host, "client_sessions");
            if (sessions.HasValue && sessions.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var session in sessions.Value.EnumerateArray())
                {
                    TryGetDateTimeUtc(session, "started_at", out var startedUtc);
                    TryGetDateTimeUtc(session, "finished_at", out var finishedUtc);
                    var timeLeft = GetInt(session, "time_left");
                    var startedLocal = startedUtc == default ? DateTime.MinValue : startedUtc.ToLocalTime();
                    var finishedLocal = finishedUtc == default
                        ? timeLeft > 0 ? DateTime.Now.AddSeconds(timeLeft) : DateTime.MinValue
                        : finishedUtc.ToLocalTime();
                    item.Sessions.Add(new BookingSessionInfo
                    {
                        Id = GetInt(session, "id"),
                        StartedLocal = startedLocal,
                        FinishedLocal = finishedLocal
                    });
                }
            }

            result.Hosts.Add(item);
        }

        return result;
    }

    public async Task<List<BookingInfo>> LoadBookingsAsync(AppConfig config, int hostId, DateTime fromUtc, DateTime toUtc, CancellationToken token)
    {
        if (hostId <= 0) throw new InvalidOperationException("У выбранного ПК нет SmartShell host_id.");

        var result = await LoadBookingsAsync(config, fromUtc.ToLocalTime(), toUtc.ToLocalTime(), token);
        return result.Where(x => x.HostIds.Contains(hostId)).OrderBy(x => x.FromLocal).ToList();
    }

    public async Task<List<BookingInfo>> LoadBookingsAsync(AppConfig config, DateTime fromLocal, DateTime toLocal, CancellationToken token)
    {

        using var doc = await ExecuteGraphQlAsync(
            config,
            "bookingsV2",
            "query bookingsV2($from: DateTime, $to: DateTime) { getBookings(from: $from, to: $to) { data { id group hostIds byClient client { uuid nickname phone } from to status comment } } }",
            new Dictionary<string, object?>
            {
                ["from"] = fromLocal.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                ["to"] = toLocal.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
            },
            token,
            GetBookingEndpoint(config));

        var result = new List<BookingInfo>();
        if (!doc.RootElement.TryGetProperty("data", out var data)) return result;
        if (!data.TryGetProperty("getBookings", out var bookings)) return result;
        if (!bookings.TryGetProperty("data", out var items) || items.ValueKind != JsonValueKind.Array) return result;

        foreach (var item in items.EnumerateArray())
        {
            var hostIds = new List<int>();
            var hostIdsElement = TryGet(item, "hostIds");
            if (hostIdsElement.HasValue && hostIdsElement.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var host in hostIdsElement.Value.EnumerateArray())
                {
                    if (host.TryGetInt32(out var id))
                        hostIds.Add(id);
                }
            }
            TryGetDateTimeUtc(item, "from", out var from);
            TryGetDateTimeUtc(item, "to", out var to);
            var client = TryGet(item, "client");
            result.Add(new BookingInfo
            {
                Id = GetInt(item, "id"),
                HostIds = hostIds,
                ClientUuid = client.HasValue ? GetString(client.Value, "uuid") : "",
                ClientName = client.HasValue ? GetString(client.Value, "nickname") : "",
                ClientPhone = client.HasValue ? GetString(client.Value, "phone") : "",
                FromLocal = from == default ? DateTime.MinValue : from.ToLocalTime(),
                ToLocal = to == default ? DateTime.MinValue : to.ToLocalTime(),
                Status = GetString(item, "status"),
                Comment = GetString(item, "comment")
            });
        }

        return result.OrderBy(x => x.FromLocal).ToList();
    }

    public async Task RedeemBookingAsync(AppConfig config, int bookingId, CancellationToken token)
    {
        if (bookingId <= 0) throw new InvalidOperationException("Бронь не найдена.");

        await ExecuteGraphQlAsync(
            config,
            "redeemBooking",
            "mutation redeemBooking($id: Int!) { setBookingStatus(id: $id, status: REDEEMED) { id } }",
            new Dictionary<string, object?> { ["id"] = bookingId },
            token);
    }

    public async Task CreateHostCommentAsync(AppConfig config, int hostId, string text, bool important, CancellationToken token)
    {
        if (hostId <= 0) throw new InvalidOperationException("У выбранного ПК нет SmartShell host_id.");
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Введите текст заметки.");

        await ExecuteGraphQlAsync(
            config,
            "createComment",
            "mutation createComment($input: CommentInput!) { createComment(input: $input) { id } }",
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?>
                {
                    ["text"] = text.Trim(),
                    ["entity_id"] = hostId,
                    ["type"] = "HOST",
                    ["is_important"] = important
                }
            },
            token);
    }

    private static string GetBookingEndpoint(AppConfig config)
    {
        var endpoint = string.IsNullOrWhiteSpace(config.SmartShellEndpoint)
            ? "https://billing.smartshell.gg/api/graphql"
            : config.SmartShellEndpoint.Trim();
        if (endpoint.Contains("/api/v2/graphql", StringComparison.OrdinalIgnoreCase))
            return endpoint;
        if (endpoint.Contains("/api/graphql", StringComparison.OrdinalIgnoreCase))
            return endpoint.Replace("/api/graphql", "/api/v2/graphql", StringComparison.OrdinalIgnoreCase);
        return endpoint.TrimEnd('/') + "/api/v2/graphql";
    }

    private async Task<JsonDocument> ExecuteGraphQlAsync(
        AppConfig config,
        string operationName,
        string query,
        Dictionary<string, object?> variables,
        CancellationToken token,
        string? endpointOverride = null,
        string? diagnosticPath = null)
    {
        if (string.IsNullOrWhiteSpace(config.AccessToken))
            throw new InvalidOperationException("В конфиге нет SmartShell access_token.");

        var body = JsonSerializer.Serialize(new
        {
            operationName,
            query,
            variables
        });

        var endpoint = endpointOverride ?? config.SmartShellEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("Не указан SmartShell GraphQL endpoint.");
        var requestUri = WithOperationName(endpoint, operationName);
        if (!string.IsNullOrWhiteSpace(diagnosticPath))
            AppendDiagnostic(diagnosticPath, "REQUEST " + requestUri + Environment.NewLine + body);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken.Trim());
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Referrer = new Uri("https://admin.smartshell.gg/");
        request.Headers.TryAddWithoutValidation("Origin", "https://admin.smartshell.gg");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, token);
        var text = await response.Content.ReadAsStringAsync(token);
        if (!string.IsNullOrWhiteSpace(diagnosticPath))
            AppendDiagnostic(diagnosticPath, "RESPONSE " + (int)response.StatusCode + Environment.NewLine + text);
        response.EnsureSuccessStatusCode();

        var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("errors", out var errors))
        {
            var errorText = FormatGraphQlErrors(errors);
            doc.Dispose();
            throw new InvalidOperationException(errorText);
        }

        return doc;
    }

    private static string WithOperationName(string endpoint, string operationName)
    {
        var hashIndex = endpoint.IndexOf('#');
        var fragment = hashIndex >= 0 ? endpoint[hashIndex..] : "";
        var withoutFragment = hashIndex >= 0 ? endpoint[..hashIndex] : endpoint;
        var queryIndex = withoutFragment.IndexOf('?');
        var baseUrl = queryIndex >= 0 ? withoutFragment[..queryIndex] : withoutFragment;
        var query = queryIndex >= 0 ? withoutFragment[(queryIndex + 1)..] : "";
        var parameters = query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !x.StartsWith("op=", StringComparison.OrdinalIgnoreCase))
            .ToList();
        parameters.Add("op=" + Uri.EscapeDataString(operationName));
        return baseUrl + "?" + string.Join("&", parameters) + fragment;
    }

    private static void AppendDiagnostic(string path, string text)
    {
        try
        {
            File.AppendAllText(
                path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {text}{Environment.NewLine}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static string FormatGraphQlErrors(JsonElement errors)
    {
        if (errors.ValueKind != JsonValueKind.Array)
            return errors.ToString();

        var messages = new List<string>();
        foreach (var error in errors.EnumerateArray())
        {
            var message = GetString(error, "message");
            if (!string.IsNullOrWhiteSpace(message))
                messages.Add(message);
        }

        return messages.Count > 0 ? string.Join(Environment.NewLine, messages) : errors.ToString();
    }

    private async Task<List<PcRecord>> LoadFromSmartShellAsync(AppConfig config, CancellationToken token)
    {
        try
        {
            return await LoadFromSmartShellAsync(config, token, "id time_left started_at finished_at seances { status tariff { title } }");
        }
        catch
        {
            try
            {
                return await LoadFromSmartShellAsync(config, token, "id started_at finished_at");
            }
            catch
            {
                return await LoadFromSmartShellAsync(config, token, "id");
            }
        }
    }

    private async Task<List<PcRecord>> LoadFromSmartShellAsync(AppConfig config, CancellationToken token, string sessionFields)
    {
        var query = $$"""
            query hostsData {
              hostGroupsOverview { id title }
              hostsOverview {
                id alias group_id in_service online locked mac_addr ip_addr shell_mode
                user { uuid nickname phone }
                info { shell_version }
                counters { active_window }
                bookings { id }
                client_sessions { {{sessionFields}} }
              }
            }
            """;

        var body = JsonSerializer.Serialize(new
        {
            operationName = "hostsData",
            query,
            variables = new { }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, config.SmartShellEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken!.Trim());
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, token);
        var text = await response.Content.ReadAsStringAsync(token);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("errors", out var errors))
            throw new InvalidOperationException(errors.ToString());

        var data = doc.RootElement.GetProperty("data");
        var groupTitles = new Dictionary<int, string>();
        if (data.TryGetProperty("hostGroupsOverview", out var groups) && groups.ValueKind == JsonValueKind.Array)
        {
            foreach (var group in groups.EnumerateArray())
                groupTitles[GetInt(group, "id")] = GetString(group, "title");
        }

        var hosts = data.GetProperty("hostsOverview");
        var result = new List<PcRecord>();
        foreach (var host in hosts.EnumerateArray())
        {
            var online = GetBool(host, "online");
            var inService = GetBool(host, "in_service");
            var locked = GetBool(host, "locked");
            var sessions = CountArray(host, "client_sessions");
            var bookings = CountArray(host, "bookings");
            var user = TryGet(host, "user");
            var info = TryGet(host, "info");
            var counters = TryGet(host, "counters");
            var firstSession = GetFirstArrayItem(host, "client_sessions");
            var timeLeft = firstSession.HasValue ? GetString(firstSession.Value, "time_left") : "";
            var timeLeftSeconds = 0;
            if (TryGetTimeLeft(firstSession, out var left))
            {
                timeLeftSeconds = Math.Max(0, (int)Math.Ceiling(left.TotalSeconds));
                if (string.IsNullOrWhiteSpace(timeLeft) || timeLeft.All(char.IsDigit))
                    timeLeft = FormatTimeLeft(left);
            }
            var endsAtUtc = TryGetSessionEndUtc(firstSession);
            var startedAtUtc = TryGetSessionStartUtc(firstSession);
            var name = NormalizeName(GetString(host, "alias"));
            var groupId = GetInt(host, "group_id");

            result.Add(new PcRecord
            {
                Name = name,
                HostId = GetInt(host, "id"),
                GroupId = groupId,
                GroupTitle = groupTitles.GetValueOrDefault(groupId),
                SourceStatus = ResolveStatus(online, inService, locked, sessions),
                ActiveSessionCount = sessions,
                ClientSessionId = GetFirstArrayId(host, "client_sessions"),
                BookingCount = bookings,
                Online = online,
                InService = inService,
                Locked = locked,
                ClientName = user.HasValue ? GetString(user.Value, "nickname") : "",
                ClientPhone = user.HasValue ? GetString(user.Value, "phone") : "",
                ClientUuid = user.HasValue ? GetString(user.Value, "uuid") : "",
                ActiveTariffTitle = GetActiveTariffTitle(firstSession),
                ActiveWindow = counters.HasValue ? GetString(counters.Value, "active_window") : "",
                IpAddress = GetString(host, "ip_addr"),
                MacAddress = GetString(host, "mac_addr"),
                ShellMode = GetString(host, "shell_mode"),
                ShellVersion = info.HasValue ? GetString(info.Value, "shell_version") : "",
                SessionTimeLeft = timeLeft,
                SessionTimeLeftSeconds = timeLeftSeconds,
                SessionStartedAtUtc = startedAtUtc,
                SessionEndsAtUtc = endsAtUtc
            });
        }

        return result.OrderBy(x => NaturalKey(x.Name)).ToList();
    }

    private List<PcRecord> LoadFromStatusFile(AppConfig config)
    {
        var path = config.StatusFile;
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(appDir, "SmartShellCleaner.status.json");

        var result = new List<PcRecord>();
        try
        {
            if (!File.Exists(path)) return result;
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            if (!doc.RootElement.TryGetProperty("computers", out var computers)) return result;
            foreach (var item in computers.EnumerateArray())
            {
                result.Add(new PcRecord
                {
                    Name = NormalizeName(GetString(item, "name")),
                    SourceStatus = GetString(item, "status").ToLowerInvariant()
                });
            }
        }
        catch
        {
        }

        return result.OrderBy(x => NaturalKey(x.Name)).ToList();
    }

    public static List<PcRecord> BuildFallbackHosts()
    {
        var records = new List<PcRecord>();
        for (var i = 1; i <= 31; i++)
            records.Add(new PcRecord { Name = "PC" + i.ToString("00"), SourceStatus = i <= 6 ? "busy" : "offline" });
        records.Add(new PcRecord { Name = "PS1", SourceStatus = "offline" });
        records.Add(new PcRecord { Name = "PS2", SourceStatus = "offline" });
        return records;
    }

    private string? FindConfigPath()
    {
        var path = GetWritableConfigPath();
        return File.Exists(path) ? path : null;
    }

    private string MakeAbsolute(string baseDir, string path)
    {
        return Path.IsPathRooted(path) ? path : Path.Combine(baseDir, path);
    }

    private static string ResolveStatus(bool online, bool inService, bool locked, int sessions)
    {
        if (!online) return "offline";
        if (sessions > 0) return "busy";
        if (locked) return "locked";
        if (inService) return "service";
        return "free";
    }

    private static JsonElement? TryGet(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;
    }

    private static string GetString(JsonElement element, string name)
    {
        var value = TryGet(element, name);
        if (!value.HasValue) return "";

        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString() ?? "",
            JsonValueKind.Number => value.Value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => value.Value.ToString()
        };
    }

    private static int GetInt(JsonElement element, string name)
    {
        var value = TryGet(element, name);
        return value.HasValue && value.Value.TryGetInt32(out var result) ? result : 0;
    }

    private static bool GetBool(JsonElement element, string name)
    {
        var value = TryGet(element, name);
        return value.HasValue && value.Value.ValueKind == JsonValueKind.True;
    }

    private static decimal GetDecimal(JsonElement element, string name)
    {
        var value = TryGet(element, name);
        if (!value.HasValue) return 0m;
        if (value.Value.ValueKind == JsonValueKind.Number && value.Value.TryGetDecimal(out var number)) return number;
        if (value.Value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.Value.GetString()?.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var textNumber))
            return textNumber;
        return 0m;
    }

    private static int CountArray(JsonElement element, string name)
    {
        var value = TryGet(element, name);
        return value.HasValue && value.Value.ValueKind == JsonValueKind.Array ? value.Value.GetArrayLength() : 0;
    }

    private static int GetFirstArrayId(JsonElement element, string name)
    {
        var value = TryGet(element, name);
        if (!value.HasValue || value.Value.ValueKind != JsonValueKind.Array || value.Value.GetArrayLength() == 0)
            return 0;
        return GetInt(value.Value.EnumerateArray().First(), "id");
    }

    private static JsonElement? GetFirstArrayItem(JsonElement element, string name)
    {
        var value = TryGet(element, name);
        if (!value.HasValue || value.Value.ValueKind != JsonValueKind.Array || value.Value.GetArrayLength() == 0)
            return null;
        return value.Value.EnumerateArray().First();
    }

    private static bool TryGetTimeLeft(JsonElement? session, out TimeSpan left)
    {
        left = TimeSpan.Zero;
        if (!session.HasValue) return false;
        var value = TryGet(session.Value, "time_left");
        if (!value.HasValue) return false;

        if (value.Value.ValueKind == JsonValueKind.Number)
        {
            if (value.Value.TryGetInt32(out var seconds))
            {
                left = TimeSpan.FromSeconds(seconds);
                return seconds > 0;
            }
            if (value.Value.TryGetDouble(out var doubleSeconds))
            {
                left = TimeSpan.FromSeconds(doubleSeconds);
                return doubleSeconds > 0;
            }
        }

        var text = value.Value.GetString();
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        var parts = text.Split(':');
        if (parts.Length == 3 && TimeSpan.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out left))
            return left > TimeSpan.Zero;
        if (parts.Length == 2
            && int.TryParse(parts[0], out var minutes)
            && int.TryParse(parts[1], out var secondsPart)
            && minutes <= 5
            && secondsPart >= 0
            && secondsPart < 60)
        {
            left = new TimeSpan(0, minutes, secondsPart);
            return left > TimeSpan.Zero;
        }
        if (double.TryParse(text.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedSeconds))
        {
            left = TimeSpan.FromSeconds(parsedSeconds);
            return parsedSeconds > 0;
        }
        return false;
    }

    private static string FormatTimeLeft(TimeSpan left)
    {
        if (left.TotalHours >= 1)
            return ((int)left.TotalHours).ToString("00") + ":" + left.Minutes.ToString("00") + ":" + left.Seconds.ToString("00");
        return left.Minutes.ToString("00") + ":" + left.Seconds.ToString("00");
    }

    private static DateTime? TryGetSessionEndUtc(JsonElement? session)
    {
        if (!session.HasValue) return null;

        if (TryGetDateTimeUtc(session.Value, "finished_at", out var finishedAtUtc))
            return finishedAtUtc;

        if (TryGetTimeLeft(session, out var left))
            return DateTime.UtcNow.Add(left);

        if (TryGetSeconds(session.Value, "duration", out var durationSeconds) &&
            TryGetSeconds(session.Value, "elapsed", out var elapsedSeconds))
        {
            var remainingSeconds = durationSeconds - elapsedSeconds;
            if (remainingSeconds > 0)
                return DateTime.UtcNow.AddSeconds(remainingSeconds);
        }

        return null;
    }

    private static DateTime? TryGetSessionStartUtc(JsonElement? session)
    {
        if (!session.HasValue) return null;
        return TryGetDateTimeUtc(session.Value, "started_at", out var startedAtUtc) ? startedAtUtc : null;
    }

    private static string GetActiveTariffTitle(JsonElement? session)
    {
        if (!session.HasValue) return "";
        var seances = TryGet(session.Value, "seances");
        if (!seances.HasValue || seances.Value.ValueKind != JsonValueKind.Array) return "";

        var values = seances.Value.EnumerateArray().ToList();
        var active = values.FirstOrDefault(x => GetString(x, "status").Equals("ACTIVE", StringComparison.OrdinalIgnoreCase));
        if (active.ValueKind == JsonValueKind.Undefined)
            active = values.LastOrDefault();
        if (active.ValueKind == JsonValueKind.Undefined) return "";
        var tariff = TryGet(active, "tariff");
        return tariff.HasValue ? GetString(tariff.Value, "title") : "";
    }

    private static bool TryGetDateTimeUtc(JsonElement element, string name, out DateTime utc)
    {
        utc = default;
        var text = GetString(element, name);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (DateTimeOffset.TryParse(
                text,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeLocal,
                out var offset))
        {
            utc = offset.UtcDateTime;
            return true;
        }

        if (DateTime.TryParse(
                text,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeLocal,
                out var local))
        {
            utc = local.ToUniversalTime();
            return true;
        }

        return false;
    }

    private static bool TryGetSeconds(JsonElement element, string name, out double seconds)
    {
        seconds = 0;
        var value = TryGet(element, name);
        if (!value.HasValue)
            return false;

        if (value.Value.ValueKind == JsonValueKind.Number)
            return value.Value.TryGetDouble(out seconds);

        return double.TryParse(
            GetString(element, name).Replace(",", "."),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out seconds);
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
}
