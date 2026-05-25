using ComputeSharp.D2D1.Interop;
using SharpGen.Runtime;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.Direct3D9;
using Vortice.DXGI;
using VorticeIsolation.Effects;
using Format = Vortice.DXGI.Format;

namespace VorticeIsolation
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private ID3D11Texture2D _renderTarget;
        private IDXGISurface _surface;
        private ID2D1Bitmap _bitmap;
        private IDirect3DTexture9 _renderTarget9;
        private ID2D1DeviceContext _d2DeviceContext;
        private ID3D11Device _d3D11Device;
        private IDirect3D9Ex _d3D9ContextEx;
        private IDirect3DDevice9Ex _d3D9DeviceEx;
        private ID2D1Effect _effect;
        private Stopwatch _stopwatch = Stopwatch.StartNew();

        private bool _reLoading = false;
        private bool _startup = true;
        private float multiplier = 0.5f;
        private float _width;
        private float _height;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_startup)
            {
                return;
            }
            var dpi = GetDpi(this);
            var width = DipToPixel((float)Grid.ActualWidth, (float)dpi.PixelsPerInchX);
            var height = DipToPixel((float)Grid.ActualHeight, (float)dpi.PixelsPerInchY);
            _width = width;
            _height = height;
            InitializeDirectXSurface((uint)(_width * multiplier), (uint)(_height * multiplier));
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _startup = true;
            InitializeDirectXDevice();
            var dpi = GetDpi(this);
            var width = DipToPixel((float)Grid.ActualWidth, (float)dpi.PixelsPerInchX);
            var height = DipToPixel((float)Grid.ActualHeight, (float)dpi.PixelsPerInchY);
            InitializeDirectXSurface((uint)(width * multiplier), (uint)(height * multiplier));
            _width = width;
            _height = height;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
            _startup = false;
        }

        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (_reLoading)
            {
                return;
            }
            _d2DeviceContext.BeginDraw();
            _d2DeviceContext.Clear(null);
            var bufferArray = D2D1PixelShader.GetConstantBuffer(new IsolationEffect(
            new float2(_width * multiplier, _height * multiplier),
            (float)_stopwatch.Elapsed.TotalSeconds,
            new(0.192f, 0.384f, 0.933f),
            new(0.957f, 0.804f, 0.623f),
            new(0.910f, 0.510f, 0.8f),
            new(0.350f, 0.71f, 0.953f),
            0f,
            0f,
            0f,
            true,
            true)).ToArray();
            IsolationD2DEffect.Instance?.DrawInfo?.SetPixelShaderConstantBuffer(bufferArray);
            _d2DeviceContext.DrawImage(_effect);
            _d2DeviceContext.EndDraw();
            _d3D11Device.ImmediateContext.Flush();
            D3DImage.Lock();
            D3DImage.AddDirtyRect(new Int32Rect(0, 0, (int)(_width * multiplier), (int)(_height * multiplier)));
            D3DImage.Unlock();
            Image.InvalidateVisual();
        }

        private IntPtr GetSharedHandle(ID3D11Texture2D texture)
        {
            using (var resource = texture.QueryInterface<IDXGIResource>())
            {
                return resource.SharedHandle;
            }
        }

        private static Vortice.Direct3D9.PresentParameters GetPresentParameters()
        {
            var presentParams = new Vortice.Direct3D9.PresentParameters();

            presentParams.Windowed = true;
            presentParams.SwapEffect = Vortice.Direct3D9.SwapEffect.Discard;
            presentParams.DeviceWindowHandle = NativeMethods.GetDesktopWindow();
            presentParams.PresentationInterval = PresentInterval.Default;
            return presentParams;
        }

        private void SetRenderTarget(ID3D11Texture2D target)
        {
            var format = Vortice.Direct3D9.Format.A8R8G8B8;
            var handle = GetSharedHandle(target);

            _renderTarget9?.Release();
            _renderTarget9 = _d3D9DeviceEx.CreateTexture(target.Description.Width, target.Description.Height, 1,
                Vortice.Direct3D9.Usage.RenderTarget, format, Pool.Default, ref handle);

            using (var surface = _renderTarget9.GetSurfaceLevel(0))
            {
                D3DImage.Lock();
                D3DImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, surface.NativePointer,
                    enableSoftwareFallback: true);
                D3DImage.AddDirtyRect(new Int32Rect(0, 0, (int)(_width * multiplier), (int)(_height * multiplier)));
                D3DImage.Unlock();
            }
        }

        private void InitializeDirectXDevice()
        {
            ID3D11Device device =
                D3D11.D3D11CreateDevice(Vortice.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport);
            _d3D11Device = device;

            IDXGIDevice dXGIDevice = device.QueryInterface<IDXGIDevice>();

            var d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
            var d2Device = d2dFactory.CreateDevice(dXGIDevice);
            d2dFactory.RegisterEffect<IsolationD2DEffect>();
            var context = d2Device.CreateDeviceContext();
            _d2DeviceContext = context;
            _effect?.Release();
            IsolationD2DEffect.OnEffectDestroyed();
            var id = context.CreateEffect(typeof(IsolationD2DEffect).GUID);
            _effect = id.As<ID2D1Effect>();
            var presentParams = GetPresentParameters();
            var createFlags = CreateFlags.HardwareVertexProcessing | CreateFlags.Multithreaded |
                              CreateFlags.FpuPreserve;

            var d3DContext = D3D9.Direct3DCreate9Ex();
            _d3D9ContextEx = d3DContext;
            IDirect3DDevice9Ex d3DDevice =
                _d3D9ContextEx.CreateDeviceEx(0, DeviceType.Hardware, IntPtr.Zero, createFlags, presentParams);
            _d3D9DeviceEx = d3DDevice;
        }

        private void InitializeDirectXSurface(uint width, uint height)
        {
            _reLoading = true;
            var desc = new Texture2DDescription()
            {
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                Format = Format.B8G8R8A8_UNorm,
                Width = width,
                Height = height,
                MipLevels = 1,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                MiscFlags = ResourceOptionFlags.Shared,
                CPUAccessFlags = CpuAccessFlags.None,
                ArraySize = 1
            };
            _renderTarget?.Release();
            _renderTarget = _d3D11Device.CreateTexture2D(desc);

            _surface?.Release();
            _surface = _renderTarget.QueryInterface<IDXGISurface>();
            var bitmap = _d2DeviceContext.CreateBitmapFromDxgiSurface(_surface);
            _bitmap?.Release();
            _bitmap = bitmap;
            _d2DeviceContext.Target = bitmap;
            SetRenderTarget(_renderTarget);
            _reLoading = false;
        }
        public static float DipToPixel(float dip, float dpi)
        {
            return dip * dpi / 96.0f;
        }
        public static DpiScale GetDpi(Visual visual)
        {
            return VisualTreeHelper.GetDpi(visual);
        }
    }
}

public static class NativeMethods
{
    [DllImport("user32.dll", SetLastError = false)]
    public static extern IntPtr GetDesktopWindow();
}
public class IsolationD2DEffect : CustomEffectBase, ID2D1DrawTransform
{
    public static IsolationD2DEffect? Instance { get; private set; }
    public ID2D1DrawInfo? DrawInfo { get; private set; }

    public IsolationD2DEffect()
    {
        Instance = this;
    }

    public override void Initialize(ID2D1EffectContext effectContext, ID2D1TransformGraph transformGraph)
    {
        var bytecode = D2D1PixelShader.LoadBytecode<IsolationEffect>();
        effectContext.LoadPixelShader(typeof(IsolationD2DEffect).GUID, bytecode.ToArray(), (uint)bytecode.Length);
        transformGraph.SetSingleTransformNode(this);
        base.Initialize(effectContext, transformGraph);
    }

    public void MapOutputRectToInputRects(RawRect outputRect, RawRect[] inputRects)
    {
        return;
    }

    public void MapInputRectsToOutputRect(RawRect[] inputRects, RawRect[] inputOpaqueSubRects, out RawRect outputRect, out RawRect outputOpaqueSubRect)
    {
        outputRect = new RawRect(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue);
        outputOpaqueSubRect = new RawRect(0, 0, 0, 0);
    }

    public RawRect MapInvalidRect(uint inputIndex, RawRect invalidInputRect)
    {
        return new RawRect(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue);
    }

    public uint GetInputCount()
    {
        return 0;
    }

    public void SetDrawInfo(ID2D1DrawInfo drawInfo)
    {
        drawInfo.SetPixelShader(typeof(IsolationD2DEffect).GUID, PixelOptions.None);
        DrawInfo = drawInfo;
    }
    public static void OnEffectDestroyed()
    {
        Instance = null;
    }
}