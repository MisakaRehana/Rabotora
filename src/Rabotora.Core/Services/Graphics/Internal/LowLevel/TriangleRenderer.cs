using Rabotora.Core.Abstractions.Graphics.Internal.LowLevel;
using Rabotora.Core.Data;
using Rabotora.Core.Data.Graphics.Internal.LowLevel;
using Rabotora.Core.Utility;
using Silk.NET.WebGPU;

namespace Rabotora.Core.Services.Graphics.Internal.LowLevel;

public sealed unsafe class TriangleRenderer : IFrameRenderer
{
	/// <summary>
	/// The three clip-space corners as the vertex buffer holds them: two <c>float</c>s each.
	/// </summary>
	private static readonly float[] TriangleVertices =
	[
		-1.0f, -1.0f, // bottom left
		0.0f, 1.0f, // top middle
		1.0f, -1.0f, // bottom right
	];

	private const uint PositionShaderLocation = 0;

	private WebGpuGraphicsDevice? _graphics;
	private WebGpuSwapChain? _swapChain;

	private ShaderModule* _shader;
	private WebGpuBuffer? _vertexBuffer;
	private RenderPipeline* _pipeline;
	private bool _isDisposed;

	public void Load(WebGpuGraphicsDevice graphics, WebGpuSwapChain swapChain)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		if (_pipeline != null)
		{
			// A second Load would silently orphan the first pipeline, shader module and vertex buffer.
			throw new RabotoraInternalException($"{nameof(TriangleRenderer)}.cs", "This renderer is already loaded.");
		}

		_graphics = graphics;
		_swapChain = swapChain;

		var api = graphics.Api;
		var device = graphics.Device;

		using var ve = new Utf8String("vs_main"); // vertex entry
		using var fe = new Utf8String("fs_main"); // fragment entry

		var desWgsl = new ShaderModuleWGSLDescriptor()
		{
			Chain = new ChainedStruct()
			{
				Next = null,
				SType = SType.ShaderModuleWgslDescriptor
			},
			Code = ShaderLibrary.Get("triangle.wgsl") // owned by the library: valid for the whole process
		};

		var desShader = new ShaderModuleDescriptor()
		{
			NextInChain = (ChainedStruct*)&desWgsl
		};

		_shader = api.DeviceCreateShaderModule(device, in desShader);
		if (_shader == null)
		{
			throw new RabotoraInternalException($"{nameof(TriangleRenderer)}.cs", "Creating the shader module failed.");
		}

		_vertexBuffer = WebGpuBuffer.CreateVertex(graphics, TriangleVertices, "triangle vertices");

		// The layout and the buffer describe the same vertices from two sides: 2 floats per vertex, handed to
		// @location(0) of vs_main. The stride is derived from the element size so only the attribute list has
		// to be kept in step with the shader.
		VertexAttribute* attributes = stackalloc VertexAttribute[1]
		{
			new()
			{
				Format = VertexFormat.Float32x2,
				Offset = 0,
				ShaderLocation = PositionShaderLocation
			}
		};

		var vertexBufferLayout = new VertexBufferLayout()
		{
			ArrayStride = 2 * sizeof(float),
			StepMode = VertexStepMode.Vertex,
			AttributeCount = 1,
			Attributes = attributes
		};

		var blendState = new BlendState() // Blend One Zero
		{
			Color = new BlendComponent()
			{
				SrcFactor = BlendFactor.One,
				DstFactor = BlendFactor.Zero,
				Operation = BlendOperation.Add
			},
			Alpha = new BlendComponent()
			{
				SrcFactor = BlendFactor.One,
				DstFactor = BlendFactor.Zero,
				Operation = BlendOperation.Add
			}
		};

		var colorTarget = new ColorTargetState()
		{
			Format = swapChain.Format,
			Blend = &blendState,
			WriteMask = ColorWriteMask.All, // 0xF, write all RGBA
		};

		var fs = new FragmentState()
		{
			Module = _shader,
			EntryPoint = fe.Pointer,
			TargetCount = 1,
			Targets = &colorTarget
		};

		var vs = new VertexState()
		{
			Module = _shader,
			EntryPoint = ve.Pointer,
			BufferCount = 1,
			Buffers = &vertexBufferLayout, // stack local: only has to outlive this call
		};

		var ps = new PrimitiveState()
		{
			Topology = PrimitiveTopology.TriangleList,
			StripIndexFormat = IndexFormat.Undefined,
			FrontFace = FrontFace.Ccw,
			CullMode = CullMode.None
		};

		var ms = new MultisampleState()
		{
			Count = 1,
			Mask = ~0u,
			AlphaToCoverageEnabled = false
		};

		var desPipeline = new RenderPipelineDescriptor()
		{
			Vertex = vs,
			Primitive = ps,
			Multisample = ms,
			Fragment = &fs,
			DepthStencil = null
		};

		_pipeline = api.DeviceCreateRenderPipeline(device, in desPipeline);
		if (_pipeline == null)
		{
			throw new RabotoraInternalException($"{nameof(TriangleRenderer)}.cs", "Failed to create render pipeline");
		}
	}

	public void RenderFrame(WebGpuFrame frame, in FrameInfo info)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		if (_pipeline == null || _vertexBuffer == null)
		{
			throw new RabotoraInternalException($"{nameof(TriangleRenderer)}.cs", "RenderFrame was called before Load.");
		}

		var api = frame.Api;

		var colorAttachment = new RenderPassColorAttachment()
		{
			View = frame.View,
			DepthSlice = uint.MaxValue,
			ResolveTarget = null,
			LoadOp = LoadOp.Clear,
			StoreOp = StoreOp.Store,
			ClearValue = new Color {R = 0.05, G = 0.05, B = 0.08, A = 1}
		};

		var desPass = new RenderPassDescriptor()
		{
			ColorAttachmentCount = 1,
			ColorAttachments = &colorAttachment,
			DepthStencilAttachment = null
		};

		var pass = api.CommandEncoderBeginRenderPass(frame.Encoder, in desPass);
		if (pass == null) return;

		api.RenderPassEncoderSetPipeline(pass, _pipeline);
		api.RenderPassEncoderSetVertexBuffer(pass, 0, _vertexBuffer.Handle, 0, _vertexBuffer.Size);
		api.RenderPassEncoderDraw(pass, 3, 1, 0, 0);

		api.RenderPassEncoderEnd(pass);
		api.RenderPassEncoderRelease(pass);
	}

	public void Resize(int width, int height)
	{
		// triangle is not related to size.
	}

	public void Dispose()
	{
		if (_isDisposed) return;
		_isDisposed = true;

		if (_graphics == null) return;
		var api = _graphics.Api;

		// Newest first: the pipeline, then the buffer it reads, then the module both were built from.
		if (_pipeline != null)
		{
			api.RenderPipelineRelease(_pipeline);
			_pipeline = null;
		}
		if (_vertexBuffer != null)
		{
			_vertexBuffer.Dispose();
			_vertexBuffer = null;
		}
		if (_shader != null)
		{
			api.ShaderModuleRelease(_shader);
			_shader = null;
		}
	}
}
