// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Windows.Controls;

using TeslaPowerwallLibrary.App.ViewModels;

namespace TeslaPowerwallLibrary.App.Views;

/// <summary>
/// Interaction logic for <c>ConnectView.xaml</c>. Bridges the <see cref="PasswordBox"/> (whose content cannot
/// be data-bound for security reasons) to the <see cref="ConnectViewModel"/>.
/// </summary>
public partial class ConnectView : UserControl
	{
	/// <summary>Initializes a new instance of the <see cref="ConnectView"/> class.</summary>
	public ConnectView ()
		{
		InitializeComponent ();
		Loaded += OnLoaded;
		}

	private void OnLoaded (object sender, System.Windows.RoutedEventArgs e)
		{
		// Seed the password box from any remembered credential the first time the view is shown.
		if (DataContext is ConnectViewModel vm && string.IsNullOrEmpty (LocalPasswordBox.Password) && !string.IsNullOrEmpty (vm.Password))
			LocalPasswordBox.Password = vm.Password;
		}

	private void OnPasswordChanged (object sender, System.Windows.RoutedEventArgs e)
		{
		if (DataContext is ConnectViewModel vm)
			vm.Password = LocalPasswordBox.Password;
		}

	private void OnLocalChecked (object sender, System.Windows.RoutedEventArgs e)
		{
		if (DataContext is ConnectViewModel vm)
			{
			vm.IsCloudMode = false;
			vm.IsFleetApiMode = false;
			}
		}

	private void OnFleetApiClientSecretChanged (object sender, System.Windows.RoutedEventArgs e)
		{
		if (DataContext is ConnectViewModel vm)
			vm.FleetApiClientSecret = FleetApiClientSecretBox.Password;
		}
	}
