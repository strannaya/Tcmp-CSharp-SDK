﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using TapTrack.Tcmp.CommandFamilies;
using TapTrack.Tcmp.Communication.Exceptions;
using System.Linq;
using TapTrack.Tcmp.CommandFamilies.System;
using System.Threading;
using TapTrack.Tcmp.CommandFamilies.BasicNfc;
using TapTrack.Ndef;

namespace TapTrack.Tcmp.Communication
{
    public delegate void Callback(ResponseFrame frame, Exception e);

    /// <summary>
    /// The Driver class is used to communicate(send commands and receive data) with a Tappy device
    /// </summary>
    public class Driver
    {
        private Connection conn;
        private List<byte> buffer;

        private Callback responseCallback;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="protocol">Which protocol the driver should communicate over</param>
        public Driver(CommunicationProtocol protocol)
        {
            if (protocol == CommunicationProtocol.Usb)
            {
                conn = new UsbConnection();
            }

            buffer = new List<byte>();
            conn.DataReceived += new EventHandler(DataReceivedHandler);
        }

        private void DataReceivedHandler(object sender, EventArgs e)
        {
            Debug.WriteLine("Data is being recieve");
            if (!conn.IsOpen())
                responseCallback(null, new HardwareException("Connection to device is not open"));

            if (conn.Read(this.buffer) == 0)
                return;

            ResponseFrame resp;

            for (int i = 0; i < buffer.Count; i++)
            {
                resp = null;
                try
                {
                    if (buffer[i] == 0x7E)
                    {
                        resp = ExtractFrame(i);

                        if (resp != null)
                        {
                            buffer.RemoveRange(i, resp.Length + 5 + i);

                            Debug.WriteLine(string.Format("Command family: {0:X}, {1:X}\nResponse Code: {2:X}", resp.CommandFamily[0], resp.CommandFamily[1], resp.ResponseCode));
                            responseCallback?.Invoke(resp, null);
                            i -= 1;
                        }
                    }
                }
                catch (LcsException exc)
                {
                    responseCallback?.Invoke(null, new LcsException(this.buffer.ToArray(), "There is an error in the length bytes since Len1+Len0+Lcs != 0"));
                }
                catch (LackOfDataException exc)
                {
                    if (conn.Read(this.buffer) > 0)
                        i -= 1;
                }
                catch (HardwareException exc)
                {
                    responseCallback(null, new HardwareException("Connection to device is not open"));
                }
            }
        }

        private ResponseFrame ExtractFrame(int start)
        {
            if (buffer.Count - start < 10)
                return null;

            byte len1 = buffer[start + 1];
            byte len0 = buffer[start + 2];
            byte lcs = buffer[start + 3];

            int payLoadLength = len1 * 256 + len0 - 5;

            if (buffer.Count - start < len1 * 256 + len0 + 5)
                throw new LackOfDataException();

            if ((byte)(lcs + len1 + len0) != 0)
                throw new LcsException(this.buffer.ToArray());

            if (buffer[start + 9 + payLoadLength] != 0x7E)
                return null;

            byte[] frame = new byte[payLoadLength + 10];
            Array.Copy(buffer.ToArray(), start, frame, 0, frame.Length);

            ResponseFrame result = new ResponseFrame(frame);

            if (result.IsApplicationErrorFrame())
                result = new ApplicationErrorFrame(result.ToArray());

            return result;
        }

        /// <summary>
        /// Clears the contents of the driver buffer
        /// </summary>
        public void FlushBuffer()
        {
            this.buffer.Clear();
        }

        /// <summary>
        /// Connect to the first Tappy device the driver finds
        /// </summary>
        /// <returns>True if connection to a Tappy device was successful, false otherwise</returns>
        public bool AutoDetect()
        {
            bool success;
            foreach (string name in conn.GetAvailableDevices())
            {
                Command cmd = new Ping();
                AutoResetEvent receivedResp = new AutoResetEvent(false);
                success = false;

                conn.Connect(name);

                Callback resp = (ResponseFrame frame, Exception e) =>
                {
                    success = true;
                    receivedResp.Set();
                };

                SendCommand(cmd, resp);
                receivedResp.WaitOne(100);

                if (success)
                    return true;
            }
            return false;
        }

        public void ConfigurePlatform(Callback responseCallback)
        {
            Command readCommand = new DetectSingleTagUid(0, DetectTagSetting.Type2Type4AandMifare);

            SendCommand(readCommand, delegate (ResponseFrame frame, Exception e)
            {
                if (e != null){
                    responseCallback?.Invoke(null, e);
                    return;
                }

                Tag tag = new Tag(frame.Data);
                string uid = BitConverter.ToString(tag.UID).Replace("-", "");
                string url = $"https://members.taptrack.com/m?id={uid}";

                Command write = new WriteUri(0, false, new NdefUri(url));

                SendCommand(write, responseCallback);
            });
        }

        /// <summary>
        /// Get all TappyUSB connected to this machine
        /// </summary>
        /// <returns></returns>
        public string[] GetAvailableDevices()
        {
            return conn.GetAvailableDevices();
        }

        /// <summary>
        /// Send a command to the Tappy
        /// </summary>
        /// <param name="command">Command to send</param>
        /// <param name="responseCallback">Method to be called when a data is receieved or a error has occurred</param>
        public void SendCommand(Command command, Callback responseCallback = null)
        {
            CommandFrame frame = new CommandFrame(command);
            _Send(frame, responseCallback);
        }

        /// <summary>
        /// Send a command to the Tappy 
        /// </summary>
        /// <typeparam name="T">Type of command to send</typeparam>
        /// <param name="responseCallback">Method to be called when a data is receieved or a error has occurred</param>
        /// <param name="parameters">Parameters of the command</param>
        public void SendCommand<T>(Callback responseCallback, params object[] parameters) where T : Command
        {
            CommandFrame frame = new CommandFrame((Command)Activator.CreateInstance(typeof(T), parameters));
            _Send(frame, responseCallback);
        }

        /// <summary>
        /// Send a command with no response call back
        /// </summary>
        /// <typeparam name="T">Type of command to send</typeparam>
        /// <param name="parameters">Parameters of the command</param>
        public void SendCommand<T>(params object[] parameters) where T : Command
        {
            CommandFrame frame = new CommandFrame((Command)Activator.CreateInstance(typeof(T), parameters));
            _Send(frame);
        }

        private void _Send(CommandFrame frame, Callback responseCallback = null)
        {
            this.responseCallback = responseCallback;

            try
            {
                conn.Send(frame.ToArray());
            }
            catch (HardwareException e)
            {
                this.responseCallback?.Invoke(null, e);
            }
        }
    }
}
