using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using EverythingLabeled.Client;

namespace EverythingLabeled.ExplorerHost;

public partial class LibraryWindow : Window
{
    private const int PageSize = 50;
    private readonly HostUiContext _ctx;
    private readonly ObservableCollection<LibraryRow> _rows = new();
    private CancellationTokenSource? _searchCts;
    private LibraryRow? _pendingDelete;
    private bool _mutating;
    private int _loadGen;

    public LibraryWindow(HostUiContext ctx)
    {
        _ctx = ctx;
        InitializeComponent();
        EnableHostWindowChrome();
        TagList.ItemsSource = _rows;
        SearchBox.IsEnabled = ctx.BusinessEnabled && ctx.Api is not null;
        SearchPlaceholder.Text = Copy.LibrarySearchPlaceholder;
        ApplyBanner(ctx.Banner);
        Loaded += async (_, _) => await ReloadAsync().ConfigureAwait(true);
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

    private async void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        try
        {
            await Task.Delay(200, token).ConfigureAwait(true);
            await ReloadAsync(token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // newer keystroke
        }
    }

    private async Task ReloadAsync(CancellationToken ct = default)
    {
        if (_ctx.Api is null || !_ctx.BusinessEnabled)
        {
            _rows.Clear();
            EmptyHint.Visibility = Visibility.Visible;
            TagList.Visibility = Visibility.Collapsed;
            StatusText.Text = string.IsNullOrEmpty(_ctx.Banner) ? Copy.ServiceUnreachable : string.Empty;
            return;
        }

        var gen = ++_loadGen;
        var q = SearchBox.Text.Trim();
        string? query = q.Length == 0 ? null : q;
        var result = await _ctx.Api.ListTagsAsync(query, PageSize, 0, ct).ConfigureAwait(true);
        if (gen != _loadGen || ct.IsCancellationRequested)
            return;

        if (!result.IsSuccess)
        {
            StatusText.Text = result.Error?.Message ?? Copy.LibraryLoadFailed;
            return;
        }

        _rows.Clear();
        foreach (var tag in result.Data!.Items)
            _rows.Add(new LibraryRow(tag));

        var empty = _rows.Count == 0;
        var hasQuery = q.Length > 0;
        EmptyHint.Text = hasQuery ? Copy.LibraryNoMatch : Copy.LibraryEmpty;
        EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        TagList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Text = empty ? string.Empty : Copy.LibraryCount(result.Data.Total);
    }

    private void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LibraryRow row })
            return;

        foreach (var other in _rows)
        {
            if (other != row)
            {
                other.Editing = false;
                other.EditError = null;
            }
        }

        row.EditName = row.Name;
        row.EditError = null;
        row.Editing = true;
    }

    private void OnRenameCancel(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LibraryRow row })
            return;
        row.Editing = false;
        row.EditError = null;
        row.EditName = row.Name;
    }

    private async void OnRenameSave(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LibraryRow row } || _ctx.Api is null || _mutating)
            return;

        var next = row.EditName.Trim();
        if (next.Length == 0)
        {
            row.EditError = Copy.LibraryEmptyName;
            return;
        }

        _mutating = true;
        try
        {
            var from = ResolveRenameFrom(row);
            var result = await _ctx.Api.RenameTagAsync(from, next).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                row.EditError = result.Error?.Message ?? Copy.LibraryRenameFailed;
                return;
            }

            row.Replace(result.Data!);
            StatusText.Text = Copy.LibraryCount(_rows.Count);
        }
        finally
        {
            _mutating = false;
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LibraryRow row })
            return;
        _pendingDelete = row;
        DeleteMessage.Text = Copy.LibraryDeleteConfirm(row.Name);
        DeleteOverlay.Visibility = Visibility.Visible;
    }

    private void OnDeleteCancel(object sender, RoutedEventArgs e)
    {
        _pendingDelete = null;
        DeleteOverlay.Visibility = Visibility.Collapsed;
    }

    private async void OnDeleteConfirm(object sender, RoutedEventArgs e)
    {
        if (_pendingDelete is null || _ctx.Api is null || _mutating)
            return;

        var row = _pendingDelete;
        _mutating = true;
        try
        {
            var result = await _ctx.Api.DeleteTagAsync(row.Id).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                StatusText.Text = result.Error?.Message ?? Copy.LibraryDeleteFailed;
                return;
            }

            _rows.Remove(row);
            _pendingDelete = null;
            DeleteOverlay.Visibility = Visibility.Collapsed;
            var empty = _rows.Count == 0;
            EmptyHint.Text = Copy.LibraryEmpty;
            EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            TagList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            StatusText.Text = empty ? string.Empty : Copy.LibraryCount(_rows.Count);
        }
        finally
        {
            _mutating = false;
        }
    }

    private static string ResolveRenameFrom(LibraryRow row)
    {
        foreach (var alias in row.Tag.Aliases)
        {
            if (string.Equals(alias, row.Name, StringComparison.Ordinal))
                return alias;
        }

        return row.Tag.Aliases.Count > 0 ? row.Tag.Aliases[0] : row.Name;
    }

    private void OnCaptionMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            OnMaximize(sender, e);
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
