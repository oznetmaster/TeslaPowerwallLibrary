// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace TeslaPowerwallLibrary.Login;

/// <summary>
/// A minimal <see cref="SynchronizationContext"/> for a thread running a raw Win32 message loop.
/// Queues posted callbacks and wakes the loop with a custom window message, mirroring what
/// <c>DispatcherSynchronizationContext</c> (WPF) and <c>WindowsFormsSynchronizationContext</c> (WinForms)
/// do internally — required here because WebView2's async APIs must resume on the thread that owns the
/// host window, and a bare Win32 message loop has no synchronization context by default.
/// </summary>
internal sealed class Win32MessageLoopSynchronizationContext : SynchronizationContext
	{
	public const uint InvokeMessage = NativeMethods.WM_APP + 2;

	private readonly IntPtr _hwnd;
	private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new ();

	/// <summary>Initializes a new instance of the <see cref="Win32MessageLoopSynchronizationContext"/> class.</summary>
	/// <param name="hwnd">The message-loop window used to schedule queued callbacks.</param>
	public Win32MessageLoopSynchronizationContext (IntPtr hwnd)
		{
		_hwnd = hwnd;
		}

	/// <inheritdoc/>
	public override void Post (SendOrPostCallback d, object? state)
		{
		_queue.Enqueue ((d, state));
		NativeMethods.PostMessage (_hwnd, InvokeMessage, IntPtr.Zero, IntPtr.Zero);
		}

	/// <inheritdoc/>
	public override void Send (SendOrPostCallback d, object? state) =>
		// Not exercised by await continuations (which use Post); provided only to satisfy the base contract.
		d (state);

	/// <inheritdoc/>
	public override SynchronizationContext CreateCopy () => this;

	/// <summary>Dequeues and invokes a single queued callback, called from the message loop thread.</summary>
	public void RunOne ()
		{
		if (_queue.TryDequeue (out var item))
			item.Callback (item.State);
		}
	}

/// <summary>
/// Hosts the interactive Tesla OAuth login in a bare Win32 top-level window with an embedded WebView2
/// control, using Core WebView2 APIs directly rather than the WPF or WinForms wrappers. Runs on its own
/// dedicated thread with a classic <c>GetMessage</c>/<c>DispatchMessage</c> loop so the calling process
/// (console or app) does not need to own or share a UI message loop.
/// </summary>
internal sealed class NativeLoginWindow
	{
	private const string WindowClassName = "TeslaPowerwallLibrary.Login.NativeLoginWindow";
	private const int WindowWidth = 560;
	private const int WindowHeight = 820;

	private const uint WM_APP_ABORT = NativeMethods.WM_APP + 1;

	private readonly TaskCompletionSource<TeslaCloudLoginResult> _completion =
		new (TaskCreationOptions.RunContinuationsAsynchronously);

	private readonly string _region;
	private readonly TimeSpan _timeout;
	private readonly CancellationToken _cancellationToken;

	// Keeps the delegate passed to RegisterClassEx alive for the lifetime of the window class.
	private NativeMethods.WndProcDelegate? _wndProcDelegate;

	private IntPtr _hwnd;
	private Microsoft.Web.WebView2.Core.CoreWebView2Controller? _controller;
	private TeslaAuthRequest? _authRequest;
	private bool _exchangeStarted;
	private bool _completed;

	private NativeLoginWindow (string region, TimeSpan timeout, CancellationToken cancellationToken)
		{
		_region = region;
		_timeout = timeout;
		_cancellationToken = cancellationToken;
		}

	/// <summary>
	/// Runs the interactive Tesla login on a dedicated STA thread and returns the outcome once the user
	/// completes, cancels, or the operation fails or times out.
	/// </summary>
	/// <param name="region">The Tesla region to authenticate against (<c>us</c> or <c>cn</c>).</param>
	/// <param name="timeout">Maximum time to wait for the user to complete the login.</param>
	/// <param name="cancellationToken">A token used to abandon the login early.</param>
	/// <returns>The login result, including tokens on success.</returns>
	public static Task<TeslaCloudLoginResult> RunAsync (string region, TimeSpan timeout, CancellationToken cancellationToken)
		{
		var window = new NativeLoginWindow (region, timeout, cancellationToken);

		var thread = new Thread (window.ThreadMain)
			{
			IsBackground = true,
			Name = "TeslaCloudLogin"
			};
		thread.SetApartmentState (ApartmentState.STA);
		thread.Start ();

		return window._completion.Task;
		}

	private void ThreadMain ()
		{
		try
			{
			CreateWindow ();
			}
		catch (Exception exc)
			{
			Complete (new TeslaCloudLoginResult (TeslaCloudLoginStatus.Failed, null, $"Unable to create the login window: {exc.Message}"));
			return;
			}

		var syncContext = new Win32MessageLoopSynchronizationContext (_hwnd);
		SynchronizationContext.SetSynchronizationContext (syncContext);

		using var cancelRegistration = _cancellationToken.Register (RequestAbort);
		using var timeoutTimer = new Timer (_ => RequestAbort (), null, _timeout, Timeout.InfiniteTimeSpan);

		_ = InitializeAndNavigateAsync ();

		RunMessageLoop (syncContext);
		}

	private void RunMessageLoop (Win32MessageLoopSynchronizationContext syncContext)
		{
		while (NativeMethods.GetMessage (out var msg, IntPtr.Zero, 0, 0) > 0)
			{
			if (msg.message == Win32MessageLoopSynchronizationContext.InvokeMessage)
				{
				syncContext.RunOne ();
				continue;
				}

			NativeMethods.TranslateMessage (ref msg);
			NativeMethods.DispatchMessage (ref msg);
			}
		}

	private void CreateWindow ()
		{
		// Best-effort per-monitor DPI opt-in for hosts that have not already declared this via manifest;
		// safe to attempt even when the process already established a DPI mode (fails silently).
		NativeMethods.SetProcessDpiAwarenessContext (NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

		var hInstance = NativeMethods.GetModuleHandle (null);
		_wndProcDelegate = WndProc;

		var wndClass = new NativeMethods.WNDCLASSEX
			{
			cbSize = (uint) Marshal.SizeOf<NativeMethods.WNDCLASSEX> (),
			lpfnWndProc = _wndProcDelegate,
			hInstance = hInstance,
			hCursor = NativeMethods.LoadCursor (IntPtr.Zero, NativeMethods.IDC_ARROW),
			hbrBackground = (IntPtr) (NativeMethods.COLOR_WINDOW + 1),
			lpszClassName = WindowClassName
			};

		if (NativeMethods.RegisterClassEx (ref wndClass) == 0)
			throw new InvalidOperationException ($"Unable to register the login window class (error {Marshal.GetLastWin32Error ()}).");

		var screenWidth = NativeMethods.GetSystemMetrics (NativeMethods.SM_CXSCREEN);
		var screenHeight = NativeMethods.GetSystemMetrics (NativeMethods.SM_CYSCREEN);
		var x = Math.Max (0, (screenWidth - WindowWidth) / 2);
		var y = Math.Max (0, (screenHeight - WindowHeight) / 2);

		_hwnd = NativeMethods.CreateWindowEx (
			0, WindowClassName, "Tesla™ Powerwall™ Login", NativeMethods.WS_OVERLAPPEDWINDOW,
			x, y, WindowWidth, WindowHeight, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

		if (_hwnd == IntPtr.Zero)
			throw new InvalidOperationException ($"Unable to create the login window (error {Marshal.GetLastWin32Error ()}).");

		NativeMethods.ShowWindow (_hwnd, NativeMethods.SW_SHOW);
		NativeMethods.SetForegroundWindow (_hwnd);
		}

	private async Task InitializeAndNavigateAsync ()
		{
		try
			{
			_authRequest = TeslaAuth.BuildAuthUrl (_region);

			// Use an isolated, app-specific user-data folder so login state never leaks into other apps.
			var userDataFolder = System.IO.Path.Combine (
				Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData),
				"TeslaPowerwallLogin",
				"WebView2");

			var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment
				.CreateAsync (userDataFolder: userDataFolder).ConfigureAwait (true);
			_controller = await environment.CreateCoreWebView2ControllerAsync (_hwnd).ConfigureAwait (true);

			ResizeControllerToClientArea ();
			_controller.IsVisible = true;

			var coreWebView2 = _controller.CoreWebView2;
			coreWebView2.Settings.AreDefaultContextMenusEnabled = false;
			coreWebView2.Settings.IsStatusBarEnabled = false;
			coreWebView2.NavigationStarting += OnNavigationStarting;

			// Clear any cached Tesla session so each run starts from a fresh login.
			coreWebView2.CookieManager.DeleteAllCookies ();
			coreWebView2.Navigate (_authRequest.AuthUrl);
			}
		catch (Exception exc)
			{
			RequestClose (TeslaCloudLoginStatus.Failed, $"Unable to start the Tesla login: {exc.Message}");
			}
		}

	// Mirrors upstream _patch_pywebview_win32: intercept tesla:// before the browser tries to navigate.
	private async void OnNavigationStarting (object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
		{
		if (!TeslaAuth.TryParseCallback (e.Uri, out var callback))
			return;

		// tesla:// is not a real scheme — cancel the navigation and complete the exchange ourselves.
		e.Cancel = true;

		if (_exchangeStarted)
			return;
		_exchangeStarted = true;

		try
			{
			await CompleteLoginAsync (callback).ConfigureAwait (true);
			}
		catch (Exception exc)
			{
			RequestClose (TeslaCloudLoginStatus.Failed, $"Login failed: {exc.Message}");
			}
		}

	private async Task CompleteLoginAsync (TeslaCallback callback)
		{
		if (_authRequest is null)
			{
			RequestClose (TeslaCloudLoginStatus.Failed, "Login state was lost. Please try again.");
			return;
			}

		if (!string.IsNullOrEmpty (callback.Error))
			{
			RequestClose (TeslaCloudLoginStatus.Failed, $"Tesla returned an error: {callback.Error}");
			return;
			}

		if (string.IsNullOrEmpty (callback.Code))
			{
			RequestClose (TeslaCloudLoginStatus.Failed, "No authorization code was returned by Tesla.");
			return;
			}

		if (!string.Equals (callback.State, _authRequest.State, StringComparison.Ordinal))
			{
			RequestClose (TeslaCloudLoginStatus.Failed, "Security check failed (CSRF state mismatch). Please try again.");
			return;
			}

		using var exchangeTimeout = new CancellationTokenSource (TimeSpan.FromSeconds (30));
		using var linked = CancellationTokenSource.CreateLinkedTokenSource (exchangeTimeout.Token, _cancellationToken);

		TeslaTokens tokens;
		try
			{
			tokens = await TeslaAuth.ExchangeCodeAsync (
				callback.Code!,
				_authRequest.CodeVerifier,
				_region,
				linked.Token).ConfigureAwait (true);
			}
		catch (TeslaAuthException exc)
			{
			RequestClose (TeslaCloudLoginStatus.Failed, exc.Message);
			return;
			}
		catch (OperationCanceledException)
			{
			if (_cancellationToken.IsCancellationRequested)
				RequestClose (TeslaCloudLoginStatus.Cancelled, "Login was cancelled before completion.");
			else
				RequestClose (TeslaCloudLoginStatus.Failed, "Timed out exchanging the authorization code for tokens.");
			return;
			}

		RequestClose (
			TeslaCloudLoginStatus.Success,
			null,
			new TeslaCloudLoginTokens (tokens.RefreshToken, tokens.AccessToken, tokens.Email));
		}

	private void ResizeControllerToClientArea ()
		{
		if (_controller is null || _hwnd == IntPtr.Zero)
			return;

		if (NativeMethods.GetClientRect (_hwnd, out var rect))
			{
			_controller.Bounds = new System.Drawing.Rectangle (0, 0, rect.Right - rect.Left, rect.Bottom - rect.Top);
			}
		}

	// Posted from the cancellation-token registration or the timeout Timer, both of which may run on a
	// thread other than the message loop; PostMessage is safe to call across threads.
	private void RequestAbort ()
		{
		if (_hwnd != IntPtr.Zero)
			NativeMethods.PostMessage (_hwnd, WM_APP_ABORT, IntPtr.Zero, IntPtr.Zero);
		}

	private void RequestClose (TeslaCloudLoginStatus status, string? message, TeslaCloudLoginTokens? tokens = null)
		{
		Complete (new TeslaCloudLoginResult (status, tokens, message));
		if (_hwnd != IntPtr.Zero)
			NativeMethods.DestroyWindow (_hwnd);
		}

	private void Complete (TeslaCloudLoginResult result)
		{
		if (_completed)
			return;
		_completed = true;
		_completion.TrySetResult (result);
		}

	private IntPtr WndProc (IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
		{
		switch (msg)
			{
			case NativeMethods.WM_SIZE:
				ResizeControllerToClientArea ();
				return IntPtr.Zero;

			case WM_APP_ABORT:
				if (_cancellationToken.IsCancellationRequested)
					RequestClose (TeslaCloudLoginStatus.Cancelled, "Login was cancelled before completion.");
				else
					RequestClose (TeslaCloudLoginStatus.Failed, "Timed out waiting for the Tesla login to complete.");
				return IntPtr.Zero;

			case NativeMethods.WM_CLOSE:
				// The user closed the window before completing authentication.
				RequestClose (TeslaCloudLoginStatus.Cancelled, "Login was cancelled before completion.");
				return IntPtr.Zero;

			case NativeMethods.WM_DESTROY:
				try
					{
					_controller?.Close ();
					}
				catch (Exception)
					{
					// Best-effort cleanup; the process is tearing this window down regardless.
					}
				NativeMethods.PostQuitMessage (0);
				return IntPtr.Zero;

			default:
				return NativeMethods.DefWindowProc (hWnd, msg, wParam, lParam);
			}
		}
	}
