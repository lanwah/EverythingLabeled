using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;
using EverythingLabeled.Client;

namespace EverythingLabeled.ExplorerHost;

public partial class MainWindow : Window
{
    private readonly HostUiContext _ctx;
    private HostMode _mode;
    private bool _businessEnabled;
    private bool _createMissing;
    private CancellationTokenSource? _batchCts;
    private CancellationTokenSource? _suggestCts;
    private CancellationTokenSource? _previewCts;
    private readonly DispatcherTimer _suggestTimer;
    private TextBox? _activeAliasBox;
    private ListBox? _activeSuggestList;
    private IReadOnlyList<PreviewRow> _previewRows = Array.Empty<PreviewRow>();
    private bool _suppressFindTextChanged;
    private readonly ObservableCollection<string> _findTags = new();
    private bool _findUseOr;

    public MainWindow(HostUiContext ctx)
    {
        _ctx = ctx;
        _mode = ctx.Mode;
        _businessEnabled = ctx.BusinessEnabled;
        InitializeComponent();

        FindChipList.ItemsSource = _findTags;

        if (ctx.Mode == HostMode.Find)
            EnableFindWindowChrome();

        _suggestTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SuggestQuery.DebounceMs) };
        _suggestTimer.Tick += async (_, _) =>
        {
            _suggestTimer.Stop();
            if (_mode == HostMode.Find)
                await RunFindDebouncedAsync().ConfigureAwait(true);
            else
                await RunSuggestAsync().ConfigureAwait(true);
        };

        Loaded += (_, _) =>
        {
            if (_mode == HostMode.Find)
                FocusFindQuery();
        };

        ApplyBanner(ctx.Banner);
        ShowMode(_mode);
        if (_mode is HostMode.Tags or HostMode.Untag)
            _ = LoadResourceTagsAsync();
    }

    private void EnableFindWindowChrome()
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
        Deactivated += OnFindDeactivated;
    }

    private void OnFindDeactivated(object? sender, EventArgs e)
    {
        if (_mode != HostMode.Find)
            return;

        Dispatcher.BeginInvoke(
            () =>
            {
                if (_mode == HostMode.Find && !IsActive)
                    Close();
            },
            DispatcherPriority.Background);
    }

    public void ActivateFromIpc()
    {
        void ActivateCore()
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            Show();
            Activate();

            // Brief Topmost flip helps some foreground races; ignore failures.
            var wasTopmost = Topmost;
            try
            {
                Topmost = true;
                Topmost = wasTopmost;
            }
            catch
            {
                // ignore
            }

            try
            {
                var helper = new WindowInteropHelper(this);
                if (helper.Handle != IntPtr.Zero)
                    _ = NativeMethods.SetForegroundWindow(helper.Handle);
            }
            catch
            {
                // Best-effort; taskbar flash is acceptable.
            }

            FocusFindQuery();
        }

        if (Dispatcher.CheckAccess())
            ActivateCore();
        else
            Dispatcher.Invoke(ActivateCore);
    }

    private void FocusFindQuery()
    {
        if (_mode != HostMode.Find)
            return;

        Dispatcher.BeginInvoke(
            () =>
            {
                FindQueryBox.Focus();
                Keyboard.Focus(FindQueryBox);
                FindQueryBox.CaretIndex = FindQueryBox.Text.Length;
                UpdateFindPlaceholder();
            },
            DispatcherPriority.Input);
    }

    private void UpdateFindPlaceholder()
    {
        FindQueryPlaceholder.Visibility = string.IsNullOrEmpty(FindQueryBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyFindChrome(bool findMode)
    {
        FindOpenButton.Visibility = findMode ? Visibility.Visible : Visibility.Collapsed;
        FindClearButton.Visibility = findMode ? Visibility.Visible : Visibility.Collapsed;
        FindCaptionBar.Visibility = findMode ? Visibility.Visible : Visibility.Collapsed;

        if (findMode)
        {
            var bg = (System.Windows.Media.Brush)FindResource("FindBgBrush");
            var muted = (System.Windows.Media.Brush)FindResource("FindMutedBrush");
            Background = bg;
            RootDock.Background = bg;
            RootDock.Margin = new Thickness(0);
            FooterDock.Margin = new Thickness(16, 0, 16, 12);
            StatusText.Foreground = muted;
            FindAndRadio.IsChecked = !_findUseOr;
            FindOrRadio.IsChecked = _findUseOr;
            StatusText.Text = Copy.PreviewCount(0);
            return;
        }

        ClearValue(BackgroundProperty);
        RootDock.ClearValue(Panel.BackgroundProperty);
        RootDock.Margin = new Thickness(10);
        FooterDock.Margin = new Thickness(0, 8, 0, 0);
        StatusText.ClearValue(TextBlock.ForegroundProperty);
    }

    private void OnFindCaptionMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            OnFindMaximize(sender, e);
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void OnFindMinimize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnFindMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnFindClose(object sender, RoutedEventArgs e) => Close();

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
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

    private void ShowMode(HostMode mode)
    {
        if (_mode == HostMode.Tag)
            _createMissing = CreateMissingCheck.IsChecked == true;

        _mode = mode;
        Title = mode switch
        {
            HostMode.Tag => Copy.WindowTitleTag,
            HostMode.Untag => Copy.WindowTitleUntag,
            HostMode.Tags => Copy.WindowTitleTags,
            HostMode.Find => Copy.WindowTitleFind,
            _ => "EverythingLabeled",
        };

        TagPanel.Visibility = mode == HostMode.Tag ? Visibility.Visible : Visibility.Collapsed;
        UntagPanel.Visibility = mode == HostMode.Untag ? Visibility.Visible : Visibility.Collapsed;
        TagsPanel.Visibility = mode == HostMode.Tags ? Visibility.Visible : Visibility.Collapsed;
        FindPanel.Visibility = mode == HostMode.Find ? Visibility.Visible : Visibility.Collapsed;

        CreateMissingCheck.IsChecked = _createMissing;

        FillPathLists();
        ApplyBusinessEnabled();
        UpdateUntagTagListVisibility();
        ApplyFindChrome(mode == HostMode.Find);

        _activeAliasBox = mode switch
        {
            HostMode.Tag => TagAliasBox,
            HostMode.Untag => UntagAliasBox,
            HostMode.Find => FindQueryBox,
            _ => null,
        };
        _activeSuggestList = mode switch
        {
            HostMode.Tag => TagSuggestList,
            HostMode.Untag => UntagSuggestList,
            HostMode.Find => FindSuggestList,
            _ => null,
        };

        if (mode == HostMode.Find && IsLoaded)
            FocusFindQuery();
    }

    private void UpdateUntagTagListVisibility()
    {
        var show = _mode == HostMode.Untag && UntagClickDelete.IsEnabled(IncludedUris().Count);
        UntagTagListPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
        {
            UntagTagList.ItemsSource = null;
            UntagTagList.SelectedItem = null;
        }
    }

    private void FillPathLists()
    {
        var selection = _ctx.Selection;
        var lines = selection is null
            ? Array.Empty<string>()
            : selection.Items
                .Select(i => Copy.PathLine(i.RawPath, i.Included, i.ExcludeReason))
                .ToArray();

        TagPathList.ItemsSource = lines;
        UntagPathList.ItemsSource = lines;
        TagsPathList.ItemsSource = lines;
    }

    private void ApplyBusinessEnabled()
    {
        var on = _businessEnabled;
        TagAliasBox.IsEnabled = on;
        TagSuggestList.IsEnabled = on;
        CreateMissingCheck.IsEnabled = on;
        TagSubmitButton.IsEnabled = on;
        UntagAliasBox.IsEnabled = on;
        UntagSuggestList.IsEnabled = on;
        UntagTagList.IsEnabled = on;
        UntagSubmitButton.IsEnabled = on;
        TagsTagList.IsEnabled = on;
        SwitchToTagButton.IsEnabled = on;
        SwitchToUntagButton.IsEnabled = on;
        FindQueryBox.IsEnabled = on;
        FindSuggestList.IsEnabled = on;
        FindResultList.IsEnabled = on;
        FindOpenButton.IsEnabled = on;
        FindClearButton.IsEnabled = on;
        FindAndRadio.IsEnabled = on;
        FindOrRadio.IsEnabled = on;
    }

    private void SetBusy(bool busy)
    {
        var on = _businessEnabled && !busy;
        TagAliasBox.IsEnabled = on;
        TagSuggestList.IsEnabled = on;
        CreateMissingCheck.IsEnabled = on;
        TagSubmitButton.IsEnabled = on;
        UntagAliasBox.IsEnabled = on;
        UntagSuggestList.IsEnabled = on;
        UntagTagList.IsEnabled = on;
        UntagSubmitButton.IsEnabled = on;
        SwitchToTagButton.IsEnabled = on;
        SwitchToUntagButton.IsEnabled = on;
        FindQueryBox.IsEnabled = on;
        FindSuggestList.IsEnabled = on;
        FindResultList.IsEnabled = on;
        FindOpenButton.IsEnabled = on;
        FindClearButton.IsEnabled = on;
        FindAndRadio.IsEnabled = on;
        FindOrRadio.IsEnabled = on;

        TagCancelButton.IsEnabled = busy && _mode == HostMode.Tag;
        UntagCancelButton.IsEnabled = busy && _mode == HostMode.Untag;
    }

    private IReadOnlyList<(string Uri, string Kind)> IncludedTagTargets()
    {
        var selection = _ctx.Selection;
        if (selection is null || selection.RejectedOverLimit)
            return Array.Empty<(string, string)>();

        return selection.Items
            .Where(i => i.Included && i.CanonicalUri is not null && i.Kind is not null)
            .Select(i => (i.CanonicalUri!, i.Kind!))
            .ToList();
    }

    private IReadOnlyList<string> IncludedUris()
    {
        var selection = _ctx.Selection;
        if (selection is null || selection.RejectedOverLimit)
            return Array.Empty<string>();

        return selection.Items
            .Where(i => i.Included && i.CanonicalUri is not null)
            .Select(i => i.CanonicalUri!)
            .ToList();
    }

    private async Task LoadResourceTagsAsync()
    {
        UntagTagList.ItemsSource = null;
        TagsTagList.ItemsSource = null;
        UpdateUntagTagListVisibility();

        if (!_businessEnabled || _ctx.Api is null)
            return;

        var uris = IncludedUris();
        if (uris.Count != 1)
            return;

        try
        {
            var result = await ResourceTags.GetTagsOrEmptyAsync(_ctx.Api, uris[0]).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                StatusText.Text = result.Error?.Message ?? Copy.TagsLoadFailed;
                return;
            }

            var rows = FlattenAliases(result.Data!.Items);
            if (_mode == HostMode.Untag && UntagClickDelete.IsEnabled(uris.Count))
                UntagTagList.ItemsSource = rows;
            TagsTagList.ItemsSource = rows;
        }
        catch (Exception ex)
        {
            StatusText.Text = Copy.TagsLoadFailed + "：" + ex.Message;
        }
    }

    private static List<AliasRow> FlattenAliases(IReadOnlyList<TagInfo> tags)
    {
        var rows = new List<AliasRow>();
        foreach (var tag in tags)
        {
            if (tag.Aliases.Count == 0)
            {
                rows.Add(new AliasRow(tag.Name, FormatTagLabel(tag.Name, tag.Namespace, tag.Name)));
                continue;
            }

            foreach (var alias in tag.Aliases)
                rows.Add(new AliasRow(alias, FormatTagLabel(alias, tag.Namespace, tag.Name)));
        }

        return rows;
    }

    private static string FormatTagLabel(string alias, string? ns, string name)
    {
        var head = string.IsNullOrEmpty(ns) ? name : ns + "/" + name;
        return alias == name ? head : $"{alias}  ({head})";
    }

    private void OnSuggestAliasChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box || !_businessEnabled)
            return;

        _activeAliasBox = box;
        _activeSuggestList = box == TagAliasBox ? TagSuggestList : UntagSuggestList;
        _suggestTimer.Stop();
        _suggestTimer.Start();
    }

    private async Task RunSuggestAsync()
    {
        if (_ctx.Api is null || _activeAliasBox is null || _activeSuggestList is null || !_businessEnabled)
            return;

        var q = SuggestQuery.Normalize(_activeAliasBox.Text);
        if (q is null)
        {
            _activeSuggestList.ItemsSource = null;
            return;
        }

        _suggestCts?.Cancel();
        _suggestCts = new CancellationTokenSource();
        var ct = _suggestCts.Token;

        try
        {
            var result = await _ctx.Api.SuggestAsync(q, SuggestQuery.Limit, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested)
                return;

            if (!result.IsSuccess)
            {
                _activeSuggestList.ItemsSource = null;
                return;
            }

            _activeSuggestList.ItemsSource = result.Data!.Items
                .Select(i => new AliasRow(i.Alias, FormatTagLabel(i.Alias, i.Namespace, i.Name)))
                .ToList();
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch
        {
            _activeSuggestList.ItemsSource = null;
        }
    }

    private void OnSuggestItemActivate(object sender, MouseButtonEventArgs e) => ApplySuggestSelection(sender as ListBox);

    private void OnSuggestListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplySuggestSelection(sender as ListBox);
            e.Handled = true;
        }
    }

    private void ApplySuggestSelection(ListBox? list)
    {
        if (list?.SelectedItem is not AliasRow row || _activeAliasBox is null)
            return;

        if (list == FindSuggestList)
        {
            ApplyFindSuggest(row.Alias);
            return;
        }

        _activeAliasBox.Text = row.Alias;
        list.ItemsSource = null;
    }

    private void ApplyFindSuggest(string alias) => CommitFindTag(alias);

    private void CommitFindTag(string? raw)
    {
        if (!FindQueryCompose.TryAdd(_findTags, raw))
            return;

        _suppressFindTextChanged = true;
        try
        {
            FindQueryBox.Text = string.Empty;
        }
        finally
        {
            _suppressFindTextChanged = false;
        }

        FindSuggestList.ItemsSource = null;
        UpdateFindPlaceholder();
        FocusFindQuery();
        _ = RunFindPreviewAsync();
    }

    private void OnFindChipRemove(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string alias })
            return;

        _findTags.Remove(alias);
        _ = RunFindPreviewAsync();
        FocusFindQuery();
    }

    private void OnFindClearTags(object sender, RoutedEventArgs e)
    {
        _findTags.Clear();
        _suppressFindTextChanged = true;
        try
        {
            FindQueryBox.Text = string.Empty;
        }
        finally
        {
            _suppressFindTextChanged = false;
        }

        FindSuggestList.ItemsSource = null;
        UpdateFindPlaceholder();
        _ = RunFindPreviewAsync();
        FocusFindQuery();
    }

    private void OnFindModeChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _mode != HostMode.Find)
            return;

        var useOr = FindOrRadio.IsChecked == true;
        if (useOr == _findUseOr)
            return;

        _findUseOr = useOr;
        _ = RunFindPreviewAsync();
    }

    private async void OnTagSubmit(object sender, RoutedEventArgs e)
    {
        _createMissing = CreateMissingCheck.IsChecked == true;
        var alias = SuggestQuery.Normalize(TagAliasBox.Text);
        if (alias is null)
        {
            StatusText.Text = Copy.EmptyAlias;
            return;
        }

        var targets = IncludedTagTargets();
        if (targets.Count == 0)
        {
            StatusText.Text = Copy.NoIncludedPaths;
            return;
        }

        if (_ctx.Api is null || _ctx.Config is null)
            return;

        await RunBatchAsync(
            ct => BatchTagOps.TagAsync(
                _ctx.Api,
                targets,
                alias,
                _createMissing,
                _ctx.Config.MaxConcurrency,
                ct)).ConfigureAwait(true);
    }

    private async void OnUntagSubmit(object sender, RoutedEventArgs e) =>
        await SubmitUntagAsync(SuggestQuery.Normalize(UntagAliasBox.Text)).ConfigureAwait(true);

    private async Task SubmitUntagAsync(string? alias)
    {
        if (alias is null)
        {
            StatusText.Text = Copy.EmptyAlias;
            return;
        }

        var uris = IncludedUris();
        if (uris.Count == 0)
        {
            StatusText.Text = Copy.NoIncludedPaths;
            return;
        }

        if (_ctx.Api is null || _ctx.Config is null || !_businessEnabled)
            return;

        if (_ctx.Config.ConfirmUntag)
        {
            var confirm = MessageBox.Show(
                Copy.ConfirmUntag(uris.Count),
                Copy.WindowTitleUntag,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK)
                return;
        }

        await RunBatchAsync(
            ct => BatchTagOps.UntagAsync(
                _ctx.Api,
                uris,
                alias,
                _ctx.Config.MaxConcurrency,
                ct)).ConfigureAwait(true);

        if (_mode == HostMode.Untag && UntagClickDelete.IsEnabled(uris.Count))
            await LoadResourceTagsAsync().ConfigureAwait(true);
    }

    private async Task RunBatchAsync(Func<CancellationToken, Task<BatchSummary>> run)
    {
        _batchCts?.Cancel();
        _batchCts = new CancellationTokenSource();
        SetBusy(true);
        StatusText.Text = Copy.BatchRunning;

        try
        {
            var summary = await run(_batchCts.Token).ConfigureAwait(true);
            StatusText.Text = Copy.BatchSummary(summary.Success, summary.Failed, summary.CancelledNotSent);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = Copy.BatchSummary(0, 0, IncludedUris().Count);
        }
        finally
        {
            SetBusy(false);
            _batchCts = null;
        }
    }

    private void OnBatchCancel(object sender, RoutedEventArgs e) => _batchCts?.Cancel();

    private async void OnUntagTagSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0)
            return;
        if (!UntagClickDelete.IsEnabled(IncludedUris().Count))
            return;
        if (_batchCts is not null || !_businessEnabled)
            return;
        if (e.AddedItems[0] is not AliasRow row)
            return;

        UntagAliasBox.Text = row.Alias;
        // Clear so the same alias can be clicked again after cancel / completion.
        UntagTagList.SelectedItem = null;
        await SubmitUntagAsync(row.Alias).ConfigureAwait(true);
    }

    private void OnSwitchToTag(object sender, RoutedEventArgs e)
    {
        ShowMode(HostMode.Tag);
    }

    private void OnSwitchToUntag(object sender, RoutedEventArgs e)
    {
        ShowMode(HostMode.Untag);
        _ = LoadResourceTagsAsync();
    }

    private void OnFindQueryChanged(object sender, TextChangedEventArgs e)
    {
        UpdateFindPlaceholder();

        if (_suppressFindTextChanged || !_businessEnabled || _mode != HostMode.Find)
            return;

        _activeAliasBox = FindQueryBox;
        _activeSuggestList = FindSuggestList;
        _suggestTimer.Stop();
        _suggestTimer.Start();
    }

    private async Task RunFindDebouncedAsync()
    {
        await RunFindSuggestAsync().ConfigureAwait(true);
        await RunFindPreviewAsync().ConfigureAwait(true);
    }

    private async Task RunFindSuggestAsync()
    {
        if (_ctx.Api is null || !_businessEnabled)
            return;

        var token = SuggestQuery.Normalize(FindQueryBox.Text);
        if (token is null)
        {
            FindSuggestList.ItemsSource = null;
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
                FindSuggestList.ItemsSource = null;
                return;
            }

            var rows = result.Data!.Items
                .Select(i => new AliasRow(i.Alias, FormatTagLabel(i.Alias, i.Namespace, i.Name)))
                .ToList();
            FindSuggestList.ItemsSource = rows;
            if (rows.Count > 0)
                FindSuggestList.SelectedIndex = 0;
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch
        {
            FindSuggestList.ItemsSource = null;
        }
    }

    private async Task RunFindPreviewAsync()
    {
        if (_ctx.Api is null || _ctx.Config is null || !_businessEnabled)
            return;

        var q = FindQueryCompose.Compose(_findTags, _findUseOr);
        if (q is null)
        {
            _previewRows = Array.Empty<PreviewRow>();
            FindResultList.ItemsSource = null;
            StatusText.Text = Copy.PreviewCount(0);
            return;
        }

        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;

        try
        {
            var result = await _ctx.Api.PreviewAsync(
                q,
                expand: null,
                maxDepth: null,
                expandedTagLimit: null,
                _ctx.Config.PreviewLimit,
                ct).ConfigureAwait(true);

            if (ct.IsCancellationRequested)
                return;

            if (!result.IsSuccess)
            {
                StatusText.Text = result.Error?.Message ?? Copy.PreviewFailed;
                _previewRows = Array.Empty<PreviewRow>();
                FindResultList.ItemsSource = null;
                return;
            }

            var items = result.Data!.Items;
            _previewRows = items
                .Select(h =>
                {
                    var action = FindOpen.Decide(h.Uri, h.Kind);
                    var canOpen = action.Kind != OpenKind.Unsupported;
                    return new PreviewRow(
                        h.Uri,
                        h.Kind,
                        FindResultPresentation.Title(h.Uri, h.DisplayName),
                        FindResultPresentation.Subtitle(h.Uri, h.Kind, canOpen),
                        FindResultPresentation.Glyph(h.Kind),
                        action);
                })
                .ToList();

            FindResultList.ItemsSource = _previewRows;
            if (_previewRows.Count > 0)
                FindResultList.SelectedIndex = 0;

            StatusText.Text = _previewRows.Count == 0
                ? Copy.PreviewEmpty
                : Copy.PreviewCount(_previewRows.Count);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            StatusText.Text = Copy.PreviewFailed + "：" + ex.Message;
            _previewRows = Array.Empty<PreviewRow>();
            FindResultList.ItemsSource = null;
        }
    }

    private void OnFindQueryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Back
            && FindQueryBox.CaretIndex == 0
            && string.IsNullOrEmpty(FindQueryBox.Text)
            && _findTags.Count > 0)
        {
            _findTags.RemoveAt(_findTags.Count - 1);
            _ = RunFindPreviewAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            if (FindSuggestList.Items.Count > 0)
            {
                ApplyFindListDown(FindSuggestList);
                e.Handled = true;
                return;
            }

            if (_previewRows.Count > 0)
            {
                ApplyFindListDown(FindResultList);
                e.Handled = true;
            }

            return;
        }

        if (e.Key != Key.Enter)
            return;

        if (TryCommitFindTagFromEnter())
        {
            e.Handled = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(FindQueryBox.Text) && TryOpenSelectedFindResult())
            e.Handled = true;
    }

    private void OnFindResultKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && TryOpenSelectedFindResult())
            e.Handled = true;
    }

    private static void ApplyFindListDown(ListBox list)
    {
        var next = FindListNavigation.NextDownIndex(list.SelectedIndex, list.Items.Count);
        if (next < 0)
            return;

        list.Focus();
        list.SelectedIndex = next;
        if (list.SelectedItem is not null)
            list.ScrollIntoView(list.SelectedItem);
    }

    private bool TryCommitFindTagFromEnter()
    {
        if (FindSuggestList.Items.Count > 0 && SuggestQuery.Normalize(FindQueryBox.Text) is not null)
        {
            if (FindSuggestList.SelectedItem is not AliasRow row)
            {
                FindSuggestList.SelectedIndex = 0;
                row = (AliasRow)FindSuggestList.SelectedItem!;
            }

            CommitFindTag(row.Alias);
            return true;
        }

        var draft = SuggestQuery.Normalize(FindQueryBox.Text);
        if (draft is null)
            return false;

        CommitFindTag(draft);
        return true;
    }

    private bool TryOpenSelectedFindResult()
    {
        if (_previewRows.Count == 0)
            return false;

        if (FindResultList.SelectedIndex < 0)
            FindResultList.SelectedIndex = 0;

        OnFindOpen(this, new RoutedEventArgs());
        return true;
    }

    private void OnFindOpen(object sender, RoutedEventArgs e)
    {
        var index = FindResultList.SelectedIndex;
        if (index < 0 || index >= _previewRows.Count)
            return;

        var row = _previewRows[index];
        if (row.Action.Kind == OpenKind.Unsupported)
        {
            StatusText.Text = Copy.CannotOpen;
            return;
        }

        FindOpen.Execute(row.Action);
    }

    private sealed record AliasRow(string Alias, string Display)
    {
        public override string ToString() => Display;
    }

    private sealed record PreviewRow(
        string Uri,
        string Kind,
        string Title,
        string Subtitle,
        string Glyph,
        OpenAction Action);
}
