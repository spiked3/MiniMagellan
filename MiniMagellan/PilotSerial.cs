using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MiniMagellan
{
    public class PilotSerial : SerialPort
    {
        public delegate void ReceiveHandler(dynamic json);
        public event ReceiveHandler OnReceive;

        int recvIdx = 0;
        byte[] recvbuf = new byte[4096];

        public PilotSerial(string portName, int baudRate) : base(portName, baudRate)
        {
        }

        public void Start()
        {
            byte[] buffer = new byte[1024];
            Action kickoffRead = null;
            kickoffRead = delegate
            {
                BaseStream.BeginRead(buffer, 0, buffer.Length, delegate (IAsyncResult ar)
                {
                    try
                    {
                        int actualLength = BaseStream.EndRead(ar);
                        byte[] received = new byte[actualLength];
                        Buffer.BlockCopy(buffer, 0, received, 0, actualLength);
                        AppSerialDataEvent(received);
                    }
                    catch (Exception exc)
                    {
                        //System.Diagnostics.Debugger.Break();
                        Trace.WriteLine(exc.Message);
                    }
                    if (IsOpen)
                        kickoffRead();  // re-trigger
                }, null);
            };

            kickoffRead();
        }

        void AppSerialDataEvent(byte[] received)
        {
            foreach (var b in received)
            {
                if (b == '\n')
                {
                    recvbuf[recvIdx] = 0;
                    string line = Encoding.UTF8.GetString(recvbuf, 0, recvIdx).Trim(new char[] { '\r', '\n' });
                    if (line.StartsWith("//"))      // deprecated
                    {
                        Trace.WriteLine(line);
                        recvIdx = 0; //Trace.WriteLine("com->" + line,"+");
                    }
                    else
                    {
                        try
                        {
                            if (OnReceive != null)
                                OnReceive(JsonConvert.DeserializeObject(line));
                        }
                        catch (Exception)
                        {
                            //System.Diagnostics.Debugger.Break();
                            //throw;
                        }
                    }
                    recvIdx = 0;
                }
                else
                    recvbuf[recvIdx++] = (byte)b;

                if (recvIdx >= recvbuf.Length)
                    System.Diagnostics.Debugger.Break();    // overflow +++ atempt recovery
            }
        }
    }
}
