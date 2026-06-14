using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DNFWeeklyWidget;

public sealed class CachedImageSourceConverter : IValueConverter
{
	private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

	public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (value is not string source || string.IsNullOrWhiteSpace(source))
			return null;

		return GetImageSource(source);
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
		System.Windows.Data.Binding.DoNothing;

	public static ImageSource? GetImageSource(string source)
	{
		if (string.IsNullOrWhiteSpace(source))
			return null;

		try
		{
			return Cache.GetOrAdd(source, CreateImageSource);
		}
		catch
		{
			return null;
		}
	}

	private static ImageSource CreateImageSource(string source)
	{
		var bitmap = new BitmapImage();
		bitmap.BeginInit();
		bitmap.CacheOption = BitmapCacheOption.OnLoad;
		bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
		bitmap.UriSource = File.Exists(source)
			? new Uri(source, UriKind.Absolute)
			: new Uri(source, UriKind.RelativeOrAbsolute);
		bitmap.EndInit();

		if (bitmap.CanFreeze)
			bitmap.Freeze();

		return bitmap;
	}
}
