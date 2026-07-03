// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TeslaPowerwallLibrary.App.Converters;

/// <summary>Converts a <see cref="bool"/> to <see cref="Visibility"/> (<c>true</c> =&gt; Visible, <c>false</c> =&gt; Collapsed).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
	{
	/// <inheritdoc/>
	public object Convert (object value, Type targetType, object parameter, CultureInfo culture) =>
		value is true ? Visibility.Visible : Visibility.Collapsed;

	/// <inheritdoc/>
	public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture) =>
		value is Visibility.Visible;
	}

/// <summary>Inverts a <see cref="bool"/> value.</summary>
public sealed class InverseBoolConverter : IValueConverter
	{
	/// <inheritdoc/>
	public object Convert (object value, Type targetType, object parameter, CultureInfo culture) =>
		value is not true;

	/// <inheritdoc/>
	public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture) =>
		value is not true;
	}

/// <summary>Converts a <see cref="bool"/> to <see cref="Visibility"/> inverted (<c>false</c> =&gt; Visible, <c>true</c> =&gt; Collapsed).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
	{
	/// <inheritdoc/>
	public object Convert (object value, Type targetType, object parameter, CultureInfo culture) =>
		value is true ? Visibility.Collapsed : Visibility.Visible;

	/// <inheritdoc/>
	public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture) =>
		value is Visibility.Collapsed;
	}

/// <summary>Converts a <see langword="null"/> or empty value to <see cref="Visibility.Collapsed"/>, otherwise Visible.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
	{
	/// <inheritdoc/>
	public object Convert (object value, Type targetType, object parameter, CultureInfo culture) =>
		value is null || (value is string s && string.IsNullOrWhiteSpace (s))
			? Visibility.Collapsed
			: Visibility.Visible;

	/// <inheritdoc/>
	public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture) =>
		throw new NotSupportedException ();
	}

/// <summary>
/// Converts an <see cref="AppScreen"/>-style enum comparison against a parameter to <see cref="bool"/>, used to
/// highlight the active navigation button. Returns <see langword="true"/> when the bound value equals the parameter.
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
	{
	/// <inheritdoc/>
	public object Convert (object value, Type targetType, object parameter, CultureInfo culture) =>
		value is not null && parameter is not null
		&& string.Equals (value.ToString (), parameter.ToString (), StringComparison.Ordinal);

	/// <inheritdoc/>
	public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture) =>
		throw new NotSupportedException ();
	}
