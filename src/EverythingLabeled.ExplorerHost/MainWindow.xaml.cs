using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;
using EverythingLabeled.Client;
using EverythingLabeled.ExplorerShared;

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
    private readonly ObservableCollection<string> _tagAppliedAliases = new();
    private List<AliasRow> _untagAliases = [];
    private bool _untagTagsLoaded;
    private bool _findUseOr;
    private string? _tagBatchStatusUntilEdit;
    private bool _tagHadAliasDraft;
    private bool _allowFindExit;
    private int _findActivation;
    private bool _blockDeactivateDismiss;

    public MainWindow(HostUiContext ctx)
    {
        _ctx = ctx;
        _mode = ctx.Mode;
        _businessEnabled = ctx.BusinessEnabled;
        InitializeComponent();

        FindChipList.ItemsSource = _findTags;
        TagChipList.ItemsSource = _tagAppliedAliases;
        _findUseOr = ctx.Config?.FindUseOr ?? false;

        EnableHostWindowChrome();
        HostWindowDeactivateDismiss.Attach(
            this,
            () => !_blockDeactivateDismiss,
            DismissOnDeactivate,
            () => _findActivation);
        if (ctx.Mode == HostMode.Find)
        {
            Closing += OnFindClosing;
            var app = System.Windows.Application.Current;
            if (app is not null)
                app.SessionEnding += (_, _) => _allowFindExit = true;
        }

        _suggestTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SuggestQuery.DebounceMs) };
        _suggestTimer.Tick += async (_, _) =>
        {
            _suggestTimer.Stop();
            if (_mode == HostMode.Find)
                await RunFindDebouncedAsync().ConfigureAwait(true);
            else
                await RunSuggestAsync().ConfigureAwait(true);
        };

        Loaded += (_, _) => FocusModeInput();

        ApplyBanner(ctx.Banner);
        ShowMode(_mode);
        if (_mode is HostMode.Tags or HostMode.Untag or HostMode.Tag)
            _ = LoadResourceTagsAsync();
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

    private void DismissOnDeactivate()
    {
        if (_mode == HostMode.Find)
            HideFind();
        else
            Close();
    }

    private void OnFindClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowFindExit || _mode != HostMode.Find)
            return;

        e.Cancel = true;
        HideFind();
    }

    private void HideFind()
    {
        if (_mode != HostMode.Find)
            return;

        Hide();
    }

    /// <summary>Show/restore Find and return HWND for the caller (Tray) to steal foreground.</summary>
    public nint ActivateFromIpc()
    {
        nint hwnd = 0;

        void ActivateCore()
        {
            _findActivation++;
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            Show();
            Activate();

            // Brief Topmost flip helps some Z-order races; ignore failures.
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

            var helper = new WindowInteropHelper(this);
            helper.EnsureHandle();
            hwnd = helper.Handle;
            if (hwnd != 0)
                WindowForeground.TryBringToForeground(hwnd);

            FocusFindQuery();
        }

        if (Dispatcher.CheckAccess())
            ActivateCore();
        else
            Dispatcher.Invoke(ActivateCore);

        return hwnd;
    }

    private void FocusModeInput()
    {
        if (!IsLoaded)
            return;

        switch (_mode)
        {
            case HostMode.Find:
                FocusFindQuery();
                break;
            case HostMode.Tag:
                FocusTagAlias();
                break;
            case HostMode.Untag:
                FocusUntagTagFilter();
                break;
        }
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

    private void FocusTagAlias()
    {
        if (_mode != HostMode.Tag)
            return;

        Dispatcher.BeginInvoke(
            () =>
            {
                TagAliasBox.Focus();
                Keyboard.Focus(TagAliasBox);
                TagAliasBox.CaretIndex = TagAliasBox.Text.Length;
                UpdateTagAliasPlaceholder();
            },
            DispatcherPriority.Input);
    }

    private void FocusUntagTagFilter()
    {
        if (_mode != HostMode.Untag)
            return;

        Dispatcher.BeginInvoke(
            () =>
            {
                UntagTagFilterBox.Focus();
                Keyboard.Focus(UntagTagFilterBox);
                UntagTagFilterBox.CaretIndex = UntagTagFilterBox.Text.Length;
                UpdateUntagFilterPlaceholder();
            },
            DispatcherPriority.Input);
    }

    private void UpdateFindPlaceholder()
    {
        FindQueryPlaceholder.Visibility = string.IsNullOrEmpty(FindQueryBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        FindClearTagsButton.Visibility = _findTags.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateTagAliasPlaceholder()
    {
        TagAliasPlaceholder.Text = _tagAppliedAliases.Count > 0
            ? Copy.TagAliasContinuePlaceholder
            : Copy.AliasPlaceholder;
        TagAliasPlaceholder.Visibility = string.IsNullOrEmpty(TagAliasBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RefreshTagAliasStatus()
    {
        if (_mode != HostMode.Tag || _batchCts is not null)
            return;

        var alias = SuggestQuery.Normalize(TagAliasBox.Text);
        if (_tagBatchStatusUntilEdit is not null)
        {
            if (alias is null)
            {
                if (_tagHadAliasDraft)
                    _tagBatchStatusUntilEdit = null;
                else
                {
                    SetHostStatus(_tagBatchStatusUntilEdit);
                    _tagHadAliasDraft = false;
                    return;
                }
            }
            else
            {
                _tagBatchStatusUntilEdit = null;
            }
        }

        _tagHadAliasDraft = alias is not null;

        if (alias is null)
        {
            SetHostStatus(Copy.TagReady);
            return;
        }

        var count = IncludedTagTargets().Count;
        SetHostStatus(count == 0
            ? Copy.NoIncludedPaths
            : Copy.TagWillApply(count, alias));
    }

    private void SetHostStatus(string text) => StatusText.Text = text;

    private void ApplyHostChrome(HostMode mode)
    {
        var tagsMode = mode == HostMode.Tags;
        SwitchToTagButton.Visibility = tagsMode ? Visibility.Visible : Visibility.Collapsed;
        SwitchToUntagButton.Visibility = tagsMode ? Visibility.Visible : Visibility.Collapsed;
        FindCaptionBar.Visibility = Visibility.Visible;
        FindCaptionTitle.Text = Title;
        HostCaptionIcon.Text = mode switch
        {
            HostMode.Tag => "\uE8EC",
            HostMode.Untag => "\uE74D",
            HostMode.Tags => "\uE8FD",
            _ => "\uE8B7",
        };

        var bg = (System.Windows.Media.Brush)FindResource("HostBgBrush");
        var muted = (System.Windows.Media.Brush)FindResource("HostMutedBrush");
        Background = bg;
        RootDock.Background = bg;
        RootDock.Margin = new Thickness(0);
        FooterDock.Margin = new Thickness(16, 12, 16, 12);
        StatusText.Foreground = muted;
        StatusText.Visibility = Visibility.Visible;
        FooterDock.Visibility = Visibility.Visible;
        var untagFooter = mode == HostMode.Untag;
        if (untagFooter)
        {
            FooterDock.Height = 40;
            FooterDock.MinHeight = 40;
            StatusText.TextWrapping = TextWrapping.NoWrap;
            StatusText.TextTrimming = TextTrimming.CharacterEllipsis;
        }
        else
        {
            FooterDock.ClearValue(FrameworkElement.HeightProperty);
            FooterDock.ClearValue(FrameworkElement.MinHeightProperty);
            StatusText.TextWrapping = TextWrapping.Wrap;
            StatusText.TextTrimming = TextTrimming.None;
        }

        if (mode == HostMode.Find)
        {
            SetHostStatus(Copy.PreviewCount(0));
            ApplyFindListLayout();
        }
        else if (mode == HostMode.Tag)
            RefreshTagAliasStatus();
        else if (mode == HostMode.Untag)
            RefreshUntagChrome();
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

    private void OnFindClose(object sender, RoutedEventArgs e)
    {
        if (_mode == HostMode.Find)
            HideFind();
        else
            Close();
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
        UpdateCurrentTagListVisibility();
        ApplyHostChrome(mode);
        CloseAliasSuggestPopups();

        _activeAliasBox = mode switch
        {
            HostMode.Tag => TagAliasBox,
            HostMode.Find => FindQueryBox,
            _ => null,
        };
        _activeSuggestList = mode switch
        {
            HostMode.Tag => TagSuggestList,
            HostMode.Find => FindSuggestList,
            _ => null,
        };

        FocusModeInput();
    }

    private void UpdateCurrentTagListVisibility()
    {
        var count = IncludedUris().Count;
        var showTag = _mode == HostMode.Tag && UntagClickDelete.ShowsPanel(HostMode.Tag, count);
        var showUntag = _mode == HostMode.Untag;
        TagCurrentTagPanel.Visibility = showTag ? Visibility.Visible : Visibility.Collapsed;
        UntagTagListPanel.Visibility = showUntag ? Visibility.Visible : Visibility.Collapsed;
        if (!showTag)
            TagCurrentTagList.ItemsSource = null;
        if (!showUntag)
        {
            _untagAliases = [];
            _untagTagsLoaded = false;
            UntagTagList.ItemsSource = null;
            ApplyUntagTagFilter(updateStatus: false);
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
        TagChipList.IsEnabled = on;
        TagSuggestList.IsEnabled = on;
        CreateMissingCheck.IsEnabled = on;
        TagSubmitButton.IsEnabled = on;
        TagCurrentTagList.IsEnabled = on;
        UntagTagList.IsEnabled = on;
        TagsTagList.IsEnabled = on;
        SwitchToTagButton.IsEnabled = on;
        SwitchToUntagButton.IsEnabled = on;
        FindQueryBox.IsEnabled = on;
        FindSuggestList.IsEnabled = on;
        FindResultList.IsEnabled = on;
        FindClearTagsButton.IsEnabled = on;
        UpdateUntagFilterEnabled();
    }

    private void SetBusy(bool busy)
    {
        var on = _businessEnabled && !busy;
        TagAliasBox.IsEnabled = on;
        TagChipList.IsEnabled = on;
        TagSuggestList.IsEnabled = on;
        CreateMissingCheck.IsEnabled = on;
        TagSubmitButton.IsEnabled = on;
        TagCurrentTagList.IsEnabled = on;
        UntagTagList.IsEnabled = on;
        SwitchToTagButton.IsEnabled = on;
        SwitchToUntagButton.IsEnabled = on;
        FindQueryBox.IsEnabled = on;
        FindSuggestList.IsEnabled = on;
        FindResultList.IsEnabled = on;
        FindClearTagsButton.IsEnabled = on;
        UpdateUntagFilterEnabled();

        TagCancelButton.IsEnabled = busy && _mode == HostMode.Tag;
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

    private async Task LoadResourceTagsAsync(bool refreshUntagStatus = true)
    {
        TagCurrentTagList.ItemsSource = null;
        TagsTagList.ItemsSource = null;
        _untagAliases = [];
        _untagTagsLoaded = false;
        UpdateCurrentTagListVisibility();

        if (!_businessEnabled || _ctx.Api is null)
        {
            if (refreshUntagStatus)
                RefreshUntagChrome();
            return;
        }

        var uris = IncludedUris();
        if (uris.Count != 1)
        {
            if (refreshUntagStatus)
                RefreshUntagChrome();
            return;
        }

        try
        {
            var result = await ResourceTags.GetTagsOrEmptyAsync(_ctx.Api, uris[0]).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                SetHostStatus(result.Error?.Message ?? Copy.TagsLoadFailed);
                ApplyUntagTagFilter(updateStatus: false);
                return;
            }

            var rows = FlattenAliases(result.Data!.Items);
            if (UntagClickDelete.ShowsPanel(HostMode.Tag, uris.Count))
                TagCurrentTagList.ItemsSource = rows;
            _untagAliases = rows;
            _untagTagsLoaded = true;
            TagsTagList.ItemsSource = rows;
            if (_mode == HostMode.Untag)
            {
                ApplyUntagTagFilter(updateStatus: refreshUntagStatus);
                FocusUntagTagFilter();
            }
            else if (UntagClickDelete.ShowsPanel(HostMode.Untag, uris.Count))
                UntagTagList.ItemsSource = rows;
        }
        catch (Exception ex)
        {
            SetHostStatus(Copy.TagsLoadFailed + "：" + ex.Message);
            ApplyUntagTagFilter(updateStatus: false);
        }
    }

    private static List<AliasRow> FlattenAliases(IReadOnlyList<TagInfo> tags)
    {
        var rows = new List<AliasRow>();
        foreach (var tag in tags)
        {
            if (tag.Aliases.Count == 0)
            {
                rows.Add(new AliasRow(tag.Name, FormatTagLabel(tag.Name, tag.Namespace, tag.Name), tag.NameInitials));
                continue;
            }

            foreach (var alias in tag.Aliases)
                rows.Add(new AliasRow(alias, FormatTagLabel(alias, tag.Namespace, tag.Name), tag.NameInitials));
        }

        return rows;
    }

    private static string FormatTagLabel(string alias, string? ns, string name)
    {
        var head = string.IsNullOrEmpty(ns) ? name : ns + "/" + name;
        return alias == name ? head : $"{alias}  ({head})";
    }

    private void OnUntagTagFilterChanged(object sender, TextChangedEventArgs e)
    {
        UpdateUntagFilterPlaceholder();
        if (_mode == HostMode.Untag)
            ApplyUntagTagFilter(updateStatus: _batchCts is null);
    }

    private void OnUntagTagFilterFocusChanged(object sender, RoutedEventArgs e) =>
        UpdateUntagFilterPlaceholder();

    private void RefreshUntagChrome()
    {
        if (_mode != HostMode.Untag || _batchCts is not null)
            return;

        var included = IncludedUris().Count;
        if (!UntagClickDelete.ShowsPanel(HostMode.Untag, included))
        {
            ApplyUntagTagFilter(updateStatus: false);
            if (included == 0)
                SetHostStatus(Copy.TagReady);
            else
            {
                SetHostStatus(Copy.UntagNeedsOnePath);
                UntagTagsEmpty.Text = Copy.UntagNeedsOnePath;
                UntagTagsEmpty.Visibility = Visibility.Visible;
            }

            return;
        }

        if (!_untagTagsLoaded)
        {
            SetHostStatus(Copy.TagReady);
            ApplyUntagTagFilter(updateStatus: false);
            return;
        }

        ApplyUntagTagFilter(updateStatus: true);
    }

    private void UpdateUntagFilterPlaceholder()
    {
        var show = string.IsNullOrEmpty(UntagTagFilterBox.Text) && !UntagTagFilterBox.IsKeyboardFocused;
        UntagTagFilterPlaceholder.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateUntagFilterEnabled()
    {
        var enabled = _businessEnabled
            && _batchCts is null
            && _untagTagsLoaded
            && _untagAliases.Count > 0
            && UntagClickDelete.ShowsPanel(HostMode.Untag, IncludedUris().Count);
        UntagTagFilterBox.IsEnabled = enabled;
        UntagTagFilterPlaceholder.Opacity = enabled ? 1 : 0.45;
    }

    private void ApplyUntagTagFilter(bool updateStatus)
    {
        var query = UntagTagFilterBox.Text;
        var shown = _untagAliases
            .Where(row => CurrentTagFilter.Matches(row.Display, row.Alias, row.NameInitials, query))
            .ToList();
        UntagTagList.ItemsSource = shown;
        var view = CurrentTagFilter.Describe(_untagAliases.Count, shown.Count, query);
        UntagTagFilterCount.Text = view.CountText;
        UntagTagFilterCount.Visibility = view.ShowCount ? Visibility.Visible : Visibility.Collapsed;
        UntagTagsEmpty.Text = Copy.UntagTagsEmpty;
        UntagTagsEmpty.Visibility = view.ShowEmpty ? Visibility.Visible : Visibility.Collapsed;
        UntagFilterMiss.Visibility = view.ShowMiss ? Visibility.Visible : Visibility.Collapsed;
        UpdateUntagFilterPlaceholder();
        UpdateUntagFilterEnabled();
        if (updateStatus && _mode == HostMode.Untag && _batchCts is null)
            SetHostStatus(view.Status);
    }

    private void OnSuggestAliasChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box || box != TagAliasBox || !_businessEnabled)
            return;

        _activeAliasBox = box;
        _activeSuggestList = TagSuggestList;
        UpdateTagAliasPlaceholder();
        RefreshTagAliasStatus();

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
            SyncAliasSuggestPopup(_activeSuggestList);
            if (_mode == HostMode.Tag)
                CreateMissingCheck.IsChecked = false;
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
                SyncAliasSuggestPopup(_activeSuggestList);
                return;
            }

            var items = result.Data!.Items;
            var rows = items
                .Select(i => new AliasRow(i.Alias, FormatTagLabel(i.Alias, i.Namespace, i.Name), i.NameInitials))
                .ToList();
            _activeSuggestList.ItemsSource = rows;
            if (rows.Count > 0)
                _activeSuggestList.SelectedIndex = 0;
            SyncAliasSuggestPopup(_activeSuggestList);

            if (_mode == HostMode.Tag)
                CreateMissingCheck.IsChecked = CreateMissingSuggest.ShouldCheck(q, items.Select(i => i.Alias));
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch
        {
            _activeSuggestList.ItemsSource = null;
            SyncAliasSuggestPopup(_activeSuggestList);
        }
    }

    private void SyncAliasSuggestPopup(ListBox? list)
    {
        TagSuggestPopup.IsOpen = list == TagSuggestList && list.Items.Count > 0;
    }

    private void CloseAliasSuggestPopups()
    {
        TagSuggestPopup.IsOpen = false;
        TagSuggestList.ItemsSource = null;
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
        SyncAliasSuggestPopup(list);
        if (_mode == HostMode.Tag)
            CreateMissingCheck.IsChecked = false;
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
        ApplyFindListLayout();
        FocusFindQuery();
        _ = RunFindPreviewAsync();
    }

    private void OnFindChipRemove(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string alias })
            return;

        _findTags.Remove(alias);
        UpdateFindPlaceholder();
        ApplyFindListLayout();
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
        ApplyFindListLayout();
        _ = RunFindPreviewAsync();
        FocusFindQuery();
    }

    private async void OnTagSubmit(object sender, RoutedEventArgs e) =>
        await SubmitTagAsync().ConfigureAwait(true);

    private async Task SubmitTagAsync()
    {
        _createMissing = CreateMissingCheck.IsChecked == true;
        var alias = ResolveTagAliasForSubmit();
        if (alias is null)
        {
            SetHostStatus(Copy.EmptyAlias);
            return;
        }

        var targets = IncludedTagTargets();
        if (targets.Count == 0)
        {
            SetHostStatus(Copy.NoIncludedPaths);
            return;
        }

        if (_ctx.Api is null || _ctx.Config is null)
            return;

        var summary = await RunBatchAsync(
            ct => BatchTagOps.TagAsync(
                _ctx.Api,
                targets,
                alias,
                _createMissing,
                _ctx.Config.MaxConcurrency,
                ct)).ConfigureAwait(true);

        if (_mode != HostMode.Tag)
            return;

        if (summary.Success > 0 && !_tagAppliedAliases.Contains(alias))
            _tagAppliedAliases.Add(alias);

        TagAliasBox.Text = string.Empty;
        CloseAliasSuggestPopups();
        UpdateTagAliasPlaceholder();
        _tagBatchStatusUntilEdit = Copy.BatchSummary(summary.Success, summary.Failed, summary.CancelledNotSent);
        SetHostStatus(_tagBatchStatusUntilEdit);
        _tagHadAliasDraft = false;
        FocusTagAlias();
        if (summary.Success > 0 && UntagClickDelete.ShowsPanel(HostMode.Tag, IncludedUris().Count))
            await LoadResourceTagsAsync().ConfigureAwait(true);
    }

    private string? ResolveTagAliasForSubmit()
    {
        if (TagSuggestPopup.IsOpen
            && TagSuggestList.Items.Count > 0
            && SuggestQuery.Normalize(TagAliasBox.Text) is not null)
        {
            if (TagSuggestList.SelectedItem is not AliasRow row)
            {
                TagSuggestList.SelectedIndex = 0;
                row = (AliasRow)TagSuggestList.SelectedItem!;
            }

            return row.Alias;
        }

        return SuggestQuery.Normalize(TagAliasBox.Text);
    }

    private async void OnTagChipRemove(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string alias })
            return;

        if (_batchCts is not null || !_businessEnabled)
            return;

        if (!await SubmitUntagAsync(alias).ConfigureAwait(true))
            return;

        _tagAppliedAliases.Remove(alias);
        UpdateTagAliasPlaceholder();
        RefreshTagAliasStatus();
    }

    private void OnTagAliasPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && TagSuggestPopup.IsOpen && TagSuggestList.Items.Count > 0)
        {
            ApplySuggestListDown(TagSuggestList);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up && TagSuggestPopup.IsOpen && TagSuggestList.Items.Count > 0)
        {
            ApplySuggestListUp(TagSuggestList);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        _ = SubmitTagAsync();
    }

    private async Task<bool> SubmitUntagAsync(string? alias)
    {
        if (alias is null)
        {
            SetHostStatus(Copy.EmptyAlias);
            return false;
        }

        var uris = IncludedUris();
        if (uris.Count == 0)
        {
            SetHostStatus(Copy.NoIncludedPaths);
            return false;
        }

        if (_ctx.Api is null || _ctx.Config is null || !_businessEnabled)
            return false;

        if (_ctx.Config.ConfirmUntag)
        {
            _blockDeactivateDismiss = true;
            try
            {
                var confirm = MessageBox.Show(
                    Copy.ConfirmUntag(uris.Count),
                    Copy.WindowTitleUntag,
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question);
                if (confirm != MessageBoxResult.OK)
                    return false;
            }
            finally
            {
                _blockDeactivateDismiss = false;
            }
        }

        var summary = await RunBatchAsync(
            ct => BatchTagOps.UntagAsync(
                _ctx.Api,
                uris,
                alias,
                _ctx.Config.MaxConcurrency,
                ct)).ConfigureAwait(true);

        if (UntagClickDelete.ShowsPanel(_mode, uris.Count))
            await LoadResourceTagsAsync(refreshUntagStatus: false).ConfigureAwait(true);

        return summary.Success > 0;
    }

    private async Task<BatchSummary> RunBatchAsync(Func<CancellationToken, Task<BatchSummary>> run)
    {
        _batchCts?.Cancel();
        _batchCts = new CancellationTokenSource();
        SetBusy(true);
        SetHostStatus(Copy.BatchRunning);

        try
        {
            var summary = await run(_batchCts.Token).ConfigureAwait(true);
            if (_mode != HostMode.Tag)
                SetHostStatus(Copy.BatchSummary(summary.Success, summary.Failed, summary.CancelledNotSent));
            return summary;
        }
        catch (OperationCanceledException)
        {
            var cancelled = new BatchSummary(0, 0, IncludedUris().Count, Array.Empty<BatchItemResult>());
            if (_mode != HostMode.Tag)
                SetHostStatus(Copy.BatchSummary(0, 0, cancelled.CancelledNotSent));
            return cancelled;
        }
        finally
        {
            SetBusy(false);
            _batchCts = null;
        }
    }

    private void OnBatchCancel(object sender, RoutedEventArgs e) => _batchCts?.Cancel();

    private async void OnCurrentTagChipRemove(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string alias })
            return;
        if (!UntagClickDelete.ShowsPanel(_mode, IncludedUris().Count))
            return;
        if (_batchCts is not null || !_businessEnabled)
            return;

        if (!await SubmitUntagAsync(alias).ConfigureAwait(true))
            return;

        if (_mode != HostMode.Tag || !_tagAppliedAliases.Remove(alias))
            return;

        UpdateTagAliasPlaceholder();
        RefreshTagAliasStatus();
    }

    private void OnSwitchToTag(object sender, RoutedEventArgs e)
    {
        ShowMode(HostMode.Tag);
        _ = LoadResourceTagsAsync();
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

    private void ApplyFindListLayout()
    {
        if (_mode != HostMode.Find)
            return;

        var hasDraft = SuggestQuery.Normalize(FindQueryBox.Text) is not null;
        var suggestCount = FindSuggestList.Items.Count;
        var hasPreviewQuery = FindQueryCompose.Compose(_findTags, _findUseOr) is not null;
        var (placement, showPreview) = FindListLayout.Resolve(
            hasDraft,
            suggestCount,
            _findTags.Count,
            hasPreviewQuery);

        switch (placement)
        {
            case FindSuggestPlacement.Main:
                Grid.SetRow(FindSuggestList, 2);
                FindSuggestList.Visibility = Visibility.Visible;
                FindSuggestList.VerticalAlignment = VerticalAlignment.Stretch;
                FindSuggestList.ClearValue(FrameworkElement.MaxHeightProperty);
                FindSuggestList.Margin = new Thickness(8, 4, 8, 8);
                break;
            case FindSuggestPlacement.Compact:
                Grid.SetRow(FindSuggestList, 1);
                FindSuggestList.Visibility = Visibility.Visible;
                FindSuggestList.VerticalAlignment = VerticalAlignment.Top;
                FindSuggestList.MaxHeight = FindListLayout.CompactSuggestMaxHeight;
                FindSuggestList.Margin = new Thickness(16, 6, 16, 0);
                break;
            default:
                Grid.SetRow(FindSuggestList, 1);
                FindSuggestList.Visibility = Visibility.Collapsed;
                FindSuggestList.VerticalAlignment = VerticalAlignment.Top;
                FindSuggestList.MaxHeight = FindListLayout.CompactSuggestMaxHeight;
                FindSuggestList.Margin = new Thickness(16, 6, 16, 0);
                break;
        }

        FindResultList.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task RunFindSuggestAsync()
    {
        if (_ctx.Api is null || !_businessEnabled)
            return;

        var token = SuggestQuery.Normalize(FindQueryBox.Text);
        if (token is null)
        {
            FindSuggestList.ItemsSource = null;
            if (_findTags.Count == 0)
                SetHostStatus(FindStatusText.IdleOrSuggest(null, 0));
            ApplyFindListLayout();
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
                if (_findTags.Count == 0)
                    SetHostStatus(FindStatusText.IdleOrSuggest(token, 0));
                ApplyFindListLayout();
                return;
            }

            var rows = result.Data!.Items
                .Select(i => new AliasRow(i.Alias, FormatTagLabel(i.Alias, i.Namespace, i.Name), i.NameInitials))
                .ToList();
            FindSuggestList.ItemsSource = rows;
            if (rows.Count > 0)
                FindSuggestList.SelectedIndex = 0;
            if (_findTags.Count == 0)
                SetHostStatus(FindStatusText.IdleOrSuggest(token, rows.Count));
            ApplyFindListLayout();
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch
        {
            FindSuggestList.ItemsSource = null;
            if (_findTags.Count == 0)
                SetHostStatus(FindStatusText.IdleOrSuggest(token, 0));
            ApplyFindListLayout();
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
            SetHostStatus(FindStatusText.IdleOrSuggest(
                SuggestQuery.Normalize(FindQueryBox.Text),
                FindSuggestList.Items.Count));
            ApplyFindListLayout();
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
                SetHostStatus(result.Error?.Message ?? Copy.PreviewFailed);
                _previewRows = Array.Empty<PreviewRow>();
                FindResultList.ItemsSource = null;
                ApplyFindListLayout();
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
                        action);
                })
                .ToList();

            FindResultList.ItemsSource = _previewRows;
            if (_previewRows.Count > 0)
                FindResultList.SelectedIndex = 0;

            SetHostStatus(_previewRows.Count == 0
                ? Copy.PreviewEmpty
                : Copy.PreviewCount(_previewRows.Count));
            ApplyFindListLayout();
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            SetHostStatus(Copy.PreviewFailed + "：" + ex.Message);
            _previewRows = Array.Empty<PreviewRow>();
            FindResultList.ItemsSource = null;
            ApplyFindListLayout();
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
            UpdateFindPlaceholder();
            ApplyFindListLayout();
            _ = RunFindPreviewAsync();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Down or Key.Up)
        {
            if (MoveFocusedFindList(e.Key == Key.Down))
                e.Handled = true;
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

    private bool MoveFocusedFindList(bool down)
    {
        ListBox? list = FindSuggestList.Items.Count > 0
            ? FindSuggestList
            : _previewRows.Count > 0
                ? FindResultList
                : null;
        if (list is null)
            return false;

        if (down)
            ApplySuggestListDown(list);
        else
            ApplySuggestListUp(list);
        return true;
    }

    private static void ApplySuggestListDown(ListBox list)
    {
        var next = FindListNavigation.NextDownIndex(list.SelectedIndex, list.Items.Count);
        if (next < 0)
            return;

        list.SelectedIndex = next;
        if (list.SelectedItem is not null)
            list.ScrollIntoView(list.SelectedItem);
    }

    private static void ApplySuggestListUp(ListBox list)
    {
        var next = FindListNavigation.NextUpIndex(list.SelectedIndex, list.Items.Count);
        if (next < 0)
            return;

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
            SetHostStatus(Copy.CannotOpen);
            return;
        }

        FindOpen.Execute(row.Action);
    }

    private sealed record AliasRow(string Alias, string Display, string NameInitials)
    {
        public override string ToString() => Display;
    }

    private sealed record PreviewRow(
        string Uri,
        string Kind,
        string Title,
        string Subtitle,
        OpenAction Action);
}
