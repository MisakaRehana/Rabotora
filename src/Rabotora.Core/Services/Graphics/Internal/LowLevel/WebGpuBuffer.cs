using System.Runtime.InteropServices;
using Rabotora.Core.Data;
using Rabotora.Core.Utility;
using Silk.NET.WebGPU;

namespace Rabotora.Core.Services.Graphics.Internal.LowLevel;

/// <summary>
/// Owns a WebGPU buffer: the handle, its size, and the release that goes with them.
/// </summary>
/// <remarks>
/// Buffers are uploaded through the queue rather than mapped at creation, because every buffer this layer
/// has needed so far is written once while loading and then only read by the GPU. A buffer that is
/// rewritten every frame (a per-frame uniform block, for example) wants
/// <c>MappedAtCreation</c>/<c>BufferGetMappedRange</c> or a ring of buffers instead — that is Stage 1 work.
/// </remarks>
public sealed unsafe class WebGpuBuffer : IDisposable
{
	/// <summary>
	/// <c>COPY_BUFFER_ALIGNMENT</c>: buffer sizes and the sizes passed to <c>QueueWriteBuffer</c> have to be
	/// multiples of this, so a 6-byte payload (three <c>float</c>s) still occupies an 8-byte buffer.
	/// </summary>
	private const ulong Alignment = 4;

	/// <summary>The buffer handle, <see langword="null"/> after <see cref="Dispose"/>.</summary>
	public Buffer* Handle { get; private set; }

	/// <summary>The buffer's size in bytes, which is the uploaded payload rounded up to <see cref="Alignment"/>.</summary>
	public ulong Size { get; private set; }

	private readonly WebGpuGraphicsDevice _graphics;
	private bool _isDisposed;

	private WebGpuBuffer(WebGpuGraphicsDevice graphics, Buffer* handle, ulong size)
	{
		_graphics = graphics;
		Handle = handle;
		Size = size;
	}

	/// <summary>
	/// Creates a buffer of <paramref name="usage"/> and uploads <paramref name="data"/> into it.
	/// </summary>
	/// <remarks><see cref="BufferUsage.CopyDst"/> is added to <paramref name="usage"/>: the upload needs it.</remarks>
	/// <exception cref="RabotoraInternalException">The data is empty, or the buffer could not be created.</exception>
	public static WebGpuBuffer Create<T>(WebGpuGraphicsDevice graphics, BufferUsage usage, ReadOnlySpan<T> data, string? label = null)
		where T : unmanaged
	{
		if (data.IsEmpty)
		{
			throw new RabotoraInternalException($"{nameof(WebGpuBuffer)}.cs", "A WebGPU buffer cannot have a size of zero.");
		}

		var api = graphics.Api;
		ulong byteCount = (ulong) data.Length * (ulong) sizeof(T);
		ulong size = Align(byteCount);

		using var labelUtf8 = new Utf8String(label);
		var descriptor = new BufferDescriptor
		{
			Label = labelUtf8.Pointer,
			Usage = usage | BufferUsage.CopyDst,
			Size = size,
			MappedAtCreation = false,
		};

		Buffer* handle = api.DeviceCreateBuffer(graphics.Device, in descriptor);
		if (handle == null)
		{
			throw new RabotoraInternalException($"{nameof(WebGpuBuffer)}.cs", $"Creating a {size} byte buffer failed.");
		}

		var buffer = new WebGpuBuffer(graphics, handle, size);
		try
		{
			if (byteCount == size)
			{
				buffer.Write(data);
			}
			else
			{
				// The last few bytes cannot be written on their own, so the payload is uploaded as one
				// zero-padded copy instead.
				byte[] padded = new byte[(int) size];
				MemoryMarshal.AsBytes(data).CopyTo(padded);
				buffer.Write<byte>(padded);
			}

			return buffer;
		}
		catch
		{
			buffer.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Creates a buffer that vertex data can be bound from.
	/// </summary>
	public static WebGpuBuffer CreateVertex<T>(WebGpuGraphicsDevice graphics, ReadOnlySpan<T> vertices, string? label = null)
		where T : unmanaged
	{
		return Create(graphics, BufferUsage.Vertex, vertices, label);
	}

	/// <summary>
	/// Uploads <paramref name="data"/> at <paramref name="offset"/> through the queue, replacing what was
	/// there. The write is ordered with the submissions, so a frame that was already submitted keeps seeing
	/// the old contents.
	/// </summary>
	/// <exception cref="RabotoraInternalException">The offset or size is not a multiple of <see cref="Alignment"/>, or it would run past the end of the buffer.</exception>
	public void Write<T>(ReadOnlySpan<T> data, ulong offset = 0) where T : unmanaged
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		ulong byteCount = (ulong) data.Length * (ulong) sizeof(T);
		if (offset % Alignment != 0 || byteCount % Alignment != 0)
		{
			throw new RabotoraInternalException($"{nameof(WebGpuBuffer)}.cs", $"QueueWriteBuffer needs offset and size to be multiples of {Alignment} (offset={offset}, size={byteCount}).");
		}

		if (offset + byteCount > Size)
		{
			throw new RabotoraInternalException($"{nameof(WebGpuBuffer)}.cs", $"Writing {byteCount} bytes at {offset} runs past the end of a {Size} byte buffer.");
		}

		fixed (T* pointer = data)
		{
			_graphics.Api.QueueWriteBuffer(_graphics.Queue, Handle, offset, pointer, (nuint) byteCount);
		}
	}

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}
		_isDisposed = true;

		if (Handle != null)
		{
			// Releasing only drops this reference: a submission that is still in flight keeps the buffer
			// alive until it is done with it.
			_graphics.Api.BufferRelease(Handle);
			Handle = null;
		}
	}

	private static ulong Align(ulong value)
	{
		return (value + Alignment - 1) / Alignment * Alignment;
	}
}
