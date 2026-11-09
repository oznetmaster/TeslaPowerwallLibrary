// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TeslaPowerwallLibrary.Concurrency;

// Cache methods are synchronous, so the named mutex is acquired and released on
// the same thread. It covers the entire read/modify/write across cache instances
// and processes, not merely the final write. Replacement never truncates a good file.
internal static class TokenCacheFile
	{
	internal static IDisposable Enter (string path)
		{
		string normalized = Path.GetFullPath (path);
		if (Path.DirectorySeparatorChar == '\\')
			normalized = normalized.ToUpperInvariant ();
#if NETFRAMEWORK
		using SHA256 sha = SHA256.Create ();
		byte[] hash = sha.ComputeHash (Encoding.UTF8.GetBytes (normalized));
		string suffix = BitConverter.ToString (hash).Replace ("-", string.Empty);
#else
		byte[] hash = SHA256.HashData (Encoding.UTF8.GetBytes (normalized));
		string suffix = Convert.ToHexString (hash);
#endif
		string name = (Path.DirectorySeparatorChar == '\\' ? "Global\\" : string.Empty) + "TeslaPowerwallLibrary.TokenCache." + suffix;
		var mutex = new Mutex (false, name);
		try
			{
			bool acquired;
			try
				{
				acquired = mutex.WaitOne (TimeSpan.FromSeconds (30));
				}
			catch (AbandonedMutexException) { acquired = true; }
			if (!acquired)
				throw new IOException ("Timed out waiting for the token cache file lock.");
			return new Lease (mutex);
			}
		catch
			{
			mutex.Dispose ();
			throw;
			}
		}
	internal static void Write (string path, string contents)
		{
		string target = Path.GetFullPath (path);
		string directory = Path.GetDirectoryName (target)!;
		Directory.CreateDirectory (directory);
		string temporary = Path.Combine (directory, "." + Path.GetFileName (target) + "." + Guid.NewGuid ().ToString ("N") + ".tmp");
		try
			{
			using (var stream = new FileStream (temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
				byte[] bytes = new UTF8Encoding (false).GetBytes (contents);
				stream.Write (bytes, 0, bytes.Length);
				stream.Flush (flushToDisk: true);
				}
			if (File.Exists (target))
				File.Replace (temporary, target, null);
			else
				File.Move (temporary, target);
			}
		finally
			{
			if (File.Exists (temporary))
				File.Delete (temporary);
			}
		}
	private sealed class Lease (Mutex mutex) : IDisposable
		{
		private Mutex? _mutex = mutex;
		public void Dispose ()
			{
			Mutex? held = Interlocked.Exchange (ref _mutex, null);
			if (held is null)
				return;
			try
				{
				held.ReleaseMutex ();
				}
			finally { held.Dispose (); }
			}
		}
	}