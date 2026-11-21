using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Serilog;
using XIVLauncher.DCTravel;

namespace XIVLauncher.Views;

public partial class TravelWindow : Window
{
    private readonly DCTravelClient client;
    private readonly bool canLaunchGame;
    private IReadOnlyList<DCTravelGroup> sourceGroups = [];
    private CancellationTokenSource? pollCancellation;
    private PendingOrder? pending;
    private string? destinationArea;
    private int page = 1;
    private int pages;
    private bool loaded;
    private bool busy;
    private bool submitUncertain;
    private bool needsConfirmation;
    private bool closeConfirmationOpen;
    private bool closeConfirmed;

    public TravelWindow(DCTravelClient client, string account, bool canLaunchGame)
    {
        this.client = client;
        this.canLaunchGame = canLaunchGame;
        InitializeComponent();
        AccountLabel.Text = account;
        SourceBox.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(GroupOption.Label));
        TargetBox.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(GroupOption.Label));
        OrderList.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(OrderOption.Label));
        CharacterBox.SelectionChanged += (_, _) => UpdateControls();
        TargetBox.SelectionChanged += (_, _) => UpdateControls();
        Closing += OnClosing;
        Opened += async (_, _) => await RunAsync(LoadAsync);
        UpdateControls();
    }

    private async void OnRefresh(object? sender, RoutedEventArgs args) => await RunAsync(LoadAsync);

    private async Task LoadAsync()
    {
        if (string.IsNullOrWhiteSpace(destinationArea))
            Notice.Text = "正在连接旅行服务…";
        if (!loaded)
        {
            await client.GetValidCookie();
            loaded = true;
        }
        sourceGroups = (await client.QueryGroupListTravelSource()).SelectMany(area => area.GroupList).ToArray();
        SourceBox.ItemsSource = sourceGroups.Select(group => new GroupOption(group)).ToArray();
        SourceBox.SelectedItem = null;
        CharacterBox.ItemsSource = null;
        TargetBox.ItemsSource = null;
        await LoadOrdersAsync(1);
        Notice.Text = pending != null ? "有一笔待完成的旅行订单。" : "请选择来源服务器和角色。";
    }

    private async void OnSourceChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (busy || SourceBox.SelectedItem is not GroupOption source)
            return;
        await RunAsync(async () =>
        {
            destinationArea = null;
            CharacterBox.ItemsSource = null;
            TargetBox.ItemsSource = null;
            Notice.Text = "正在查询角色和目的服务器…";
            var group = source.Group;
            var roles = client.QueryRoleList(group.AreaID, group.GroupID);
            var targets = client.QueryGroupListTravelTarget(group.AreaID, group.GroupID);
            await Task.WhenAll(roles, targets);
            CharacterBox.ItemsSource = await roles;
            TargetBox.ItemsSource = (await targets).SelectMany(area => area.GroupList)
                                                  .Select(item => new GroupOption(item)).ToArray();
            Notice.Text = CharacterBox.ItemCount == 0 ? "此服务器没有可旅行的角色。" : "请选择角色和目的服务器。";
        });
    }

    private async void OnTravel(object? sender, RoutedEventArgs args) => await RunAsync(async () =>
    {
        if (pending != null || submitUncertain || SourceBox.SelectedItem is not GroupOption source ||
            TargetBox.SelectedItem is not GroupOption target || CharacterBox.SelectedItem is not DCTravelCharacter character)
            return;

        if (!await ConfirmDialog.AskAsync(this, "提交旅行申请",
            $"将「{character.Name}」从 {source.Label} 传送到 {target.Label}？", "提交申请"))
            return;

        await SubmitAsync(() => client.TravelOrder(target.Group, source.Group, character), target.Group.AreaName);
    });

    private async void OnReturn(object? sender, RoutedEventArgs args) => await RunAsync(async () =>
    {
        if (pending != null || submitUncertain || OrderList.SelectedItem is not OrderOption option)
            return;
        var order = option.Order;
        var groups = sourceGroups.Where(group => group.AreaName == order.TargetAreaName &&
                                                group.GroupName == order.TargetGroupName).ToArray();
        var group = groups.Length == 1 ? groups[0] : null;
        if (group == null)
            throw new InvalidOperationException("无法确认角色当前所在的目的服务器，请刷新旅行服务后重试。");
        if (!await ConfirmDialog.AskAsync(this, "返回原大区", $"让「{order.RoleName}」从 {order.TargetAreaName} · {order.TargetGroupName} 返回 {order.SourceAreaName} · {order.SourceGroupName}？", "提交返回"))
            return;

        await SubmitAsync(() => client.TravelBack(order.OrderID, group.GroupID, group.GroupCode, group.GroupName), order.SourceAreaName);
    });

    private async Task SubmitAsync(Func<Task<string>> submit, string targetArea)
    {
        destinationArea = null;
        Notice.Text = "正在提交申请…";
        try
        {
            pending = new PendingOrder(await submit(), targetArea);
        }
        catch (Exception ex) when (HasUncertainResult(ex))
        {
            submitUncertain = true;
            Notice.Text = "提交结果未知。请先查看在途角色或盛趣订单，确认服务器是否已受理。";
            TravelTabs.SelectedIndex = 1;
            return;
        }

        await PollAsync();
    }

    private async Task PollAsync()
    {
        if (pending is not { } order)
            return;

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        pollCancellation = cancellation;
        needsConfirmation = false;
        UpdateControls();
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var result = await client.QueryOrderStatus(order.Id);
                var message = string.Join(" ", new[] { result.CheckMessage, result.MigrationMessage }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
                switch (result.Status)
                {
                    case DCTravelStatusType.NeedConfirmation:
                        needsConfirmation = true;
                        Notice.Text = string.IsNullOrWhiteSpace(message) ? "服务器正在等待确认传送。" : message;
                        return;
                    case DCTravelStatusType.Success:
                        pending = null;
                        destinationArea = order.Area;
                        Notice.Text = $"旅行已完成。当前大区为 {order.Area}。";
                        return;
                    case DCTravelStatusType.TravelFailed:
                    case DCTravelStatusType.PreCheckFailed:
                        pending = null;
                        Notice.Text = string.IsNullOrWhiteSpace(message) ? "服务器未能完成旅行。" : message;
                        return;
                    default:
                        Notice.Text = string.IsNullOrWhiteSpace(message) ? "服务器正在处理订单…" : message;
                        break;
                }
                await Task.Delay(TimeSpan.FromSeconds(2), cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally { pollCancellation = null; }
        Notice.Text = "已停止查询。服务器仍可能继续处理此订单。";
    }

    private async void OnCheckOrder(object? sender, RoutedEventArgs args) => await RunAsync(PollAsync);
    private async void OnConfirmOrder(object? sender, RoutedEventArgs args) => await RunAsync(() => ConfirmOrderAsync(true));
    private async void OnRejectOrder(object? sender, RoutedEventArgs args) => await RunAsync(() => ConfirmOrderAsync(false));

    private async Task ConfirmOrderAsync(bool confirmed)
    {
        if (!needsConfirmation || pending is not { } order)
            return;
        if (!confirmed && !await ConfirmDialog.AskAsync(this, "取消服务器订单", "确定取消等待确认的旅行订单？", "取消订单"))
            return;

        needsConfirmation = false;
        try { await client.MigrationConfirmOrder(order.Id, confirmed); }
        catch (Exception ex) when (HasUncertainResult(ex))
        {
            Notice.Text = "服务器未返回确认结果。请继续查询订单状态。";
            return;
        }
        if (!confirmed)
        {
            pending = null;
            Notice.Text = "订单已取消。";
            return;
        }
        await PollAsync();
    }

    private async Task LoadOrdersAsync(int index)
    {
        if (!loaded)
            return;
        if (!await client.InitOrderPage())
            throw new InvalidOperationException("暂时无法打开订单页，请稍后刷新。");
        var result = await client.QueryMigrationOrders(index);
        page = index;
        pages = Math.Max(1, result.TotalPageNum);
        PageLabel.Text = $"第 {page} / {pages} 页";
        OrderList.ItemsSource = result.Orders.Select(order => new OrderOption(order)).ToArray();
    }

    private async void OnRefreshOrders(object? sender, RoutedEventArgs args) => await RunAsync(() => LoadOrdersAsync(1));
    private async void OnPreviousPage(object? sender, RoutedEventArgs args) => await RunAsync(() => LoadOrdersAsync(page - 1));
    private async void OnNextPage(object? sender, RoutedEventArgs args) => await RunAsync(() => LoadOrdersAsync(page + 1));
    private void OnOrderChanged(object? sender, SelectionChangedEventArgs args) => UpdateControls();

    private void OnUseOrderArea(object? sender, RoutedEventArgs args)
    {
        if (OrderList.SelectedItem is OrderOption option)
            Close(new TravelWindowResult(option.Order.TargetAreaName, false));
    }

    private void OnPause(object? sender, RoutedEventArgs args) => pollCancellation?.Cancel();
    private void OnLaunch(object? sender, RoutedEventArgs args) => Close(new TravelWindowResult(destinationArea, true));

    private void OnClose(object? sender, RoutedEventArgs args) => Close(new TravelWindowResult(destinationArea, false));

    private void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (busy || closeConfirmationOpen)
        {
            args.Cancel = true;
            return;
        }
        if (pending == null || closeConfirmed || args.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
            return;

        args.Cancel = true;
        closeConfirmationOpen = true;
        UpdateControls();
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                closeConfirmed = await ConfirmDialog.AskAsync(this, "关闭旅行窗口",
                    "关闭后将停止查询，服务器仍可能继续处理订单。", "关闭窗口");
            }
            finally
            {
                closeConfirmationOpen = false;
                UpdateControls();
            }
            if (closeConfirmed)
                Close(new TravelWindowResult(destinationArea, false));
        });
    }

    private void UpdateControls()
    {
        if (TravelTabs == null)
            return;
        var canInteract = !busy && !closeConfirmationOpen;
        TravelTabs.IsEnabled = loaded && canInteract;
        RefreshServiceButton.IsEnabled = canInteract;
        SubmitButton.IsEnabled = !submitUncertain && pending == null && SourceBox.SelectedItem != null
                                  && CharacterBox.SelectedItem != null && TargetBox.SelectedItem != null;
        ReturnButton.IsEnabled = !submitUncertain && pending == null && OrderList.SelectedItem != null;
        ChooseDestinationButton.IsEnabled = pending == null && OrderList.SelectedItem != null;
        PreviousPageButton.IsEnabled = page > 1;
        NextPageButton.IsEnabled = page < pages;
        PauseButton.IsVisible = pollCancellation != null;
        CloseButton.IsEnabled = canInteract;
        AcceptOrderButton.IsVisible = RejectOrderButton.IsVisible = canInteract && needsConfirmation;
        CheckOrderButton.IsVisible = canInteract && pending != null && !needsConfirmation;
        LaunchButton.IsVisible = canInteract && canLaunchGame && pending == null && !string.IsNullOrWhiteSpace(destinationArea);
    }

    private static bool HasUncertainResult(Exception error) =>
        error is HttpRequestException or IOException or OperationCanceledException or System.Text.Json.JsonException ||
        error is DCTravelAPIException api && (api.IsNetworkTimeout || api.ErrorCode is 0 or >= 500);

    private async Task RunAsync(Func<Task> run)
    {
        if (busy || closeConfirmationOpen)
            return;
        busy = true;
        UpdateControls();
        try { await run(); }
        catch (DCTravelAPIException ex) when (ex.IsServiceMaintenance)
        {
            loaded = false;
            Notice.Text = "跨大区旅行服务维护中，请稍后刷新。";
        }
        catch (Exception ex)
        {
            Log.Warning("[TravelWindow] 操作失败: {ErrorType}", ex.GetType().Name);
            Notice.Text = ex is DCTravelAPIException api ? api.ReturnMessage ?? api.Message : ex.Message;
        }
        finally
        {
            busy = false;
            UpdateControls();
        }
    }

    private sealed record PendingOrder(string Id, string Area);
    private sealed record GroupOption(DCTravelGroup Group)
    {
        public string Label => $"{Group.AreaName} · {Group.GroupName}";
    }

    private sealed record OrderOption(DCTravelMigrationOrder Order)
    {
        public string Label => $"{Order.RoleName}   {Order.SourceAreaName} · {Order.SourceGroupName} → {Order.TargetAreaName} · {Order.TargetGroupName}";
    }
}

public sealed record TravelWindowResult(string? Destination, bool StartGame);
