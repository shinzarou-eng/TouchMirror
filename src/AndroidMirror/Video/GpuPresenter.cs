using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.D3DCompiler;
using D3D9 = Vortice.Direct3D9;

namespace TouchMirror.Video;

public sealed class GpuPresenter : IDisposable
{
    private static ID3D11Device? _device;
    private static readonly object _deviceLock = new();

    public static ID3D11Device? SharedDevice
    {
        get
        {
            lock (_deviceLock)
            {
                if (_device == null)
                {
                    var r = Vortice.Direct3D11.D3D11.D3D11CreateDevice(
                        null, DriverType.Hardware,
                        DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                        new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
                        out ID3D11Device dev);
                    _device = r.Success ? dev : null;
                }
                return _device;
            }
        }
    }

    public static IntPtr SharedDevicePtr
    {
        get
        {
            var d = SharedDevice;
            if (d == null)
                return IntPtr.Zero;
            Marshal.AddRef(d.NativePointer);
            return d.NativePointer;
        }
    }

    private readonly ID3D11DeviceContext _ctx;
    private readonly object _sync = new();

    private ID3D11VertexShader? _vs;
    private ID3D11PixelShader? _ps;
    private ID3D11SamplerState? _sampler;

    private ID3D11Texture2D? _nv12;
    private ID3D11ShaderResourceView? _srvY, _srvUV;
    private ID3D11Texture2D? _bgra;
    private ID3D11RenderTargetView? _rtv;
    private int _w, _h;
    private bool _pendingRebind;

    private readonly Dictionary<IntPtr, (ID3D11Texture2D tex, ID3D11ShaderResourceView? y,
        ID3D11ShaderResourceView? uv)> _srcCache = new();

    private static D3D9.IDirect3D9Ex? _d3d9;
    private static D3D9.IDirect3DDevice9Ex? _dev9;
    private D3D9.IDirect3DTexture9? _tex9;
    private D3DImage? _image;
    private IntPtr _hwnd;

    public event Action? FrameReady;
    public event Action<int, int>? SizeChanged;

    private const string VsSrc = @"
struct VSOut { float4 pos : SV_POSITION; float2 uv : TEXCOORD; };
VSOut main(uint id : SV_VertexID) {
    VSOut o;
    o.uv = float2((id << 1) & 2, id & 2);
    o.pos = float4(o.uv.x * 2 - 1, 1 - o.uv.y * 2, 0, 1);
    return o;
}";

    private const string PsSrc = @"
Texture2D<float> texY : register(t0);
Texture2D<float2> texUV : register(t1);
SamplerState samp : register(s0);
float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD) : SV_TARGET {
    float y = texY.Sample(samp, uv);
    float2 c = texUV.Sample(samp, uv) - 0.5;
    float3 rgb;
    rgb.r = 1.164 * (y - 0.0625) + 1.596 * c.y;
    rgb.g = 1.164 * (y - 0.0625) - 0.392 * c.x - 0.813 * c.y;
    rgb.b = 1.164 * (y - 0.0625) + 2.017 * c.x;
    return float4(saturate(rgb), 1.0);
}";

    public GpuPresenter()
    {
        var dev = SharedDevice ?? throw new InvalidOperationException("D3D11 indisponible");
        _ctx = dev.ImmediateContext;

        var vsBytes = Compiler.Compile(VsSrc, "main", "vs", "vs_4_0",
            ShaderFlags.None, EffectFlags.None);
        var psBytes = Compiler.Compile(PsSrc, "main", "ps", "ps_4_0",
            ShaderFlags.None, EffectFlags.None);
        _vs = dev.CreateVertexShader(vsBytes.Span, null);
        _ps = dev.CreatePixelShader(psBytes.Span, null);
        _sampler = dev.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
        });
    }

    private void EnsureTargets(int w, int h)
    {
        if (w == _w && h == _h && _nv12 != null)
            return;
        _w = w;
        _h = h;
        var dev = SharedDevice!;

        _srvY?.Dispose(); _srvUV?.Dispose(); _nv12?.Dispose();
        _rtv?.Dispose(); _bgra?.Dispose();
        _rtv = null; _bgra = null;
        _pendingRebind = true;

        _nv12 = dev.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)w, Height = (uint)h, MipLevels = 1, ArraySize = 1,
            Format = Format.NV12,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        });
        _srvY = dev.CreateShaderResourceView(_nv12, new ShaderResourceViewDescription(
            _nv12, ShaderResourceViewDimension.Texture2D, Format.R8_UNorm, 0, 1, 0, 1));
        _srvUV = dev.CreateShaderResourceView(_nv12, new ShaderResourceViewDescription(
            _nv12, ShaderResourceViewDimension.Texture2D, Format.R8G8_UNorm, 0, 1, 0, 1));

        SizeChanged?.Invoke(w, h);
    }

    public void Present(IntPtr srcTexture, int sliceIndex, int w, int h)
    {
        if (srcTexture == IntPtr.Zero)
            return;
        lock (_sync)
        {
            EnsureTargets(w, h);
            if (_rtv == null || _pendingRebind)
                return;

            ID3D11ShaderResourceView srvY, srvUV;
            if (!_srcCache.TryGetValue(srcTexture, out var entry))
            {
                var src = new ID3D11Texture2D(srcTexture);
                ID3D11ShaderResourceView? y = null, uv = null;
                try
                {
                    y = SharedDevice!.CreateShaderResourceView(src, new ShaderResourceViewDescription(
                        src, ShaderResourceViewDimension.Texture2DArray, Format.R8_UNorm, 0, 1,
                        (uint)sliceIndex, 1));
                    uv = SharedDevice!.CreateShaderResourceView(src, new ShaderResourceViewDescription(
                        src, ShaderResourceViewDimension.Texture2DArray, Format.R8G8_UNorm, 0, 1,
                        (uint)sliceIndex, 1));
                }
                catch { y = null; uv = null; }
                entry = (src, y, uv);
                _srcCache[srcTexture] = entry;
            }

            if (entry.y != null && entry.uv != null)
            {
                srvY = entry.y; srvUV = entry.uv;
            }
            else
            {
                _ctx.CopySubresourceRegion(_nv12!, 0, 0, 0, 0, entry.tex, (uint)sliceIndex, null);
                srvY = _srvY!; srvUV = _srvUV!;
            }

            _ctx.OMSetRenderTargets(_rtv);
            _ctx.RSSetViewport(0, 0, w, h);
            _ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _ctx.VSSetShader(_vs!);
            _ctx.PSSetShader(_ps!);
            _ctx.PSSetShaderResources(0, new[] { srvY, srvUV });
            _ctx.PSSetSampler(0, _sampler!);
            _ctx.Draw(3, 0);
            _ctx.Flush();
        }
        FrameReady?.Invoke();
    }

    public void Attach(D3DImage image, IntPtr hwnd)
    {
        _image = image;
        _hwnd = hwnd;
        if (_w > 0)
            Rebind();
    }

    public void Rebind()
    {
        if (_image == null || _w <= 0)
            return;

        if (_dev9 == null)
        {
            _d3d9 = D3D9.D3D9.Direct3DCreate9Ex();
            var pp = new D3D9.PresentParameters
            {
                Windowed = true,
                SwapEffect = D3D9.SwapEffect.Discard,
                BackBufferFormat = D3D9.Format.X8R8G8B8,
                BackBufferWidth = 1,
                BackBufferHeight = 1,
                PresentationInterval = D3D9.PresentInterval.Default,
            };
            _dev9 = _d3d9.CreateDeviceEx(0, D3D9.DeviceType.Hardware, _hwnd,
                D3D9.CreateFlags.HardwareVertexProcessing | D3D9.CreateFlags.Multithreaded |
                D3D9.CreateFlags.FpuPreserve, pp);
        }

        IntPtr handle = IntPtr.Zero;
        _tex9?.Dispose();
        _tex9 = _dev9.CreateTexture((uint)_w, (uint)_h, 1,
            D3D9.Usage.RenderTarget, D3D9.Format.X8R8G8B8,
            D3D9.Pool.Default, ref handle);
        var surface = _tex9.GetSurfaceLevel(0);

        lock (_sync)
        {
            _rtv?.Dispose(); _bgra?.Dispose();
            _bgra = SharedDevice!.OpenSharedResource<ID3D11Texture2D>(handle);
            _rtv = SharedDevice!.CreateRenderTargetView(_bgra);
            _pendingRebind = false;
        }

        _image.Lock();
        _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, surface.NativePointer);
        _image.Unlock();
        surface.Dispose();
    }

    public void Invalidate()
    {
        if (_image == null)
            return;
        _image.Lock();
        _image.AddDirtyRect(new Int32Rect(0, 0, _w, _h));
        _image.Unlock();
    }

    public void ClearSources()
    {
        lock (_sync)
        {
            foreach (var e in _srcCache.Values)
            {
                e.y?.Dispose();
                e.uv?.Dispose();
                e.tex.Dispose();
            }
            _srcCache.Clear();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            foreach (var e in _srcCache.Values)
            {
                e.y?.Dispose(); e.uv?.Dispose(); e.tex.Dispose();
            }
            _srcCache.Clear();
            _srvY?.Dispose(); _srvUV?.Dispose(); _nv12?.Dispose();
            _rtv?.Dispose(); _bgra?.Dispose();
            _vs?.Dispose(); _ps?.Dispose(); _sampler?.Dispose();
        }
        _tex9?.Dispose();
    }
}
