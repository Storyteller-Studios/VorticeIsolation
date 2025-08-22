using SharpGen.Runtime;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.Direct3D9;
using Vortice.DXGI;
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
        private ID2D1Factory1 _factory;
        private ID2D1Effect _effect;
        private Stopwatch _stopwatch = Stopwatch.StartNew();

        private bool _reLoading = false;
        private bool _startup = true;
        private float multiplier = 0.5f;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            SizeChanged += MainWindow_SizeChanged;
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_startup) 
            {
                return;
            }
            _effect?.SetValue(4, (float)Grid.ActualWidth * multiplier);
            _effect?.SetValue(5, (float)Grid.ActualHeight * multiplier);
            InitializeDirectXSurface((uint)(Grid.ActualWidth * multiplier), (uint)(Grid.ActualHeight * multiplier));
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _startup = true;
            InitializeDirectXDevice();
            InitializeDirectXSurface((uint)(Grid.ActualWidth * multiplier), (uint)(Grid.ActualHeight * multiplier));
            _effect?.SetValue(4, (float)Grid.ActualWidth * multiplier);
            _effect?.SetValue(5, (float)Grid.ActualHeight * multiplier);
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
            _effect.SetValue(3, (float)_stopwatch.Elapsed.TotalSeconds);
            _d2DeviceContext.DrawImage(_effect);
            _d2DeviceContext.EndDraw();
            _d3D11Device.ImmediateContext.Flush();
            D3DImage.Lock();
            D3DImage.AddDirtyRect(new Int32Rect(0, 0, (int)(Grid.ActualWidth * multiplier), (int)(Grid.ActualHeight * multiplier)));
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
            presentParams.SwapEffect = Vortice.Direct3D9.SwapEffect.Flip;
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
                D3DImage.AddDirtyRect(new Int32Rect(0, 0, (int)(Grid.ActualWidth * multiplier), (int)(Grid.ActualHeight * multiplier)));
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
            _factory = d2dFactory;
            var d2Device = d2dFactory.CreateDevice(dXGIDevice);
            d2dFactory.RegisterEffect<IsolationEffect>();
            var context = d2Device.CreateDeviceContext();
            _d2DeviceContext = context;
            var id = context.CreateEffect(typeof(IsolationEffect).GUID);
            _effect?.Release();
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
    }
}

public static class NativeMethods
{
    [DllImport("user32.dll", SetLastError = false)]
    public static extern IntPtr GetDesktopWindow();
}
public class IsolationEffect : CustomEffectBase, ID2D1DrawTransform
{
    public static readonly List<IsolationEffect> CurrentInstances = new();
    public IsolationEffect()
    {
        CurrentInstances.Add(this);
    }
    private ID2D1DrawInfo? drawInfo;
    [CustomEffectProperty(PropertyType.Float, 0)]
    public float RandomValue1
    {
        get => Buffer.RandomValue1;
        set
        {
            Buffer.RandomValue1 = value;
            drawInfo?.SetPixelShaderConstantBuffer(Buffer);
        }
    }
    [CustomEffectProperty(PropertyType.Float, 1)]
    public float RandomValue2
    {
        get => Buffer.RandomValue2;
        set
        {
            Buffer.RandomValue2 = value;
            drawInfo?.SetPixelShaderConstantBuffer(Buffer);
        }
    }
    [CustomEffectProperty(PropertyType.Float, 2)]
    public float RandomValue3
    {
        get => Buffer.RandomValue3;
        set
        {
            Buffer.RandomValue3 = value;
            drawInfo?.SetPixelShaderConstantBuffer(Buffer);
        }
    }
    [CustomEffectProperty(PropertyType.Float, 3)]
    public float iTime
    {
        get => Buffer.iTime;
        set
        {
            Buffer.iTime = value;
            drawInfo?.SetPixelShaderConstantBuffer(Buffer);
        }
    }
    [CustomEffectProperty(PropertyType.Float, 4)]
    public float Width
    {
        get => Buffer.Width;
        set
        {
            Buffer.Width = value;
            drawInfo?.SetPixelShaderConstantBuffer(Buffer);
        }
    }
    [CustomEffectProperty(PropertyType.Float, 5)]
    public float Height
    {
        get => Buffer.Height;
        set
        {
            Buffer.Height = value;
            drawInfo?.SetPixelShaderConstantBuffer(Buffer);
        }
    }
    [CustomEffectProperty(PropertyType.Bool, 6)]
    public bool EnableLightWave
    {
        get => Buffer.EnableLightWave;
        set
        {
            Buffer.EnableLightWave = value;
            drawInfo?.SetPixelShaderConstantBuffer(Buffer);
        }
    }
    [CustomEffectProperty(PropertyType.Vector3, 7)]
    public Vector3 iResolution
    {
        get => Buffer.iResolution;
        set
        {
            Buffer.iResolution = value;
            drawInfo?.SetPixelShaderConstantBuffer(Buffer);
        }
    }
    [CustomEffectProperty(PropertyType.Vector3, 8)]
    public Vector3 Color1
    {
        get => Buffer.color1;
        set
        {
            Buffer.color1 = value;
            drawInfo?.SetPixelShaderConstantBuffer(Buffer);
        }
    }
    [CustomEffectProperty(PropertyType.Vector3, 9)]
    public Vector3 Color2
    {
        get => Buffer.color2;
        set
        {
            Buffer.color2 = value;
            drawInfo?.SetPixelShaderConstantBuffer(Buffer);
        }
    }
    [CustomEffectProperty(PropertyType.Vector3, 10)]
    public Vector3 Color3
    {
        get => Buffer.color3;
        set
        {
            Buffer.color3 = value;
            drawInfo?.SetPixelShaderConstantBuffer(Buffer);
        }
    }
    [CustomEffectProperty(PropertyType.Vector3, 11)]
    public Vector3 Color4
    {
        get => Buffer.color4;
        set
        {
            Buffer.color4 = value;
            drawInfo?.SetPixelShaderConstantBuffer(Buffer);
        }
    }
    private IsolationEffectConstants Buffer = new()
    {
        RandomValue1 = 0f,
        RandomValue2 = 0f,
        RandomValue3 = 0f,
        iTime = 0f,
        Width = 1280,
        Height = 720,
        EnableLightWave = true,
        iResolution = new(1, 1, 1),
        color1 = new(0.192f, 0.384f, 0.933f),
        color2 = new(0.957f, 0.804f, 0.623f),
        color3 = new(0.910f, 0.510f, 0.8f),
        color4 = new(0.350f, 0.71f, 0.953f)
    };

    public override void Initialize(ID2D1EffectContext effectContext, ID2D1TransformGraph transformGraph)
    {
        var data = File.ReadAllBytes("effect.ps");
        effectContext.LoadPixelShader(typeof(IsolationEffect).GUID, data, (uint)data.Length);
        transformGraph.SetSingleTransformNode(this);
        base.Initialize(effectContext, transformGraph);
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct IsolationEffectConstants
    {
        public Vector3 iResolution { get; set; }
        public float RandomValue1 { get; set; }
        public Vector3 color1 { get; set; }
        public float RandomValue2 { get; set; }
        public Vector3 color2 { get; set; }
        public float RandomValue3 { get; set; }
        public Vector3 color3 { get; set; }
        public float iTime { get; set; }
        public Vector3 color4 { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public bool EnableLightWave { get; set; }
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
        drawInfo.SetPixelShader(typeof(IsolationEffect).GUID, PixelOptions.None);
        this.drawInfo = drawInfo;
    }
}