using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinRT;

namespace Murmuration;

// Minimal D3D11 renderer hosted in a SwapChainPanel; clears the screen each frame.
public sealed unsafe class Renderer : IDisposable
{
    private static readonly Guid SwapChainPanelNativeIid = new("63aad0b8-7c24-40ff-85a8-640d944cc325");

    private readonly SwapChainPanel _panel;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGISwapChain1 _swapChain;
    private ID3D11RenderTargetView _rtv;
    private uint _width;
    private uint _height;

    public Renderer(SwapChainPanel panel)
    {
        _panel = panel;

        D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
            out _device!,
            out _context!).CheckError();

        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        using var factory = adapter.GetParent<IDXGIFactory2>();

        _width = (uint)Math.Max(1, (int)(panel.ActualWidth * panel.CompositionScaleX));
        _height = (uint)Math.Max(1, (int)(panel.ActualHeight * panel.CompositionScaleY));

        var desc = new SwapChainDescription1
        {
            Width = _width,
            Height = _height,
            Format = Format.B8G8R8A8_UNorm,
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            SampleDescription = new SampleDescription(1, 0),
            SwapEffect = SwapEffect.FlipSequential,
            Scaling = Scaling.Stretch,
            AlphaMode = AlphaMode.Ignore,
        };
        _swapChain = factory.CreateSwapChainForComposition(_device, desc);

        AttachToPanel();
        _rtv = CreateRenderTargetView();

        panel.SizeChanged += OnSizeChanged;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering;
    }

    private void AttachToPanel()
    {
        IntPtr unknown = MarshalInspectable<SwapChainPanel>.FromManaged(_panel);
        try
        {
            Guid iid = SwapChainPanelNativeIid;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out IntPtr native));
            try
            {
                // ISwapChainPanelNative::SetSwapChain is vtable slot 3.
                var vtbl = *(void***)native;
                var setSwapChain = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)vtbl[3];
                Marshal.ThrowExceptionForHR(setSwapChain(native, _swapChain.NativePointer));
            }
            finally
            {
                Marshal.Release(native);
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    private ID3D11RenderTargetView CreateRenderTargetView()
    {
        using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        return _device.CreateRenderTargetView(backBuffer);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        uint w = (uint)Math.Max(1, (int)(e.NewSize.Width * _panel.CompositionScaleX));
        uint h = (uint)Math.Max(1, (int)(e.NewSize.Height * _panel.CompositionScaleY));
        if (w == _width && h == _height) return;

        _width = w;
        _height = h;
        _context.ClearState();
        _rtv.Dispose();
        _swapChain.ResizeBuffers(0, w, h, Format.Unknown, SwapChainFlags.None).CheckError();
        _rtv = CreateRenderTargetView();
    }

    private void OnRendering(object? sender, object e)
    {
        _context.OMSetRenderTargets(_rtv);
        _context.RSSetViewport(0, 0, _width, _height);
        _context.ClearRenderTargetView(_rtv, new Color4(0.05f, 0.08f, 0.15f, 1f));
        _swapChain.Present(1, PresentFlags.None).CheckError();
    }

    public void Dispose()
    {
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
        _panel.SizeChanged -= OnSizeChanged;
        _rtv.Dispose();
        _swapChain.Dispose();
        _context.Dispose();
        _device.Dispose();
    }
}
