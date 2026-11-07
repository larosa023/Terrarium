using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Terrarium.App.Services;

namespace Terrarium.App;

public partial class App : Application
{
    private const string MutexName = @"Local\Terrarium.larosa023.SingleInstance";
    private const string ShowEventName = @"Local\Terrarium.larosa023.Show";

    private Mutex? _mutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => AppPaths.Log($"Fatal: {args.ExceptionObject}");

        bool justUpdated = e.Args.Contains("--updated", StringComparer.OrdinalIgnoreCase);

        // One terrarium at a time. A second launch just brings the existing widget back.
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
        _ownsMutex = created;
        if (!created && justUpdated)
        {
            // The previous version is still shutting down after launching us.
            try { _ownsMutex = _mutex.WaitOne(TimeSpan.FromSeconds(20)); }
            catch (AbandonedMutexException) { _ownsMutex = true; }
        }
        if (!_ownsMutex)
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
            {
                existing.Set();
                existing.Dispose();
            }
            Shutdown();
            return;
        }

        Updater.CleanupAfterUpdate();

        _controller = new AppController();
        _controller.Start(justUpdated);

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.InvokeAsync(() => _controller?.ShowWidget()), null, Timeout.Infinite, executeOnlyOnce: false);

        SessionEnding += (_, _) => _controller?.SaveAll();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.SaveAll();
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        if (_ownsMutex)
        {
            try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { /* not owned on this thread */ }
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A cozy desk toy should never crash on you: log it and keep going.
        AppPaths.Log($"Unhandled: {e.Exception}");
        e.Handled = true;
    }
}
