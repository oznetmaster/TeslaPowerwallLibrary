// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NUnit.Framework;
using TeslaPowerwallLibrary.App.Services;
using TeslaPowerwallLibrary.App.ViewModels;
using TeslaPowerwallLibrary.Local;

namespace TeslaPowerwallLibrary.App.Tests;

/// <summary>Checks discovery progress and outcomes without sending network requests.</summary>
[TestFixture]
public sealed class LocalDiscoveryPresentationTests
	{
	/// <summary>Reopening settings resolves DHCP changes and retains IPv6 scope without changing the configured hostname.</summary>
	[Test]
	public async Task Settings_RefreshesAddressesWithoutPersistingAnIp ()
		{
		using var connection = new PowerwallConnectionService ();
		var model = new SettingsViewModel (connection);
		await model.RefreshLocalAddressAsync ("powerwall.local", (host, _) => Task.FromResult (
			new PowerwallHost (host, 443, new[] { IPAddress.Parse ("192.0.2.10") })));
		Assert.That (model.LocalIpAddresses, Is.EqualTo ("192.0.2.10"));
		await model.RefreshLocalAddressAsync ("powerwall.local", (host, _) => Task.FromResult (
			new PowerwallHost (host, 443, new[] { IPAddress.Parse ("192.0.2.11"), IPAddress.Parse ("fe80::1234%3") })));
		Assert.That (model.LocalHostname, Is.EqualTo ("powerwall.local"));
		Assert.That (model.LocalIpAddresses, Is.EqualTo ("192.0.2.11"));
		Assert.That (connection.IsConnected, Is.False);
		}

	/// <summary>Literal addresses need no duplicate resolved-address row or DNS request.</summary>
	[TestCase ("192.0.2.1"), TestCase ("192.0.2.1:8443"), TestCase ("fe80::1234%3"), TestCase ("[fe80::1234%3]:8443")]
	public async Task Settings_LiteralAddressesHideRedundantResolution (string host)
		{
		using var connection = new PowerwallConnectionService ();
		var model = new SettingsViewModel (connection);
		await model.RefreshLocalAddressAsync (host, (_, _) => throw new AssertionException ("An IP literal does not need DNS."));
		Assert.That (model.ShowResolvedAddress, Is.False);
		Assert.That (model.LocalHostname, Is.EqualTo (host));
		}

	/// <summary>IPv6 remains available when the host has no IPv4 response.</summary>
	[Test]
	public async Task Settings_Ipv6OnlyHostRetainsScopedAddress ()
		{
		using var connection = new PowerwallConnectionService ();
		var model = new SettingsViewModel (connection);
		await model.RefreshLocalAddressAsync ("powerwall.local", (host, _) => Task.FromResult (
			new PowerwallHost (host, 443, new[] { IPAddress.Parse ("fe80::1234%3") })));
		Assert.That (model.ShowResolvedAddress, Is.True);
		Assert.That (model.LocalIpAddresses, Is.EqualTo ("fe80::1234%3"));
		}

	/// <summary>Lookup errors and cloud connections clear stale addresses rather than presenting an old IP as current.</summary>
	[Test]
	public async Task Settings_LookupFailureAndCloudClearOldAddress ()
		{
		using var connection = new PowerwallConnectionService ();
		var model = new SettingsViewModel (connection);
		await model.RefreshLocalAddressAsync ("powerwall.local", (host, _) => Task.FromResult (
			new PowerwallHost (host, 443, new[] { IPAddress.Loopback })));
		await model.RefreshLocalAddressAsync ("powerwall.local", (_, _) => Task.FromException<PowerwallHost> (new SocketException ()));
		Assert.That (model.LocalIpAddresses, Is.EqualTo ("Name lookup unavailable"));
		await model.RefreshLocalAddressAsync (null);
		Assert.That (model.LocalHostname, Is.Null);
		Assert.That (model.LocalIpAddresses, Is.EqualTo ("Unavailable"));
		}

	/// <summary>A search shows progress, reports no results, and preserves the manually entered hostname.</summary>
	[Test]
	public async Task Discovery_NoResultsShowsFeedbackAndPreservesManualAddress ()
		{
		using var connection = new PowerwallConnectionService ();
		var completion = new TaskCompletionSource<IReadOnlyList<PowerwallHost>> (TaskCreationOptions.RunContinuationsAsynchronously);
		var model = new ConnectViewModel (connection, _ => completion.Task) { Host = "manual.local" };
		Task search = model.DiscoverCommand.ExecuteAsync (null);
		Assert.That (model.IsDiscovering, Is.True);
		Assert.That (model.DiscoveryButtonText, Does.Contain ("Searching"));
		Assert.That (model.DiscoveryStatus, Does.Contain ("Searching"));
		model.Host = "edited-during-search.local";
		completion.SetResult (Array.Empty<PowerwallHost> ());
		await search;
		Assert.That (model.Host, Is.EqualTo ("edited-during-search.local"));
		Assert.That (model.DiscoveryStatus, Does.Contain ("No advertised Powerwalls found"));
		Assert.That (model.IsDiscovering, Is.False);
		Assert.That (model.IsBusy, Is.False);
		Assert.That (connection.IsConnected, Is.False);
		}

	/// <summary>Discovered candidates are offered without silently selecting or connecting to one.</summary>
	[Test]
	public async Task Discovery_FoundCandidateRemainsAnExplicitChoice ()
		{
		using var connection = new PowerwallConnectionService ();
		var model = new ConnectViewModel (connection, _ => Task.FromResult<IReadOnlyList<PowerwallHost>> (
			new[] { new PowerwallHost ("candidate.local", 8443, new[] { IPAddress.Loopback }) })) { Host = "manual.local" };
		await model.DiscoverCommand.ExecuteAsync (null);
		Assert.That (model.DiscoveredHosts, Is.EqualTo (new[] { "candidate.local:8443" }));
		Assert.That (model.Host, Is.EqualTo ("manual.local"));
		Assert.That (model.DiscoveryStatus, Does.Contain ("Found 1"));
		Assert.That (connection.IsConnected, Is.False);
		}

	/// <summary>Timeout and network errors remain visible beside discovery and release the busy state.</summary>
	[TestCase (true)]
	[TestCase (false)]
	public async Task Discovery_FailureAllowsManualEntry (bool timeout)
		{
		using var connection = new PowerwallConnectionService ();
		Exception failure = timeout ? new OperationCanceledException () : new SocketException ((int)SocketError.NetworkDown);
		var model = new ConnectViewModel (connection, _ => Task.FromException<IReadOnlyList<PowerwallHost>> (failure));
		await model.DiscoverCommand.ExecuteAsync (null);
		Assert.That (model.DiscoveryStatus, Does.Contain (timeout ? "timed out" : "unavailable"));
		Assert.That (model.DiscoveryStatus, Does.Contain ("hostname or IP address"));
		Assert.That (model.IsBusy, Is.False);
		Assert.That (model.IsDiscovering, Is.False);
		}
	}

/// <summary>Exercises the actual WPF theme template rather than just the editable property.</summary>
[TestFixture, Apartment (ApartmentState.STA)]
public sealed class LocalHostEditorTests
	{
	/// <summary>Typing or pasting in the template editor updates the hostname used for connecting.</summary>
	[Test]
	public void EditableHostTemplate_UpdatesBoundHostnameAndSupportsSelection ()
		{
		using var connection = new PowerwallConnectionService ();
		var model = new ConnectViewModel (connection, _ => Task.FromResult<IReadOnlyList<PowerwallHost>> (Array.Empty<PowerwallHost> ()));
		var resources = new ResourceDictionary { Source = new Uri ("/TeslaPowerwallApp;component/Themes/Theme.xaml", UriKind.Relative) };
		var combo = new ComboBox
			{
			Resources = resources, Style = (Style)resources[typeof (ComboBox)], Width = 400,
			IsEditable = true, IsTextSearchEnabled = false, ItemsSource = model.DiscoveredHosts
			};
		combo.SetBinding (ComboBox.TextProperty, new Binding (nameof (model.Host))
			{ Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
		combo.Measure (new Size (400, 100));
		combo.Arrange (new Rect (0, 0, 400, 40));
		combo.ApplyTemplate ();
		var editor = combo.Template.FindName ("PART_EditableTextBox", combo) as TextBox;
		Assert.That (editor, Is.Not.Null, "An editable ComboBox requires a real template text editor.");
		Assert.That (editor!.Visibility, Is.EqualTo (Visibility.Visible));
		Assert.That (editor.IsReadOnly, Is.False);
		editor.SelectAll ();
		editor.SelectedText = "powerwall-test.local";
		combo.GetBindingExpression (ComboBox.TextProperty)!.UpdateSource ();
		Assert.That (model.Host, Is.EqualTo ("powerwall-test.local"));
		model.DiscoveredHosts.Add ("candidate.local");
		combo.SelectedItem = "candidate.local";
		combo.GetBindingExpression (ComboBox.TextProperty)!.UpdateSource ();
		Assert.That (model.Host, Is.EqualTo ("candidate.local"));
		combo.IsEditable = false;
		Assert.That (editor.Visibility, Is.EqualTo (Visibility.Hidden));
		}
	}
