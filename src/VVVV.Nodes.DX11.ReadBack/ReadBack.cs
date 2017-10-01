using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VVVV.Core.Logging;
using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using System.ComponentModel.Composition;
using VVVV.PluginInterfaces.V2.NonGeneric;

using VVVV.Utils.VMath;

using SlimDX;
using SlimDX.Direct3D11;

using FeralTic.DX11.Resources;
using FeralTic.DX11;
using VVVV.DX11;



namespace VVVV.Nodes.DX11.ReadBack
{
    #region PluginInfo
    [PluginInfo(Name = "ReadBack",
                Category = "DX11.Buffer",
                Version = "Async",
                Help = "",
                Tags = "",
                Author = "vux, sebl",
                AutoEvaluate = true)]
    #endregion PluginInfo
    public class ReadBackAsyncNode : IPluginEvaluate, IDX11ResourceDataRetriever, IPartImportsSatisfiedNotification
    {
        [Config("Layout")]
        protected IDiffSpread<string> FLayout;

        [Input("Input", AutoValidate = false)]
        protected Pin<DX11Resource<IDX11ReadableStructureBuffer>> FInput;

        [Input("Enabled", DefaultValue = 1, IsSingle = true)]
        protected ISpread<bool> doRead;

        List<IIOContainer> outspreads = new List<IIOContainer>();
        string[] layout;

        [Import()]
        protected IPluginHost FHost;

        [Import()]
        protected IIOFactory FIO;

        public DX11RenderContext AssignedContext
        {
            get;
            set;
        }

        public event DX11RenderRequestDelegate RenderRequest;


        #region IPluginEvaluate Members
        public async void Evaluate(int SpreadMax)
        {
            if (this.doRead.SliceCount == 0)
                return;

            if (this.FInput.IsConnected && this.doRead[0])
            {
                this.FInput.Sync();

                if (this.RenderRequest != null) { this.RenderRequest(this, this.FHost); }

                IDX11ReadableStructureBuffer b = this.FInput[0][this.AssignedContext];

                if (b != null)
                {


                    DX11StagingStructuredBuffer staging = new DX11StagingStructuredBuffer(this.AssignedContext.Device
                        , b.ElementCount, b.Stride);

                    this.AssignedContext.CurrentDeviceContext.CopyResource(b.Buffer, staging.Buffer);

                    foreach (IIOContainer sp in this.outspreads)
                    {
                        ISpread s = (ISpread)sp.RawIOObject;
                        s.SliceCount = b.ElementCount;
                    }

                    DataStream ds = staging.MapForRead(this.AssignedContext.CurrentDeviceContext);

                    //for (int i = 0; i < b.ElementCount; i++)
                    //{
                    int cnt = 0;
                    foreach (string lay in layout)
                    {
                        switch (lay)
                        {
                            case "float":
                                ISpread<float> spr = (ISpread<float>)this.outspreads[cnt].RawIOObject;
                                byte[] target = new byte[ds.Length];

                                int t = await Task.Run(() => ds.ReadAsync(target, 0, (int)ds.Length));

                                for (int k = 0; k < ds.Length / 4; k++)
                                {
                                    spr[k] = System.BitConverter.ToSingle(target, k * 4);
                                }
                                break;
                            case "float2":
                                ISpread<Vector2> spr2 = (ISpread<Vector2>)this.outspreads[cnt].RawIOObject;
                                byte[] target2 = new byte[ds.Length];

                                int t2 = await Task.Run(() => ds.ReadAsync(target2, 0, (int)ds.Length));
                                for (int k = 0; k < (ds.Length / 4) / 2; k++)
                                {
                                    spr2[k] = new Vector2(BitConverter.ToSingle(target2, k * 8),
                                                          BitConverter.ToSingle(target2, k * 8 + 4));
                                }
                                break;
                            case "float3":
                                ISpread<Vector3> spr3 = (ISpread<Vector3>)this.outspreads[cnt].RawIOObject;
                                byte[] target3 = new byte[ds.Length];

                                int t3 = await Task.Run(() => ds.ReadAsync(target3, 0, (int)ds.Length));

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

                                int t4 = await Task.Run(() => ds.ReadAsync(target4, 0, (int)ds.Length));

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

                                int tm = await Task.Run(() => ds.ReadAsync(targetm, 0, (int)ds.Length));

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
                                int ti = await Task.Run(() => ds.ReadAsync(targeti, 0, (int)ds.Length));

                                for (int k = 0; k < ds.Length / 4; k++)
                                {
                                    spri[k] = System.BitConverter.ToInt32(targeti, k * 4);
                                }
                                break;
                            case "uint":
                                ISpread<uint> sprui = (ISpread<uint>)this.outspreads[cnt].RawIOObject;
                                byte[] targetui = new byte[ds.Length];
                                int tui = await Task.Run(() => ds.ReadAsync(targetui, 0, (int)ds.Length));

                                for (int k = 0; k < ds.Length / 4; k++)
                                {
                                    sprui[k] = System.BitConverter.ToUInt32(targetui, k * 4);
                                }
                                break;
                            case "uint2":
                                ISpread<Vector2> sprui2 = (ISpread<Vector2>)this.outspreads[cnt].RawIOObject;
                                byte[] targetui2 = new byte[ds.Length];

                                int tui2 = await Task.Run(() => ds.ReadAsync(targetui2, 0, (int)ds.Length));
                                for (int k = 0; k < (ds.Length / 4) / 2; k++)
                                {
                                    sprui2[k] = new Vector2(BitConverter.ToUInt32(targetui2, k * 8),
                                                            BitConverter.ToUInt32(targetui2, k * 8 + 4));
                                }
                                break;
                            case "uint3":
                                ISpread<Vector3> sprui3 = (ISpread<Vector3>)this.outspreads[cnt].RawIOObject;
                                byte[] targetui3 = new byte[ds.Length];

                                int tui3 = await Task.Run(() => ds.ReadAsync(targetui3, 0, (int)ds.Length));

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

                    //}

                    staging.UnMap(this.AssignedContext.CurrentDeviceContext);

                    staging.Dispose();
                }
                else
                {
                    foreach (IIOContainer sp in this.outspreads)
                    {
                        ISpread s = (ISpread)sp.RawIOObject;
                        s.SliceCount = 0;
                    }
                }
            }
            else
            {
                foreach (IIOContainer sp in this.outspreads)
                {
                    ISpread s = (ISpread)sp.RawIOObject;
                    s.SliceCount = 0;
                }
            }
        }


        #endregion

        public void OnImportsSatisfied()
        {
            this.FLayout.Changed += new SpreadChangedEventHander<string>(FLayout_Changed);
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
    }


}
