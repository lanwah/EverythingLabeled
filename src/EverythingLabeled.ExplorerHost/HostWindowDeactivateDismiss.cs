using System.Windows;
using System.Windows.Threading;

namespace EverythingLabeled.ExplorerHost;

/// <summary>
/// Click outside the window (deactivate) dismisses Host surfaces — Find hides; other modes close.</summary>
internal static class HostWindowDeactivateDismiss
{
    public static void Attach(
        Window window,
        Func<bool> shouldDismissOnDeactivate,
        Action dismiss,
        Func<int>? getActivationGeneration = null)
    {
        window.Deactivated += (_, _) =>
        {
            if (!shouldDismissOnDeactivate())
                return;

            var activation = getActivationGeneration?.Invoke() ?? 0;
            window.Dispatcher.BeginInvoke(
                () =>
                {
                    if (!shouldDismissOnDeactivate())
                        return;
                    if (window.IsActive || !window.IsVisible)
                        return;
                    if (window.WindowState == WindowState.Minimized)
                        return;
                    if (getActivationGeneration is not null && activation != getActivationGeneration())
                        return;

                    dismiss();
                },
                DispatcherPriority.Background);
        };
    }
}
