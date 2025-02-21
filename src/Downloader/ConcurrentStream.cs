﻿using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Downloader
{
    public class ConcurrentStream : IDisposable
    {
        private readonly SemaphoreSlim _queueCheckerSemaphore = new SemaphoreSlim(0);
        private readonly ManualResetEventSlim _completionEvent = new ManualResetEventSlim(true);
        private readonly ConcurrentBag<Packet> _inputBag = new ConcurrentBag<Packet>();
        private int? _resourceReleaseThreshold;
        private long _packetCounter = 0;
        private bool _disposed;
        private Stream _stream;
        private string _path;

        public string Path
        {
            get => _path;
            set
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _path = value;
                    _stream = new FileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                }
            }
        }
        
        public byte[] Data
        {
            get
            {
                Flush();
                if (_stream is MemoryStream mem)
                    return mem.ToArray();

                return null;
            }
            set
            {
                if (value != null)
                    _stream = new MemoryStream(value, true);
            }
        }

        public long Length => _stream?.Length ?? 0;

        public ConcurrentStream(Stream stream)
        {
            _stream = stream;
            Initial();
        }

        public ConcurrentStream(string filename, long initSize)
        {
            _path = filename;
            _stream = new FileStream(filename, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

            if (initSize > 0)
                _stream.SetLength(initSize);

            Initial();
        }

        public ConcurrentStream()
        {
            _stream = new MemoryStream();
            Initial();
        }

        private void Initial()
        {
            Task.Run(Watcher).ConfigureAwait(false);
        }

        public Stream OpenRead()
        {
            _completionEvent.Wait();
            if (_stream?.CanSeek == true)
                _stream.Seek(0, SeekOrigin.Begin);

            return _stream;
        }

        public void WriteAsync(long position, byte[] bytes, int length)
        {
            _inputBag.Add(new Packet(position, bytes, length));
            _completionEvent.Reset();
            _queueCheckerSemaphore.Release();
        }

        private async Task Watcher()
        {
            while (!_disposed)
            {
                await _queueCheckerSemaphore.WaitAsync().ConfigureAwait(false);
                if (_inputBag.TryTake(out var packet))
                {
                    await WritePacket(packet).ConfigureAwait(false);
                    ReleasePackets(packet.Data.Length);
                    packet.Dispose();
                }
            }
        }
        private async Task WritePacket(Packet packet)
        {
            if (_stream.CanSeek)
            {
                _stream.Position = packet.Position;
                await _stream.WriteAsync(packet.Data, 0, packet.Length).ConfigureAwait(false);
                _packetCounter++;
            }

            if (_inputBag.IsEmpty)
                _completionEvent.Set();
        }

        private void ReleasePackets(int packetSize)
        {
            _resourceReleaseThreshold ??= 1024 * 1024 * 50 / packetSize; // 50MB / a packet size

            // Clean up RAM every _resourceReleaseThreshold packet
            if (_packetCounter % _resourceReleaseThreshold == 0)
                GC.Collect();
        }

        public void Flush()
        {
            _completionEvent.Wait();
            _stream?.Flush();
            GC.Collect();
        }

        public void Dispose()
        {
            if (_disposed == false)
            {
                Flush();
                _disposed = true;
                _queueCheckerSemaphore.Dispose();
                _stream.Dispose();
            }
        }
    }
}
