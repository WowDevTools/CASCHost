using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace CASCEdit.ZLib
{
    public class ZStreamReader : Stream, IDisposable
    {
        Stream baseStream;
        zlib.z_stream zstream;

        public ZStreamReader(Stream stream) : this(stream, 0x1000) { }

        public ZStreamReader(Stream stream, uint bufferSize, int bits = zlib.DEF_WBITS)
        {
            if (!stream.CanRead)
                throw new ArgumentException("Stream not readable.", "stream");

            if (bufferSize == 0)
                throw new ArgumentOutOfRangeException("bufferSize", "must be greater zero");

            baseStream = stream;

            zstream = new zlib.z_stream();

            switch (zlib.inflateInit(zstream))
            {
                case zlib.Z_OK:
                    break;
                case zlib.Z_MEM_ERROR:
                case zlib.Z_STREAM_ERROR:
                    throw new Exception("zlib memory error");
                default:
                    throw new Exception("Unknown zlib error");
            }

            zstream.in_buf = new byte[bufferSize];
            zstream.next_in = 0;
            zstream.avail_in = (uint)baseStream.Read(zstream.in_buf, (int)zstream.next_in, (int)bufferSize);
        }

        public override bool CanRead => baseStream != null && baseStream.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override void Flush()
        {
            if (baseStream == null)
                throw new ObjectDisposedException(null, "File closed");
        }

        public override long Length
        {
            get
            {
                if (baseStream == null)
                    throw new ObjectDisposedException(null, "File closed");

                return baseStream.Length;
            }
        }

        public override long Position
        {
            get
            {
                if (baseStream == null)
                    throw new ObjectDisposedException(null, "File closed");

                return baseStream.Position - zstream.avail_in;
            }
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException("value", "must not be negative");

                if (baseStream == null)
                    throw new ObjectDisposedException(null, "File closed");

                throw new NotSupportedException("Seeking not supported");
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (baseStream == null)
                throw new ObjectDisposedException(null, "File closed");

            throw new NotSupportedException("Seeking not supported");
        }

        public override void SetLength(long value)
        {
            if (baseStream == null)
                throw new ObjectDisposedException(null, "File closed");

            throw new NotSupportedException("Setting the length is not supported");
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (baseStream == null)
                throw new ObjectDisposedException(null, "File closed");

            zstream.out_buf = buffer;
            zstream.next_out = offset;
            zstream.avail_out = (uint)count;

            do
            {
                if ((zstream.avail_in) == 0)
                {
                    zstream.avail_in = (uint)zstream.in_buf.Length;
                    zstream.next_in = 0;
                    zstream.avail_in = (uint)baseStream.Read(zstream.in_buf, 0, (int)zstream.in_buf.Length);
                }

                int ret = zlib.inflate(zstream, zlib.Z_PARTIAL_FLUSH);
                if (ret == zlib.Z_STREAM_END)
                {
                    if (zstream.avail_in != 0)
                        throw new Exception("Extra compressed data");

                    break;
                }

                if (ret != zlib.Z_OK)
                    throw new Exception((zstream.msg != null && zstream.msg.Length > 0) ? zstream.msg : "Decompression error");

            } while (zstream.avail_out != 0);

            return zstream.next_out;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (baseStream == null)
                throw new ObjectDisposedException(null, "File closed");

            throw new NotSupportedException("Writting not supported");
        }

#pragma warning disable CS0114
        public void Close()
        {
            if (baseStream != null)
                baseStream.Dispose();
            baseStream = null;

            if (zstream != null)
                zlib.inflateEnd(zstream);

            zstream = null;

            base.Dispose();
        }
#pragma warning restore CS0114

        void IDisposable.Dispose()
        {
            Close();
        }
    }
}
