using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using EverythingLabeled.Client;

namespace EverythingLabeled.ExplorerHost;

public partial class ResourcesWindow : Window
{
    private readonly HostUiContext _ctx;
    private readonly ObservableCollection<string> _chips = new();
    private readonly ObservableCollection<ResourceRow> _rows = new();
    private readonly DispatcherTimer _suggestTimer;
    private readonly bool _useOr;
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _suggestCts;
    private ResourceRow? _pending;
    private string? _selectedKind;
    private bool _mutating;
    private int _loadGen;

    public ResourcesWindow(HostUiContext ctx)
    {
        _ctx = ctx;
        _useOr = ctx.Config?.FindUseOr ?? false;
        InitializeComponent();
        Title = Copy.WindowTitleResources;
        EnableHostWindowChrome();
        HostWindowDeactivateDismiss.Attach(this, () => true, Close);
        ChipList.ItemsSource = _chips;
        ResultList.ItemsSource = _rows;
        QueryBox.IsEnabled = ctx.BusinessEnabled && ctx.Api is not null;
        _suggestTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SuggestQuery.DebounceMs) };
        _suggestTimer.Tick += async (_, _) =>
        {
            _suggestTimer.Stop();
            await RunSuggestAsync().ConfigureAwait(true);
        };
        ApplyBanner(ctx.Banner);
        ShowIdle();
        Loaded += (_, _) => FocusQuery();
    }

    private void FocusQuery()
    {
        if (!QueryBox.IsEnabled)
            return;

        Dispatcher.BeginInvoke(
            () =>
            {
                if (!IsLoaded)
                    return;
                QueryBox.Focus();
                Keyboard.Focus(QueryBox);
            },
            DispatcherPriority.Input);
    }

    private void EnableHostWindowChrome()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(
            this,
            new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                UseAeroCaptionButtons = false,
            });
    }

    private void ApplyBanner(string? banner)
    {
        if (string.IsNullOrWhiteSpace(banner))
        {
            BannerText.Visibility = Visibility.Collapsed;
            BannerText.Text = string.Empty;
            return;
        }

        BannerText.Text = banner;
        BannerText.Visibility = Visibility.Visible;
    }

    private void ShowIdle()
    {
        _rows.Clear();
        SuggestList.ItemsSource = null;
        EmptyHint.Text = Copy.ResourcesEmpty;
        StatusText.Text = Copy.ResourcesNeedTags;
        UpdateQueryChrome();
        ApplyListLayout();
    }

    private void UpdateQueryChrome()
    {
        var hasChips = _chips.Count > 0;
        QueryPlaceholder.Text = hasChips ? Copy.ResourcesQueryMore : Copy.ResourcesQueryPlaceholder;
        QueryPlaceholder.Visibility = string.IsNullOrEmpty(QueryBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ClearTagsButton.Visibility = hasChips ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnQueryChanged(object sender, TextChangedEventArgs e)
    {
        UpdateQueryChrome();
        if (!QueryBox.IsEnabled)
            return;

        _suggestTimer.Stop();
        _suggestTimer.Start();
    }

    private void OnQueryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Back && QueryBox.CaretIndex == 0 && QueryBox.Text.Length == 0 && _chips.Count > 0)
        {
            _chips.RemoveAt(_chips.Count - 1);
            SchedulePreview();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Down or Key.Up)
        {
            if (MoveFocusedList(e.Key == Key.Down))
                e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
            return;

        if (TryCommitChipFromEnter())
            e.Handled = true;
    }

    private void OnChipRemove(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string alias })
            return;

        _chips.Remove(alias);
        SchedulePreview();
        FocusQuery();
    }

    private void OnClearTags(object sender, RoutedEventArgs e)
    {
        _chips.Clear();
        QueryBox.Clear();
        SchedulePreview();
        FocusQuery();
    }

    private void SchedulePreview()
    {
        UpdateQueryChrome();
        SuggestList.ItemsSource = null;
        ApplyListLayout();
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        _ = RunPreviewAsync(_previewCts.Token);
        _suggestTimer.Stop();
        _suggestTimer.Start();
    }

    private async Task RunSuggestAsync()
    {
        if (_ctx.Api is null || !_ctx.BusinessEnabled)
            return;

        var token = SuggestQuery.Normalize(QueryBox.Text);
        if (token is null)
        {
            SuggestList.ItemsSource = null;
            if (_chips.Count == 0)
                StatusText.Text = Copy.ResourcesNeedTags;
            ApplyListLayout();
            return;
        }

        _suggestCts?.Cancel();
        _suggestCts = new CancellationTokenSource();
        var ct = _suggestCts.Token;
        try
        {
            var result = await _ctx.Api.SuggestAsync(token, SuggestQuery.Limit, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested)
                return;

            if (!result.IsSuccess)
            {
                SuggestList.ItemsSource = null;
                if (_chips.Count == 0)
                    StatusText.Text = FindStatusText.IdleOrSuggest(token, 0);
                ApplyListLayout();
                return;
            }

            var rows = result.Data!.Items.Select(TagLabelFormat.FromSuggest).ToList();
            SuggestList.ItemsSource = rows;
            if (rows.Count > 0)
                SuggestList.SelectedIndex = 0;
            if (_chips.Count == 0)
                StatusText.Text = FindStatusText.IdleOrSuggest(token, rows.Count);
            ApplyListLayout();
        }
        catch (OperationCanceledException)
        {
            // newer keystroke
        }
        catch
        {
            SuggestList.ItemsSource = null;
            if (_chips.Count == 0)
                StatusText.Text = FindStatusText.IdleOrSuggest(token, 0);
            ApplyListLayout();
        }
    }

    private async Task RunPreviewAsync(CancellationToken ct)
    {
        var q = FindQueryCompose.Compose(_chips, _useOr);
        if (q is null || _ctx.Api is null || !_ctx.BusinessEnabled)
        {
            _rows.Clear();
            EmptyHint.Text = Copy.ResourcesEmpty;
            if (SuggestQuery.Normalize(QueryBox.Text) is null)
                StatusText.Text = Copy.ResourcesNeedTags;
            ApplyListLayout();
            return;
        }

        var gen = ++_loadGen;
        try
        {
            await Task.Delay(200, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var result = await _ctx.Api.PreviewAsync(
            q,
            expand: null,
            maxDepth: null,
            expandedTagLimit: null,
            _ctx.Config?.PreviewLimit ?? 16,
            ct: ct).ConfigureAwait(true);
        if (gen != _loadGen || ct.IsCancellationRequested)
            return;

        if (!result.IsSuccess)
        {
            StatusText.Text = result.Error?.Message ?? Copy.PreviewFailed;
            return;
        }

        _rows.Clear();
        foreach (var hit in result.Data!.Items)
            _rows.Add(new ResourceRow(hit));

        var empty = _rows.Count == 0;
        EmptyHint.Text = Copy.ResourcesNoMatch;
        StatusText.Text = Copy.PreviewCount(_rows.Count);
        ApplyListLayout();
        if (!empty)
            ResultList.SelectedIndex = 0;
    }

    private void ApplyListLayout()
    {
        var hasDraft = SuggestQuery.Normalize(QueryBox.Text) is not null;
        var suggestCount = SuggestList.Items.Count;
        var hasPreviewQuery = FindQueryCompose.Compose(_chips, _useOr) is not null;
        var (placement, showPreview) = FindListLayout.Resolve(
            hasDraft,
            suggestCount,
            _chips.Count,
            hasPreviewQuery);

        switch (placement)
        {
            case FindSuggestPlacement.Main:
                Grid.SetRow(SuggestList, 2);
                SuggestList.Visibility = Visibility.Visible;
                SuggestList.VerticalAlignment = VerticalAlignment.Stretch;
                SuggestList.ClearValue(FrameworkElement.MaxHeightProperty);
                SuggestList.Margin = new Thickness(0, 8, 0, 0);
                break;
            case FindSuggestPlacement.Compact:
                Grid.SetRow(SuggestList, 1);
                SuggestList.Visibility = Visibility.Visible;
                SuggestList.VerticalAlignment = VerticalAlignment.Top;
                SuggestList.MaxHeight = FindListLayout.CompactSuggestMaxHeight;
                SuggestList.Margin = new Thickness(8, 6, 8, 0);
                break;
            default:
                Grid.SetRow(SuggestList, 1);
                SuggestList.Visibility = Visibility.Collapsed;
                SuggestList.VerticalAlignment = VerticalAlignment.Top;
                SuggestList.MaxHeight = FindListLayout.CompactSuggestMaxHeight;
                SuggestList.Margin = new Thickness(8, 6, 8, 0);
                break;
        }

        var showResults = showPreview && _rows.Count > 0;
        var showEmpty = placement != FindSuggestPlacement.Main && !showResults;
        EmptyHint.Visibility = showEmpty ? Visibility.Visible : Visibility.Collapsed;
        ResultList.Visibility = showResults ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Text = _chips.Count == 0 ? Copy.ResourcesEmpty : Copy.ResourcesNoMatch;
    }

    private bool TryCommitChipFromEnter()
    {
        if (SuggestList.Items.Count > 0 && SuggestQuery.Normalize(QueryBox.Text) is not null)
        {
            if (SuggestList.SelectedItem is not AliasRow row)
            {
                SuggestList.SelectedIndex = 0;
                row = (AliasRow)SuggestList.SelectedItem!;
            }

            CommitChip(row.Alias);
            return true;
        }

        var draft = SuggestQuery.Normalize(QueryBox.Text);
        if (draft is null)
            return false;

        CommitChip(draft);
        return true;
    }

    private void CommitChip(string? raw)
    {
        if (!FindQueryCompose.TryAdd(_chips, raw))
            return;

        QueryBox.Clear();
        SuggestList.ItemsSource = null;
        SchedulePreview();
        FocusQuery();
    }

    private void OnSuggestActivate(object sender, MouseButtonEventArgs e)
    {
        if (SuggestList.SelectedItem is AliasRow row)
            CommitChip(row.Alias);
    }

    private void OnSuggestKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        if (SuggestList.SelectedItem is AliasRow row)
        {
            CommitChip(row.Alias);
            e.Handled = true;
        }
    }

    private bool MoveFocusedList(bool down)
    {
        ListBox? list = SuggestList.Items.Count > 0
            ? SuggestList
            : _rows.Count > 0
                ? ResultList
                : null;
        if (list is null)
            return false;

        var next = down
            ? FindListNavigation.NextDownIndex(list.SelectedIndex, list.Items.Count)
            : FindListNavigation.NextUpIndex(list.SelectedIndex, list.Items.Count);
        if (next < 0)
            return false;

        list.SelectedIndex = next;
        if (list.SelectedItem is not null)
            list.ScrollIntoView(list.SelectedItem);
        return true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DeleteOverlay.Visibility == Visibility.Visible || ReplaceOverlay.Visibility == Visibility.Visible)
            return;
        if (e.Key is not (Key.Up or Key.Down))
            return;
        if (Keyboard.FocusedElement is TextBox box && !ReferenceEquals(box, QueryBox))
            return;
        if (MoveFocusedList(e.Key == Key.Down))
            e.Handled = true;
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ResourceRow row })
            return;

        ResultList.SelectedItem = row;
        _pending = row;
        DeleteMessage.Text = Copy.ResourceDeleteConfirm(row.Title, row.Hit.Uri);
        DeleteOverlay.Visibility = Visibility.Visible;
    }

    private void OnDeleteCancel(object sender, RoutedEventArgs e)
    {
        _pending = null;
        DeleteOverlay.Visibility = Visibility.Collapsed;
    }

    private async void OnDeleteConfirm(object sender, RoutedEventArgs e)
    {
        if (_pending is null || _ctx.Api is null || _mutating)
            return;

        var row = _pending;
        _mutating = true;
        try
        {
            var result = await _ctx.Api.DeleteResourceAsync(row.Hit.Id).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                StatusText.Text = result.Error?.Message ?? Copy.LibraryDeleteFailed;
                return;
            }

            _rows.Remove(row);
            _pending = null;
            DeleteOverlay.Visibility = Visibility.Collapsed;
            EmptyHint.Text = Copy.ResourcesNoMatch;
            StatusText.Text = Copy.PreviewCount(_rows.Count);
            ApplyListLayout();
            if (_rows.Count > 0)
                ResultList.SelectedIndex = 0;
        }
        finally
        {
            _mutating = false;
        }
    }

    private void OnReplaceClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ResourceRow row })
            return;

        ResultList.SelectedItem = row;
        _pending = row;
        ReplaceLead.Text = Copy.ResourceReplaceLead(row.Title);
        ReplaceTags.ItemsSource = row.TagLabels;
        AddressBox.Text = string.Empty;
        ReplaceError.Visibility = Visibility.Collapsed;
        ReplaceError.Text = string.Empty;
        ApplyKind(null);
        ReplaceOverlay.Visibility = Visibility.Visible;
        AddressBox.Focus();
    }

    private void OnAddressChanged(object sender, TextChangedEventArgs e) =>
        ApplyKind(ReplaceKindGuess.Kind(AddressBox.Text));

    private void OnKindClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind })
            return;

        _selectedKind = kind;
        RefreshKindHint();
        PaintKindButtons();
    }

    private void ApplyKind(string? kind)
    {
        _selectedKind = kind;
        RefreshKindHint();
        PaintKindButtons();
    }

    private void RefreshKindHint() =>
        KindHint.Text = ReplaceKindGuess.Hint(AddressBox.Text, _selectedKind);

    private void PaintKindButtons()
    {
        PaintKindButton(KindFile, ReplaceKindGuess.File);
        PaintKindButton(KindFolder, ReplaceKindGuess.Folder);
        PaintKindButton(KindUrl, ReplaceKindGuess.Url);
    }

    private void PaintKindButton(Button button, string kind)
    {
        var selected = string.Equals(_selectedKind, kind, StringComparison.Ordinal);
        button.Background = (Brush)FindResource(selected ? "HostRowSelectedBrush" : "HostInputBgBrush");
        button.BorderBrush = (Brush)FindResource(selected ? "HostAccentBrush" : "HostBorderBrush");
        button.Foreground = (Brush)FindResource(selected ? "HostFgBrush" : "HostMutedBrush");
    }

    private void OnReplaceCancel(object sender, RoutedEventArgs e)
    {
        _pending = null;
        ReplaceOverlay.Visibility = Visibility.Collapsed;
    }

    private async void OnReplaceConfirm(object sender, RoutedEventArgs e)
    {
        if (_pending is null || _ctx.Api is null || _mutating || _selectedKind is null)
        {
            if (_selectedKind is null)
                ShowReplaceError(Copy.ReplaceAddressRequired);
            return;
        }

        var row = _pending;
        _mutating = true;
        try
        {
            var result = await ResourceReplace.ExecuteAsync(
                _ctx.Api,
                row.Hit.Id,
                row.TagLabels,
                AddressBox.Text,
                _selectedKind).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                ShowReplaceError(result.Error?.Message ?? Copy.ReplaceKindMismatch);
                return;
            }

            _rows.Remove(row);
            _pending = null;
            ReplaceOverlay.Visibility = Visibility.Collapsed;
            SchedulePreview();
        }
        finally
        {
            _mutating = false;
        }
    }

    private void ShowReplaceError(string message)
    {
        ReplaceError.Text = message;
        ReplaceError.Visibility = Visibility.Visible;
    }

    private void OnCaptionMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            OnMaximize(sender, e);
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
