using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;
using Windows.Media.Audio;
using Windows.Media.Core;
using Windows.Media.Render;
using Color = SharpDX.Color;

namespace Match3
{
    public partial class MainForm : Form
    {
        private AudioGraph mAudioGraph;
        private AudioDeviceOutputNode mAudioOutput;
        private CancellationTokenSource mMusicControl;

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        public MainForm()
        {
            InitializeComponent();
        }

        private void renderTimer_Tick(object sender, EventArgs e)
        {
            display.Render();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            InitAudioEngine();
        }

        private void display_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                PlaySoundEffect("sound.wav", 0.2);

            if (e.Button == MouseButtons.Right)
            {
                if (mMusicControl is null)
                {
                    mMusicControl = new CancellationTokenSource();
                    PlayMusicLoop("music.mp3", 0.4, mMusicControl.Token);
                }
                else
                {
                    mMusicControl.Cancel();
                }
            }
        }

        internal static Stream LoadStream(string filename)
        {
            return typeof(MainForm).Assembly.GetManifestResourceStream(typeof(MainForm), "Resources." + filename) ?? throw new FileNotFoundException();
        }

        #region Audio

        private async void InitAudioEngine()
        {
            var graphResult = await AudioGraph.CreateAsync(new AudioGraphSettings(AudioRenderCategory.Media));
            if (graphResult.Status != AudioGraphCreationStatus.Success) return;
            mAudioGraph = graphResult.Graph;
            var audioResult = await mAudioGraph.CreateDeviceOutputNodeAsync();
            if (audioResult.Status != AudioDeviceNodeCreationStatus.Success) return;
            mAudioOutput = audioResult.DeviceOutputNode;
            mAudioGraph.Start();
        }

        private async void PlayMusicLoop(string filename, double gain = 1, CancellationToken ct = default)
        {
            var source = MediaSource.CreateFromStream(LoadStream(filename).AsRandomAccessStream(), "");
            var result = await mAudioGraph.CreateMediaSourceAudioInputNodeAsync(source);
            if (result.Status != MediaSourceAudioInputNodeCreationStatus.Success) return;
            var node = result.Node;
            node.LoopCount = null;
            node.AddOutgoingConnection(mAudioOutput, gain);
            ct.Register(() =>
            {
                node.Stop();
                node.RemoveOutgoingConnection(mAudioOutput);
                node.Dispose();
            }, true);
        }

        private async void PlaySoundEffect(string filename, double gain = 1)
        {
            var source = MediaSource.CreateFromStream(LoadStream(filename).AsRandomAccessStream(), "");
            var result = await mAudioGraph.CreateMediaSourceAudioInputNodeAsync(source);
            if (result.Status != MediaSourceAudioInputNodeCreationStatus.Success) return;
            var fileNode = result.Node;
            fileNode.MediaSourceCompleted += HandleSoundEffectCompleted;
            fileNode.AddOutgoingConnection(mAudioOutput, gain);
        }

        private void HandleSoundEffectCompleted(MediaSourceAudioInputNode sender, object args)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => HandleSoundEffectCompleted(sender, args)));
                return;
            }

            sender.RemoveOutgoingConnection(mAudioOutput);
            sender.MediaSourceCompleted -= HandleSoundEffectCompleted;
            sender.Dispose();
        }

        #endregion
    }

    public struct Vertex
    {
        public RawVector4 Position;
        public RawVector2 TexturePosition;
        public RawColorBGRA Color;

        public Vertex(RawVector4 position, RawVector2 texpos, RawColorBGRA color)
        {
            Position = position;
            TexturePosition = texpos;
            Color = color;
        }
    }

    public class DisplayControl : Control
    {
        private const float kPointToPixel = 96.0f / 72.0f;

        private SharpDX.DXGI.Factory5 mGraphicsFactory;
        private SharpDX.DXGI.SwapChain4 mGraphicsDisplay;

        private Device5 mRenderDevice;
        private DeviceContext4 mRenderContext;
        private RenderTargetView1 mRenderTarget;

        private SharpDX.Direct2D1.Factory6 mDrawingFactory;
        private SharpDX.Direct2D1.DeviceContext5 mDrawingContext;
        private SharpDX.Direct2D1.SolidColorBrush mDrawingBrush;

        private SharpDX.DirectWrite.Factory5 mTextFactory;
        private SharpDX.DirectWrite.TextFormat2 mTextFormat;

        private SharpDX.Direct3D11.Buffer mConstantBuffer;
        private SharpDX.Direct3D11.Buffer mVertexBuffer;
        private SharpDX.Direct3D11.Buffer mIndexBuffer;

        private Vertex[] mVertexBufferArray;
        private int[] mIndexBufferArray;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (DesignMode) return;

            using (var factory = new SharpDX.DXGI.Factory2(Debugger.IsAttached))
                mGraphicsFactory = factory.QueryInterface<SharpDX.DXGI.Factory5>();

            using (var device = new Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport | (Debugger.IsAttached ? DeviceCreationFlags.Debug : DeviceCreationFlags.None)))
                mRenderDevice = device.QueryInterface<Device5>();

            var swapChainDesc = new SharpDX.DXGI.SwapChainDescription1();
            swapChainDesc.BufferCount = 2;
            swapChainDesc.Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm;
            swapChainDesc.SampleDescription.Count = 1;
            swapChainDesc.SwapEffect = SharpDX.DXGI.SwapEffect.FlipDiscard;
            swapChainDesc.Usage = SharpDX.DXGI.Usage.RenderTargetOutput;
            swapChainDesc.Flags = SharpDX.DXGI.SwapChainFlags.AllowTearing;
            using (var display = new SharpDX.DXGI.SwapChain1(mGraphicsFactory, mRenderDevice, Handle, ref swapChainDesc))
                mGraphicsDisplay = display.QueryInterface<SharpDX.DXGI.SwapChain4>();

            using (var target = mGraphicsDisplay.GetBackBuffer<Texture2D>(0))
                mRenderTarget = new RenderTargetView1(mRenderDevice, target);

            mRenderContext = mRenderDevice.ImmediateContext.QueryInterface<DeviceContext4>();

            using (var factory = new SharpDX.Direct2D1.Factory2(SharpDX.Direct2D1.FactoryType.SingleThreaded, Debugger.IsAttached ? SharpDX.Direct2D1.DebugLevel.Warning : SharpDX.Direct2D1.DebugLevel.None))
                mDrawingFactory = factory.QueryInterface<SharpDX.Direct2D1.Factory6>();

            var drawingDesc = new SharpDX.Direct2D1.RenderTargetProperties(new SharpDX.Direct2D1.PixelFormat(SharpDX.DXGI.Format.Unknown, SharpDX.Direct2D1.AlphaMode.Premultiplied));
            using (var context = new SharpDX.Direct2D1.RenderTarget(mDrawingFactory, mGraphicsDisplay.GetBackBuffer<SharpDX.DXGI.Surface>(0), drawingDesc))
                mDrawingContext = context.QueryInterface<SharpDX.Direct2D1.DeviceContext5>();

            mDrawingBrush = new SharpDX.Direct2D1.SolidColorBrush(mDrawingContext, Color.Black);

            using (var factory = new SharpDX.DirectWrite.Factory1(SharpDX.DirectWrite.FactoryType.Shared))
                mTextFactory = factory.QueryInterface<SharpDX.DirectWrite.Factory5>();

            using (var format = new SharpDX.DirectWrite.TextFormat(mTextFactory, "Segoe UI", 12.0f * kPointToPixel))
                mTextFormat = format.QueryInterface<SharpDX.DirectWrite.TextFormat2>();

            mVertexBufferArray = new Vertex[1 << 20];
            mIndexBufferArray = new int[1 << 20];

            mConstantBuffer = new SharpDX.Direct3D11.Buffer(mRenderDevice, new BufferDescription(4 * 4 * sizeof(float), BindFlags.ConstantBuffer, ResourceUsage.Default));
            mVertexBuffer = SharpDX.Direct3D11.Buffer.Create(mRenderDevice, BindFlags.VertexBuffer, mVertexBufferArray);
            mIndexBuffer = SharpDX.Direct3D11.Buffer.Create(mRenderDevice, BindFlags.IndexBuffer, mIndexBufferArray);

            var vs = SharpDX.D3DCompiler.ShaderBytecode.Compile(ShaderText.kVertexShader, "VS", "vs_4_0");
            mRenderContext.VertexShader.Set(new VertexShader(mRenderDevice, vs));
            mRenderContext.VertexShader.SetConstantBuffer(0, mConstantBuffer);

            var ps = SharpDX.D3DCompiler.ShaderBytecode.Compile(ShaderText.kPixelShader, "PS", "ps_4_0");
            mRenderContext.PixelShader.Set(new PixelShader(mRenderDevice, ps));
            mRenderContext.PixelShader.SetSampler(0, new SamplerState(mRenderDevice, SamplerStateDescription.Default()));
            mRenderContext.PixelShader.SetShaderResource(0, LoadTexture("texture.png"));

            mRenderContext.InputAssembler.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            mRenderContext.InputAssembler.SetIndexBuffer(mIndexBuffer, SharpDX.DXGI.Format.R32_UInt, 0);
            mRenderContext.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(mVertexBuffer, Marshal.SizeOf<Vertex>(), 0));
            mRenderContext.InputAssembler.InputLayout = new InputLayout(mRenderDevice, SharpDX.D3DCompiler.ShaderSignature.GetInputSignature(vs), new InputElement[]
            {
                new InputElement("position", 0, SharpDX.DXGI.Format.R32G32B32A32_Float, 0),
                new InputElement("texcoord", 0, SharpDX.DXGI.Format.R32G32_Float, 0),
                new InputElement("color", 0, SharpDX.DXGI.Format.B8G8R8A8_UNorm, 0),
            });

            mRenderContext.Rasterizer.SetViewport(0, 0, ClientSize.Width, ClientSize.Height);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            Utils.Dispose(ref mDrawingBrush);
            Utils.Dispose(ref mDrawingContext);
            Utils.Dispose(ref mDrawingFactory);

            Utils.Dispose(ref mTextFormat);
            Utils.Dispose(ref mTextFactory);

            Utils.Dispose(ref mConstantBuffer);
            Utils.Dispose(ref mVertexBuffer);
            Utils.Dispose(ref mIndexBuffer);
            Utils.Dispose(ref mRenderTarget);
            Utils.Dispose(ref mRenderContext);
            Utils.Dispose(ref mGraphicsDisplay);
            Utils.Dispose(ref mRenderDevice);
            Utils.Dispose(ref mGraphicsFactory);

            base.OnHandleDestroyed(e);
        }

        private ShaderResourceView LoadTexture(string filename)
        {
            using (var stream = MainForm.LoadStream(filename))
            using (var bitmap = new Bitmap(stream))
                return LoadTexture(bitmap);
        }

        private ShaderResourceView LoadTexture(Bitmap bitmap)
        {
            var width = bitmap.Width;
            var height = bitmap.Height;
            var info = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var desc = new Texture2DDescription();
                desc.Width = width;
                desc.Height = height;
                desc.MipLevels = 1;
                desc.ArraySize = 1;
                desc.Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm;
                desc.SampleDescription.Count = 1;
                desc.Usage = ResourceUsage.Immutable;
                desc.BindFlags = BindFlags.ShaderResource;
                desc.CpuAccessFlags = CpuAccessFlags.None;
                desc.OptionFlags = ResourceOptionFlags.None;

                var data = new DataRectangle();
                data.DataPointer = info.Scan0;
                data.Pitch = info.Stride;

                using (var texture = new Texture2D(mRenderDevice, desc, data))
                {
                    var view = new ShaderResourceViewDescription();
                    view.Format = desc.Format;
                    view.Dimension = SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D;
                    view.Texture2D.MipLevels = 1;
                    view.Texture2D.MostDetailedMip = 0;
                    return new ShaderResourceView(mRenderDevice, texture, view);
                }
            }
            finally
            {
                bitmap.UnlockBits(info);
            }
        }

        public void Render()
        {
            if (mRenderContext is null) return;

            var matrix = Matrix.OrthoOffCenterLH(0, ClientSize.Width, ClientSize.Height, 0, -1, +1);

            int j = 0;
            mVertexBufferArray[j++] = new Vertex(new Vector4(100, 100, 0, 1), new Vector2(0, 0), Color.White);
            mVertexBufferArray[j++] = new Vertex(new Vector4(200, 100, 0, 1), new Vector2(16.0f / 512.0f, 0), Color.White);
            mVertexBufferArray[j++] = new Vertex(new Vector4(100, 200, 0, 1), new Vector2(0, 16.0f / 512.0f), Color.White);
            mVertexBufferArray[j++] = new Vertex(new Vector4(200, 200, 0, 1), new Vector2(16.0f / 512.0f, 16.0f / 512.0f), Color.White);

            int i = 0;
            mIndexBufferArray[i++] = 0;
            mIndexBufferArray[i++] = 1;
            mIndexBufferArray[i++] = 2;
            mIndexBufferArray[i++] = 2;
            mIndexBufferArray[i++] = 1;
            mIndexBufferArray[i++] = 3;

            mRenderContext.OutputMerger.SetRenderTargets(mRenderTarget);
            mRenderContext.ClearRenderTargetView(mRenderTarget, Color.CornflowerBlue);
            mRenderContext.UpdateSubresource(ref matrix, mConstantBuffer);
            mRenderContext.UpdateSubresource(mVertexBufferArray, mVertexBuffer);
            mRenderContext.UpdateSubresource(mIndexBufferArray, mIndexBuffer);
            mRenderContext.DrawIndexed(i, 0, 0);

            mDrawingContext.BeginDraw();
            mDrawingBrush.Color = Color.Black;
            mDrawingContext.DrawText($"Hello World {DateTime.Now}", mTextFormat, new RawRectangleF(0, 0, float.PositiveInfinity, float.PositiveInfinity), mDrawingBrush);
            mDrawingContext.EndDraw();

            mGraphicsDisplay.Present(0, SharpDX.DXGI.PresentFlags.None);
        }
    }

    public static class ShaderText
    {
        private const string kVertexStructure = @"

struct VS_DATA
{
    float4 position : POSITION;
    float4 color : COLOR;
    float2 texpos : TEXCOORD0;
};

";

        private const string kPixelStructure = @"

struct PS_DATA
{
    float4 position : SV_POSITION;
    float4 color : COLOR;
    float2 texpos : TEXCOORD0;
};

";

        public const string kVertexShader = kVertexStructure + kPixelStructure + @"

float4x4 camera;

PS_DATA VS(VS_DATA input)
{
    PS_DATA output = (PS_DATA)0;

    output.position = mul(camera, input.position);
    output.color = input.color;
    output.texpos = input.texpos;

    return output;
};

";

        public const string kPixelShader = kPixelStructure + @"

Texture2D texture0;
SamplerState sampler0;

float4 PS(PS_DATA input) : SV_TARGET
{
    return texture0.Sample(sampler0, input.texpos) * input.color;
}

";
    }

    public static class Utils
    {
        public static void Dispose<T>(ref T field) where T : IDisposable
        {
            field?.Dispose();
            field = default;
        }
    }
}
