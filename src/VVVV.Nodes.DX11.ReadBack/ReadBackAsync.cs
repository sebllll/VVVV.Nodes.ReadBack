#region usings
using System;
using System.ComponentModel.Composition;
using System.Runtime.InteropServices;

using SlimDX;
//using SlimDX.Direct3D9;
using VVVV.Core.Logging;
using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.PluginInterfaces.V2.NonGeneric;
using VVVV.PluginInterfaces.V2.EX9;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;
//using VVVV.Utils.SlimDX;

using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing.Imaging;
using System.Diagnostics;
using FeralTic.DX11.Resources;
using FeralTic.DX11;
using VVVV.DX11;
using System.Security.Permissions;
using FeralTic.DX11;
using SlimDX.Direct3D11;

#endregion usings

namespace VVVV.Nodes.DX11.ReadBack
{
	#region PluginInfo
	[PluginInfo(Name = "ReadBack",
				Category = "Buffer",
				Version = "Async",
				Help = "Write textures to disk in background",
				Tags = "",
				AutoEvaluate = true)]
	#endregion PluginInfo
	public class ReadBackAsync : IPluginEvaluate, IDX11ResourceDataRetriever, IPartImportsSatisfiedNotification, IDisposable
	{
		class ReaderAsync : Reader
		{
			public static int DefaultZombieSurvivalAge = 2;

			int FZombieSurvivalAge = DefaultZombieSurvivalAge;
			/// <summary>
			/// For Async Writers, this is the number of frames a completed saver will stay alive for allowing it to be recycled
			/// </summary>
			public int ZombieSurvivalAge {
				get
				{
					return FZombieSurvivalAge;
				}
				set
				{
					if(this.FramesUntilDead == this.ZombieSurvivalAge)
					{
						this.FramesUntilDead = value;
					}
					this.ZombieSurvivalAge = value;
				}
			}

			public int FramesUntilDead = DefaultZombieSurvivalAge;

			public bool IsAvailable(DeviceContext Context, BufferDescription Description)
			{
				if (!this.Completed)
				{
					return false;
				}
				return this.FAssets.RenderDeviceContext == Context && this.FAssets.Description == Description;
			}

			public override void Recycle()
			{
				FramesUntilDead = ZombieSurvivalAge;
				base.Recycle();
			}
		}

        #region fields & pins
#pragma warning disable 0649

        [Config("Layout")]
        protected IDiffSpread<string> FLayout;

        [Input("Input", AutoValidate = false)]
        protected Pin<DX11Resource<IDX11ReadableStructureBuffer>> FInput;

		[Input("Max Saver Count", DefaultValue = 8, IsSingle = true)]
		ISpread<int> FInMaxSavers;        

        [Input("Zombie Survival Age", DefaultValue = 2, IsSingle = true)]
		ISpread<int> FConfigZombieSurvivalAge;

        [Input("Enabled", DefaultValue = 1, IsSingle = true)]
        protected ISpread<bool> doRead;

		[Output("Success")]
		ISpread<bool> FOutSuccess;

		[Output("Status")]
		ISpread<string> FOutStatus;

		[Output("Queue Size")]
		ISpread<int> FOutQueueSize;

		[Import()]
		protected IPluginHost FHost;

		[Import]
		public ILogger FLogger;

		[Import]
		IHDEHost FHDEHost;

        [Import()]
        protected IIOFactory FIO;

        List<IIOContainer> outspreads = new List<IIOContainer>();
        string[] layout;

        List<ReaderAsync> FReaders = new List<ReaderAsync>();

#pragma warning restore 0649
		#endregion fields & pins

		// import host and hand it to base constructor
		[ImportingConstructor()]
		public ReadBackAsync(IPluginHost host)
		{
		}

        public void OnImportsSatisfied()
        {
            this.FLayout.Changed += new SpreadChangedEventHander<string>(FLayout_Changed);
        }

        public int QueueSize
		{
			get
			{
				int queueSize = 0;
				foreach(var reader in FReaders)
				{
					if(!reader.Completed)
					{
						queueSize++;
					}
				}
				return queueSize;
			}
		}

		void TrimReaders()
		{
			foreach (var reader in FReaders)
			{
				if (reader.Completed)
				{
					reader.FramesUntilDead--;
					if (reader.FramesUntilDead < 0)
					{
						reader.Dispose();
					}
				}
			}
			FReaders.RemoveAll(reader => reader.FramesUntilDead < 0);
		}

        void FLayout_Changed(IDiffSpread<string> spread)
        {
            foreach (IIOContainer sp in this.outspreads)
            {
                sp.Dispose();
            }
            this.outspreads.Clear();

            layout = spread[0].Split(",".ToCharArray());

            int id = 1;

            foreach (string lay in layout)
            {
                OutputAttribute attr = new OutputAttribute("Output " + id.ToString());
                IIOContainer container = null;
                switch (lay)
                {
                    case "float":
                        container = this.FIO.CreateIOContainer<ISpread<float>>(attr);
                        break;
                    case "float2":
                        container = this.FIO.CreateIOContainer<ISpread<Vector2>>(attr);
                        break;
                    case "float3":
                        container = this.FIO.CreateIOContainer<ISpread<Vector3>>(attr);
                        break;
                    case "float4":
                        container = this.FIO.CreateIOContainer<ISpread<Vector4>>(attr);
                        break;
                    case "float4x4":
                        container = this.FIO.CreateIOContainer<ISpread<Matrix4x4>>(attr);
                        break;
                    case "int":
                        container = this.FIO.CreateIOContainer<ISpread<int>>(attr);
                        break;
                    case "uint":
                        container = this.FIO.CreateIOContainer<ISpread<uint>>(attr);
                        break;
                    case "uint2":
                        //attr.AsInt = true;
                        container = this.FIO.CreateIOContainer<ISpread<Vector2>>(attr);
                        break;
                    case "uint3":
                        //attr.AsInt = true;
                        container = this.FIO.CreateIOContainer<ISpread<Vector3>>(attr);
                        break;
                }

                if (container != null) { this.outspreads.Add(container); id++; }
            }
        }

        public void Evaluate(int SpreadMax)
		{
			//Check our GPU connection is setup
			try
			{
				if (FInput.PluginIO.IsConnected)
				{
					if (this.RenderRequest != null) { this.RenderRequest(this, this.FHost); }
					if (this.AssignedContext == null) { return; }
				}
			}
			catch (Exception e)
			{
				FLogger.Log(e);
				FOutStatus.SliceCount = 1;
				FOutStatus[0] = e.Message;
				return;
			}

			FOutStatus.SliceCount = 0;
			FOutSuccess.SliceCount = 0;

			//perform the calls
			for (int i = 0; i < SpreadMax; i++)
			{
				if (doRead[i])
				{
                    this.FInput.Sync();

                    DX11RWStructuredBuffer inputBuffer = (DX11RWStructuredBuffer)this.FInput[0][this.AssignedContext];

                    if (inputBuffer != null)
                    {

                        try
                        {
                            ReaderAsync reader = null;

                            while (reader == null)
                            {
                                //attempt to recycle an existing saver
                                foreach (var existingReader in FReaders)
                                {
                                    if (existingReader.IsAvailable(AssignedContext.Device.ImmediateContext, FInput[i][AssignedContext].Buffer.Description))
                                    {
                                        reader = existingReader;
                                        break;
                                    }
                                }

                                //make a reader if none available (only if we haven't exceeded max count)
                                if (reader == null && FReaders.Count < FInMaxSavers[0])
                                {
                                    reader = new ReaderAsync();
                                    //reader.ZombieSurvivalAge = FConfigZombieSurvivalAge[0]; // crashes :(
                                    FReaders.Add(reader);
                                }

                                if (reader == null)
                                {
                                    //kill off expired readers (e.g. on other devices that we can't use)
                                    TrimReaders();
                                    Thread.Sleep(1);
                                }
                            }

                            reader.Read(AssignedContext.Adapter, inputBuffer);
                        }
                        catch (Exception e)
                        {
                            FOutStatus.Add(e.Message);
                            FOutSuccess.Add(false);
                        }
                    }
				}
			}

			//read out the status of anything which completed this frame
			{
				foreach(var reader in FReaders)
				{
					if(reader.CompletedBang)
					{
                        // TODO: get values back from thread here
                        int elementcountcount =  reader.Result.Length;

                        foreach (IIOContainer sp in this.outspreads)
                        {
                            ISpread s = (ISpread)sp.RawIOObject;
                            s.SliceCount = elementcountcount;
                        }

                        var ds = reader.Result;

                        int cnt = 0;
                        foreach (string lay in layout)
                        {
                            switch (lay)
                            {
                                case "float":
                                    ISpread<float> spr = (ISpread<float>)this.outspreads[cnt].RawIOObject;
                                    byte[] target = new byte[ds.Length];

                                    //int t = await Task.Run(() => ds.ReadAsync(target, 0, (int)ds.Length));

                                    for (int k = 0; k < ds.Length / 4; k++)
                                    {
                                        spr[k] = System.BitConverter.ToSingle(target, k * 4);
                                    }
                                    break;
                                case "float2":
                                    ISpread<Vector2> spr2 = (ISpread<Vector2>)this.outspreads[cnt].RawIOObject;
                                    byte[] target2 = new byte[ds.Length];

                                    //int t2 = await Task.Run(() => ds.ReadAsync(target2, 0, (int)ds.Length));
                                    for (int k = 0; k < (ds.Length / 4) / 2; k++)
                                    {
                                        spr2[k] = new Vector2(BitConverter.ToSingle(target2, k * 8),
                                                              BitConverter.ToSingle(target2, k * 8 + 4));
                                    }
                                    break;
                                case "float3":
                                    ISpread<Vector3> spr3 = (ISpread<Vector3>)this.outspreads[cnt].RawIOObject;
                                    byte[] target3 = new byte[ds.Length];

                                    //int t3 = await Task.Run(() => ds.ReadAsync(target3, 0, (int)ds.Length));

                                    for (int k = 0; k < (ds.Length / 4) / 3; k++)
                                    {
                                        spr3[k] = new Vector3(BitConverter.ToSingle(target3, k * 12),
                                                              BitConverter.ToSingle(target3, k * 12 + 4),
                                                              BitConverter.ToSingle(target3, k * 12 + 8));
                                    }
                                    break;
                                case "float4":
                                    ISpread<Vector4> spr4 = (ISpread<Vector4>)this.outspreads[cnt].RawIOObject;
                                    byte[] target4 = new byte[ds.Length];

                                    //int t4 = await Task.Run(() => ds.ReadAsync(target4, 0, (int)ds.Length));

                                    for (int k = 0; k < (ds.Length / 4) / 4; k++)
                                    {
                                        spr4[k] = new Vector4(BitConverter.ToSingle(target4, k * 16),
                                                              BitConverter.ToSingle(target4, k * 16 + 4),
                                                              BitConverter.ToSingle(target4, k * 16 + 8),
                                                              BitConverter.ToSingle(target4, k * 16 + 12));
                                    }
                                    break;
                                case "float4x4":
                                    ISpread<Matrix4x4> sprm = (ISpread<Matrix4x4>)this.outspreads[cnt].RawIOObject;
                                    byte[] targetm = new byte[ds.Length];

                                    //int tm = await Task.Run(() => ds.ReadAsync(targetm, 0, (int)ds.Length));

                                    for (int k = 0; k < (ds.Length / 4) / 16; k++)
                                    {
                                        sprm[k] = new Matrix4x4(new Vector4D(BitConverter.ToSingle(targetm, k * 64),
                                                                        BitConverter.ToSingle(targetm, k * 64 + 4),
                                                                        BitConverter.ToSingle(targetm, k * 64 + 8),
                                                                        BitConverter.ToSingle(targetm, k * 64 + 12)),
                                                                new Vector4D(BitConverter.ToSingle(targetm, k * 64 + 16),
                                                                        BitConverter.ToSingle(targetm, k * 64 + 20),
                                                                        BitConverter.ToSingle(targetm, k * 64 + 24),
                                                                        BitConverter.ToSingle(targetm, k * 64 + 28)),
                                                                new Vector4D(BitConverter.ToSingle(targetm, k * 64 + 32),
                                                                        BitConverter.ToSingle(targetm, k * 64 + 36),
                                                                        BitConverter.ToSingle(targetm, k * 64 + 40),
                                                                        BitConverter.ToSingle(targetm, k * 64 + 44)),
                                                                new Vector4D(BitConverter.ToSingle(targetm, k * 64 + 48),
                                                                        BitConverter.ToSingle(targetm, k * 64 + 52),
                                                                        BitConverter.ToSingle(targetm, k * 64 + 56),
                                                                        BitConverter.ToSingle(targetm, k * 64 + 60))
                                                                        );
                                    }
                                    break;
                                case "int":
                                    ISpread<int> spri = (ISpread<int>)this.outspreads[cnt].RawIOObject;
                                    byte[] targeti = new byte[ds.Length];
                                    //int ti = await Task.Run(() => ds.ReadAsync(targeti, 0, (int)ds.Length));

                                    for (int k = 0; k < ds.Length / 4; k++)
                                    {
                                        spri[k] = System.BitConverter.ToInt32(targeti, k * 4);
                                    }
                                    break;
                                case "uint":
                                    ISpread<uint> sprui = (ISpread<uint>)this.outspreads[cnt].RawIOObject;
                                    byte[] targetui = new byte[ds.Length];
                                    //int tui = await Task.Run(() => ds.ReadAsync(targetui, 0, (int)ds.Length));

                                    for (int k = 0; k < ds.Length / 4; k++)
                                    {
                                        sprui[k] = System.BitConverter.ToUInt32(targetui, k * 4);
                                    }
                                    break;
                                case "uint2":
                                    ISpread<Vector2> sprui2 = (ISpread<Vector2>)this.outspreads[cnt].RawIOObject;
                                    byte[] targetui2 = new byte[ds.Length];

                                    //int tui2 = await Task.Run(() => ds.ReadAsync(targetui2, 0, (int)ds.Length));
                                    for (int k = 0; k < (ds.Length / 4) / 2; k++)
                                    {
                                        sprui2[k] = new Vector2(BitConverter.ToUInt32(targetui2, k * 8),
                                                                BitConverter.ToUInt32(targetui2, k * 8 + 4));
                                    }
                                    break;
                                case "uint3":
                                    ISpread<Vector3> sprui3 = (ISpread<Vector3>)this.outspreads[cnt].RawIOObject;
                                    byte[] targetui3 = new byte[ds.Length];

                                    //int tui3 = await Task.Run(() => ds.ReadAsync(targetui3, 0, (int)ds.Length));

                                    for (int k = 0; k < (ds.Length / 4) / 3; k++)
                                    {
                                        sprui3[k] = new Vector3(BitConverter.ToUInt32(targetui3, k * 12),
                                                                BitConverter.ToUInt32(targetui3, k * 12 + 4),
                                                                BitConverter.ToUInt32(targetui3, k * 12 + 8));
                                    }

                                    break;
                            }

                            cnt++;
                        }


                        FOutStatus.Add(reader.Status);
						FOutSuccess.Add(reader.Success);
					}
				}
			}

			//delete expired zombies
			//TrimReaders();

			FOutQueueSize[0] = this.QueueSize;
		}

		public FeralTic.DX11.DX11RenderContext AssignedContext
		{
			get;
			set;
		}

		public event DX11RenderRequestDelegate RenderRequest;

		public void Dispose()
		{
			foreach (var reader in this.FReaders)
			{
				if (reader != null)
				{
					reader.Dispose();
				}
			}
			FReaders.Clear();
		}
	}
}