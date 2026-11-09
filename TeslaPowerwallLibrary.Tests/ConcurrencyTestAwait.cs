// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace TeslaPowerwallLibrary.Tests;

// Keep generic async state machines outside attributed fixtures for merged-host discovery.
internal static class ConcurrencyTestAwait
	{
	internal static async Task Ready (Task task)
		{
		Assert.That (await Task.WhenAny (task, Task.Delay (5000)).ConfigureAwait (false), Is.SameAs (task), "Bounded concurrency test timed out.");
		await task.ConfigureAwait (false);
		}
	internal static async Task<T> Ready<T> (Task<T> task)
		{
		await Ready ((Task)task).ConfigureAwait (false);
		return await task.ConfigureAwait (false);
		}
	}