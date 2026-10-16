using System.Runtime.InteropServices;

namespace Rabotora.Core.Utility;

internal readonly unsafe struct Utf8String : IDisposable
{
	public byte* Pointer => (byte*)_ptr;
	
	private readonly nint _ptr;

	public Utf8String(string? value)
	{
		_ptr = value is null ? 0 : Marshal.StringToCoTaskMemUTF8(value);
	}
	
	public void Dispose()
	{
		if (_ptr != 0)
		{
			Marshal.FreeCoTaskMem(_ptr);
		}
	}
}