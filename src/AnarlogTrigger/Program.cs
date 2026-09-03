using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace AnarlogTrigger;

public static class Program
{
    private static SingleInstance? _singleInstance;

    [STAThread]
    private static void Main(string[] args)
    {
        _singleInstance = SingleInstance.TryAcquire();
        if (_singleInstance is null)
        {
            return;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        _singleInstance.Dispose();
        _singleInstance = null;
    }
}
