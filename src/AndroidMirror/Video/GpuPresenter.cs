using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.D3DCompiler;
using Vortice.Mathematics;
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
                        out ID3D11Device? dev);
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
    private ID3D11Texture2D? _frame;
    private ID3D11RenderTargetView? _frameRtv;
    private int _w, _h;
    private bool _pendingRebind;
    private bool _disposed;

    private readonly Dictionary<(IntPtr tex, int slice), (ID3D11Texture2D tex, ID3D11ShaderResourceView? y,
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
cbuffer ColorCB : register(b0) {
    float4 yuvT;
    float4 coefR;
    float4 coefG;
    float4 coefB;
    float4 uvRect;
    float4 adj;
};
Texture2D<float> texY : register(t0);
Texture2D<float2> texUV : register(t1);
SamplerState samp : register(s0);
float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD) : SV_TARGET {
    float2 suv = uv * uvRect.zw + uvRect.xy;
    float y0 = texY.Sample(samp, suv);
    if (coefB.y > 0.0) {
        float b = texY.Sample(samp, suv, int2(0, -1));
        float d = texY.Sample(samp, suv, int2(-1, 0));
        float f = texY.Sample(samp, suv, int2(1, 0));
        float h = texY.Sample(samp, suv, int2(0, 1));
        float mn = min(y0, min(min(b, d), min(f, h)));
        float mx = max(y0, max(max(b, d), max(f, h)));
        float r = y0 + coefB.y * (4.0 * y0 - b - d - f - h);
        float pad = (mx - mn) * 0.25;
        y0 = clamp(r, mn - pad, mx + pad);
    }
    float y = y0 * yuvT.x + yuvT.y;
    float2 c = texUV.Sample(samp, suv) * yuvT.z + yuvT.w;
    float3 rgb;
    rgb.r = y + coefR.x * c.y;
    rgb.g = y + coefG.x * c.x + coefG.y * c.y;
    rgb.b = y + coefB.x * c.x;
    float lum = dot(rgb, float3(0.2126, 0.7152, 0.0722));
    rgb = lum + (rgb - lum) * adj.z;
    rgb = (rgb - 0.5) * adj.y + 0.5 + adj.x;
    return float4(saturate(rgb), 1.0);
}";

    private ID3D11Buffer? _cb;
    private int _lastColorInfo = -1;
    private float _sharpness;
    private bool _cbDirty = true;
    private readonly float[] _cbData = new float[24];
    private float _viewX, _viewY, _viewS = 1f;
    private float _adjB, _adjC = 1f, _adjS = 1f;
    private ID3D11ShaderResourceView? _lastY, _lastUV;
    public const float DefaultSharpness = 0.22f;

    public float Sharpness
    {
        get => _sharpness;
        set
        {
            _sharpness = Math.Clamp(value, 0f, 1f);
            _cbDirty = true;
        }
    }

    private static readonly float[][] ColorTable =
    {
        new[] { 1.164383f, -0.073059f, 1.138393f, -0.571429f, 1.402f, 0f, 0f, 0f, -0.344136f, -0.714136f, 0f, 0f, 1.772f, 0f, 0f, 0f },
        new[] { 1f, 0f, 1f, -0.5f, 1.402f, 0f, 0f, 0f, -0.344136f, -0.714136f, 0f, 0f, 1.772f, 0f, 0f, 0f },
        new[] { 1.164383f, -0.073059f, 1.138393f, -0.571429f, 1.5748f, 0f, 0f, 0f, -0.187324f, -0.468124f, 0f, 0f, 1.8556f, 0f, 0f, 0f },
        new[] { 1f, 0f, 1f, -0.5f, 1.5748f, 0f, 0f, 0f, -0.187324f, -0.468124f, 0f, 0f, 1.8556f, 0f, 0f, 0f },
        new[] { 1.164383f, -0.073059f, 1.138393f, -0.571429f, 1.4746f, 0f, 0f, 0f, -0.164553f, -0.571353f, 0f, 0f, 1.8814f, 0f, 0f, 0f },
        new[] { 1f, 0f, 1f, -0.5f, 1.4746f, 0f, 0f, 0f, -0.164553f, -0.571353f, 0f, 0f, 1.8814f, 0f, 0f, 0f },
    };

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
        _cb = dev.CreateBuffer(new BufferDescription
        {
            ByteWidth = 96,
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ConstantBuffer,
        });
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
        _frameRtv?.Dispose(); _frame?.Dispose(); _bgra?.Dispose();
        _frameRtv = null; _frame = null; _bgra = null;
        _lastY = _lastUV = null;
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

    private unsafe void UpdateColor(int colorInfo)
    {
        if ((colorInfo != _lastColorInfo || _cbDirty) && colorInfo >= 0 && colorInfo < ColorTable.Length)
        {
            var coefs = ColorTable[colorInfo];
            Array.Copy(coefs, _cbData, 16);
            _cbData[13] = _sharpness;
            _cbData[16] = _viewX; _cbData[17] = _viewY;
            _cbData[18] = _viewS; _cbData[19] = _viewS;
            _cbData[20] = _adjB; _cbData[21] = _adjC; _cbData[22] = _adjS;
            fixed (float* p = _cbData)
                _ctx.UpdateSubresource(_cb!, 0, null, (IntPtr)p, 0, 0);
            _lastColorInfo = colorInfo;
            _cbDirty = false;
        }
    }

    public void SetViewRect(float x, float y, float scale)
    {
        _viewX = x; _viewY = y; _viewS = Math.Max(scale, 0.0001f);
        _cbDirty = true;
    }

    public void SetColorAdjust(float brightness, float contrast, float saturation)
    {
        _adjB = brightness; _adjC = contrast; _adjS = saturation;
        _cbDirty = true;
    }

    public void Redraw()
    {
        lock (_sync)
        {
            if (_disposed || _frameRtv == null || _pendingRebind || _lastY == null)
                return;
            UpdateColor(_lastColorInfo);
            Draw(_lastY, _lastUV!, _w, _h);
        }
        Invalidate();
    }

    private void Draw(ID3D11ShaderResourceView srvY, ID3D11ShaderResourceView srvUV, int w, int h)
    {
        _lastY = srvY; _lastUV = srvUV;
        _ctx.OMSetRenderTargets(_frameRtv!);
        _ctx.RSSetViewport(0, 0, w, h);
        _ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _ctx.VSSetShader(_vs!);
        _ctx.PSSetShader(_ps!);
        _ctx.PSSetShaderResources(0, new[] { srvY, srvUV });
        _ctx.PSSetConstantBuffer(0, _cb!);
        _ctx.PSSetSampler(0, _sampler!);
        _ctx.Draw(3, 0);
        _ctx.Flush();
    }

    public unsafe void Present(IntPtr srcTexture, int sliceIndex, int w, int h, int colorInfo)
    {
        if (srcTexture == IntPtr.Zero || _disposed)
            return;
        lock (_sync)
        {
            if (_disposed)
                return;
            UpdateColor(colorInfo);
            EnsureTargets(w, h);
            if (_frameRtv == null || _pendingRebind)
                return;

            ID3D11ShaderResourceView srvY, srvUV;
            var srcKey = (srcTexture, sliceIndex);
            if (!_srcCache.TryGetValue(srcKey, out var entry))
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
                _srcCache[srcKey] = entry;
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

            Draw(srvY, srvUV, w, h);
        }
        FrameReady?.Invoke();
    }

    private byte[]? _uvTmp;

    public unsafe void PresentSoftware(IntPtr yPlane, int yStride, IntPtr uPlane, int uStride,
        IntPtr vPlane, int vStride, int w, int h, int colorInfo)
    {
        if (yPlane == IntPtr.Zero || uPlane == IntPtr.Zero || _disposed)
            return;
        lock (_sync)
        {
            if (_disposed)
                return;
            UpdateColor(colorInfo);
            EnsureTargets(w, h);
            if (_frameRtv == null || _pendingRebind)
                return;

            _ctx.UpdateSubresource(_nv12!, 0, new Box(0, 0, 0, w, h, 1),
                yPlane, (uint)yStride, 0u);

            var cw = w / 2;
            var ch = h / 2;
            if (vPlane == IntPtr.Zero)
            {
                _ctx.UpdateSubresource(_nv12!, 1, new Box(0, 0, 0, cw, ch, 1),
                    uPlane, (uint)uStride, 0u);
            }
            else
            {
                var need = cw * 2 * ch;
                if (_uvTmp == null || _uvTmp.Length < need)
                    _uvTmp = new byte[need];
                fixed (byte* dst = _uvTmp)
                {
                    var sU = (byte*)uPlane;
                    var sV = (byte*)vPlane;
                    for (var row = 0; row < ch; row++)
                    {
                        var du = dst + row * cw * 2;
                        var ru = sU + row * uStride;
                        var rv = sV + row * vStride;
                        for (var i = 0; i < cw; i++)
                        {
                            du[0] = ru[i];
                            du[1] = rv[i];
                            du += 2;
                        }
                    }
                }
                fixed (byte* src = _uvTmp)
                    _ctx.UpdateSubresource(_nv12!, 1, new Box(0, 0, 0, cw, ch, 1),
                        (IntPtr)src, (uint)(cw * 2), 0u);
            }

            Draw(_srvY!, _srvUV!, w, h);
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
            _frameRtv?.Dispose(); _frame?.Dispose(); _bgra?.Dispose();
            _bgra = SharedDevice!.OpenSharedResource<ID3D11Texture2D>(handle);
            var d = _bgra.Description;
            _frame = SharedDevice!.CreateTexture2D(new Texture2DDescription
            {
                Width = d.Width, Height = d.Height, MipLevels = 1, ArraySize = 1,
                Format = d.Format,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.None,
            });
            _frameRtv = SharedDevice!.CreateRenderTargetView(_frame);
            _pendingRebind = false;
        }

        _image.Lock();
        _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, surface.NativePointer);
        _image.Unlock();
        surface.Dispose();
    }

    public unsafe byte[]? CaptureBgra(out int w, out int h)
    {
        lock (_sync)
        {
            w = _w; h = _h;
            if (_frame == null || _w <= 0)
                return null;
            var desc = _frame.Description;
            desc.Usage = ResourceUsage.Staging;
            desc.BindFlags = BindFlags.None;
            desc.CPUAccessFlags = CpuAccessFlags.Read;
            desc.MiscFlags = ResourceOptionFlags.None;
            using var staging = SharedDevice!.CreateTexture2D(desc);
            _ctx.CopyResource(staging, _frame);
            var map = _ctx.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                var buf = new byte[w * h * 4];
                var src = (byte*)map.DataPointer;
                var pitch = (int)map.RowPitch;
                fixed (byte* dst = buf)
                {
                    if (pitch == w * 4)
                        Buffer.MemoryCopy(src, dst, buf.Length, buf.Length);
                    else
                        for (var row = 0; row < h; row++)
                            Buffer.MemoryCopy(src + row * pitch, dst + row * w * 4,
                                (long)w * h * 4, w * 4L);
                }
                return buf;
            }
            finally
            {
                _ctx.Unmap(staging, 0);
            }
        }
    }

    public void Invalidate()
    {
        var img = _image;
        if (_disposed || img == null)
            return;
        img.Lock();
        try
        {
            lock (_sync)
            {
                if (_bgra != null && _frame != null)
                    _ctx.CopyResource(_bgra, _frame);
            }
            img.AddDirtyRect(new Int32Rect(0, 0, _w, _h));
        }
        finally { img.Unlock(); }
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
            _lastY = _lastUV = null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        lock (_sync)
        {
            foreach (var e in _srcCache.Values)
            {
                e.y?.Dispose(); e.uv?.Dispose(); e.tex.Dispose();
            }
            _srcCache.Clear();
            _srvY?.Dispose(); _srvUV?.Dispose(); _nv12?.Dispose();
            _frameRtv?.Dispose(); _frame?.Dispose(); _bgra?.Dispose();
            _vs?.Dispose(); _ps?.Dispose(); _sampler?.Dispose(); _cb?.Dispose();
        }
        _tex9?.Dispose();
    }
}
