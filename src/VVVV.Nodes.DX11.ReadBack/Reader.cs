using FeralTic.DX11.Resources;
using SlimDX.Direct3D11;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Permissions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using SlimDX;

namespace VVVV.Nodes.DX11.ReadBack
{
	public class Reader : IDisposable
	{
		public enum ImageFileFormat { Bmp = 1, Jpeg = 2, Png = 3, Tiff = 4, Gif = 5, Hdp = 6, Dds = 128, Tga = 129 }

		public bool Completed { get; private set; } = false;
		public bool Success { get; private set; } = false;

		bool FCompletedBang = false;
		public bool CompletedBang
		{
			get
			{
				if (FCompletedBang)
				{
					FCompletedBang = false;
					return true;
				}
				else
				{
					return false;
				}
			}
			set
			{
				FCompletedBang = value;
			}
		}


        byte[] FResult = new byte[0];
        public byte[] Result
        {
            get
            {
                byte[] copy;
                lock (FResult)
                {
                    copy = FResult;
                }
                return copy;
            }
            set
            {
                lock (FResult)
                {
                    FResult = value;
                }
            }
        }

        string FStatus = "";
		public string Status
		{
			get
			{
				string copy;
				lock (FStatus)
				{
					copy = FStatus;
				}
				return copy;
			}
			set
			{
				lock (FStatus)
				{
					FStatus = value;
				}
			}
		}

		string FFilename = "";
		public string Filename
		{
			get
			{
				string copy;
				lock (FFilename)
				{
					copy = FFilename;
				}
				return copy;
			}
			set
			{
				lock (FFilename)
				{
					FFilename = value;
				}
			}
		}

		protected class Assets : IDisposable
		{
			public DeviceContext RenderDeviceContext = null;
			public Device RenderDevice = null;

            public BufferDescription Description;
            public BufferDescription SharedDescription;

            public SlimDX.Direct3D11.Buffer SharedBufferOnRenderDevice;

			public Device ReadbackDevice = null;
			public DeviceContext ReadbackDeviceContext = null;
            

            public SlimDX.Direct3D11.Buffer SharedBufferOnReadbackDevice;
            public DX11StagingStructuredBuffer StagingBufferOnReadbackDevice;

			public void Dispose()
			{
				if (StagingBufferOnReadbackDevice != null)
				{
                    StagingBufferOnReadbackDevice.Dispose();
                    StagingBufferOnReadbackDevice = null;
				}

				if (SharedBufferOnReadbackDevice != null)
				{
                    SharedBufferOnReadbackDevice.Dispose();
                    SharedBufferOnReadbackDevice = null;
				}

				if (ReadbackDevice != null)
				{
					ReadbackDeviceContext.Dispose();
					ReadbackDevice.Dispose();

					ReadbackDevice = null;
					ReadbackDeviceContext = null;
				}

				if (SharedBufferOnRenderDevice != null)
				{
                    SharedBufferOnRenderDevice.Dispose();
                    SharedBufferOnRenderDevice = null;
				}

				Description = new BufferDescription();
				SharedDescription = new BufferDescription();
			}
		}
		protected Assets FAssets = new Assets();

		Thread FThread;

		public Reader()
		{
		}

		public virtual void Recycle()
		{
			this.Success = false;
			this.Completed = false;
			this.FCompletedBang = false;
			lock (FStatus)
			{
				this.FStatus = "";
			}
			lock(FFilename)
			{
				this.FFilename = "";
			}
		}

		public /*async*/ void Read(SlimDX.DXGI.Adapter adapter, DX11RWStructuredBuffer buffer)
		{
			//If we're being re-used, called recycle
			if(this.Completed)
			{
				this.Recycle();
			}

			try
			{
                //log the render device context
                if (this.FAssets.RenderDeviceContext != buffer.SRV.Device.ImmediateContext) 
                   {
					this.FAssets.Dispose();

					this.FAssets.RenderDeviceContext = buffer.SRV.Device.ImmediateContext;
					this.FAssets.RenderDevice = buffer.SRV.Device;
				}

                //allocate buffer
                if (FAssets.Description != buffer.Buffer.Description)
                {
                    this.FAssets.Dispose();

                    FAssets.Description = buffer.Buffer.Description;
                    FAssets.SharedDescription = buffer.Buffer.Description;
                    {
                        FAssets.SharedDescription.Usage = ResourceUsage.Default;
                        FAssets.SharedDescription.OptionFlags |= ResourceOptionFlags.Shared;
                    }
                    FAssets.SharedBufferOnRenderDevice = new SlimDX.Direct3D11.Buffer(this.FAssets.RenderDevice, this.FAssets.Description);
                }

                //copy input buffer to shared buffer
                {
                    this.FAssets.RenderDeviceContext.CopyResource(buffer.Buffer, FAssets.SharedBufferOnRenderDevice);
				}

				//create a device/context for saving
				if (FAssets.ReadbackDevice == null)
				{

#if DEBUG
					FAssets.ReadbackDevice = new SlimDX.Direct3D11.Device(adapter, DeviceCreationFlags.Debug);
#else
						FAssets.SaveDevice = new SlimDX.Direct3D11.Device(adapter);
#endif
					FAssets.ReadbackDeviceContext = FAssets.ReadbackDevice.ImmediateContext;
				}

				//flush the immediate context to ensure that our shared buffer is available
				{
					var queryDescription = new QueryDescription(QueryType.Event, QueryFlags.None);
					var query = new Query(FAssets.RenderDevice, queryDescription);
					FAssets.RenderDeviceContext.Flush();
					FAssets.RenderDeviceContext.End(query);
					var result = FAssets.RenderDeviceContext.GetData<bool>(query);
					while (!result)
					{
						result = FAssets.RenderDeviceContext.GetData<bool>(query);
						Thread.Sleep(1);
					}
				}

                //create the buffer on saving device
                if (FAssets.SharedBufferOnReadbackDevice == null)
                {
                    SlimDX.DXGI.Resource r = new SlimDX.DXGI.Resource(FAssets.SharedBufferOnRenderDevice);

                    FAssets.SharedBufferOnReadbackDevice = FAssets.ReadbackDevice.OpenSharedResource<SlimDX.Direct3D11.Buffer>(r.SharedHandle);

                    var stagingBufferDescription = buffer.Buffer.Description;
					{
                        stagingBufferDescription.Usage = ResourceUsage.Default;
					}

					FAssets.StagingBufferOnReadbackDevice = new  DX11StagingStructuredBuffer(FAssets.ReadbackDevice, buffer.ElementCount, buffer.Stride);

				}

				//copy the shared texture into a staging texture (actually for our purpose, we will not use an actual staging texture since we use SaveTextureToFile which handles staging for us)
				FAssets.ReadbackDeviceContext.CopyResource(FAssets.SharedBufferOnReadbackDevice, FAssets.StagingBufferOnReadbackDevice.Buffer);
                
                // Task Version:
                //DataStream ds = FAssets.StagingBufferOnReadbackDevice.MapForRead(FAssets.ReadbackDeviceContext);
                //byte[] target = new byte[ds.Length];
                //int t = await Task.Run(() => ds.ReadAsync(target, 0, (int)ds.Length));

                this.FThread = new Thread(() =>
				{
					try
					{

						//perform read back in thread
						try
						{

                            // TODOOOOOOOOOOOOOOOOOOOOOOO

                            DataStream ds = FAssets.StagingBufferOnReadbackDevice.MapForRead(FAssets.ReadbackDeviceContext);

                            byte[] target = new byte[ds.Length];

                            ds.Read(target, 0, (int)ds.Length);

                            //int t = await Task.Run(() => ds.ReadAsync(target, 0, (int)ds.Length));

                            lock (FResult)
                            {
                                this.FResult = new byte[] { 0x00 };
                            }

                            FAssets.StagingBufferOnReadbackDevice.UnMap(FAssets.ReadbackDeviceContext);
                        }
                        catch (Exception e)
						{
							throw (new Exception("SaveTextureToFile failed : " + e.Message));
						}

						this.CompletedBang = true;
						this.Completed = true;
						this.Status = "OK";
						this.Success = true;
					}
					catch (Exception e)
					{
						this.Completed = true;
						this.Status = e.Message;
						this.Success = false;
					}
				});

				this.FThread.Name = "DX11 readBack";
				this.FThread.Start();
			}

			catch (Exception e)
			{
				this.Completed = true;
				this.Success = false;
				this.Status = e.Message;
			}
		}

		public void Dispose()
		{
			if (!this.Completed)
			{
				if (this.FThread != null)
				{
					if (!this.FThread.Join(1000))
					{
						this.FThread.Abort();
					}
					this.FThread = null;
				}
				this.Completed = true;

				FAssets.Dispose();
			}
		}
	};
}
