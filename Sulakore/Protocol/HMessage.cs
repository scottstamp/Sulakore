﻿using System;
using System.Text;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Sulakore.Protocol
{
    public class HMessage
    {
        private byte[] _toBytesCache;
        private string _toStringCache;
        private bool _suppressRead, _beganConstructing;

        private readonly List<byte> _body;
        private readonly List<object> _read, _written;

        private ushort _header;
        /// <summary>
        /// Gets or sets the header of the packet.
        /// </summary>
        public ushort Header
        {
            get { return _header; }
            set
            {
                if (IsCorrupted || _header == value) return;
                else
                {
                    _header = value;
                    ResetCache();
                }
            }
        }

        /// <summary>
        /// Gets or sets the position that determines where to begin the next read/write operation of an object.
        /// </summary>
        public int Position { get; set; }
        /// <summary>
        /// Gets or sets a value that indicates the destination for the packet.
        /// </summary>
        public HDestination Destination { get; set; }

        /// <summary>
        /// Gets the length of the packet, excluding the first four bytes(Length) of the original block of data.
        /// </summary>
        public int Length { get; private set; }
        /// <summary>
        /// Gets the block of data considered as the body of the packet, excluding the first six bytes(Length, Header) of the original block of data.
        /// </summary>
        public byte[] Body { get; private set; }
        /// <summary>
        /// Gets a value that determines whether given block of data is readable/writable.
        /// </summary>
        public bool IsCorrupted { get; private set; }

        private readonly ReadOnlyCollection<object> _chunksRead;
        /// <summary>
        /// Gets a read-only collection of objects that have been read from the packet so far.
        /// </summary>
        public ReadOnlyCollection<object> ChunksRead
        {
            get { return _chunksRead; }
        }

        private readonly ReadOnlyCollection<object> _chunksWritten;
        /// <summary>
        /// Gets a read-only collection of objects that have been written to the packet so far.
        /// </summary>
        public ReadOnlyCollection<object> ChunksWritten
        {
            get { return _chunksWritten; }
        }

        private HMessage()
        {
            _body = new List<byte>();

            _read = new List<object>();
            _written = new List<object>();

            _chunksRead = new ReadOnlyCollection<object>(_read);
            _chunksWritten = new ReadOnlyCollection<object>(_written);
        }
        public HMessage(byte[] data)
            : this(data, HDestination.Unknown)
        { }
        public HMessage(string packet)
            : this(ToBytes(packet), HDestination.Unknown)
        { }
        public HMessage(string packet, HDestination destination)
            : this(ToBytes(packet), destination)
        { }
        public HMessage(byte[] data, HDestination destination)
            : this()
        {
            if (data == null) throw new NullReferenceException();
            if (data.Length < 6) throw new Exception("Insufficient data, minimum length is '6'(Six). [Length{4}][Header{2}]");

            Destination = destination;
            IsCorrupted = (BigEndian.DecypherInt(data) != data.Length - 4);
            if (!IsCorrupted)
            {
                Header = BigEndian.DecypherShort(data, 4);

                _body.AddRange(data);
                _body.RemoveRange(0, 6);

                Reconstruct();
            }
            else
            {
                Length = data.Length;
                _toBytesCache = data;
            }
        }
        public HMessage(ushort header, params object[] chunks)
            : this(header, HDestination.Unknown, chunks)
        { }
        public HMessage(ushort header, HDestination destination, params object[] chunks)
            : this(Construct(header, chunks), destination)
        {
            _beganConstructing = true;
            AddToWritten(chunks);
        }

        public int ReadInt()
        {
            int index = Position;
            int value = ReadInt(ref index);
            Position = index;
            return value;
        }
        public int ReadInt(int index)
        {
            return ReadInt(ref index);
        }
        public virtual int ReadInt(ref int index)
        {
            if (index >= Body.Length || index + 4 > Body.Length)
                throw new Exception("Not enough data at the current position to begin reading a Int32 type object.");

            int value = BigEndian.DecypherInt(Body[index++], Body[index++], Body[index++], Body[index++]);
            AddToRead(value);

            return value;
        }

        public ushort ReadShort()
        {
            int index = Position;
            ushort value = ReadShort(ref index);
            Position = index;
            return value;
        }
        public ushort ReadShort(int index)
        {
            return ReadShort(ref index);
        }
        public virtual ushort ReadShort(ref int index)
        {
            if (index >= Body.Length || index + 2 > Body.Length)
                throw new Exception("Not enough data at the current position to begin reading a UInt16 type object.");

            ushort value = BigEndian.DecypherShort(Body[index++], Body[index++]);
            AddToRead(value);

            return value;
        }

        public bool ReadBool()
        {
            int index = Position;
            bool value = ReadBool(ref index);
            Position = index;
            return value;
        }
        public bool ReadBool(int index)
        {
            return ReadBool(ref index);
        }
        public virtual bool ReadBool(ref int index)
        {
            if (index >= Body.Length || index + 1 > Body.Length)
                throw new Exception("Not enough data at the current position to begin reading a Boolean type object.");

            bool value = Body[index++] == 1;
            AddToRead(value);

            return value;
        }

        public string ReadString()
        {
            int index = Position;
            string value = ReadString(ref index);
            Position = index;
            return value;
        }
        public string ReadString(int index)
        {
            return ReadString(ref index);
        }
        public virtual string ReadString(ref int index)
        {
            try
            {
                _suppressRead = true;
                ushort length = ReadShort(ref index);

                if (index >= Body.Length)
                    throw new Exception("Not enough data at the current position to begin reading a String type object.");

                string value = Encoding.UTF8.GetString(Body, index, length);
                index += length;
                AddToRead(value);

                return value;
            }
            finally { _suppressRead = false; }
        }

        public void Append(params object[] chunks)
        {
            AddToWritten(chunks);
            byte[] constructed = Encode(chunks);

            _body.AddRange(constructed);
            Reconstruct();
        }

        public void ClearChunks()
        {
            if (!_beganConstructing)
                throw new Exception("This method cannot be called on a packet that you did not begin constructing.");

            if (_written.Count < 1)
                return;

            _body.Clear();
            _written.Clear();

            Reconstruct();
        }

        public void Remove<T>()
        {
            RemoveAt<T>(Position);
        }
        public void RemoveChunk(int rank)
        {
            if (!_beganConstructing)
                throw new Exception("This method cannot be called on a packet that you did not begin constructing.");

            if (rank < 0 || rank >= _written.Count)
                return;

            _written.RemoveAt(rank);

            _body.Clear();
            if (_written.Count > 0)
                _body.AddRange(Encode(_written.ToArray()));

            Reconstruct();
        }
        public void RemoveAt<T>(int index)
        {
            int chunkLength = 0;
            switch (Type.GetTypeCode(typeof(T)))
            {
                case TypeCode.Int32: chunkLength = 4; break;
                case TypeCode.UInt16: chunkLength = 2; break;
                case TypeCode.Boolean: chunkLength = 1; break;
                case TypeCode.String:
                {
                    try
                    {
                        _suppressRead = true;
                        chunkLength = (2 + ReadShort(index));
                    }
                    finally { _suppressRead = false; }
                    break;
                }
            }
            if (chunkLength < 1) return;
            _body.RemoveRange(index, chunkLength);
            Reconstruct();
        }

        public bool CanRead<T>()
        {
            return CanReadAt<T>(Position);
        }
        public bool CanReadAt<T>(int index)
        {
            int bytesLeft = (Body.Length - index), bytesNeeded = -1;
            if (bytesLeft < 1) return false;

            switch (Type.GetTypeCode(typeof(T)))
            {
                case TypeCode.Int32: bytesNeeded = 4; break;
                case TypeCode.UInt16: bytesNeeded = 2; break;
                case TypeCode.Boolean: bytesNeeded = 1; break;
                case TypeCode.String:
                {
                    if (bytesLeft > 2)
                    {
                        try
                        {
                            _suppressRead = true;
                            bytesNeeded = (2 + ReadShort(index));
                        }
                        finally { _suppressRead = false; }
                    }
                    break;
                }
            }
            return bytesLeft >= bytesNeeded && bytesNeeded != -1;
        }

        public void Replace<T>(object chunk)
        {
            ReplaceAt<T>(Position, chunk);
        }
        public void ReplaceChunk(int rank, object chunk)
        {
            if (!_beganConstructing)
                throw new Exception("This method cannot be called on a packet that you did not begin constructing.");

            if (rank < 0 || rank >= _written.Count)
                return;

            _written[rank] = chunk;

            _body.Clear();
            _body.AddRange(Encode(_written.ToArray()));
            Reconstruct();
        }
        /// <summary>
        /// Replaces the chunk at the given position.
        /// </summary>
        /// <typeparam name="T">The type of the chunk to replace in the packet.</typeparam>
        /// <param name="index">The zero-based index that determines the position of the chunk.</param>
        /// <param name="chunk">The new value to replace the chunk.</param>
        public void ReplaceAt<T>(int index, object chunk)
        {
            switch (Type.GetTypeCode(typeof(T)))
            {
                case TypeCode.Int32: _body.RemoveRange(index, 4); break;
                case TypeCode.UInt16: _body.RemoveRange(index, 2); break;

                case TypeCode.Byte:
                case TypeCode.Boolean: _body.RemoveAt(index); break;

                case TypeCode.String:
                {
                    try
                    {
                        _suppressRead = true;
                        _body.RemoveRange(index, 2 + ReadShort(index));
                    }
                    finally { _suppressRead = false; }
                    break;
                }
            }
            _body.InsertRange(index, Encode(chunk));
            Reconstruct();
        }

        /// <summary>
        /// Moves a chunk in the body to the right.
        /// </summary>
        /// <typeparam name="T">The type of the chunk to begin pushing.</typeparam>
        /// <param name="rank">The zero-based value that determines the order in which the chunk was written.</param>
        /// <param name="jump">The amount of chunks to jump over.</param>
        public void PushChunk(int rank, int jump)
        {
            Move(rank, jump, true);
        }
        /// <summary>
        /// Moves a chunk in the body to the left.
        /// </summary>
        /// <typeparam name="T">The type of the chunk to begin pulling.</typeparam>
        /// <param name="rank">The zero-based value that determines the order in which the chunk was written.</param>
        /// <param name="jump">The amount of chunks to jump over.</param>
        public void PullChunk(int rank, int jump)
        {
            Move(rank, jump, false);
        }
        protected virtual void Move(int rank, int jump, bool isPush)
        {
            if (!_beganConstructing)
                throw new Exception("This method cannot be called on a packet that you did not begin constructing.");

            if (jump < 1) return;
            int newRank = (isPush ? rank + jump : rank - jump);
            if (newRank < 0) newRank = 0;
            if (newRank >= _written.Count) newRank = _written.Count - 1;

            object chunk = _written[rank];
            _written.Remove(chunk);
            _written.Insert(newRank, chunk);

            _body.Clear();
            _body.AddRange(Encode(_written.ToArray()));
            Reconstruct();
        }

        private void AddToRead(params object[] chunks)
        {
            if (!_suppressRead) _read.AddRange(chunks);
            else _suppressRead = false;
        }
        private void AddToWritten(params object[] chunks)
        {
            _written.AddRange(chunks);
        }

        private void ResetCache()
        {
            _toBytesCache = null;
            _toStringCache = null;
        }
        private void Reconstruct()
        {
            ResetCache();

            Length = _body.Count + 2;
            Body = new byte[_body.Count];

            Buffer.BlockCopy(_body.ToArray(), 0, Body, 0, Body.Length);
        }

        public byte[] ToBytes()
        {
            return _toBytesCache ?? (_toBytesCache = Construct(Header, Body));
        }
        public override string ToString()
        {
            return _toStringCache ?? (_toStringCache = ToString(ToBytes()));
        }

        public static byte[] ToBytes(string packet)
        {
            for (int i = 0; i <= 13; i++)
                packet = packet.Replace("[" + i + "]", ((char)i).ToString());

            int endIndex;
            byte[] chunk = null;
            string value, replaceBy, chunkFormat;
            bool writeLength = false, expectClose = false;
            for (int i = 0; i < packet.Length; i++)
            {
                char pByte = packet[i];
                if (!expectClose && pByte != '{') continue;
                else if (pByte == '{') expectClose = true;
                else if (expectClose && pByte != '}')
                {
                    expectClose = false;
                    endIndex = packet.Substring(i).IndexOf('}') + i;
                    if (endIndex == -1 || endIndex == 0 || i + 2 > packet.Length) continue;

                    if (pByte == 'l')
                    {
                        writeLength = true;
                        packet = packet.Remove(i - 1, 3);
                        i = -1;
                        continue;
                    }

                    int vLength = endIndex - i - 2;
                    if (vLength < 1) continue;

                    value = packet.Substring(i + 2, vLength);
                    chunkFormat = string.Format("{{{0}:{1}}}", pByte, value);

                    switch (pByte)
                    {
                        case 's': chunk = Encode(value); break;
                        case 'i': chunk = Encode(int.Parse(value)); break;
                        case 'b':
                        {
                            chunk = value.Length < 4
                                ? Encode(byte.Parse(value)) : Encode(bool.Parse(value));
                            break;
                        }
                        case 'u': chunk = Encode(ushort.Parse(value)); break;
                    }

                    replaceBy = Encoding.Default.GetString(chunk);
                    packet = packet.Replace(chunkFormat, replaceBy);

                    int nextParam = packet.Substring(i).IndexOf('{');
                    if (nextParam == -1) break;
                    else i = nextParam + (i - 1);
                }
            }

            if (writeLength)
                packet = Encoding.Default.GetString(Encode(packet.Length)) + packet;

            return Encoding.Default.GetBytes(packet);
        }
        public static string ToString(byte[] packet)
        {
            string result = Encoding.Default.GetString(packet);
            for (int i = 0; i <= 13; i++)
                result = result.Replace(((char)i).ToString(), "[" + i + "]");
            return result;
        }

        public static byte[] Encode(params object[] chunks)
        {
            if (chunks == null || chunks.Length < 1)
                throw new NullReferenceException();

            var buffer = new List<byte>();
            for (int i = 0; i < chunks.Length; i++)
            {
                object chunk = chunks[i];
                if (chunk == null)
                    throw new NullReferenceException();

                switch (Type.GetTypeCode(chunk.GetType()))
                {
                    case TypeCode.Byte: buffer.Add((byte)chunk); break;
                    case TypeCode.Boolean: buffer.Add(Convert.ToByte((bool)chunk)); break;
                    case TypeCode.Int32: buffer.AddRange(BigEndian.CypherInt((int)chunk)); break;
                    case TypeCode.UInt16: buffer.AddRange(BigEndian.CypherShort((ushort)chunk)); break;

                    default:
                    case TypeCode.String:
                    {
                        byte[] data = chunk as byte[];
                        if (data == null)
                        {
                            string value = chunk.ToString();
                            value = value.Replace("\\r", "\r");
                            value = value.Replace("\\n", "\n");

                            data = new byte[2 + Encoding.UTF8.GetByteCount(value)];
                            Buffer.BlockCopy(BigEndian.CypherShort((ushort)(data.Length - 2)), 0, data, 0, 2);
                            Buffer.BlockCopy(Encoding.UTF8.GetBytes(value), 0, data, 2, data.Length - 2);
                        }
                        buffer.AddRange(data);
                        break;
                    }
                }
            }
            return buffer.ToArray();
        }
        public static byte[] Construct(ushort header, params object[] chunks)
        {
            byte[] body = chunks != null && chunks.Length > 0 ? Encode(chunks) : new byte[0];
            byte[] data = new byte[6 + body.Length];

            Buffer.BlockCopy(BigEndian.CypherInt(body.Length + 2), 0, data, 0, 4);
            Buffer.BlockCopy(BigEndian.CypherShort(header), 0, data, 4, 2);
            Buffer.BlockCopy(body, 0, data, 6, body.Length);

            return data;
        }
    }
}