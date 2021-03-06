﻿using System;
using System.IO;
using System.Net;
using System.Text;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;

using Sulakore.Protocol;
using Sulakore.Habbo.Headers;
using Sulakore.Protocol.Encryption;

namespace Sulakore.Communication
{
    public class HConnection : IHConnection, IDisposable
    {
        public event EventHandler<EventArgs> Connected;
        protected virtual void OnConnected(EventArgs e)
        {
            EventHandler<EventArgs> handler = Connected;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<EventArgs> Disconnected;
        protected virtual void OnDisconnected(EventArgs e)
        {
            EventHandler<EventArgs> handler = Disconnected;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<DataToEventArgs> DataToClient;
        protected virtual void OnDataToClient(DataToEventArgs e)
        {
            EventHandler<DataToEventArgs> handler = DataToClient;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<DataToEventArgs> DataToServer;
        protected virtual void OnDataToServer(DataToEventArgs e)
        {
            EventHandler<DataToEventArgs> handler = DataToServer;
            if (handler != null) handler(this, e);
        }

        private TcpListenerEx _htcpExt;
        private Socket _clientS, _serverS;
        private int _toClientS, _toServerS, _socketCount;
        private byte[] _clientB, _serverB, _clientC, _serverC;
        private bool _hasOfficialSocket, _disconnectAllowed, _grabHeaders;

        private static readonly string _hostsPath;
        private readonly object _resetHostLock, _disconnectLock, _sendToClientLock, _sendToServerLock;

        public int Port { get; private set; }
        public string Host { get; private set; }
        public string[] Addresses { get; private set; }

        private readonly HTriggers _triggers;
        public HTriggers Triggers
        {
            get { return _triggers; }
        }

        private readonly HFilters _filters;
        public HFilters Filters
        {
            get { return _filters; }
        }

        private Rc4 _serverDecrypt;
        public Rc4 IncomingDecrypt
        {
            get { return _serverDecrypt; }
            set
            {
                if ((_serverDecrypt = value) != null)
                    IsIncomingEncrypted = false;
            }
        }
        public Rc4 IncomingEncrypt { get; set; }

        private Rc4 _clientDecrypt;
        public Rc4 OutgoingDecrypt
        {
            get { return _clientDecrypt; }
            set
            {
                if ((_clientDecrypt = value) != null)
                    IsOutgoingEncrypted = false;
            }
        }
        public Rc4 OutgoingEncrypt { get; set; }

        public bool IsConnected
        {
            get { return _serverS != null && _serverS.Connected; }
        }
        public bool IsOutgoingEncrypted { get; private set; }
        public bool IsIncomingEncrypted { get; private set; }

        public int SocketSkip { get; set; }

        static HConnection()
        {
            _hostsPath = Environment.GetFolderPath(Environment.SpecialFolder.System) + "\\drivers\\etc\\hosts";
        }
        public HConnection()
        {
            _filters = new HFilters();
            _triggers = new HTriggers(false);

            _resetHostLock = new object();
            _disconnectLock = new object();
            _sendToClientLock = new object();
            _sendToServerLock = new object();

        }

        public void Connect(bool hostsWrite, string host, int port)
        {
            Host = host;
            Port = port;
            ResetHost();

            Addresses = Dns.GetHostAddresses(host)
                .Select(ip => ip.ToString()).ToArray();

            if (hostsWrite)
            {
                EnforceHost();
                string[] lines = File.ReadAllLines(_hostsPath);
                if (!Array.Exists(lines, ip => Addresses.Contains(ip)))
                {
                    List<string> gameIPs = Addresses.ToList();

                    if (!gameIPs.Contains(Host))
                        gameIPs.Add(Host);

                    string mapping = string.Format("127.0.0.1\t\t{{0}}\t\t#{0}[{{1}}/{1}]", Host, gameIPs.Count);
                    File.AppendAllLines(_hostsPath, gameIPs.Select(ip => string.Format(mapping, ip, gameIPs.IndexOf(ip) + 1)));
                }
            }

            (_htcpExt = new TcpListenerEx(IPAddress.Any, Port)).Start();
            _htcpExt.BeginAcceptSocket(SocketAccepted, null);
            _disconnectAllowed = true;
        }

        public int SendToClient(byte[] data)
        {
            if (_clientS == null || !_clientS.Connected) return 0;
            lock (_sendToClientLock)
            {
                if (IncomingEncrypt != null)
                    data = IncomingEncrypt.SafeParse(data);

                return _clientS.Send(data);
            }
        }
        public int SendToClient(ushort header, params object[] chunks)
        {
            return SendToClient(HMessage.Construct(header, chunks));
        }

        public int SendToServer(byte[] data)
        {
            if (!IsConnected) return 0;

            lock (_sendToServerLock)
            {
                if (OutgoingEncrypt != null)
                    data = OutgoingEncrypt.SafeParse(data);

                return _serverS.Send(data);
            }
        }
        public int SendToServer(ushort header, params object[] chunks)
        {
            return SendToServer(HMessage.Construct(header, chunks));
        }

        public void ResetHost()
        {
            lock (_resetHostLock)
            {
                if (Host == null || !File.Exists(_hostsPath)) return;
                string[] hostsL = File.ReadAllLines(_hostsPath).Where(line => !line.Contains(Host) && !line.StartsWith("127.0.0.1")).ToArray();
                File.WriteAllLines(_hostsPath, hostsL);
            }
        }
        public void Disconnect()
        {
            if (!_disconnectAllowed) return;
            _disconnectAllowed = false;

            lock (_disconnectLock)
            {
                if (_clientS != null)
                {
                    _clientS.Shutdown(SocketShutdown.Both);
                    _clientS.Close();
                    _clientS = null;
                }
                if (_serverS != null)
                {
                    _serverS.Shutdown(SocketShutdown.Both);
                    _serverS.Close();
                    _serverS = null;
                }

                ResetHost();
                if (_htcpExt != null)
                {
                    _htcpExt.Stop();
                    _htcpExt = null;
                }
                _toClientS = _toServerS = _socketCount = 0;
                _clientB = _serverB = _clientC = _serverC = null;
                OutgoingEncrypt = OutgoingDecrypt = IncomingEncrypt = IncomingDecrypt = null;
                _hasOfficialSocket = _grabHeaders = IsOutgoingEncrypted = IsIncomingEncrypted = false;

                OnDisconnected(EventArgs.Empty);
            }
        }
        public void EnforceHost()
        {
            if (!File.Exists(_hostsPath))
                File.Create(_hostsPath).Close();

            File.SetAttributes(_hostsPath, FileAttributes.Normal);
        }

        private void SocketAccepted(IAsyncResult iAr)
        {
            try
            {
                if (++_socketCount == SocketSkip)
                {
                    _htcpExt.EndAcceptSocket(iAr).Close();
                    _htcpExt.BeginAcceptSocket(SocketAccepted, null);
                }
                else
                {
                    _clientS = _htcpExt.EndAcceptSocket(iAr);
                    _serverS = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    _serverS.BeginConnect(Addresses[0], Port, ConnectedToServer, null);
                }
            }
            catch
            {
                if (_htcpExt != null && _htcpExt.Active)
                {
                    if (_htcpExt.Pending()) _htcpExt.EndAcceptSocket(iAr).Close();
                    _htcpExt.BeginAcceptSocket(SocketAccepted, null);
                }
                else Disconnect();
            }
        }
        private void ConnectedToServer(IAsyncResult iAr)
        {
            _serverS.EndConnect(iAr);

            _grabHeaders = true;
            _serverB = new byte[1024];
            _clientB = new byte[512];
            ReadClientData();
            ReadServerData();
        }

        private void ReadClientData()
        {
            if (_clientS != null && _clientS.Connected)
                _clientS.BeginReceive(_clientB, 0, _clientB.Length, SocketFlags.None, DataFromClient, null);
        }
        private void DataFromClient(IAsyncResult iAr)
        {
            try
            {
                if (_clientS == null) return;
                int length = _clientS.EndReceive(iAr);
                if (length < 1) { Disconnect(); return; }

                byte[] data = new byte[length];
                Buffer.BlockCopy(_clientB, 0, data, 0, length);

                if (!_hasOfficialSocket)
                {
                    if (_hasOfficialSocket = (BigEndian.DecypherShort(data, 4) == 4000))
                    {
                        ResetHost();
                        _htcpExt.Stop();
                        _htcpExt = null;

                        OnConnected(EventArgs.Empty);
                    }
                    else
                    {
                        SendToServer(data);
                        return;
                    }
                }

                if (OutgoingDecrypt != null)
                    OutgoingDecrypt.Parse(data);

                if (_toServerS == 3)
                {
                    int dLength = data.Length >= 6 ? BigEndian.DecypherInt(data) : 0;
                    IsOutgoingEncrypted = (dLength != data.Length - 4);
                }
                IList<byte[]> chunks = ByteUtils.Split(ref _clientC, data, !IsOutgoingEncrypted);

                foreach (byte[] chunk in chunks)
                    ProcessOutgoing(chunk);

                ReadClientData();
            }
            catch { Disconnect(); }
        }
        private void ProcessOutgoing(byte[] data)
        {
            ++_toServerS;
            if (!IsOutgoingEncrypted)
                Task.Factory.StartNew(() =>
                    Triggers.ProcessOutgoing(data), TaskCreationOptions.LongRunning);

            if (DataToServer == null) SendToServer(data);
            else
            {
                var e = new DataToEventArgs(data, HDestination.Server, _toServerS, Filters);
                try { OnDataToServer(e); }
                catch { e.Cancel = true; }
                finally
                {
                    if (e.Cancel) SendToServer(data = e.Packet.ToBytes());
                    else if (!e.IsBlocked) SendToServer(data = e.Replacement.ToBytes());
                }
            }

            if (_grabHeaders)
            {
                switch (_toServerS)
                {
                    case 2: Outgoing.Global.InitiateHandshake = BigEndian.DecypherShort(data, 4); break;
                    case 3: Outgoing.Global.ClientPublicKey = BigEndian.DecypherShort(data, 4); break;
                    case 4: Outgoing.Global.FlashClientUrl = BigEndian.DecypherShort(data, 4); break;
                    case 6: Outgoing.Global.ClientSsoTicket = BigEndian.DecypherShort(data, 4); break;
                    case 7: _grabHeaders = false; break;
                }
            }
        }

        private void ReadServerData()
        {
            if (IsConnected)
                _serverS.BeginReceive(_serverB, 0, _serverB.Length, SocketFlags.None, DataFromServer, null);
        }
        private void DataFromServer(IAsyncResult iAr)
        {
            try
            {
                if (_serverS == null) return;
                int length = _serverS.EndReceive(iAr);
                if (length < 1) { Disconnect(); return; }

                byte[] data = new byte[length];
                Buffer.BlockCopy(_serverB, 0, data, 0, length);

                if (!_hasOfficialSocket)
                {
                    string possiblePolicyResponse = Encoding.UTF8.GetString(data);
                    if (possiblePolicyResponse.Contains("</cross-domain-policy>"))
                    {
                        possiblePolicyResponse = possiblePolicyResponse
                            .Replace("</cross-domain-policy>",
                            "<allow-access-from domain=\"*\" to-ports=\"*\"/>\r\n</cross-domain-policy>");
                    }
                    SendToClient(Encoding.UTF8.GetBytes(possiblePolicyResponse));
                    _htcpExt.BeginAcceptSocket(SocketAccepted, null);
                    return;
                }

                if (IncomingDecrypt != null)
                    IncomingDecrypt.Parse(data);

                if (_toClientS == 2)
                {
                    length = data.Length >= 6 ? BigEndian.DecypherInt(data) : 0;
                    IsIncomingEncrypted = (length != data.Length - 4);
                }
                IList<byte[]> chunks = ByteUtils.Split(ref _serverC, data, !IsIncomingEncrypted);

                foreach (byte[] chunk in chunks)
                    ProcessIncoming(chunk);

                ReadServerData();
            }
            catch { Disconnect(); }
        }
        private void ProcessIncoming(byte[] data)
        {
            ++_toClientS;
            if (!IsIncomingEncrypted)
                Task.Factory.StartNew(() =>
                    Triggers.ProcessIncoming(data), TaskCreationOptions.LongRunning);

            if (DataToClient == null) SendToClient(data);
            else
            {
                var e = new DataToEventArgs(data, HDestination.Client, _toClientS, Filters);
                try { OnDataToClient(e); }
                catch { e.Cancel = true; }
                finally
                {
                    if (e.Cancel) SendToClient(e.Packet.ToBytes());
                    else if (!e.IsBlocked) SendToClient(e.Replacement.ToBytes());
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        protected virtual void Dispose(bool disposing)
        {
            SKore.Unsubscribe(ref Connected);
            SKore.Unsubscribe(ref DataToClient);
            SKore.Unsubscribe(ref DataToServer);
            SKore.Unsubscribe(ref Disconnected);

            if (disposing)
            {
                Disconnect();

                Host = null;
                Addresses = null;
                Port = SocketSkip = 0;

                Triggers.Dispose();
            }
        }
    }
}