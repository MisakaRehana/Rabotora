using System.Reflection;
using System.Text;
using Rabotora.Core.Utility;

namespace Rabotora.Core.Data.Graphics.Internal.LowLevel;

/// <summary>
/// Loads the WGSL sources that ship inside this assembly and hands out UTF-8, NUL-terminated pointers
/// that stay valid for the lifetime of the process.
/// </summary>
/// <remarks>
/// Each source is copied into native memory once instead of being pinned per call: a pointer taken inside
/// a <c>fixed</c> block is only valid until the next collection moves the managed array, and handing such
/// a pointer to the driver is a use-after-free waiting for a garbage collection to happen at the wrong
/// moment. Shader sources are a few hundred bytes each, so they are simply never freed — the cache is the
/// owner.
/// </remarks>
public static unsafe class ShaderLibrary
{
	private static readonly Dictionary<string, Utf8String> Cache = [];
	private static readonly object Gate = new();

	/// <summary>
	/// Returns the WGSL source of <paramref name="name"/> (for example <c>triangle.wgsl</c>) as a
	/// NUL-terminated UTF-8 string, ready for <c>ShaderModuleWGSLDescriptor.Code</c>.
	/// </summary>
	/// <exception cref="RabotoraInternalException">The resource is not embedded in this assembly.</exception>
	public static byte* Get(string name)
	{
		lock (Gate)
		{
			if (!Cache.TryGetValue(name, out Utf8String source))
			{
				source = new Utf8String(ReadEmbedded(name));
				Cache[name] = source;
			}

			return source.Pointer;
		}
	}

	private static string ReadEmbedded(string name)
	{
		Assembly asm = Assembly.GetExecutingAssembly();
		string? resName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith($".{name}", StringComparison.Ordinal));
		if (string.IsNullOrEmpty(resName))
		{
			throw new RabotoraInternalException($"{nameof(ShaderLibrary)}.cs", $"Embedded shader '{name}' was not found. Known resources: " +
			                                                                   string.Join(", ", asm.GetManifestResourceNames()));
		}

		using var rs = asm.GetManifestResourceStream(resName); // rs: resource stream
		if (rs == null)
		{
			throw new RabotoraInternalException($"{nameof(ShaderLibrary)}.cs", $"Failed to load embedded shader '{resName}'.");
		}

		// StreamReader drops a byte order mark for us; a BOM in front of a WGSL module would be a parse error.
		using var reader = new StreamReader(rs, Encoding.UTF8);
		return reader.ReadToEnd();
	}
}
