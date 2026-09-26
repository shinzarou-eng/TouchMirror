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
                var removed = false;
                if (_device != null)
                {
                    try { removed = _device.DeviceRemovedReason.Failure; }
                    catch { }
                }
                if (removed)
                {
                    try { _device!.Dispose(); } catch { }
                    _device = null;
                }
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
    private ID3D11PixelShader? _psFxaa;
    private ID3D11Buffer? _cbFxaa;
    private ID3D11SamplerState? _sampler;

    private ID3D11Texture2D? _nv12;
    private ID3D11ShaderResourceView? _srvY, _srvUV;
    private ID3D11Texture2D? _bgra;
    private ID3D11Texture2D? _frame;
    private ID3D11RenderTargetView? _frameRtv;
    private ID3D11Texture2D? _pre;
    private ID3D11RenderTargetView? _preRtv;
    private ID3D11ShaderResourceView? _preSrv;
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
    public bool PendingRebind => _pendingRebind;

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
    float4 fx;
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
        float amp = saturate(min(mn, 1.0 - mx) / max(mx, 1e-4));
        float w = -sqrt(amp) * coefB.y * 0.5;
        y0 = (y0 + (b + d + f + h) * w) / (1.0 + 4.0 * w);
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
    rgb = saturate(rgb);
    if (fx.x != 0.0) {
        float lum2 = dot(rgb, float3(0.2126, 0.7152, 0.0722));
        float sat = max(rgb.r, max(rgb.g, rgb.b)) - min(rgb.r, min(rgb.g, rgb.b));
        rgb = lum2 + (rgb - lum2) * (1.0 + fx.x * (1.0 - sat));
    }
    if (fx.z != 1.0)
        rgb = pow(saturate(rgb), fx.z);
    if (fx.y != 0.0) {
        float d = distance(uv, float2(0.5, 0.5));
        float v = smoothstep(0.85, 0.35, d);
        rgb *= lerp(1.0, v, fx.y);
    }
    return float4(saturate(rgb), 1.0);
}";

    private const string PsFxaaSrc = @"
cbuffer FxaaCB : register(b0) { float4 rcpFrame; };
Texture2D<float4> tex : register(t0);
SamplerState samp : register(s0);
static const float3 LUMA = float3(0.299, 0.587, 0.114);
float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD) : SV_TARGET {
    float2 px = rcpFrame.xy;
    float3 nw = tex.Sample(samp, uv + float2(-1.0, -1.0) * px).rgb;
    float3 ne = tex.Sample(samp, uv + float2( 1.0, -1.0) * px).rgb;
    float3 sw = tex.Sample(samp, uv + float2(-1.0,  1.0) * px).rgb;
    float3 se = tex.Sample(samp, uv + float2( 1.0,  1.0) * px).rgb;
    float3 m  = tex.Sample(samp, uv).rgb;
    float lnw = dot(nw, LUMA);
    float lne = dot(ne, LUMA);
    float lsw = dot(sw, LUMA);
    float lse = dot(se, LUMA);
    float lm  = dot(m, LUMA);
    float lmin = min(lm, min(min(lnw, lne), min(lsw, lse)));
    float lmax = max(lm, max(max(lnw, lne), max(lsw, lse)));
    if (lmax - lmin < 0.0312)
        return float4(m, 1.0);
    float2 dir = float2((lsw + lse) - (lnw + lne), (lnw + lsw) - (lne + lse));
    float dirReduce = max((lnw + lne + lsw + lse) * 0.03125, 0.0078125);
    float rcpMin = 1.0 / (min(abs(dir.x), abs(dir.y)) + dirReduce);
    dir = clamp(dir * rcpMin, -8.0, 8.0) * px;
    float3 a = 0.5 * (tex.Sample(samp, uv + dir * (1.0 / 3.0 - 0.5)).rgb
                    + tex.Sample(samp, uv + dir * (2.0 / 3.0 - 0.5)).rgb);
    float3 b = a * 0.5 + 0.25 * (tex.Sample(samp, uv + dir * -0.5).rgb
                               + tex.Sample(samp, uv + dir *  0.5).rgb);
    float lb = dot(b, LUMA);
    return float4((lb < lmin || lb > lmax) ? a : b, 1.0);
}";

    private ID3D11Buffer? _cb;
    private int _lastColorInfo = -1;
    private float _sharpness;
    private bool _cbDirty = true;
    private readonly float[] _cbData = new float[28];
    private float _viewX, _viewY, _viewS = 1f;
    private float _adjB, _adjC = 1f, _adjS = 1f;
    private float _vibrance, _vignette, _gamma = 1f;
    private bool _fxaa;
    private ID3D11ShaderResourceView? _lastY, _lastUV;
    public const float DefaultSharpness = 0.22f;

    public bool Fxaa
    {
        get => _fxaa;
        set
        {
            _fxaa = value;
            if (!value)
            {
                lock (_sync)
                {
                    _preSrv?.Dispose(); _preRtv?.Dispose(); _pre?.Dispose();
                    _preSrv = null; _preRtv = null; _pre = null;
                }
            }
        }
    }

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
        var fxaaBytes = Compiler.Compile(PsFxaaSrc, "main", "ps", "ps_4_0",
            ShaderFlags.None, EffectFlags.None);
        _psFxaa = dev.CreatePixelShader(fxaaBytes.Span, null);
        _cbFxaa = dev.CreateBuffer(new BufferDescription
        {
            ByteWidth = 16,
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ConstantBuffer,
        });
        _cb = dev.CreateBuffer(new BufferDescription
        {
            ByteWidth = 112,
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
        _srvY = null; _srvUV = null; _nv12 = null;
        _frameRtv?.Dispose(); _frame?.Dispose(); _bgra?.Dispose();
        _preSrv?.Dispose(); _preRtv?.Dispose(); _pre?.Dispose();
        _frameRtv = null; _frame = null; _bgra = null;
        _preSrv = null; _preRtv = null; _pre = null;
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
            _cbData[24] = _vibrance; _cbData[25] = _vignette;
            _cbData[26] = _gamma != 1f ? 1f / _gamma : 1f;
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

    public void SetEffects(float vibrance, float vignette, float gamma)
    {
        _vibrance = Math.Clamp(vibrance, -1f, 1f);
        _vignette = Math.Clamp(vignette, -1f, 1f);
        _gamma = Math.Clamp(gamma, 0.5f, 1.8f);
        _cbDirty = true;
    }

    public void Redraw()
    {
        if (_gpuDead || !Monitor.TryEnter(_sync))
            return;
        try
        {
            if (_disposed || _frameRtv == null || _pendingRebind || _lastY == null)
                return;
            UpdateColor(_lastColorInfo);
            Draw(_lastY, _lastUV!, _w, _h);
        }
        finally { Monitor.Exit(_sync); }
        Invalidate();
    }

    private void EnsurePre()
    {
        if (_pre != null)
            return;
        var dev = SharedDevice!;
        _pre = dev.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)_w, Height = (uint)_h, MipLevels = 1, ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
        });
        _preRtv = dev.CreateRenderTargetView(_pre);
        _preSrv = dev.CreateShaderResourceView(_pre);

        var rcp = new float[] { 1f / _w, 1f / _h, 0, 0 };
        _ctx.UpdateSubresource(rcp, _cbFxaa!);
    }

    private void Draw(ID3D11ShaderResourceView srvY, ID3D11ShaderResourceView srvUV, int w, int h)
    {
        _lastY = srvY; _lastUV = srvUV;
        if (_fxaa)
            EnsurePre();
        _ctx.OMSetRenderTargets(_fxaa ? _preRtv! : _frameRtv!);
        _ctx.RSSetViewport(0, 0, w, h);
        _ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _ctx.VSSetShader(_vs!);
        _ctx.PSSetShader(_ps!);
        _ctx.PSSetShaderResources(0, new[] { srvY, srvUV });
        _ctx.PSSetConstantBuffer(0, _cb!);
        _ctx.PSSetSampler(0, _sampler!);
        _ctx.Draw(3, 0);

        if (_fxaa)
        {
            _ctx.PSSetShaderResources(0, new ID3D11ShaderResourceView[2]);
            _ctx.OMSetRenderTargets(_frameRtv!);
            _ctx.PSSetShader(_psFxaa!);
            _ctx.PSSetShaderResources(0, new[] { _preSrv! });
            _ctx.PSSetConstantBuffer(0, _cbFxaa!);
            _ctx.Draw(3, 0);
            _ctx.PSSetShaderResources(0, new ID3D11ShaderResourceView[1]);
        }
        _ctx.Flush();
    }

    public unsafe void Present(IntPtr srcTexture, int sliceIndex, int w, int h, int colorInfo)
    {
        if (srcTexture == IntPtr.Zero || _disposed || _gpuDead)
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
                Marshal.AddRef(srcTexture);
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
        if (yPlane == IntPtr.Zero || uPlane == IntPtr.Zero || _disposed || _gpuDead)
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
        image.IsFrontBufferAvailableChanged += OnFrontBufferChanged;
        if (_w > 0)
            Rebind();
    }

    private void OnFrontBufferChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_image is { IsFrontBufferAvailable: true })
        {
            Rebind();
            Redraw();
        }
    }

    private int _rebindRetryQueued;
    private int _rebindBusy;
    private int _rebindFails;
    private volatile bool _gpuDead;
    private int _frontOk = -1;
    private long _invCopied;
    public int FrontOk => _frontOk;
    public long InvCopied => Interlocked.Read(ref _invCopied);
    public event Action? GpuFailed;
    private static readonly object _d3d9Lock = new();

    private void ScheduleRebindRetry()
    {
        var img = _image;
        if (img == null || Interlocked.Exchange(ref _rebindRetryQueued, 1) == 1)
            return;
        img.Dispatcher.BeginInvoke(() =>
        {
            _rebindRetryQueued = 0;
            if (_pendingRebind && !_disposed)
                Rebind();
        });
    }

    public void Rebind()
    {
        var img = _image;
        if (img == null || _w <= 0 || _disposed || _gpuDead || !img.IsFrontBufferAvailable)
            return;
        if (Interlocked.Exchange(ref _rebindBusy, 1) == 1)
            return;
        var w = _w; var h = _h; var hwnd = _hwnd;
        Task.Run(() => RebindWorker(img, w, h, hwnd));
        _ = Task.Run(async () =>
        {
            await Task.Delay(5000);
            if (Volatile.Read(ref _rebindBusy) == 1 && !_disposed && !_gpuDead)
            {
                _gpuDead = true;
                _pendingRebind = true;
                GpuFailed?.Invoke();
            }
        });
    }

    private void RebindWorker(D3DImage img, int w, int h, IntPtr hwnd)
    {
        D3D9.IDirect3DTexture9? tex9 = null;
        D3D9.IDirect3DSurface9? surface = null;
        try
        {
            lock (_d3d9Lock)
            {
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
                    _dev9 = _d3d9.CreateDeviceEx(0, D3D9.DeviceType.Hardware, hwnd,
                        D3D9.CreateFlags.HardwareVertexProcessing | D3D9.CreateFlags.Multithreaded |
                        D3D9.CreateFlags.FpuPreserve, pp);
                }
            }
            if (_disposed || _gpuDead)
                return;

            IntPtr handle = IntPtr.Zero;
            tex9 = _dev9!.CreateTexture((uint)w, (uint)h, 1,
                D3D9.Usage.RenderTarget, D3D9.Format.X8R8G8B8,
                D3D9.Pool.Default, ref handle);
            surface = tex9.GetSurfaceLevel(0);

            var dev = SharedDevice;
            if (dev == null)
                throw new InvalidOperationException("D3D11 indisponible");
            var bgraNew = dev.OpenSharedResource<ID3D11Texture2D>(handle);
            var d = bgraNew.Description;
            var frameNew = dev.CreateTexture2D(new Texture2DDescription
            {
                Width = d.Width, Height = d.Height, MipLevels = 1, ArraySize = 1,
                Format = d.Format,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.None,
            });
            var rtvNew = dev.CreateRenderTargetView(frameNew);

            if (!Monitor.TryEnter(_sync, 3000))
            {
                rtvNew.Dispose(); frameNew.Dispose(); bgraNew.Dispose();
                FailRebind();
                return;
            }
            try
            {
                if (_disposed || _gpuDead || w != _w || h != _h)
                {
                    rtvNew.Dispose(); frameNew.Dispose(); bgraNew.Dispose();
                    tex9?.Dispose(); surface?.Dispose();
                    Interlocked.Exchange(ref _rebindBusy, 0);
                    if (!_disposed && !_gpuDead)
                        ScheduleRebindRetry();
                    return;
                }
                _frameRtv?.Dispose(); _frame?.Dispose(); _bgra?.Dispose();
                _bgra = bgraNew; _frame = frameNew; _frameRtv = rtvNew;
                var old = _tex9;
                _tex9 = tex9;
                tex9 = null;
                try { old?.Dispose(); } catch { }
                _pendingRebind = false;
            }
            finally { Monitor.Exit(_sync); }

            img.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (!_disposed && _image == img && img.IsFrontBufferAvailable && surface != null)
                    {
                        img.Lock();
                        img.SetBackBuffer(D3DResourceType.IDirect3DSurface9, surface.NativePointer);
                        img.Unlock();
                    }
                }
                catch { }
                try { surface?.Dispose(); } catch { }
                Interlocked.Exchange(ref _rebindBusy, 0);
            });
            _rebindFails = 0;
            return;
        }
        catch
        {
            tex9?.Dispose();
            surface?.Dispose();
            FailRebind();
            return;
        }

        void FailRebind()
        {
            Interlocked.Exchange(ref _rebindBusy, 0);
            _pendingRebind = true;
            if (++_rebindFails >= 4)
            {
                _gpuDead = true;
                GpuFailed?.Invoke();
            }
            else if (!_disposed)
            {
                ScheduleRebindRetry();
            }
        }
    }

    public unsafe byte[]? CaptureBgra(out int w, out int h)
    {
        if (_gpuDead || !Monitor.TryEnter(_sync))
        {
            w = 0; h = 0;
            return null;
        }
        try
        {
            w = _w; h = _h;
            var src2 = _bgra ?? _frame;
            if (src2 == null || _w <= 0)
                return null;
            var desc = src2.Description;
            desc.Usage = ResourceUsage.Staging;
            desc.BindFlags = BindFlags.None;
            desc.CPUAccessFlags = CpuAccessFlags.Read;
            desc.MiscFlags = ResourceOptionFlags.None;
            using var staging = SharedDevice!.CreateTexture2D(desc);
            _ctx.CopyResource(staging, src2);
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
        finally { Monitor.Exit(_sync); }
    }

    public unsafe (long hash, bool uniform)? SampleFrame()
    {
        if (_gpuDead || !Monitor.TryEnter(_sync))
            return null;
        try
        {
            if (_frame == null || _w <= 0 || _h <= 0)
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
                var src = (byte*)map.DataPointer;
                var pitch = (int)map.RowPitch;
                var rowStep = Math.Max(1, _h / 64);
                var colStep = Math.Max(4, _w * 4 / 256) & ~3;
                int minB = 255, maxB = 0, minG = 255, maxG = 0, minR = 255, maxR = 0;
                unchecked
                {
                    long hash = -3750763034362895579L;
                    for (var row = 0; row < _h; row += rowStep)
                    {
                        var r = src + (long)row * pitch;
                        for (var col = 0; col + 3 < _w * 4; col += colStep)
                        {
                            var v = *(uint*)(r + col);
                            hash = (hash ^ v) * 1099511628211L;
                            int b = (int)(v & 0xFF), g = (int)((v >> 8) & 0xFF), rr = (int)((v >> 16) & 0xFF);
                            if (b < minB) minB = b; if (b > maxB) maxB = b;
                            if (g < minG) minG = g; if (g > maxG) maxG = g;
                            if (rr < minR) minR = rr; if (rr > maxR) maxR = rr;
                        }
                    }
                    var uniform = (maxB - minB) < 10 && (maxG - minG) < 10 && (maxR - minR) < 10;
                    return (hash, uniform);
                }
            }
            finally { _ctx.Unmap(staging, 0); }
        }
        finally { Monitor.Exit(_sync); }
    }

    public void Invalidate()
    {
        var img = _image;
        _frontOk = img == null ? -1 : img.IsFrontBufferAvailable ? 1 : 0;
        if (_disposed || _gpuDead || img == null || !img.IsFrontBufferAvailable)
            return;
        img.Lock();
        try
        {
            if (Monitor.TryEnter(_sync))
            {
                try
                {
                    if (_bgra != null && _frame != null)
                    {
                        try
                        {
                            _ctx.CopyResource(_bgra, _frame);
                            Interlocked.Increment(ref _invCopied);
                        }
                        catch (SharpGen.Runtime.SharpGenException)
                        {
                            _gpuDead = true;
                            _pendingRebind = true;
                            _ = Task.Run(() => GpuFailed?.Invoke());
                        }
                    }
                }
                finally { Monitor.Exit(_sync); }
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

    private static void Drop<T>(ref T? obj) where T : class, IDisposable
    {
        try { obj?.Dispose(); } catch { }
        obj = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_image != null)
        {
            _image.IsFrontBufferAvailableChanged -= OnFrontBufferChanged;
            _image = null;
        }
        if (_gpuDead)
        {
            Drop(ref _tex9);
            return;
        }
        lock (_sync)
        {
            foreach (var e in _srcCache.Values)
            {
                try { e.y?.Dispose(); } catch { }
                try { e.uv?.Dispose(); } catch { }
                try { e.tex.Dispose(); } catch { }
            }
            _srcCache.Clear();
            Drop(ref _srvY); Drop(ref _srvUV); Drop(ref _nv12);
            Drop(ref _frameRtv); Drop(ref _frame); Drop(ref _bgra);
            Drop(ref _preSrv); Drop(ref _preRtv); Drop(ref _pre);
            Drop(ref _vs); Drop(ref _ps); Drop(ref _psFxaa); Drop(ref _sampler);
            Drop(ref _cb); Drop(ref _cbFxaa);
        }
        Drop(ref _tex9);
    }
}
