using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SharpDX;
using SharpDX.Direct3D11;
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

    public class DisplayControl : Control
    {
        private SharpDX.DXGI.Factory5 mGraphicsFactory;
        private SharpDX.DXGI.SwapChain4 mGraphicsDisplay;

        private Device5 mRenderDevice;
        private DeviceContext4 mRenderContext;
        private RenderTargetView1 mRenderTarget;

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
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            Utils.Dispose(ref mRenderTarget);
            Utils.Dispose(ref mRenderContext);
            Utils.Dispose(ref mGraphicsDisplay);
            Utils.Dispose(ref mRenderDevice);
            Utils.Dispose(ref mGraphicsFactory);

            base.OnHandleDestroyed(e);
        }

        public void Render()
        {
            if (mRenderContext is null) return;

            mRenderContext.OutputMerger.SetRenderTargets(mRenderTarget);
            mRenderContext.ClearRenderTargetView(mRenderTarget, Color.CornflowerBlue);

            mGraphicsDisplay.Present(0, SharpDX.DXGI.PresentFlags.None);
        }
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
