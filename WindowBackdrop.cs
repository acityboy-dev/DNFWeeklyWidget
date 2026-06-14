using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DNFWeeklyWidget;

internal static class WindowBackdrop
{
	private enum DWMWINDOWATTRIBUTE
	{
		DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
		DWMWA_WINDOW_CORNER_PREFERENCE = 33
	}

	private enum DWM_WINDOW_CORNER_PREFERENCE
	{
		DWMWCP_ROUND = 2
	}

	private enum ACCENT_STATE
	{
		ACCENT_ENABLE_ACRYLICBLURBEHIND = 4
	}

	private enum WINDOWCOMPOSITIONATTRIB
	{
		WCA_ACCENT_POLICY = 19
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct ACCENT_POLICY
	{
		public ACCENT_STATE AccentState;
		public int AccentFlags;
		public int GradientColor;
		public int AnimationId;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct WINDOWCOMPOSITIONATTRIBDATA
	{
		public WINDOWCOMPOSITIONATTRIB Attribute;
		public IntPtr Data;
		public int SizeOfData;
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(
		IntPtr hwnd,
		DWMWINDOWATTRIBUTE dwAttribute,
		ref int pvAttribute,
		int cbAttribute);

	[DllImport("user32.dll")]
	private static extern int SetWindowCompositionAttribute(
		IntPtr hwnd,
		ref WINDOWCOMPOSITIONATTRIBDATA data);

	public static void Apply(Window window, bool isLightTheme)
	{
		var hwnd = new WindowInteropHelper(window).Handle;
		if (hwnd == IntPtr.Zero)
			return;

		if (PresentationSource.FromVisual(window) is HwndSource source)
			source.CompositionTarget.BackgroundColor = Colors.Transparent;

		var corner = (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
		DwmSetWindowAttribute(
			hwnd,
			DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE,
			ref corner,
			sizeof(int));

		var useDarkMode = isLightTheme ? 0 : 1;
		DwmSetWindowAttribute(
			hwnd,
			DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE,
			ref useDarkMode,
			sizeof(int));

		var accent = new ACCENT_POLICY
		{
			AccentState = ACCENT_STATE.ACCENT_ENABLE_ACRYLICBLURBEHIND,
			AccentFlags = 2,
			GradientColor = isLightTheme
				? ToAbgr(0x88, 0xF8, 0xF8, 0xF8)
				: ToAbgr(0x88, 0x20, 0x22, 0x28),
			AnimationId = 0
		};

		var accentSize = Marshal.SizeOf<ACCENT_POLICY>();
		var accentPtr = Marshal.AllocHGlobal(accentSize);

		try
		{
			Marshal.StructureToPtr(accent, accentPtr, false);
			var data = new WINDOWCOMPOSITIONATTRIBDATA
			{
				Attribute = WINDOWCOMPOSITIONATTRIB.WCA_ACCENT_POLICY,
				Data = accentPtr,
				SizeOfData = accentSize
			};
			SetWindowCompositionAttribute(hwnd, ref data);
		}
		finally
		{
			Marshal.FreeHGlobal(accentPtr);
		}
	}

	private static int ToAbgr(byte alpha, byte red, byte green, byte blue)
	{
		return alpha << 24 | blue << 16 | green << 8 | red;
	}
}
