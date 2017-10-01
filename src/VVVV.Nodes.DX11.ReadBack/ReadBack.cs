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
                AutoEvaluate = true)]
    #endregion PluginInfo
    public class ReadBackAsync : IPluginEvaluate, IDX11ResourceDataRetriever, IPartImportsSatisfiedNotification
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
                                    //spr[i] = ds.Read<float>();
                                    //spr[i] = await GetDataAsyncFloat(ds);
                                    byte[] target = new byte[ds.Length];

                                    int t = await Task.Run(() => ds.ReadAsync(target, 0, (int)ds.Length));

                                    for (int k = 0; k < ds.Length / 4; k++)
                                    {
                                        spr[k] = System.BitConverter.ToSingle(target, k * 4);  
                                    }
                                    


                                    break;
                            case "float2":
                                ISpread<Vector2> spr2 = (ISpread<Vector2>)this.outspreads[cnt].RawIOObject;
                                //spr2[i] = ds.Read<Vector2>();

                                byte[] target2 = new byte[ds.Length];

                                int t2 = await Task.Run(() => ds.ReadAsync(target2, 0, (int)ds.Length));

                                for (int k = 0; k < (ds.Length / 4)/2; k++)
                                {
                                    spr2[k] = new Vector2(BitConverter.ToSingle(target2, k * 8), 
                                                          BitConverter.ToSingle(target2, k * 8 + 4));
                                }

                                break;
                            case "float3":
                                ISpread<Vector3> spr3 = (ISpread<Vector3>)this.outspreads[cnt].RawIOObject;
                                //spr3[i] = ds.Read<Vector3>();
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
                                //spr4[i] = ds.Read<Vector4>();
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
                                //case "float4x4":
                                //    ISpread<Matrix> sprm = (ISpread<Matrix>)this.outspreads[cnt].RawIOObject;
                                //    sprm[i] = ds.Read<Matrix>();
                                //    break;
                                //case "int":
                                //    ISpread<int> spri = (ISpread<int>)this.outspreads[cnt].RawIOObject;
                                //    spri[i] = ds.Read<int>();
                                //    break;
                                //case "uint":
                                //    ISpread<uint> sprui = (ISpread<uint>)this.outspreads[cnt].RawIOObject;
                                //    sprui[i] = ds.Read<uint>();
                                //    break;
                                //case "uint2":
                                //    ISpread<Vector2> sprui2 = (ISpread<Vector2>)this.outspreads[cnt].RawIOObject;
                                //    uint ui1 = ds.Read<uint>();
                                //    uint ui2 = ds.Read<uint>();
                                //    sprui2[i] = new Vector2(ui1, ui2);
                                //    break;
                                //case "uint3":
                                //    ISpread<Vector3> sprui3 = (ISpread<Vector3>)this.outspreads[cnt].RawIOObject;
                                //    uint ui31 = ds.Read<uint>();
                                //    uint ui32 = ds.Read<uint>();
                                //    uint ui33 = ds.Read<uint>();
                                //    sprui3[i] = new Vector3(ui31, ui32, ui33);
                                //    break;
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

        //private async Task<float> GetDataAsyncFloat(DataStream ds)
        //{
        //    byte[] target = new byte[ds.Length];

        //    float a = await ds.ReadAsync(target, 0, (int)ds.Length);

        //    return a;
        //}

        //private async Task<Vector2> GetDataAsyncVector2(DataStream ds)
        //{
        //    byte[] target = new byte[ds.Length];

        //    int a = await ds.ReadAsync(target, 0, (int)ds.Length);

        //    return a;
        //}
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
                        container = this.FIO.CreateIOContainer<ISpread<Matrix>>(attr);
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

    /*
{
    [Input("Input", AutoValidate = false)]
    protected Pin<DX11Resource<IDX11ReadableStructureBuffer>> FInput;

    [Input("Enabled", DefaultValue = 1, IsSingle = true)]
    protected ISpread<bool> doRead;

    [Output("Output")]
    protected ISpread<float> FOutput;

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

    public void Evaluate(int SpreadMax)
    {
        if (this.FInput.IsConnected && this.doRead[0])
        {
            this.FInput.Sync();

            if (this.RenderRequest != null) { this.RenderRequest(this, this.FHost); }

            IDX11ReadableStructureBuffer b = this.FInput[0][this.AssignedContext];

            bu

            byte[] target = new byte[b.Buffer.Dimension];
            float x = await ReadAsync(target, 0, (int)b.Length);

            this.AssignedContext.CurrentDeviceContext.GetData<float>();
        }
    }

    public void Dispose()
    {
        throw new NotImplementedException();
    }



}*/
}
