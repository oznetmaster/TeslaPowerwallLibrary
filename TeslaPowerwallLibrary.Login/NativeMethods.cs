// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.InteropServices;

namespace TeslaPowerwallLibrary.Login;

/// <summary>
/// Win32 interop declarations used to host a plain, dependency-free top-level window for the Tesla™
/// login WebView2 control, avoiding any WPF or WinForms dependency in the shared login library.
/// </summary>
internal static class NativeMethods
	{
	public const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;

	public const int SW_SHOW = 5;

	public const uint WM_SIZE = 0x0005;
	public const uint WM_CLOSE = 0x0010;
	public const uint WM_DESTROY = 0x0002;
	public const uint WM_APP = 0x8000;

	public const int IDC_ARROW = 32512;
	public const int COLOR_WINDOW = 5;

	public const int SM_CXSCREEN = 0;
	public const int SM_CYSCREEN = 1;

	/// <summary><c>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2</c>, used for best-effort per-monitor DPI opt-in.</summary>
	public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new (-4);

	public delegate IntPtr WndProcDelegate (IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[StructLayout (LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct WNDCLASSEX
		{
		public uint cbSize;
		public uint style;
		public WndProcDelegate lpfnWndProc;
		public int cbClsExtra;
		public int cbWndExtra;
		public IntPtr hInstance;
		public IntPtr hIcon;
		public IntPtr hCursor;
		public IntPtr hbrBackground;
		public string? lpszMenuName;
		public string lpszClassName;
		public IntPtr hIconSm;
		}

	[StructLayout (LayoutKind.Sequential)]
	public struct RECT
		{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
		}

	[StructLayout (LayoutKind.Sequential)]
	public struct MSG
		{
		public IntPtr hwnd;
		public uint message;
		public IntPtr wParam;
		public IntPtr lParam;
		public uint time;
		public int ptX;
		public int ptY;
		}

	[DllImport ("kernel32.dll", CharSet = CharSet.Unicode)]
	public static extern IntPtr GetModuleHandle (string? lpModuleName);

	[DllImport ("kernel32.dll")]
	public static extern uint GetCurrentThreadId ();

	[DllImport ("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	public static extern ushort RegisterClassEx ([In] ref WNDCLASSEX lpwcx);

	[DllImport ("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	public static extern IntPtr CreateWindowEx (
		uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
		int x, int y, int nWidth, int nHeight,
		IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

	[DllImport ("user32.dll")]
	public static extern bool ShowWindow (IntPtr hWnd, int nCmdShow);

	[DllImport ("user32.dll")]
	public static extern bool SetForegroundWindow (IntPtr hWnd);

	[DllImport ("user32.dll")]
	public static extern bool DestroyWindow (IntPtr hWnd);

	[DllImport ("user32.dll")]
	public static extern IntPtr DefWindowProc (IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport ("user32.dll")]
	public static extern void PostQuitMessage (int nExitCode);

	[DllImport ("user32.dll")]
	public static extern bool GetClientRect (IntPtr hWnd, out RECT lpRect);

	[DllImport ("user32.dll", CharSet = CharSet.Unicode)]
	public static extern int GetMessage (out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

	[DllImport ("user32.dll")]
	public static extern bool TranslateMessage ([In] ref MSG lpMsg);

	[DllImport ("user32.dll")]
	public static extern IntPtr DispatchMessage ([In] ref MSG lpMsg);

	[DllImport ("user32.dll")]
	public static extern bool PostThreadMessage (uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport ("user32.dll")]
	public static extern bool PostMessage (IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport ("user32.dll")]
	public static extern IntPtr LoadCursor (IntPtr hInstance, int lpCursorName);

	[DllImport ("user32.dll")]
	public static extern int GetSystemMetrics (int nIndex);

	[DllImport ("user32.dll")]
	public static extern bool SetProcessDpiAwarenessContext (IntPtr value);
	}
