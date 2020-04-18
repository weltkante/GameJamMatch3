using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
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
using Windows.Storage.Streams;
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

            display.Game = new GameState(10, 10);
        }

        private void renderTimer_Tick(object sender, EventArgs e)
        {
            display.Render();
        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            await InitAudioEngine();

            StartGame(0);
        }

        private void display_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !display.Game.IsInAnimation && !display.Game.IsGameOver)
            {
                var (x, y) = display.MouseToGrid(e.X, e.Y);
                if (display.Game.IsValid(x, y))
                {
                    ref var cell = ref display.Game[x, y];
                    if (!cell.IsEmpty && display.Game.HighlightCount >= 3 && display.ResolveBlocks())
                        PlaySoundEffect("sound.wav", 0.5);
                    else
                        PlaySoundEffect("reject.wav", 0.5);
                }
            }
        }

        private void display_MouseMove(object sender, MouseEventArgs e)
        {
            var (x, y) = display.MouseToGrid(e.X, e.Y);
            display.Game.Highlight(x, y);
        }

        private void display_MouseLeave(object sender, EventArgs e)
        {
            display.Game.ClearHighlight();
        }

        private void btnNewGame_Click(object sender, EventArgs e)
        {
            StartGame((int)DateTime.UtcNow.Ticks);
        }

        private void StartGame(int seed)
        {
            display.Game.StartGame(seed);

            mMusicControl?.Cancel();
            mMusicControl = new CancellationTokenSource();
            PlayMusicLoop("music.mp3", 0.4, mMusicControl.Token);
        }

        internal static byte[] LoadFile(string filename)
        {
            using (var stream = typeof(MainForm).Assembly.GetManifestResourceStream(typeof(MainForm), "Resources." + filename))
            {
                if (stream is null)
                    throw new FileNotFoundException();

                var buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);
                return buffer;
            }
        }

        #region Audio

        private async Task InitAudioEngine()
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
            var source = MediaSource.CreateFromStream(await LoadFile(filename).AsRandomAccessStream(), "");
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
            var source = MediaSource.CreateFromStream(await LoadFile(filename).AsRandomAccessStream(), "");
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
        private SharpDX.DirectWrite.TextFormat2 mTextFormat2;

        private SharpDX.Direct3D11.Buffer mConstantBuffer;
        private SharpDX.Direct3D11.Buffer mVertexBuffer;
        private SharpDX.Direct3D11.Buffer mIndexBuffer;

        private Vertex[] mVertexBufferArray;
        private int[] mIndexBufferArray;

        public GameState Game { get; set; }
        private DateTime mAnimationStart;

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

            using (var format = new SharpDX.DirectWrite.TextFormat(mTextFactory, "Segoe UI", 36.0f * kPointToPixel))
            {
                mTextFormat2 = format.QueryInterface<SharpDX.DirectWrite.TextFormat2>();
                mTextFormat2.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
                mTextFormat2.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
            }

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
            var samplerDesc = SamplerStateDescription.Default();
            samplerDesc.Filter = Filter.MinMagMipPoint;
            mRenderContext.PixelShader.SetSampler(0, new SamplerState(mRenderDevice, samplerDesc));
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
            var blendDesc = BlendStateDescription1.Default();
            blendDesc.IndependentBlendEnable = true;
            blendDesc.RenderTarget[0].SourceAlphaBlend = BlendOption.SourceAlpha;
            blendDesc.RenderTarget[0].SourceBlend = BlendOption.SourceAlpha;
            blendDesc.RenderTarget[0].DestinationAlphaBlend = BlendOption.InverseSourceAlpha;
            blendDesc.RenderTarget[0].DestinationBlend = BlendOption.InverseSourceAlpha;
            blendDesc.RenderTarget[0].IsBlendEnabled = true;
            mRenderContext.OutputMerger.BlendState = new BlendState1(mRenderDevice, blendDesc);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            Utils.Dispose(ref mDrawingBrush);
            Utils.Dispose(ref mDrawingContext);
            Utils.Dispose(ref mDrawingFactory);

            Utils.Dispose(ref mTextFormat);
            Utils.Dispose(ref mTextFormat2);
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
            using (var bitmap = new Bitmap(new MemoryStream(MainForm.LoadFile(filename))))
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

        private void WriteQuad(ref int i, ref int j, int x, float y, int w, int h, int sx, int sy, int sw, int sh, Color color)
        {
            mIndexBufferArray[i++] = j + 0;
            mIndexBufferArray[i++] = j + 1;
            mIndexBufferArray[i++] = j + 2;
            mIndexBufferArray[i++] = j + 2;
            mIndexBufferArray[i++] = j + 1;
            mIndexBufferArray[i++] = j + 3;

            mVertexBufferArray[j++] = new Vertex(new Vector4(x, y, 0, 1), new Vector2(sx / 512.0f, sy / 512.0f), color);
            mVertexBufferArray[j++] = new Vertex(new Vector4(x + w, y, 0, 1), new Vector2((sx + sw) / 512.0f, sy / 512.0f), color);
            mVertexBufferArray[j++] = new Vertex(new Vector4(x, y + h, 0, 1), new Vector2(sx / 512.0f, (sy + sh) / 512.0f), color);
            mVertexBufferArray[j++] = new Vertex(new Vector4(x + w, y + h, 0, 1), new Vector2((sx + sw) / 512.0f, (sy + sh) / 512.0f), color);
        }

        private const int kSpriteSize = 32;
        private const int kMargin = 2;
        private const int kOffsetY = 30;
        private int kOffsetX => (ClientSize.Width - Game.Width * (kSpriteSize + kMargin) + kMargin) / 2;

        public (int, int) MouseToGrid(int x, int y)
        {
            return ((x - kOffsetX) / (kSpriteSize + kMargin), (y - kOffsetY) / (kSpriteSize + kMargin));
        }

        public void Render()
        {
            if (mRenderContext is null) return;

            var matrix = Matrix.OrthoOffCenterLH(0, ClientSize.Width, ClientSize.Height, 0, -1, +1);

            int i = 0, j = 0;

            if (Game != null)
            {
                float animationProgress = 0;
                if (Game.IsInAnimation)
                {
                    const float kBlockDuration = 0.2f;
                    animationProgress = (float)(DateTime.UtcNow - mAnimationStart).TotalSeconds / kBlockDuration;
                    if (animationProgress > Game.AnimationLength)
                    {
                        animationProgress = 0;
                        Game.ClearAnimation();
                    }
                }

                bool showHighlight = (Game.HighlightCount >= 3) && !Game.IsInAnimation && !Game.IsGameOver;

                for (int iy = 0; iy < Game.Height; iy++)
                {
                    for (int ix = 0; ix < Game.Width; ix++)
                    {
                        ref var cell = ref Game[ix, iy];
                        var value = cell.Value;
                        if (value == 0)
                            continue;

                        float cellAnimationFactor = 0;
                        if (cell.DropCount > 0)
                            cellAnimationFactor = Math.Max(0, cell.DropCount - animationProgress);

                        WriteQuad(ref i, ref j,
                            ix * (kSpriteSize + kMargin) + kOffsetX,
                            iy * (kSpriteSize + kMargin) + kOffsetY - cellAnimationFactor * (kSpriteSize + kMargin),
                            kSpriteSize,
                            kSpriteSize,
                            (value - 1) * 16, 0, 16, 16,
                            showHighlight && cell.Highlight ? new Color(1.0f, 1.0f, 1.0f, 0.7f) : Color.Transparent);
                    }
                }
            }

            mRenderContext.OutputMerger.SetRenderTargets(mRenderTarget);
            mRenderContext.ClearRenderTargetView(mRenderTarget, Color.CornflowerBlue);
            mRenderContext.UpdateSubresource(ref matrix, mConstantBuffer);
            mRenderContext.UpdateSubresource(mVertexBufferArray, mVertexBuffer);
            mRenderContext.UpdateSubresource(mIndexBufferArray, mIndexBuffer);
            mRenderContext.DrawIndexed(i, 0, 0);

            var time = (int)((Game.GameOverTime ?? DateTime.UtcNow) - Game.StartTime).TotalSeconds;

            mDrawingContext.BeginDraw();
            mDrawingBrush.Color = Color.Black;
            mDrawingContext.DrawText($"Time: {time}\t\tScore: {Game.Score}", mTextFormat, new RawRectangleF(6, 3, float.PositiveInfinity, float.PositiveInfinity), mDrawingBrush);
            mDrawingBrush.Color = Color.White;
            mDrawingContext.DrawText($"Time: {time}\t\tScore: {Game.Score}", mTextFormat, new RawRectangleF(5, 2, float.PositiveInfinity, float.PositiveInfinity), mDrawingBrush);

            if (Game != null && !Game.IsInAnimation && Game.IsGameOver)
            {
                RawColor4 bg = Color.IndianRed;
                bg.A = 0.9f;
                mDrawingBrush.Color = bg;
                const int fw = 300;
                const int fh = 100;
                int x0 = (ClientSize.Width - fw) / 2;
                int y0 = (ClientSize.Height - fh - kOffsetY) / 2 + kOffsetY;
                var frame = new SharpDX.Direct2D1.RoundedRectangle();
                frame.Rect = new RawRectangleF(x0, y0, x0 + fw, y0 + fh);
                frame.RadiusX = 8;
                frame.RadiusY = 8;
                mDrawingContext.FillRoundedRectangle(ref frame, mDrawingBrush);
                mDrawingBrush.Color = Color.Black;
                mDrawingContext.DrawText("Game Over", mTextFormat2, frame.Rect, mDrawingBrush);
                mDrawingBrush.Color = Color.White;
                frame.Rect.Left -= 2;
                frame.Rect.Top -= 2;
                frame.Rect.Right -= 2;
                frame.Rect.Bottom -= 2;
                mDrawingContext.DrawText("Game Over", mTextFormat2, frame.Rect, mDrawingBrush);
            }

            mDrawingContext.EndDraw();

            mGraphicsDisplay.Present(0, SharpDX.DXGI.PresentFlags.None);
        }

        public bool ResolveBlocks()
        {
            if (!Game.ResolveBlocks())
                return false;

            mAnimationStart = DateTime.UtcNow;
            return true;
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
    float4 texcol = texture0.Sample(sampler0, input.texpos);
    return float4(texcol.rgb * (1 - input.color.a) + input.color.rgb * input.color.a, texcol.a);
}

";
    }

    public static class Utils
    {
        public static void Break()
        {
            if (Debugger.IsAttached)
                Debugger.Break();
        }

        public static void BreakIf(bool condition)
        {
            if (condition)
                Break();
        }

        public static void Assert(bool condition)
        {
            if (!condition)
            {
                Break();
                throw new InvalidOperationException();
            }
        }

        public static void Dispose<T>(ref T field) where T : IDisposable
        {
            field?.Dispose();
            field = default;
        }

        public static async Task<IRandomAccessStream> AsRandomAccessStream(this byte[] buffer)
        {
            var result = new InMemoryRandomAccessStream();
            await result.WriteAsync(buffer.AsBuffer());
            return result;
        }
    }
}
